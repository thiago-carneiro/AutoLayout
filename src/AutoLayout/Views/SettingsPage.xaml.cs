// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using AutoLayout.Core;
using AutoLayout.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace AutoLayout.Views;

/// <summary>App behavior. Every switch applies at once.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Engine engine = App.Current.Engine;
    private bool loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        loading = true;
        StartupToggle.IsOn = await Startup.IsEnabledAsync();
        NotifyToggle.IsOn = engine.Settings.NotifyOnSwitch;
        BacklightToggle.IsOn = engine.Settings.KeepBacklight;
        LogToggle.IsOn = engine.Settings.LogEnabled;
        ThinkPadSection.Visibility = App.Current.BacklightAvailable ? Visibility.Visible : Visibility.Collapsed;
        OpenLogButton.IsEnabled = File.Exists(Log.FilePath);
        Version? version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = Loc.Format("About_Version", version != null ? version.ToString(3) : "?");
        ShowStartupInfo(null);
        loading = false;
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }
        StartupChange result = await Startup.SetEnabledAsync(StartupToggle.IsOn);
        if (result != StartupChange.Done)
        {
            loading = true;
            StartupToggle.IsOn = await Startup.IsEnabledAsync();
            loading = false;
        }
        ShowStartupInfo(result);
    }

    private void ShowStartupInfo(StartupChange? result)
    {
        string? message = result switch
        {
            StartupChange.BlockedByUser => Loc.Get("Set_Startup_BlockedByUser"),
            StartupChange.BlockedByPolicy => Loc.Get("Set_Startup_BlockedByPolicy"),
            StartupChange.Failed => Loc.Get("Err_Startup"),
            _ => StartupToggle.IsOn && Startup.IsRiskyLocation ? Loc.Get("Set_Startup_RiskyLocation") : null,
        };
        StartupInfo.Message = message ?? string.Empty;
        StartupInfo.Severity = result is StartupChange.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        StartupInfo.IsOpen = message != null;
    }

    private void OnNotifyToggled(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            engine.Settings.NotifyOnSwitch = NotifyToggle.IsOn;
            App.Current.SaveSettings();
        }
    }

    private void OnBacklightToggled(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            engine.Settings.KeepBacklight = BacklightToggle.IsOn;
            App.Current.SaveSettings();
            App.Current.SetBacklight(BacklightToggle.IsOn);
        }
    }

    private void OnLogToggled(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            App.Current.SetLogging(LogToggle.IsOn);
            OpenLogButton.IsEnabled = File.Exists(Log.FilePath);
        }
    }

    private async void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Log.FilePath))
        {
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(Log.FilePath));
        }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchFolderPathAsync(AppPaths.DataDirectory);

    private async void OnViewLicense(object sender, RoutedEventArgs e) => await OpenBundled("LICENSE.txt");

    private async void OnViewNotice(object sender, RoutedEventArgs e) => await OpenBundled("NOTICE.txt");

    private static async System.Threading.Tasks.Task OpenBundled(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
        {
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
        }
    }
}
