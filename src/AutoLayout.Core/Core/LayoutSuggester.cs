// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoLayout.Core;

/// <summary>A suggested layout for a keyboard, from what the keyboard itself says.</summary>
/// <param name="Klid">The suggested layout.</param>
/// <param name="Profile">The installed profile with that layout, or null when it is not installed.</param>
public sealed record LayoutSuggestion(string Klid, InputProfile? Profile);

/// <summary>
/// Suggests a keyboard's layout only from evidence in the keyboard itself: words in its name
/// and models known by product id. Keyboards rarely tell their layout (most vendors use the same
/// product id for every regional version), so most get no suggestion and the user chooses. No
/// layout is favored: every rule is a row in the tables below, and there is no guess from the
/// Windows region or language. The user always confirms.
/// </summary>
public static class LayoutSuggester
{
    private const int AppleVendorId = 0x05AC;
    private const string JapaneseKlid = "00000411";

    // Words that name a layout in a keyboard's name, one row per layout
    private static readonly (Regex Pattern, string Klid)[] NamePatterns =
    {
        (Pattern(@"\bUS[- ]?Int(?:ernational|l)?\b"), "00020409"),
        (Pattern(@"\bABNT\s*2?\b"), "00010416"),
        (Pattern(@"\b(JP|JIS|Japanese)\b"), JapaneseKlid),
        (Pattern(@"\bKorean\b"), "00000412"),
    };

    // Apple keyboards made with one product id per variant: the JIS (Japanese) ones.
    // Ids as listed in the Linux kernel's drivers/hid/hid-ids.h.
    private static readonly HashSet<int> AppleJis = new()
    {
        0x0216, 0x0219, 0x021C, 0x021F, 0x0222, 0x0225, 0x022B, 0x022E, 0x0232, 0x0238,
        0x023B, 0x0241, 0x0244, 0x0247, 0x024B, 0x024E, 0x0251, 0x0254, 0x0257, 0x025B,
        0x0264, 0x0274, 0x0292,
    };

    public static LayoutSuggestion? Suggest(KeyboardRecord keyboard, IReadOnlyList<InputProfile> installed)
    {
        string name = keyboard.ProductName ?? string.Empty;
        foreach ((Regex pattern, string klid) in NamePatterns)
        {
            if (pattern.IsMatch(name))
            {
                return new LayoutSuggestion(klid, Find(klid, installed));
            }
        }

        if (keyboard.VendorId == AppleVendorId && AppleJis.Contains(keyboard.ProductId))
        {
            return new LayoutSuggestion(JapaneseKlid, Find(JapaneseKlid, installed));
        }
        return null;
    }

    /// <summary>
    /// The installed profile for a layout: a layout with that id, preferably under the language
    /// of its own region, or an input method (IME) of that language.
    /// </summary>
    internal static InputProfile? Find(string klid, IReadOnlyList<InputProfile> installed)
    {
        int ownLanguage = (int)(uint.Parse(klid, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) & 0xFFFF);
        InputProfile? match = null;
        foreach (InputProfile profile in installed)
        {
            if (string.Equals(profile.Klid, klid, StringComparison.OrdinalIgnoreCase))
            {
                if (match == null || profile.LanguageId == ownLanguage)
                {
                    match = profile;
                }
            }
        }
        if (match == null)
        {
            foreach (InputProfile profile in installed)
            {
                if (profile.Klid == null && profile.LanguageId == ownLanguage)
                {
                    return profile;
                }
            }
        }
        return match;
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
