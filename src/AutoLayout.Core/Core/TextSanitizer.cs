// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace AutoLayout.Core;

/// <summary>
/// Cleans names that come from devices or from the user before they are stored, shown or
/// logged. A device can report any string, including control characters (which break XML),
/// bidirectional overrides (which can make a notification read differently) or very long text.
/// </summary>
public static class TextSanitizer
{
    public const int MaxNameLength = 64;

    /// <summary>Returns the cleaned text, or null if nothing printable is left.</summary>
    public static string? CleanName(string? text, int maxLength = MaxNameLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        StringBuilder clean = new(text.Length);
        bool lastWasSpace = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c))
            {
                // Keep valid pairs only
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(text, i);
                    if (pairCategory != UnicodeCategory.PrivateUse && pairCategory != UnicodeCategory.OtherNotAssigned)
                    {
                        clean.Append(c).Append(text[i + 1]);
                        lastWasSpace = false;
                    }
                    i++;
                }
                continue;
            }

            UnicodeCategory category = char.GetUnicodeCategory(c);
            bool drop = char.IsLowSurrogate(c)
                || category == UnicodeCategory.Format          // bidi overrides, zero-width characters
                || category == UnicodeCategory.PrivateUse
                || category == UnicodeCategory.OtherNotAssigned
                || c == '￾' || c == '￿';
            if (drop)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!lastWasSpace)
                {
                    clean.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            clean.Append(c);
            lastWasSpace = false;
        }

        string result = clean.ToString().Trim();
        if (result.Length == 0)
        {
            return null;
        }
        return result.Length <= maxLength ? result : TruncateByTextElements(result, maxLength);
    }

    /// <summary>Shortens text without splitting surrogate pairs or combining sequences, adding "…".</summary>
    public static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }
        return TruncateByTextElements(text, maxLength - 1) + "…";
    }

    /// <summary>Makes text safe for a single log line: control characters become \uXXXX.</summary>
    public static string ForLog(string? text)
    {
        if (text == null)
        {
            return "(null)";
        }
        StringBuilder safe = new(text.Length);
        foreach (char c in text)
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                safe.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                safe.Append(c);
            }
        }
        return safe.ToString();
    }

    private static string TruncateByTextElements(string text, int maxLength)
    {
        StringInfo info = new(text);
        StringBuilder result = new(maxLength);
        for (int i = 0; i < info.LengthInTextElements; i++)
        {
            string element = info.SubstringByTextElements(i, 1);
            if (result.Length + element.Length > maxLength)
            {
                break;
            }
            result.Append(element);
        }
        return result.ToString().TrimEnd();
    }
}
