// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using AutoLayout.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AutoLayout.Views;

/// <summary>Keyboards and Settings, in one window. Closing it hides it; the app keeps running.</summary>
public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    private const int DefaultWidth = 960;
    private const int DefaultHeight = 720;

    internal MainWindow(Func<bool> exiting)
    {
        InitializeComponent();
        Root.FlowDirection = Loc.FlowDirection;
        if (App.Current.IsDemo)
        {
            // Screenshots can't capture Mica
            Root.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        }

        int width = App.Current.Engine.Settings.MainWindowWidth;
        int height = App.Current.Engine.Settings.MainWindowHeight;
        WindowHelper.Setup(this, TitleBar, Loc.Get("Main_Title"),
            width >= 640 ? width : DefaultWidth, height >= 480 ? height : DefaultHeight);
        WindowHelper.HideOnClose(this, exiting);

        // Remember the size in logical pixels, so it doesn't grow when the scaling changes
        AppWindow.Changed += (sender, args) =>
        {
            if (args.DidSizeChange && sender.IsVisible)
            {
                (int w, int h) = WindowHelper.LogicalSize(this);
                App.Current.Engine.Settings.MainWindowWidth = w;
                App.Current.Engine.Settings.MainWindowHeight = h;
            }
        };
        AppWindow.Closing += (_, _) => App.Current.SaveSettings();
    }

    internal void Navigate(bool settings)
    {
        if (settings)
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
        }
        else if (!ReferenceEquals(Navigation.SelectedItem, KeyboardsItem))
        {
            Navigation.SelectedItem = KeyboardsItem;
        }
        else if (ContentFrame.Content == null)
        {
            ContentFrame.Navigate(typeof(KeyboardsPage));
        }
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected ? typeof(SettingsPage) : typeof(KeyboardsPage);
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }
    }
}
