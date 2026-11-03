// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using AutoLayout.Core;
using AutoLayout.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AutoLayout.Views;

/// <summary>
/// Sets up a new keyboard or edits a known one. Nothing changes until Save. The record is
/// looked up by key on every action, so a keyboard forgotten meanwhile is never acted on.
/// </summary>
public sealed partial class KeyboardWindow : Window
{
    private const string AutomaticTag = "auto";

    private readonly Engine engine;
    private readonly string key;
    private readonly bool isRemote;

    internal KeyboardWindow(Engine engine, KeyboardRecord keyboard, bool focusName)
    {
        this.engine = engine;
        key = keyboard.Key;
        isRemote = keyboard.Connection == KeyboardConnection.RemoteDesktop;
        WasNew = keyboard.State == KeyboardState.NotSetUp;

        InitializeComponent();
        Root.FlowDirection = Loc.FlowDirection;
        if (App.Current.IsDemo)
        {
            // Screenshots can't capture Mica
            Root.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        }

        string title = isRemote ? Loc.Get("Conn_RemoteDesktop") : Loc.Get(WasNew ? "Kbd_Title_New" : "Kbd_Title_Edit");
        TitleText.Text = title;
        WindowHelper.Setup(this, TitleBar, title, WindowWidth, WasNew ? 600 : 560);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        HeadingText.Text = isRemote ? Loc.Get("Kbd_Heading_Remote")
            : WasNew ? Loc.Get("Kbd_Heading_New")
            : Ui.KeyboardName(keyboard);
        IntroText.Text = isRemote ? Loc.Get("Kbd_Hint_Remote") : WasNew ? Loc.Get("Kbd_Intro_New") : string.Empty;
        IntroText.Visibility = IntroText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsText.Text = Details(keyboard);
        DetailsText.Visibility = isRemote ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = Ui.KeyboardName(keyboard);
        // The Remote Desktop entry is not a device: nothing to name
        NameBox.Visibility = isRemote ? Visibility.Collapsed : Visibility.Visible;

        NotKeyboardButton.Visibility = WasNew && !isRemote ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(NotKeyboardButton, Loc.Get("Kbd_NotKeyboard_Tip"));
        IgnoreCheck.Visibility = !WasNew && !isRemote ? Visibility.Visible : Visibility.Collapsed;
        IgnoreCheck.IsChecked = keyboard.State == KeyboardState.Ignored;
        CancelButton.Content = Loc.Get(WasNew ? "Kbd_Btn_Later" : "Kbd_Btn_Cancel");

        FillLayouts();
        Activated += (_, args) =>
        {
            // Layouts added in Windows Settings show up when coming back here
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                engine.ReloadProfiles();
                FillLayouts();
            }
        };
        Root.KeyDown += OnKeyDown;
        Root.Loaded += (_, _) =>
        {
            FitHeight();
            if (!focusName)
            {
                // Choosing the layout is the task; the name is optional
                LayoutCombo.Focus(FocusState.Programmatic);
            }
        };
        Closed += (_, _) =>
        {
            if (WasNew && engine.Find(key)?.State == KeyboardState.NotSetUp)
            {
                // "Not now": stop asking until it is connected again
                engine.Postpone(key);
            }
        };

