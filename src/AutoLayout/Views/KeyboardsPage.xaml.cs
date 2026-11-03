// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoLayout.Core;
using AutoLayout.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AutoLayout.Views;

/// <summary>One keyboard in the list.</summary>
public sealed partial class KeyboardItem
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Layout { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public Brush? StatusBackground { get; init; }
    public Brush? StatusForeground { get; init; }
    public string Glyph { get; init; } = string.Empty;
    public double Opacity { get; init; } = 1;
    public Visibility CanIgnore { get; init; }
    public string EditLabel { get; init; } = string.Empty;
    public string IgnoreLabel { get; init; } = string.Empty;
    public string ForgetLabel { get; init; } = string.Empty;
    public string MoreLabel { get; init; } = string.Empty;
}

/// <summary>What applies now, the default layout, and every keyboard seen. Changes apply at once.</summary>
public sealed partial class KeyboardsPage : Page
{
    private readonly Engine engine = App.Current.Engine;
    private readonly ObservableCollection<KeyboardItem> items = new();
    private bool updatingDefault;

    public KeyboardsPage()
    {
        InitializeComponent();
        KeyboardList.ItemsSource = items;
        Loaded += (_, _) =>
        {
            engine.Changed += OnEngineChanged;
            Refresh();
        };
        Unloaded += (_, _) => engine.Changed -= OnEngineChanged;
    }

