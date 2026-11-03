// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using AutoLayout.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AutoLayout.Services;

/// <summary>What a click on a notification asks for.</summary>
/// <param name="Action">"setup", "edit", "main" or "default".</param>
/// <param name="Key">The keyboard concerned, if any.</param>
internal sealed record NotificationRequest(string Action, string? Key);

/// <summary>
/// Windows notifications. Each kind has its own tag, so a layout switch never replaces a
/// question about a new keyboard, and a newer switch replaces the previous one. Where Windows
/// App SDK notifications are not available (the app runs unpackaged), the tray icon's own
/// notifications are used instead.
/// </summary>
internal sealed partial class Notifier : IDisposable
{
    public const string ActionSetUp = "setup";
    public const string ActionEdit = "edit";
    public const string ActionMain = "main";
    public const string ActionDefault = "default";

    private const string Group = "autolayout";

    private readonly bool supported;
    private readonly Action<string, string, bool, NotificationRequest> fallback;

    /// <param name="fallback">Shows a notification another way: title, text, warning, request on click.</param>
    public Notifier(Action<string, string, bool, NotificationRequest> fallback)
    {
        this.fallback = fallback;
        try
        {
            supported = AppNotificationManager.IsSupported();
            if (supported)
            {
                AppNotificationManager.Default.NotificationInvoked += OnInvoked;
                AppNotificationManager.Default.Register();
            }
        }
        catch (COMException error)
        {
            supported = false;
            Log.Write("Windows App SDK notifications are not available (0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture)
                + "); using the tray icon's notifications");
        }
    }

    /// <summary>A notification was clicked while the app runs (raised on a background thread).</summary>
    public event EventHandler<NotificationRequest>? Invoked;

    public static NotificationRequest? Parse(IDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("action", out string? action))
        {
            return null;
        }
        arguments.TryGetValue("key", out string? key);
        return new NotificationRequest(action, key);
    }

    public void ShowNewKeyboard(KeyboardRecord keyboard, string title, string text, string buttonText) =>
        Show("new-" + keyboard.Key, title, text, new NotificationRequest(ActionSetUp, keyboard.Key), buttonText, false);

    public void ShowLayoutChange(string title, string text) =>
        Show("layout", title, text, new NotificationRequest(ActionMain, null), null, false);

    public void ShowProblem(string title, string text, string action, string? key) =>
        Show("problem", title, text, new NotificationRequest(action, key), null, true);

    /// <summary>Removes the question about a keyboard once it is answered.</summary>
    public void RemoveNewKeyboard(string key)
    {
        if (supported)
        {
            _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync("new-" + key, Group);
        }
    }

    public void Dispose()
    {
        if (supported)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        }
    }

    private void Show(string tag, string title, string text, NotificationRequest request, string? buttonText, bool warning)
    {
        if (!supported)
        {
            fallback(title, text, warning, request);
            return;
        }
        try
        {
            AppNotificationBuilder builder = new AppNotificationBuilder()
                .AddArgument("action", request.Action)
                .AddText(title)
                .AddText(text)
                .SetTag(tag)
                .SetGroup(Group);
            if (request.Key != null)
            {
                builder.AddArgument("key", request.Key);
            }
            if (buttonText != null)
            {
                AppNotificationButton button = new AppNotificationButton(buttonText).AddArgument("action", request.Action);
                if (request.Key != null)
                {
                    button.AddArgument("key", request.Key);
                }
                builder.AddButton(button);
            }
            if (tag == "layout")
            {
                // Layout changes can come several times a day: shown, not heard
                builder.MuteAudio();
            }
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (COMException error)
        {
            Log.Write("Notification could not be shown (0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture) + ")");
            fallback(title, text, warning, request);
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationRequest? request = Parse(args.Arguments);
        if (request != null)
        {
            Invoked?.Invoke(this, request);
        }
    }
}
