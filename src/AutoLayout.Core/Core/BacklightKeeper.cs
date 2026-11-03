// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using AutoLayout.Core.Native;

namespace AutoLayout.Core;

/// <summary>
/// ThinkPads turn the keyboard backlight off with the display (and at boot) and leave it off.
/// This follows the level the user picks with Fn+Space while the display is on, and puts it
/// back after the display turns on, after resume and at startup. Off, when chosen by the user,
/// is kept as is.
/// </summary>
public sealed partial class BacklightKeeper : IDisposable
{
    private const uint FollowMilliseconds = 3000;
    private const uint FirstRestoreMilliseconds = 1500;
    private const uint SecondRestoreMilliseconds = 3000;
    private const uint FollowTimer = 0x200;
    private const uint RestoreTimer = 0x201;

    private readonly MessageWindow window;
    private IntPtr displayNotification;
    private bool enabled;
    private int restoresLeft;

    // True from the moment the display turns off (or the PC suspends) until the level is
    // restored: changes seen then come from the firmware, not from the user
    private bool restoring;

    // A new level is accepted only when read twice in a row, so a reading taken just as the
    // firmware turns the light off is not mistaken for the user's choice
    private BacklightLevel? candidate;

    public BacklightKeeper(MessageWindow window, BacklightLevel level)
    {
        this.window = window;
        Level = level;
        window.MessageReceived += OnMessage;

        Guid displayState = NativeMethods.GuidConsoleDisplayState;
        displayNotification = NativeMethods.RegisterPowerSettingNotification(window.Handle, ref displayState,
            NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    /// <summary>The level the user picked changed, and can be saved.</summary>
    public event EventHandler? LevelChanged;

    /// <summary>The level to keep.</summary>
    public BacklightLevel Level { get; private set; }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
            {
                return;
            }
            enabled = value;
            if (enabled)
            {
                // Start from the level that is on now, if any
                if (ThinkPadBacklight.TryGetLevel(out BacklightLevel current) && current != BacklightLevel.Off)
                {
                    SetLevel(current);
                }
                window.StartTimer(FollowMilliseconds, OnFollow, FollowTimer);
                ScheduleRestore();
            }
            else
            {
                window.StopTimer(FollowTimer);
                window.StopTimer(RestoreTimer);
                restoring = false;
            }
        }
    }

    public void Dispose()
    {
        window.MessageReceived -= OnMessage;
        window.StopTimer(FollowTimer);
        window.StopTimer(RestoreTimer);
        if (displayNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterPowerSettingNotification(displayNotification);
            displayNotification = IntPtr.Zero;
        }
    }

    private void SetLevel(BacklightLevel level)
    {
        if (Level != level)
        {
            Level = level;
            LevelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleRestore()
    {
        if (!enabled)
        {
            return;
        }
        restoring = true;
        candidate = null;
        restoresLeft = 2;
        window.StartTimer(FirstRestoreMilliseconds, OnRestore, RestoreTimer);
    }

    private void OnRestore()
    {
        if (Level != BacklightLevel.Off && ThinkPadBacklight.TryGetLevel(out BacklightLevel current) && current != Level
            && ThinkPadBacklight.TrySetLevel(Level))
        {
            Log.Write("Keyboard backlight restored to " + Level);
        }

        // Checked twice: the firmware may turn the light off again right after waking up
        restoresLeft--;
        if (restoresLeft > 0)
        {
            window.StartTimer(SecondRestoreMilliseconds, OnRestore, RestoreTimer);
        }
        else
        {
            window.StopTimer(RestoreTimer);
            restoring = false;
        }
    }

    private void OnFollow()
    {
        if (restoring || !ThinkPadBacklight.TryGetLevel(out BacklightLevel current))
        {
            candidate = null;
            return;
        }
        if (current == Level)
        {
            candidate = null;
        }
        else if (candidate == current)
        {
            SetLevel(current);
            candidate = null;
        }
        else
        {
            candidate = current;
        }
    }

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message != NativeMethods.WM_POWERBROADCAST || !enabled)
        {
            return false;
        }

        // Any process on the desktop can post this message: read it defensively
        int eventType = unchecked((int)wParam.ToInt64());
        if (eventType == NativeMethods.PBT_APMSUSPEND)
        {
            restoring = true;
            candidate = null;
            window.StopTimer(RestoreTimer);
        }
        else if (eventType == NativeMethods.PBT_APMRESUMEAUTOMATIC)
        {
            ScheduleRestore();
        }
        else if (eventType == NativeMethods.PBT_POWERSETTINGCHANGE)
        {
            byte? display = ReadDisplayState(lParam);
            if (display == 1)
            {
                ScheduleRestore();
            }
            else if (display != null)
            {
                // Display off or dimmed: stop following until the level is restored
                restoring = true;
                candidate = null;
                window.StopTimer(RestoreTimer);
            }
        }
        return false;
    }

    /// <summary>The display state in a POWERBROADCAST_SETTING, or null if lParam is not one.</summary>
    private static byte? ReadDisplayState(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return null;
        }
        // GUID (16) + DataLength (4) + Data (1)
        byte[] buffer = new byte[21];
        if (!NativeMethods.ReadProcessMemory(NativeMethods.GetCurrentProcess(), lParam, buffer, (IntPtr)buffer.Length, out IntPtr read)
            || read.ToInt64() != buffer.Length)
        {
            return null;
        }
        byte[] guidBytes = new byte[16];
        Array.Copy(buffer, guidBytes, 16);
        if (new Guid(guidBytes) != NativeMethods.GuidConsoleDisplayState || BitConverter.ToUInt32(buffer, 16) < 1)
        {
            return null;
        }
        return buffer[20];
    }
}
