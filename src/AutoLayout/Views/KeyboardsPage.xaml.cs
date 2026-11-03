// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoLayout.Core;
using AutoLayout.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    public Thickness StatusPadding { get; init; }
    public Brush? NameForeground { get; init; }
    public string Glyph { get; init; } = string.Empty;
    public Visibility LayoutVisibility { get; init; }
    public Visibility SetUpVisibility { get; init; }
    public string SetUpLabel { get; init; } = string.Empty;
    public Visibility CanIgnore { get; init; }
    public Visibility CanForget { get; init; }
    public string IgnoreGlyph { get; init; } = string.Empty;
    public string EditLabel { get; init; } = string.Empty;
    public string IgnoreLabel { get; init; } = string.Empty;
    public string ForgetLabel { get; init; } = string.Empty;
    public string MoreLabel { get; init; } = string.Empty;

    /// <summary>What screen readers say for the row.</summary>
    public override string ToString() =>
        string.Join(", ", new[] { Name, Layout, Status }.Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>What applies now, the default layout, and every keyboard seen. Changes apply at once.</summary>
public sealed partial class KeyboardsPage : Page
{
    private readonly Engine engine = App.Current.Engine;
    private readonly ObservableCollection<KeyboardItem> items = new();
    private bool updatingDefault;
    private bool updatingRemote;

    public KeyboardsPage()
    {
        InitializeComponent();
        KeyboardList.ItemsSource = items;
        RemoteSectionText.Text = Loc.Get("Conn_RemoteDesktop");
        RemoteHeaderText.Text = Loc.Get("Kbd_Heading_Remote");
        RemoteDescriptionText.Text = Loc.Get("Kbd_Hint_Remote");
        AutomationProperties.SetName(RemoteCombo, Loc.Get("Kbd_Heading_Remote"));

        // The status colors are looked up in code: look them up again for the other theme
        ActualThemeChanged += (_, _) => RefreshList();
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
        RefreshRemote();
    }

    /// <summary>"Automatic (the other computer's layout)" and the installed layouts.</summary>
    private void RefreshRemote()
    {
        updatingRemote = true;
        try
        {
            RemoteCombo.Items.Clear();
            KeyboardRecord? remote = engine.Find(ExternalKeyboards.RemoteDesktopKey);
            string selected = remote?.LayoutProfileId ?? SettingsStore.AutomaticProfileId;

            ComboBoxItem automatic = new() { Content = Loc.Get("Layout_Automatic"), Tag = SettingsStore.AutomaticProfileId };
            RemoteCombo.Items.Add(automatic);
            RemoteCombo.SelectedItem = selected == SettingsStore.AutomaticProfileId ? automatic : null;
            foreach (InputProfile profile in engine.InstalledProfiles.OrderBy(p => Ui.ProfileName(engine, p), StringComparer.CurrentCultureIgnoreCase))
            {
                ComboBoxItem item = new() { Content = Ui.ProfileName(engine, profile), Tag = profile };
                RemoteCombo.Items.Add(item);
                if (string.Equals(profile.Id, selected, StringComparison.OrdinalIgnoreCase))
                {
                    RemoteCombo.SelectedItem = item;
                }
            }
            if (RemoteCombo.SelectedItem == null && remote != null)
            {
                // The chosen layout was uninstalled: show it, but it can't be chosen again
                ComboBoxItem missing = new() { Content = Ui.LayoutText(engine, remote), IsEnabled = false };
                RemoteCombo.Items.Insert(1, missing);
                RemoteCombo.SelectedItem = missing;
            }
        }
        finally
        {
            updatingRemote = false;
        }
    }

    private void OnRemoteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingRemote || RemoteCombo.SelectedItem is not ComboBoxItem { IsEnabled: true } item)
        {
            return;
        }
        InputProfile? profile = item.Tag as InputProfile;
        string chosen = profile?.Id ?? SettingsStore.AutomaticProfileId;
        string current = engine.Find(ExternalKeyboards.RemoteDesktopKey)?.LayoutProfileId ?? SettingsStore.AutomaticProfileId;
        if (!string.Equals(chosen, current, StringComparison.OrdinalIgnoreCase))
        {
            engine.SetRemoteLayout(profile);
        }
    }

    private void RefreshStatus()
    {
        string layout = App.Current.CurrentLayoutName();
        if (CurrentLayoutText.Text != layout)
        {
            CurrentLayoutText.Text = layout;
            // Screen readers announce the change (live region)
            FrameworkElementAutomationPeer.FromElement(CurrentLayoutText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        KeyboardRecord? active = engine.ActiveKeyboardKey != null ? engine.Find(engine.ActiveKeyboardKey) : null;
        Problem? problem = engine.CurrentProblem;
        if (active != null && problem?.Kind == ProblemKind.LayoutMissing && problem.Keyboard?.Key == active.Key)
        {
            // Its layout is gone, so the default is what is in use
            ReasonText.Text = Loc.Format("Main_Now_Fallback", Loc.Name(Ui.KeyboardName(active)));
        }
        else if (engine.IsRemoteAutomatic)
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
        // Remote Desktop has its own section below the list
        List<KeyboardRecord> keyboards = engine.Keyboards.Where(k => k.Connection != KeyboardConnection.RemoteDesktop).ToList();
        keyboards.Sort(Compare);

        // Rebuilding the rows takes the focus away from them: give it back to the same row
        (string? focusedKey, FocusState focusState) = FocusedRow();

        items.Clear();
        foreach (KeyboardRecord keyboard in keyboards)
        {
            items.Add(ToItem(keyboard));
        }
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyboardList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (focusedKey != null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                KeyboardItem? row = items.FirstOrDefault(i => i.Key == focusedKey);
                if (row != null && KeyboardList.ContainerFromItem(row) is UIElement container)
                {
                    container.Focus(focusState);
                }
                else if (items.Count > 0)
                {
                    // The row is gone (forgotten): stay in the list
                    KeyboardList.Focus(focusState);
                }
            });
        }
    }

    /// <summary>The keyboard row that has the focus (the row or a control in it), and how it got it.</summary>
    private (string? Key, FocusState State) FocusedRow()
    {
        if (XamlRoot == null || FocusManager.GetFocusedElement(XamlRoot) is not UIElement focused)
        {
            return (null, FocusState.Unfocused);
        }
        for (DependencyObject? element = focused; element != null && element != KeyboardList; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { DataContext: KeyboardItem item })
            {
                return (item.Key, focused.FocusState);
            }
        }
        return (null, FocusState.Unfocused);
    }

    private KeyboardItem ToItem(KeyboardRecord keyboard)
    {
        bool active = keyboard.Key == engine.ActiveKeyboardKey && engine.IsConnected(keyboard);
        bool waiting = keyboard.State == KeyboardState.NotSetUp && engine.IsConnected(keyboard);
        bool remote = keyboard.Connection == KeyboardConnection.RemoteDesktop;
        bool ignored = keyboard.State == KeyboardState.Ignored;
        string layout = Ui.LayoutText(engine, keyboard);

        // Only the states that ask for attention get a colored pill; the others are plain text
        (string? background, string foreground) = active ? ("AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush")
            : waiting ? ("SystemFillColorCautionBackgroundBrush", "TextFillColorPrimaryBrush")
            : (null, "TextFillColorSecondaryBrush");

        return new KeyboardItem
        {
            Key = keyboard.Key,
            Name = Ui.KeyboardName(keyboard),
            Details = Ui.Details(engine, keyboard),
            Layout = layout,
            Status = Ui.StatusText(engine, keyboard),
            StatusBackground = background != null ? ThemeBrush(background) : null,
            StatusForeground = ThemeBrush(foreground),
            StatusPadding = background != null ? new Thickness(8, 1, 8, 1) : new Thickness(0),
            NameForeground = ThemeBrush(ignored ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"),
            Glyph = Ui.Glyph(keyboard.Connection),
            LayoutVisibility = layout.Length > 0 && !waiting ? Visibility.Visible : Visibility.Collapsed,
            SetUpVisibility = waiting ? Visibility.Visible : Visibility.Collapsed,
            SetUpLabel = Loc.Get("Btn_SetUp"),
            CanIgnore = remote ? Visibility.Collapsed : Visibility.Visible,
            // Forgetting the Remote Desktop entry would only reset it to automatic
            CanForget = remote ? Visibility.Collapsed : Visibility.Visible,
            IgnoreGlyph = ignored ? "" : "",
            EditLabel = Loc.Get(keyboard.State == KeyboardState.NotSetUp ? "Btn_SetUp" : "Btn_Edit"),
            IgnoreLabel = Loc.Get(ignored ? "Btn_Unignore" : "Btn_Ignore"),
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

    /// <summary>The "…" button opens the card's context menu.</summary>
    private void OnMore(object sender, RoutedEventArgs e)
    {
        DependencyObject? element = sender as DependencyObject;
        while (element != null && (element as UIElement)?.ContextFlyout == null)
        {
            element = VisualTreeHelper.GetParent(element);
        }
        if (element is UIElement { ContextFlyout: FlyoutBase menu } && sender is FrameworkElement button)
        {
            menu.ShowAt(button);
        }
    }

    private void OnSetUpRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            App.Current.ShowKeyboardWindow(key);
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
            // Connected, it goes back to waiting at once; otherwise the question comes next time
            Content = Loc.Format(engine.IsConnected(keyboard) ? "Forget_Confirm_Connected" : "Forget_Confirm", Loc.Name(Ui.KeyboardName(keyboard))),
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
        if (problem.Kind == ProblemKind.LayoutMissing && problem.Keyboard?.Connection == KeyboardConnection.RemoteDesktop)
        {
            RemoteCombo.Focus(FocusState.Programmatic);
            RemoteCombo.IsDropDownOpen = true;
        }
        else if (problem.Kind == ProblemKind.LayoutMissing && problem.Keyboard != null)
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
