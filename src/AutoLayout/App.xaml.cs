// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using AutoLayout.Core;
using AutoLayout.Platform;
using AutoLayout.Services;
using AutoLayout.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace AutoLayout;

/// <summary>
/// The app: runs in the notification area and opens windows only when the user asks. New
/// keyboards are announced with a notification, so a window never takes the keys of someone
/// who just started typing.
/// </summary>
public partial class App : Application
{
    private const uint AppliedNotificationTimer = 0x300;

    private readonly string[] arguments;
    private readonly AppInstance instance;
    private readonly SettingsStore store = new();
    private readonly Dictionary<string, KeyboardWindow> keyboardWindows = new(StringComparer.Ordinal);

    private DispatcherQueue dispatcher = null!;
    private MessageWindow messageWindow = null!;
    private KeyboardWatcher watcher = null!;
    private TrayIcon tray = null!;
    private Notifier notifier = null!;
    private BacklightKeeper? backlight;
    private MainWindow? mainWindow;
    private LayoutChange? pendingChange;
    private bool exiting;

    public App(string[] arguments, AppInstance instance)
    {
        this.arguments = arguments;
        this.instance = instance;

        // --log: log this run whatever the setting (problems at startup, demo mode)
        Log.Enabled = arguments.Contains("--log");
        DebugSettings.XamlResourceReferenceFailed += (_, e) => Log.Write("XAML resource not found: " + e.Message);
        InitializeComponent();

        // Closing the windows leaves the app running in the tray
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Write("Unhandled error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled error: " + e.ExceptionObject);
    }

    public static new App Current => (App)Application.Current;

    internal Engine Engine { get; private set; } = null!;

    internal bool BacklightAvailable => backlight != null || IsDemo;

    /// <summary>Sample data for screenshots; nothing is read from or applied to the PC.</summary>
    internal bool IsDemo { get; private set; }

    internal MainWindow? MainWindowForCapture => mainWindow;

    internal KeyboardWindow? KeyboardWindowForCapture(string key) => keyboardWindows.GetValueOrDefault(key);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        IsDemo = arguments.Contains(Demo.Argument);
        AppSettings settings = IsDemo ? Demo.Settings() : store.Load();
        Log.Enabled = (settings.LogEnabled && !IsDemo) || arguments.Contains("--log");
        Log.Write("AutoLayout " + typeof(App).Assembly.GetName().Version + " started");

        messageWindow = new MessageWindow();
        watcher = new KeyboardWatcher(messageWindow);
        Engine = IsDemo
            ? new Engine(settings, new Demo.Devices(), new Demo.Inputs(), new Demo.Store())
            : new Engine(settings, new ExternalKeyboards(), new InputProfiles(), store);
        watcher.KeyboardsChanged += (_, _) => Engine.Refresh();
        watcher.LayoutsChanged += (_, _) => Engine.ReloadProfiles();
        Engine.RetryNeeded += (_, _) => watcher.ScheduleKeyboardCheck();
        Engine.Changed += (_, _) => UpdateTray();
        if (!IsDemo)
        {
            // The sample keyboards must not show real notifications on the desktop
            Engine.NewKeyboard += OnNewKeyboard;
            Engine.LayoutApplied += OnLayoutApplied;
            Engine.ProblemOccurred += OnProblem;
        }
        Engine.KeyboardForgotten += (_, key) => CloseKeyboardWindow(key);

        // The icon is shown before the engine starts, so nothing announced at startup is lost
        tray = new TrayIcon(messageWindow) { MenuBuilder = BuildMenu, RightToLeft = Loc.IsRightToLeft };
        tray.Invoked += (_, _) => OnTrayInvoked();
        tray.Update(TrayState.Normal, Loc.Get("App_Name"));

        notifier = new Notifier((title, text, warning, request) => tray.ShowBalloon(title, text, warning, () => Handle(request)));
        notifier.Invoked += (_, request) => dispatcher.TryEnqueue(() => Handle(request));
        instance.Activated += (_, e) => dispatcher.TryEnqueue(() => OnActivated(e));

        if (!IsDemo && ThinkPadBacklight.IsAvailable)
        {
            backlight = new BacklightKeeper(messageWindow, settings.BacklightLevel);
            backlight.LevelChanged += (_, _) =>
            {
                Engine.Settings.BacklightLevel = backlight.Level;
                store.Save(Engine.Settings);
            };
            backlight.Enabled = settings.KeepBacklight;
        }

        Engine.Start();
        UpdateTray();

