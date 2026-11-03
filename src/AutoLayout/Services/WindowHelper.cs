// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using AutoLayout.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace AutoLayout.Services;

/// <summary>Common window setup: Mica, title bar, icon, size in logical pixels, centered.</summary>
internal static class WindowHelper
{
    public static void Setup(Window window, FrameworkElement titleBar, string title, int width, int height)
    {
        window.Title = title;
        window.SystemBackdrop = new MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);

        AppWindow appWindow = window.AppWindow;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AutoLayout.ico");
        if (File.Exists(icon))
        {
            appWindow.SetIcon(icon);
        }
        Resize(window, width, height);
        Center(window);
    }

    public static double Scale(Window window)
    {
        int dpi = Shell.GetDpiForWindow(WindowNative.GetWindowHandle(window));
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>Sets the size in logical pixels (at 100% scaling).</summary>
    public static void Resize(Window window, int width, int height)
    {
        double scale = Scale(window);
        window.AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    public static (int Width, int Height) LogicalSize(Window window)
    {
        double scale = Scale(window);
        SizeInt32 size = window.AppWindow.Size;
        return ((int)(size.Width / scale), (int)(size.Height / scale));
    }

    public static void Center(Window window)
    {
        AppWindow appWindow = window.AppWindow;
        DisplayArea area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = area.WorkArea;
        SizeInt32 size = appWindow.Size;
        appWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - size.Width) / 2), work.Y + Math.Max(0, (work.Height - size.Height) / 2)));
    }

    /// <summary>Shows the window and brings it to the front.</summary>
    public static void Bring(Window window)
    {
        AppWindow appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        appWindow.Show();
        window.Activate();
        Shell.SetForegroundWindow(WindowNative.GetWindowHandle(window));
    }

    /// <summary>
    /// Makes the close button hide the window instead of closing it. Closing it would not give
    /// memory back: WinUI keeps what it loaded for the first window (measured).
    /// </summary>
    public static void HideOnClose(Window window, Func<bool> allowClose)
    {
        window.AppWindow.Closing += (sender, args) =>
        {
            if (!allowClose())
            {
                args.Cancel = true;
                sender.Hide();
            }
        };
    }
}
