// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AutoLayout.Core.Native;

namespace AutoLayout.Core;

/// <summary>
/// A hidden top-level window that receives notifications (devices, sessions, power, settings,
/// tray icon) and runs timers, on the thread that creates it. It is top-level, not message-only,
/// because message-only windows do not receive broadcasts.
/// </summary>
public sealed partial class MessageWindow : IDisposable
{
    public delegate bool MessageHandler(int message, IntPtr wParam, IntPtr lParam, out IntPtr result);

    private const string ClassName = "AutoLayout.MessageWindow";

    // Kept alive for as long as the class is registered
    private static readonly NativeMethods.WindowProcedure Procedure = WindowProc;
    private static readonly Dictionary<IntPtr, MessageWindow> Windows = new();
    private static bool classRegistered;

    private readonly Dictionary<uint, Action> timers = new();
    private uint nextTimerId = 1;

    public MessageWindow()
    {
        RegisterClass();
        Handle = NativeMethods.CreateWindowEx(NativeMethods.WS_EX_TOOLWINDOW, ClassName, string.Empty, NativeMethods.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Windows[Handle] = this;
    }

    /// <summary>Handlers are called in order until one returns true.</summary>
    public event MessageHandler? MessageReceived;

    public IntPtr Handle { get; private set; }

    /// <summary>Starts (or restarts) a one-shot or repeating timer and returns its id.</summary>
    public uint StartTimer(uint milliseconds, Action callback, uint id = 0)
    {
        if (id == 0)
        {
            id = nextTimerId++;
        }
        timers[id] = callback;
        NativeMethods.SetTimer(Handle, (UIntPtr)id, milliseconds, IntPtr.Zero);
        return id;
    }

    public void StopTimer(uint id)
    {
        if (timers.Remove(id))
        {
            NativeMethods.KillTimer(Handle, (UIntPtr)id);
        }
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }
        foreach (uint id in timers.Keys)
        {
            NativeMethods.KillTimer(Handle, (UIntPtr)id);
        }
        timers.Clear();
        Windows.Remove(Handle);
        NativeMethods.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    private static void RegisterClass()
    {
        if (classRegistered)
        {
            return;
        }
        NativeMethods.WindowClassEx windowClass = new()
        {
            Size = Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            WindowProc = Procedure,
            Instance = NativeMethods.GetModuleHandle(null),
            ClassName = ClassName,
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        classRegistered = true;
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (Windows.TryGetValue(hwnd, out MessageWindow? window))
            {
                if (message == NativeMethods.WM_TIMER)
                {
                    uint id = unchecked((uint)wParam.ToInt64());
                    if (window.timers.TryGetValue(id, out Action? callback))
                    {
                        callback();
                        return IntPtr.Zero;
                    }
                }

                MessageHandler? handlers = window.MessageReceived;
                if (handlers != null)
                {
                    foreach (MessageHandler handler in handlers.GetInvocationList())
                    {
                        if (handler(message, wParam, lParam, out IntPtr result))
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (Exception error)
        {
            // Exceptions must not cross back into Windows
            Log.Write("Error handling message 0x" + message.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ": " + error);
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
