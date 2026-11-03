// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using AutoLayout.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace AutoLayout.Core;

public enum BacklightLevel
{
    Off = 0,
    Low = 1,
    High = 2,
}

/// <summary>
/// ThinkPad keyboard backlight, through the power management driver Lenovo installs with its
/// ThinkPad drivers. The interface is undocumented; as observed on a ThinkPad T14 Gen 2: the
/// query control code answers 0x000502LL, where LL is the level (0 off, 1 low, 2 high), and the
/// set control code takes the level as a 32-bit value.
/// </summary>
public static class ThinkPadBacklight
{
    private const string DevicePath = @"\\.\IBMPmDrv";
    private const uint QueryControlCode = 0x00222680;
    private const uint SetControlCode = 0x00222684;
    private const uint ReadyMask = 0xFFFFFF00;
    private const uint ReadyValue = 0x00050200;

    private static readonly object Gate = new();

    // Kept open between calls. Opening costs about 4 µs, but each query about 1.9 ms in the
    // driver (measured on a T14 Gen 2); opening per call also took a second query to validate.
    private static SafeFileHandle? device;

    public static bool IsAvailable => TryGetLevel(out _);

    public static bool TryGetLevel(out BacklightLevel level)
    {
        level = BacklightLevel.Off;
        if (!Send(QueryControlCode, 0, out uint answer) || (answer & ReadyMask) != ReadyValue)
        {
            return false;
        }

        uint value = answer & ~ReadyMask;
        if (value > (uint)BacklightLevel.High)
        {
            return false;
        }
        level = (BacklightLevel)value;
        return true;
    }

    public static bool TrySetLevel(BacklightLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
        return Send(SetControlCode, (uint)level, out _);
    }

    private static bool Send(uint controlCode, uint input, out uint output)
    {
        lock (Gate)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                device ??= OpenDevice();
                if (device == null)
                {
                    break;
                }
                if (Control(device, controlCode, input, out output))
                {
                    return true;
                }

                // The driver was restarted or removed: open it again, once
                device.Dispose();
                device = null;
            }
            output = 0;
            return false;
        }
    }

    private static SafeFileHandle? OpenDevice()
    {
        // The control codes need no access rights; ask for read/write only if the driver insists
        foreach (uint access in new[] { 0u, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE })
        {
            SafeFileHandle device = NativeMethods.CreateFile(DevicePath, access,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (!device.IsInvalid)
            {
                if (Control(device, QueryControlCode, 0, out uint answer) && (answer & ReadyMask) == ReadyValue)
                {
                    return device;
                }
            }
            device.Dispose();
        }
        return null;
    }

    private static bool Control(SafeFileHandle device, uint controlCode, uint input, out uint output)
    {
        byte[] inBuffer = BitConverter.GetBytes(input);
        byte[] outBuffer = new byte[sizeof(uint)];
        bool ok = NativeMethods.DeviceIoControl(device, controlCode, inBuffer, inBuffer.Length, outBuffer, outBuffer.Length,
            out int returned, IntPtr.Zero);
        output = ok && returned >= sizeof(uint) ? BitConverter.ToUInt32(outBuffer, 0) : 0;
        return ok;
    }
}
