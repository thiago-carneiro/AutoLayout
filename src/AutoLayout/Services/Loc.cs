// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using AutoLayout.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AutoLayout.Services;

/// <summary>
/// Localized text from Strings\&lt;language&gt;\Resources.resw. Windows picks the language from
/// the user's language list, falling back to English.
/// </summary>
internal static class Loc
{
    // Unicode first-strong isolate / pop: keeps a name in another script from reordering the sentence
    private const char Isolate = '⁨';
    private const char PopIsolate = '⁩';

    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        try
        {
            string text = Loader.GetString(key);
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // A missing key is a bug; show it rather than crash
            return key;
        }
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>A name for use inside a sentence: shortened and isolated for right-to-left text.</summary>
    public static string Name(string? name, int maxLength = 48) =>
        Isolate + TextSanitizer.Shorten(name ?? string.Empty, maxLength) + PopIsolate;

    public static bool IsRightToLeft => Get("App_FlowDirection") == "RightToLeft";

    public static FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
