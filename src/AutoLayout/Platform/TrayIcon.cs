// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using AutoLayout.Core;

namespace AutoLayout.Platform;

/// <summary>One command of the tray menu.</summary>
/// <param name="Text">Menu text; null for a separator.</param>
/// <param name="Action">What clicking does.</param>
/// <param name="IsDefault">Shown in bold; also what a click on the icon does.</param>
internal sealed record TrayMenuItem(string? Text, Action? Action, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(null, null);
}

/// <summary>
/// The notification-area icon, through Shell_NotifyIcon on the app's message window. It is
/// added again when Explorer restarts and redrawn when the taskbar theme changes.
/// </summary>
internal sealed partial class TrayIcon : IDisposable
{
    private const int IconId = 1;
    private const int CallbackMessage = Shell.WM_APP + 1;
    private const int TipMaxLength = 127;

    private readonly MessageWindow window;
    private readonly int taskbarCreatedMessage;
    private IntPtr icon;
    private TrayState state;
    private string tip = string.Empty;
    private bool added;
    private Action? balloonClick;

    public TrayIcon(MessageWindow window)
    {
        this.window = window;
        taskbarCreatedMessage = Shell.RegisterWindowMessage("TaskbarCreated");
        window.MessageReceived += OnMessage;
    }

    /// <summary>Left click or Enter on the icon.</summary>
    public event EventHandler? Invoked;

    /// <summary>Builds the menu each time it opens, so it is always current.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuBuilder { get; set; }

    public bool RightToLeft { get; set; }

    public void Update(TrayState newState, string newTip)
    {
        bool redraw = icon == IntPtr.Zero || newState != state;
        state = newState;
        // Shell_NotifyIcon allows 127 characters; names were already shortened by the caller
        tip = newTip.Length > TipMaxLength ? TextSanitizer.Shorten(newTip, TipMaxLength) : newTip;
        if (redraw)
        {
            Redraw();
        }
        Notify(added ? Shell.NIM_MODIFY : Shell.NIM_ADD);
    }

    /// <summary>
    /// Shows a notification from the tray icon (used when Windows App SDK notifications are not
    /// available). Windows shows it like any other notification.
    /// </summary>
    public void ShowBalloon(string title, string text, bool warning, Action? onClick)
    {
        if (!added)
        {
            return;
        }
        balloonClick = onClick;
        Shell.NotifyIconData data = new()
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<Shell.NotifyIconData>(),
            Window = window.Handle,
            Id = IconId,
            Flags = Shell.NIF_INFO,
            Tip = tip,
            Info = TextSanitizer.Shorten(text, 255),
            InfoTitle = TextSanitizer.Shorten(title, 63),
            InfoFlags = warning ? Shell.NIIF_WARNING : Shell.NIIF_INFO,
        };
        Shell.Shell_NotifyIcon(Shell.NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        window.MessageReceived -= OnMessage;
        if (added)
        {
            Notify(Shell.NIM_DELETE);
            added = false;
        }
        if (icon != IntPtr.Zero)
        {
            Shell.DestroyIcon(icon);
            icon = IntPtr.Zero;
        }
    }

    private void Redraw()
    {
        IntPtr old = icon;
        int dpi = Shell.GetDpiForSystem();
        icon = TrayIconRenderer.Create(state, Shell.GetSystemMetricsForDpi(Shell.SM_CXSMICON, dpi));
        if (old != IntPtr.Zero)
        {
            Shell.DestroyIcon(old);
        }
    }

    private void Notify(int message)
    {
        Shell.NotifyIconData data = new()
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<Shell.NotifyIconData>(),
            Window = window.Handle,
            Id = IconId,
            Flags = Shell.NIF_MESSAGE | Shell.NIF_ICON | Shell.NIF_TIP | Shell.NIF_SHOWTIP,
            CallbackMessage = CallbackMessage,
            Icon = icon,
            Tip = tip,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
        if (Shell.Shell_NotifyIcon(message, ref data))
        {
            if (message == Shell.NIM_ADD)
            {
                added = true;
                data.VersionOrTimeout = Shell.NOTIFYICON_VERSION_4;
                Shell.Shell_NotifyIcon(Shell.NIM_SETVERSION, ref data);
            }
        }
        else if (message == Shell.NIM_MODIFY)
        {
            // Explorer may have restarted without telling us: add it again
            added = false;
            Notify(Shell.NIM_ADD);
        }
    }

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message == taskbarCreatedMessage && taskbarCreatedMessage != 0)
        {
            added = false;
            Redraw();
            Notify(Shell.NIM_ADD);
            return true;
        }
        if (message == Shell.WM_SETTINGCHANGE)
        {
            // Light/dark taskbar
            Redraw();
            if (added)
            {
                Notify(Shell.NIM_MODIFY);
            }
            return false;
        }
        if (message != CallbackMessage)
        {
            return false;
        }

        int notification = unchecked((int)lParam.ToInt64()) & 0xFFFF;
        switch (notification)
        {
            case Shell.NIN_SELECT:
            case Shell.NIN_KEYSELECT:
                Invoked?.Invoke(this, EventArgs.Empty);
                return true;
            case Shell.NIN_BALLOONUSERCLICK:
                Action? click = balloonClick;
                balloonClick = null;
                click?.Invoke();
                return true;
            case Shell.WM_CONTEXTMENU:
                long position = wParam.ToInt64();
                ShowMenu((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));
                return true;
        }
        return false;
    }

    private void ShowMenu(int x, int y)
    {
        IReadOnlyList<TrayMenuItem> items = MenuBuilder?.Invoke() ?? Array.Empty<TrayMenuItem>();
        IntPtr menu = Shell.CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                TrayMenuItem item = items[i];
                if (item.Text == null)
                {
                    Shell.AppendMenu(menu, Shell.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                int flags = Shell.MF_STRING | (item.Action == null ? Shell.MF_GRAYED : 0);
                // "&" would become an access key (keyboard names can contain it): show it as is
                Shell.AppendMenu(menu, flags, (UIntPtr)(i + 1), item.Text.Replace("&", "&&", StringComparison.Ordinal));
                if (item.IsDefault)
                {
                    Shell.SetMenuDefaultItem(menu, i + 1, 0);
                }
            }

            // Required for the menu to close when clicking elsewhere
            Shell.SetForegroundWindow(window.Handle);
            int flagsTrack = Shell.TPM_RIGHTBUTTON | Shell.TPM_RETURNCMD | Shell.TPM_NONOTIFY | (RightToLeft ? Shell.TPM_LAYOUTRTL : 0);
            int chosen = Shell.TrackPopupMenuEx(menu, flagsTrack, x, y, window.Handle, IntPtr.Zero);
            Shell.PostMessage(window.Handle, Shell.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (chosen > 0 && chosen <= items.Count)
            {
                items[chosen - 1].Action?.Invoke();
            }
        }
        finally
        {
            Shell.DestroyMenu(menu);
        }
    }
}
