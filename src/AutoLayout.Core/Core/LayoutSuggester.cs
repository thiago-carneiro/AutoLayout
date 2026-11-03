// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoLayout.Core;

public enum SuggestionConfidence
{
    /// <summary>A guess from the Windows region: the keyboard says nothing about its layout.</summary>
    Low,

    /// <summary>The keyboard's name or model tells its layout.</summary>
    High,
}

/// <summary>A suggested layout for a keyboard.</summary>
/// <param name="Klid">The suggested layout.</param>
/// <param name="Profile">The installed profile with that layout, or null when it is not installed.</param>
/// <param name="Confidence">How sure the suggestion is.</param>
public sealed record LayoutSuggestion(string Klid, InputProfile? Profile, SuggestionConfidence Confidence);

/// <summary>
/// Suggests a keyboard's layout. Keyboards rarely tell their layout (most vendors use the
/// same product id for every regional version), so this only uses what is reliable: the
/// product name, a few models known by product id, and the Windows region as a weak guess.
/// The user always confirms.
/// </summary>
public static class LayoutSuggester
{
    private const int AppleVendorId = 0x05AC;
    private const string Japanese = "00000411";
    private const string Korean = "00000412";
    private const string Abnt2 = "00010416";

    // Words some keyboards put in their name
    private static readonly (Regex Pattern, string Klid)[] NamePatterns =
    {
        (new Regex(@"\bABNT\s*2?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), Abnt2),
        (new Regex(@"\b(JP|JIS|Japanese)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), Japanese),
        (new Regex(@"\bKorean\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), Korean),
    };

    // Apple keyboards made with one product id per variant: the JIS (Japanese) ones.
    // Ids as listed in the Linux kernel's drivers/hid/hid-ids.h.
    private static readonly HashSet<int> AppleJis = new()
    {
        0x0216, 0x0219, 0x021C, 0x021F, 0x0222, 0x0225, 0x022B, 0x022E, 0x0232, 0x0238,
        0x023B, 0x0241, 0x0244, 0x0247, 0x024B, 0x024E, 0x0251, 0x0254, 0x0257, 0x025B,
        0x0264, 0x0274, 0x0292,
    };

    // Native keyboard layout of some regions, used as a weak guess
    private static readonly Dictionary<string, string> RegionLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BR"] = Abnt2, ["PT"] = "00000816", ["US"] = "00000409", ["GB"] = "00000809", ["IE"] = "00001809",
        ["DE"] = "00000407", ["AT"] = "00000407", ["CH"] = "00000807", ["FR"] = "0000040C", ["BE"] = "0000080C",
        ["ES"] = "0000040A", ["MX"] = "0000080A", ["AR"] = "0000080A", ["CO"] = "0000080A", ["CL"] = "0000080A",
        ["IT"] = "00000410", ["NL"] = "00020409", ["SE"] = "0000041D", ["NO"] = "00000414", ["DK"] = "00000406",
        ["FI"] = "0000040B", ["PL"] = "00000415", ["JP"] = Japanese, ["KR"] = Korean, ["CZ"] = "00000405",
        ["HU"] = "0000040E", ["TR"] = "0000041F", ["RU"] = "00000419", ["UA"] = "00000422", ["GR"] = "00000408",
    };

    public static LayoutSuggestion? Suggest(KeyboardRecord keyboard, IReadOnlyList<InputProfile> installed, string? region = null)
    {
        string name = keyboard.ProductName ?? string.Empty;
        foreach ((Regex pattern, string klid) in NamePatterns)
        {
            if (pattern.IsMatch(name))
            {
                return new LayoutSuggestion(klid, Find(klid, installed), SuggestionConfidence.High);
            }
        }

        if (keyboard.VendorId == AppleVendorId && AppleJis.Contains(keyboard.ProductId))
        {
            return new LayoutSuggestion(Japanese, Find(Japanese, installed), SuggestionConfidence.High);
        }

        region ??= CurrentRegion();
        if (region == null || !RegionLayouts.TryGetValue(region, out string? guess))
        {
            return null;
        }

        // A weak guess is only worth showing when that layout is installed
        InputProfile? profile = Find(guess, installed);
        return profile != null ? new LayoutSuggestion(guess, profile, SuggestionConfidence.Low) : null;
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

    private static string? CurrentRegion()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
