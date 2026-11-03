// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using AutoLayout.Core;

namespace AutoLayout.Services;

/// <summary>How keyboards are named and described in the UI.</summary>
internal static class Ui
{
    public const string GlyphKeyboard = "";
    public const string GlyphUsb = "";
    public const string GlyphBluetooth = "";
    public const string GlyphRemote = "";

    public const string LanguageSettingsUri = "ms-settings:regionlanguage";
    public const string ProjectUri = "https://github.com/thiago-carneiro/AutoLayout";

    /// <summary>The user's name, the device's name or VID:PID; Remote Desktop gets a localized name.</summary>
    public static string KeyboardName(KeyboardRecord keyboard) =>
        keyboard.Connection == KeyboardConnection.RemoteDesktop && keyboard.FriendlyName == null
            ? Loc.Get("Conn_RemoteDesktop")
            : keyboard.DisplayName;

    public static string ConnectionName(KeyboardConnection connection) => connection switch
    {
        KeyboardConnection.Bluetooth => Loc.Get("Conn_Bluetooth"),
        KeyboardConnection.RemoteDesktop => Loc.Get("Conn_RemoteDesktop"),
        _ => Loc.Get("Conn_Usb"),
    };

    public static string Glyph(KeyboardConnection connection) => connection switch
    {
        KeyboardConnection.Bluetooth => GlyphBluetooth,
        KeyboardConnection.RemoteDesktop => GlyphRemote,
        _ => GlyphUsb,
    };

    /// <summary>
    /// The layout's name, with its language only when needed to tell apart the same layout
    /// installed under two languages.
    /// </summary>
    public static string ProfileName(Engine engine, InputProfile profile)
    {
        int sameName = 0;
        foreach (InputProfile other in engine.InstalledProfiles)
        {
            if (string.Equals(other.LayoutName, profile.LayoutName, StringComparison.CurrentCultureIgnoreCase))
            {
                sameName++;
            }
        }
        if (sameName < 2 || profile.LanguageName == null)
        {
            return profile.LayoutName;
        }
        string language = profile.LanguageName.Length > 0
            ? char.ToUpper(profile.LanguageName[0], CultureInfo.CurrentCulture) + profile.LanguageName.Substring(1)
            : profile.LanguageName;
        return Loc.Format("Layout_WithLanguage", profile.LayoutName, language);
    }

    /// <summary>The layout of a keyboard as shown in the list, or "—".</summary>
    public static string LayoutText(Engine engine, KeyboardRecord keyboard)
    {
        if (keyboard.State != KeyboardState.Configured || keyboard.LayoutProfileId == null)
        {
            return Loc.Get("Layout_None");
        }
        if (keyboard.LayoutProfileId == SettingsStore.AutomaticProfileId)
        {
            return Loc.Get("Layout_Automatic");
        }
        InputProfile? profile = engine.FindProfile(keyboard.LayoutProfileId);
        if (profile != null)
        {
            return ProfileName(engine, profile);
        }
        string name = (keyboard.LayoutKlid != null ? InputProfiles.GetLayoutName(keyboard.LayoutKlid) : null) ?? keyboard.LayoutProfileId;
        return Loc.Format("Layout_NotInstalled", name);
    }

    public static string StatusText(Engine engine, KeyboardRecord keyboard)
    {
        bool connected = engine.IsConnected(keyboard);
        return keyboard.State switch
        {
            KeyboardState.Ignored => Loc.Get("Status_Ignored"),
            KeyboardState.NotSetUp => Loc.Get(connected ? "Status_NotSetUpConnected" : "Status_NotSetUp"),
            _ when !connected => Loc.Get("Status_Disconnected"),
            _ => Loc.Get(keyboard.Key == engine.ActiveKeyboardKey ? "Status_Active" : "Status_Connected"),
        };
    }

    /// <summary>"Bluetooth · 046D:B342 · Last connected 11/2/2026 8:41 PM".</summary>
    public static string Details(Engine engine, KeyboardRecord keyboard)
    {
        string connection = ConnectionName(keyboard.Connection);
        if (keyboard.Connection == KeyboardConnection.RemoteDesktop)
        {
            return connection;
        }
        string seen = engine.IsConnected(keyboard)
            ? Loc.Get("LastSeen_Now")
            : Loc.Format("LastSeen_At", keyboard.LastSeenUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        return Loc.Format("Kbd_ListDetails", connection, keyboard.VidPid, seen);
    }
}
