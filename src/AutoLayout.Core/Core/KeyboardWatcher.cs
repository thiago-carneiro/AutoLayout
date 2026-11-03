// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using AutoLayout.Core.Native;

namespace AutoLayout.Core;

/// <summary>
/// Raises <see cref="KeyboardsChanged"/> when keyboards are connected or disconnected or the
/// session becomes local or remote, and <see cref="LayoutsChanged"/> when the input languages
/// may have changed. Events come on the thread that created the window, after a short pause,
/// since Windows sends one notification per device interface and settings change in bursts.
/// </summary>
public sealed partial class KeyboardWatcher : IDisposable
{
    private const uint SettleMilliseconds = 750;
    private const uint DevicesTimer = 0x100;
    private const uint LayoutsTimer = 0x101;

    private readonly MessageWindow window;
    private IntPtr deviceNotification;
    private bool sessionNotification;

    public KeyboardWatcher(MessageWindow window)
    {
        this.window = window;
        window.MessageReceived += OnMessage;

        NativeMethods.DevBroadcastDeviceInterface filter = new()
        {
            Size = Marshal.SizeOf<NativeMethods.DevBroadcastDeviceInterface>(),
            DeviceType = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
            ClassGuid = NativeMethods.GuidDevInterfaceKeyboard,
        };
        deviceNotification = NativeMethods.RegisterDeviceNotification(window.Handle, ref filter, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (deviceNotification == IntPtr.Zero)
        {
            Log.Write("Keyboard notifications are not available (error " + Marshal.GetLastWin32Error() + ")");
        }

        // Remote Desktop connections change which keyboard is in use, like a keyboard being plugged in
        sessionNotification = NativeMethods.WTSRegisterSessionNotification(window.Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION);
    }

    public event EventHandler? KeyboardsChanged;

    public event EventHandler? LayoutsChanged;

    /// <summary>Windows is shutting down, restarting or signing out; raised at once, not after a pause.</summary>
    public event EventHandler? SessionEnding;

    /// <summary>Checks the keyboards again after the pause, e.g. when Windows could not list them.</summary>
    public void ScheduleKeyboardCheck()
    {
        window.StartTimer(SettleMilliseconds, OnDevicesSettled, DevicesTimer);
    }

    public void Dispose()
    {
        window.MessageReceived -= OnMessage;
        window.StopTimer(DevicesTimer);
        window.StopTimer(LayoutsTimer);
        if (deviceNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterDeviceNotification(deviceNotification);
            deviceNotification = IntPtr.Zero;
        }
        if (sessionNotification)
        {
            NativeMethods.WTSUnRegisterSessionNotification(window.Handle);
            sessionNotification = false;
        }
    }

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (message)
        {
            case NativeMethods.WM_DEVICECHANGE:
            case NativeMethods.WM_WTSSESSION_CHANGE:
                // Restart the pause on every notification of a burst
                ScheduleKeyboardCheck();
                break;

            case NativeMethods.WM_SETTINGCHANGE:
            case NativeMethods.WM_INPUTLANGCHANGE:
                window.StartTimer(SettleMilliseconds, OnLayoutsSettled, LayoutsTimer);
                break;

            case NativeMethods.WM_ENDSESSION when wParam != IntPtr.Zero:
                // wParam is zero when the end of the session was cancelled
                SessionEnding?.Invoke(this, EventArgs.Empty);
                break;
        }
        return false;
    }

    private void OnDevicesSettled()
    {
        window.StopTimer(DevicesTimer);
        KeyboardsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLayoutsSettled()
    {
        window.StopTimer(LayoutsTimer);
        LayoutsChanged?.Invoke(this, EventArgs.Empty);
    }
}