        if (focusName)
        {
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
        }
    }

    /// <summary>Opened for a keyboard that was not set up.</summary>
    public bool WasNew { get; }

    /// <summary>520 logical pixels, wider when Windows text is larger (up to 1.5 times).</summary>
    private static int WindowWidth => (int)(520 * Math.Clamp(new Windows.UI.ViewManagement.UISettings().TextScaleFactor, 1.0, 1.5));

    /// <summary>Makes the window as tall as its content (longer in some languages), within the screen.</summary>
    private void FitHeight()
    {
        int width = WindowWidth;
        Root.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        int height = (int)Math.Ceiling(Root.DesiredSize.Height) + 2;
        WindowHelper.Resize(this, width, Math.Clamp(height, 360, 760));
        WindowHelper.Center(this);
    }

    private string Details(KeyboardRecord keyboard)
    {
        string connection = Ui.ConnectionName(keyboard.Connection);
        // "USB Keyboard · USB" says USB twice: the product name goes when it only repeats the connection
        bool repeats = keyboard.ProductName?.Contains(connection, StringComparison.CurrentCultureIgnoreCase) == true;
        string details = keyboard.ProductName != null && !repeats
            ? Loc.Format("Kbd_Details", Loc.Name(keyboard.ProductName), connection, keyboard.VidPid)
            : Loc.Format("Kbd_Details_NoProduct", connection, keyboard.VidPid);

        // Several new keyboards wait: say which one this is
        int position = engine.SetUpQueue.ToList().FindIndex(r => r.Key == key);
        int total = engine.SetUpQueue.Count;
        return WasNew && total > 1 && position >= 0 ? Loc.Format("Kbd_Queue", details, position + 1, total) : details;
    }

    private void FillLayouts()
    {
        KeyboardRecord? keyboard = engine.Find(key);
        if (keyboard == null)
        {
            Close();
            return;
        }

        string? previous = (LayoutCombo.SelectedItem as ComboBoxItem)?.Tag switch
        {
            InputProfile profile => profile.Id,
            string tag when tag == AutomaticTag => SettingsStore.AutomaticProfileId,
            _ => null,
        };
        LayoutSuggestion? suggestion = WasNew ? engine.GetSuggestion(keyboard) : null;
        string? selected = previous ?? keyboard.LayoutProfileId ?? suggestion?.Profile?.Id;

        LayoutCombo.SelectionChanged -= OnLayoutChanged;
        LayoutCombo.Items.Clear();
        ComboBoxItem? selectedItem = null;
        if (isRemote)
        {
            ComboBoxItem automatic = new() { Content = Loc.Get("Layout_Automatic"), Tag = AutomaticTag };
            LayoutCombo.Items.Add(automatic);
            if (selected == SettingsStore.AutomaticProfileId)
            {
                selectedItem = automatic;
            }
        }
        foreach (InputProfile profile in engine.InstalledProfiles.OrderBy(p => Ui.ProfileName(engine, p), StringComparer.CurrentCultureIgnoreCase))
        {
            string text = Ui.ProfileName(engine, profile);
            if (suggestion?.Profile?.Id == profile.Id)
            {
                text = Loc.Format("Kbd_Suggested", text);
            }
            ComboBoxItem item = new() { Content = text, Tag = profile };
            LayoutCombo.Items.Add(item);
            if (string.Equals(profile.Id, selected, StringComparison.OrdinalIgnoreCase))
            {
                selectedItem = item;
            }
        }
        if (selectedItem == null && keyboard.LayoutProfileId != null && keyboard.LayoutProfileId != SettingsStore.AutomaticProfileId)
        {
            // The stored layout was uninstalled: show it, but it can't be chosen again
            selectedItem = new ComboBoxItem { Content = Ui.LayoutText(engine, keyboard), IsEnabled = false };
            LayoutCombo.Items.Insert(0, selectedItem);
        }
        LayoutCombo.PlaceholderText = Loc.Get("Kbd_Layout_Placeholder");
        LayoutCombo.SelectedItem = selectedItem;
        LayoutCombo.SelectionChanged += OnLayoutChanged;

        string? hint = null;
        if (isRemote && ExternalRemoteLayoutIgnored)
        {
            hint = Loc.Get("Kbd_Hint_RemoteIgnored");
        }
        else if (suggestion != null && suggestion.Profile == null)
        {
            hint = Loc.Format("Kbd_Hint_NotInstalled", Loc.Name(InputProfiles.GetLayoutName(suggestion.Klid) ?? suggestion.Klid, 60));
        }
        else if (suggestion != null)
        {
            hint = Loc.Get(suggestion.Confidence == SuggestionConfidence.High ? "Kbd_Hint_High" : "Kbd_Hint_Low");
        }
        HintText.Text = hint ?? string.Empty;
        HintText.Visibility = hint != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateSave();
    }

    private static bool ExternalRemoteLayoutIgnored
    {
        get
        {
            using Microsoft.Win32.RegistryKey? layouts = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout");
            return layouts?.GetValue("IgnoreRemoteKeyboardLayout") is int value && value != 0;
        }
    }

    private void UpdateSave()
    {
        object? tag = (LayoutCombo.SelectedItem as ComboBoxItem)?.Tag;
        bool ignore = IgnoreCheck.IsChecked == true;
        LayoutCombo.IsEnabled = !ignore;
        SaveButton.IsEnabled = ignore || tag is InputProfile || (tag is string t && t == AutomaticTag)
            || (!WasNew && LayoutCombo.SelectedItem is ComboBoxItem { IsEnabled: false });
    }

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e) => UpdateSave();

    private void OnIgnoreChanged(object sender, RoutedEventArgs e) => UpdateSave();

    private async void OnMissingLayout(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(Ui.LanguageSettingsUri));

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (engine.Find(key) is not KeyboardRecord keyboard)
        {
            Close();
            return;
        }

        string name = NameBox.Text;
        if (isRemote && name == Loc.Get("Conn_RemoteDesktop"))
        {
            name = string.Empty;
        }
        object? tag = (LayoutCombo.SelectedItem as ComboBoxItem)?.Tag;

        if (IgnoreCheck.IsChecked == true)
        {
            engine.Rename(key, name);
            engine.SetIgnored(key, true);
        }
        else if (tag is string automatic && automatic == AutomaticTag)
        {
            engine.SetRemoteAutomatic(name);
        }
        else if (tag is InputProfile profile)
        {
            if (keyboard.State == KeyboardState.Configured && profile.Id == keyboard.LayoutProfileId)
            {
                engine.Rename(key, name);
            }
            else
            {
                engine.Configure(key, name, profile);
            }
        }
        else
        {
            // The stored layout is not installed: keep it, rename only
            engine.Rename(key, name);
            if (keyboard.State == KeyboardState.Ignored)
            {
                engine.SetIgnored(key, false);
            }
        }
        Close();
    }

    private void OnNotKeyboard(object sender, RoutedEventArgs e)
    {
        engine.SetIgnored(key, true);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !LayoutCombo.IsDropDownOpen)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == VirtualKey.Enter && SaveButton.IsEnabled && e.OriginalSource is not Button && !LayoutCombo.IsDropDownOpen)
        {
            e.Handled = true;
            OnSave(sender, e);
        }
    }
}