    private void OnEngineChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        RefreshStatus();
        RefreshDefault();
        RefreshList();
    }

    private void RefreshStatus()
    {
        CurrentLayoutText.Text = App.Current.CurrentLayoutName();
        KeyboardRecord? active = engine.ActiveKeyboardKey != null ? engine.Find(engine.ActiveKeyboardKey) : null;
        if (engine.IsRemoteAutomatic)
        {
            ReasonText.Text = Loc.Get("Main_Now_Remote");
        }
        else if (active?.Connection == KeyboardConnection.RemoteDesktop)
        {
            ReasonText.Text = Loc.Get("Main_Now_RemoteFixed");
        }
        else if (active != null)
        {
            ReasonText.Text = Loc.Format("Main_Now_ByKeyboard", Loc.Name(Ui.KeyboardName(active)));
        }
        else
        {
            ReasonText.Text = Loc.Get("Main_Now_Default");
        }

        Problem? problem = engine.CurrentProblem;
        ProblemBar.IsOpen = problem != null;
        if (problem != null)
        {
            ProblemBar.Title = Loc.Format("Main_Now_Error", Loc.Name(problem.LayoutName, 60));
            ProblemBar.Message = problem.Kind switch
            {
                ProblemKind.LayoutMissing => Loc.Get("Main_Problem_Missing"),
                ProblemKind.DefaultMissing => Loc.Get("Main_Problem_DefaultMissing"),
                _ => Loc.Get("Main_Problem_Apply"),
            };
            ProblemButton.Content = Loc.Get(problem.Kind == ProblemKind.ApplyFailed ? "Common_ViewLog" : "Main_Problem_Choose");
            ProblemButton.Visibility = problem.Kind == ProblemKind.ApplyFailed && !Log.Enabled ? Visibility.Collapsed : Visibility.Visible;
        }

        int waiting = engine.AwaitingSetUp.Count;
        WaitingBar.IsOpen = waiting > 0;
        WaitingBar.Title = waiting == 1 ? Loc.Get("Main_Waiting_One") : Loc.Format("Main_Waiting_Many", waiting);
    }

    private void RefreshDefault()
    {
        updatingDefault = true;
        try
        {
            DefaultCombo.Items.Clear();
            string? selected = engine.Settings.DefaultLayoutProfileId;
            foreach (InputProfile profile in engine.InstalledProfiles.OrderBy(p => Ui.ProfileName(engine, p), StringComparer.CurrentCultureIgnoreCase))
            {
                ComboBoxItem item = new() { Content = Ui.ProfileName(engine, profile), Tag = profile };
                DefaultCombo.Items.Add(item);
                if (string.Equals(profile.Id, selected, StringComparison.OrdinalIgnoreCase))
                {
                    DefaultCombo.SelectedItem = item;
                }
            }
            if (DefaultCombo.SelectedItem == null && selected != null)
            {
                ComboBoxItem missing = new() { Content = Loc.Format("Layout_NotInstalled", selected), IsEnabled = false };
                DefaultCombo.Items.Insert(0, missing);
                DefaultCombo.SelectedItem = missing;
            }
        }
        finally
        {
            updatingDefault = false;
        }
    }

    private void RefreshList()
    {
        List<KeyboardRecord> keyboards = engine.Keyboards.ToList();
        keyboards.Sort(Compare);

        items.Clear();
        foreach (KeyboardRecord keyboard in keyboards)
        {
            items.Add(ToItem(keyboard));
        }
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyboardList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private KeyboardItem ToItem(KeyboardRecord keyboard)
    {
        bool active = keyboard.Key == engine.ActiveKeyboardKey && engine.IsConnected(keyboard);
        bool waiting = keyboard.State == KeyboardState.NotSetUp && engine.IsConnected(keyboard);
        string background = active ? "AccentFillColorDefaultBrush"
            : waiting ? "SystemFillColorCautionBackgroundBrush"
            : "ControlFillColorSecondaryBrush";
        string foreground = active ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush";

        return new KeyboardItem
        {
            Key = keyboard.Key,
            Name = Ui.KeyboardName(keyboard),
            Details = Ui.Details(engine, keyboard),
            Layout = Ui.LayoutText(engine, keyboard),
            Status = Ui.StatusText(engine, keyboard),
            StatusBackground = ThemeBrush(background),
            StatusForeground = ThemeBrush(foreground),
            Glyph = Ui.Glyph(keyboard.Connection),
            Opacity = keyboard.State == KeyboardState.Ignored ? 0.6 : 1,
            CanIgnore = keyboard.Connection == KeyboardConnection.RemoteDesktop ? Visibility.Collapsed : Visibility.Visible,
            EditLabel = Loc.Get(keyboard.State == KeyboardState.NotSetUp ? "Btn_SetUp" : "Btn_Edit"),
            IgnoreLabel = Loc.Get(keyboard.State == KeyboardState.Ignored ? "Btn_Unignore" : "Btn_Ignore"),
            ForgetLabel = Loc.Get("Btn_Forget"),
            MoreLabel = Loc.Format("Btn_More", Ui.KeyboardName(keyboard)),
        };
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) ? value as Brush : null;

    /// <summary>In use, then connected (newest first), waiting, disconnected (newest first), ignored.</summary>
    private int Compare(KeyboardRecord a, KeyboardRecord b)
    {
        int rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0)
        {
            return rank;
        }
        if (engine.IsConnected(a) && engine.IsConnected(b))
        {
            return engine.GetConnectionOrder(b).CompareTo(engine.GetConnectionOrder(a));
        }
        return b.LastSeenUtc.CompareTo(a.LastSeenUtc);
    }

    private int Rank(KeyboardRecord keyboard)
    {
        bool connected = engine.IsConnected(keyboard);
        if (keyboard.State == KeyboardState.Ignored)
        {
            return 5;
        }
        if (keyboard.State == KeyboardState.NotSetUp)
        {
            return connected ? 1 : 4;
        }
        if (!connected)
        {
            return 3;
        }
        return keyboard.Key == engine.ActiveKeyboardKey ? 0 : 2;
    }

    private void OnDefaultChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingDefault && DefaultCombo.SelectedItem is ComboBoxItem { Tag: InputProfile profile }
            && !string.Equals(profile.Id, engine.Settings.DefaultLayoutProfileId, StringComparison.OrdinalIgnoreCase))
        {
            engine.SetDefaultLayout(profile);
        }
    }

    private async void OnAddLayouts(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(Ui.LanguageSettingsUri));

    private void OnKeyboardClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is KeyboardItem item)
        {
            App.Current.ShowKeyboardWindow(item.Key);
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            App.Current.ShowKeyboardWindow(key);
        }
    }

    private void OnToggleIgnore(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key } && engine.Find(key) is KeyboardRecord keyboard)
        {
            engine.SetIgnored(key, keyboard.State != KeyboardState.Ignored);
        }
    }

    private async void OnForget(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key } || engine.Find(key) is not KeyboardRecord keyboard)
        {
            return;
        }
        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Forget_Title"),
            Content = Loc.Format("Forget_Confirm", Loc.Name(Ui.KeyboardName(keyboard))),
            PrimaryButtonText = Loc.Get("Btn_Forget"),
            CloseButtonText = Loc.Get("Kbd_Btn_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = Loc.FlowDirection,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            // Looked up again: the keyboard may have changed while the question was open
            if (engine.Find(key) != null)
            {
                engine.Forget(key);
            }
        }
    }

    private void OnSetUpWaiting(object sender, RoutedEventArgs e)
    {
        KeyboardRecord? first = engine.SetUpQueue.FirstOrDefault() ?? engine.AwaitingSetUp.FirstOrDefault();
        if (first != null)
        {
            App.Current.ShowKeyboardWindow(first.Key);
        }
    }

    private async void OnProblemAction(object sender, RoutedEventArgs e)
    {
        Problem? problem = engine.CurrentProblem;
        if (problem == null)
        {
            return;
        }
        if (problem.Kind == ProblemKind.LayoutMissing && problem.Keyboard != null)
        {
            App.Current.ShowKeyboardWindow(problem.Keyboard.Key);
        }
        else if (problem.Kind == ProblemKind.DefaultMissing)
        {
            DefaultCombo.Focus(FocusState.Programmatic);
            DefaultCombo.IsDropDownOpen = true;
        }
        else if (System.IO.File.Exists(Log.FilePath))
        {
            await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(Log.FilePath));
        }
    }
}