        int capture = Array.IndexOf(arguments, Capture.Argument);
        if (IsDemo && capture >= 0 && capture + 1 < arguments.Length)
        {
            _ = Capture.RunAsync(this, arguments[capture + 1]);
            return;
        }

        bool firstRun = !settings.FirstRunDone;
        if (firstRun)
        {
            settings.FirstRunDone = true;
            store.Save(settings);
        }

        AppActivationArguments activation = instance.GetActivatedEventArgs();
        bool background = arguments.Contains(StartupRegistration.BackgroundArgument) || activation.Kind == ExtendedActivationKind.StartupTask;
        if (activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs notification
            && Notifier.Parse(notification.Arguments) is NotificationRequest request)
        {
            Handle(request);
        }
        else if (firstRun || !background)
        {
            ShowMainWindow();
        }
    }

    internal void ShowMainWindow(bool settingsPage = false)
    {
        if (exiting)
        {
            return;
        }
        mainWindow ??= new MainWindow(() => exiting);
        mainWindow.Navigate(settingsPage);
        WindowHelper.Bring(mainWindow);
    }

    /// <summary>Opens the set-up window for a new keyboard, or the edit window for a known one.</summary>
    internal void ShowKeyboardWindow(string key, bool focusName = false)
    {
        KeyboardRecord? keyboard = Engine.Find(key);
        if (keyboard == null || exiting)
        {
            return;
        }
        if (!keyboardWindows.TryGetValue(key, out KeyboardWindow? window))
        {
            window = new KeyboardWindow(Engine, keyboard, focusName);
            keyboardWindows[key] = window;
            window.Closed += (_, _) =>
            {
                keyboardWindows.Remove(key);
                if (Engine.Find(key)?.State != KeyboardState.NotSetUp)
                {
                    notifier.RemoveNewKeyboard(key);
                }
                UpdateTray();
                OpenNextInQueue(window.WasNew);
            };
        }
        WindowHelper.Bring(window);
    }

    internal void SetBacklight(bool enabled)
    {
        if (backlight != null)
        {
            backlight.Enabled = enabled;
        }
    }

    internal void SetLogging(bool enabled)
    {
        Engine.Settings.LogEnabled = enabled;
        Log.Enabled = enabled;
        store.Save(Engine.Settings);
    }

    internal void SaveSettings() => store.Save(Engine.Settings);

    internal void Quit()
    {
        exiting = true;
        foreach (KeyboardWindow window in keyboardWindows.Values.ToList())
        {
            window.Close();
        }
        mainWindow?.Close();
        backlight?.Dispose();
        notifier.Dispose();
        tray.Dispose();
        watcher.Dispose();
        messageWindow.Dispose();
        Log.Write("AutoLayout stopped");
        Exit();
    }

    private void OnActivated(AppActivationArguments activation)
    {
        // A second launch, or a notification clicked while the app runs
        if (activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs notification
            && Notifier.Parse(notification.Arguments) is NotificationRequest request)
        {
            Handle(request);
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void Handle(NotificationRequest request)
    {
        switch (request.Action)
        {
            case Notifier.ActionSetUp:
            case Notifier.ActionEdit:
                if (request.Key != null && Engine.Find(request.Key) != null)
                {
                    ShowKeyboardWindow(request.Key);
                }
                else
                {
                    ShowMainWindow();
                }
                break;
            default:
                ShowMainWindow();
                break;
        }
    }

    private void OnTrayInvoked()
    {
        KeyboardRecord? waiting = Engine.SetUpQueue.FirstOrDefault() ?? Engine.AwaitingSetUp.FirstOrDefault();
        if (waiting != null)
        {
            ShowKeyboardWindow(waiting.Key);
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OpenNextInQueue(bool afterNew)
    {
        // Several new keyboards: one window at a time
        if (afterNew && !exiting && keyboardWindows.Count == 0 && Engine.SetUpQueue.FirstOrDefault() is KeyboardRecord next)
        {
            ShowKeyboardWindow(next.Key);
        }
    }

    private void CloseKeyboardWindow(string key)
    {
        if (keyboardWindows.TryGetValue(key, out KeyboardWindow? window))
        {
            window.Close();
        }
        notifier.RemoveNewKeyboard(key);
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        List<TrayMenuItem> items = new()
        {
            new TrayMenuItem(Loc.Format("Menu_Header", CurrentLayoutName()), () => ShowMainWindow()),
        };
        bool anyWaiting = false;
        foreach (KeyboardRecord keyboard in Engine.AwaitingSetUp)
        {
            string key = keyboard.Key;
            items.Add(new TrayMenuItem(Loc.Format("Menu_SetUp", Loc.Name(Ui.KeyboardName(keyboard))), () => ShowKeyboardWindow(key), !anyWaiting));
            anyWaiting = true;
        }
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Loc.Get("Menu_Keyboards"), () => ShowMainWindow(), !anyWaiting));
        items.Add(new TrayMenuItem(Loc.Get("Menu_Settings"), () => ShowMainWindow(settingsPage: true)));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(Loc.Get("Menu_Exit"), Quit));
        return items;
    }

    private void UpdateTray()
    {
        if (exiting)
        {
            return;
        }
        if (Engine.CurrentProblem != null)
        {
            tray.Update(TrayState.Error, Loc.Get("Tray_Tip_Error"));
        }
        else if (Engine.SetUpQueue.Count > 0)
        {
            tray.Update(TrayState.Attention, Loc.Get("Tray_Tip_Attention"));
        }
        else
        {
            tray.Update(TrayState.Normal, Loc.Format("Tray_Tip", Loc.Name(CurrentLayoutName(), 80)));
        }
    }

    internal string CurrentLayoutName()
    {
        if (Engine.IsRemoteAutomatic)
        {
            return Loc.Get("Layout_Automatic");
        }
        InputProfile? profile = Engine.AppliedProfile ?? Engine.DefaultProfile;
        return profile != null ? Ui.ProfileName(Engine, profile) : Loc.Get("Layout_None");
    }

    private void OnNewKeyboard(object? sender, KeyboardRecord keyboard)
    {
        // Only evidence from the keyboard itself (its name, a known model) is worth a "looks like".
        // A guess from the Windows region is offered in the window, where it is explained.
        LayoutSuggestion? suggestion = Engine.GetSuggestion(keyboard);
        string text = suggestion?.Profile != null && suggestion.Confidence == SuggestionConfidence.High
            ? Loc.Format("Toast_New_TextSuggested", Loc.Name(Ui.ProfileName(Engine, suggestion.Profile), 60))
            : Loc.Get("Toast_New_Text");
        notifier.ShowNewKeyboard(keyboard, Loc.Format("Toast_New_Title", Loc.Name(Ui.KeyboardName(keyboard), 40)), text, Loc.Get("Toast_New_Button"));
    }

    private void OnLayoutApplied(object? sender, LayoutChange change)
    {
        if (!Engine.Settings.NotifyOnSwitch)
        {
            return;
        }
        // Several switches within a second (one keyboard out, another in) make one notification
        pendingChange = change;
        messageWindow.StartTimer(1000, ShowPendingChange, AppliedNotificationTimer);
    }

    private void ShowPendingChange()
    {
        messageWindow.StopTimer(AppliedNotificationTimer);
        LayoutChange? change = pendingChange;
        pendingChange = null;
        if (change == null || exiting)
        {
            return;
        }

        string text;
        if (change.Keyboard?.Connection == KeyboardConnection.RemoteDesktop)
        {
            text = Loc.Get("Toast_Remote_Text");
        }
        else if (change.Keyboard != null)
        {
            text = Loc.Format("Toast_Applied_Text", Loc.Name(Ui.KeyboardName(change.Keyboard), 40));
        }
        else if (change.Disconnected != null)
        {
            text = Loc.Format("Toast_Reverted_Text", Loc.Name(Ui.KeyboardName(change.Disconnected), 40));
        }
        else
        {
            text = Loc.Get("Toast_Default_Text");
        }
        notifier.ShowLayoutChange(TextSanitizer.Shorten(Ui.ProfileName(Engine, change.Layout), 60), text);
    }

    private void OnProblem(object? sender, Problem problem)
    {
        string layout = Loc.Name(problem.LayoutName, 60);
        switch (problem.Kind)
        {
            case ProblemKind.LayoutMissing when problem.Keyboard != null:
                notifier.ShowProblem(Loc.Get("Toast_Err_Missing_Title"),
                    Loc.Format("Toast_Err_Missing", Loc.Name(Ui.KeyboardName(problem.Keyboard), 40), layout), Notifier.ActionEdit, problem.Keyboard.Key);
                break;
            case ProblemKind.DefaultMissing:
                notifier.ShowProblem(Loc.Get("Toast_Err_Missing_Title"), Loc.Format("Toast_Err_DefaultMissing", layout), Notifier.ActionDefault, null);
                break;
            default:
                notifier.ShowProblem(Loc.Get("Toast_Err_Apply_Title"), Loc.Format("Toast_Err_Apply", layout), Notifier.ActionMain, null);
                break;
        }
    }
}
