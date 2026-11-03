// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoLayout.Core.Tests;

/// <summary>
/// Every translation has exactly the keys of the English strings, the same placeholders, and a
/// valid text direction — so a missing or broken translation fails the build, not the user.
/// </summary>
[TestClass]
public sealed class TranslationTests
{
    private static readonly Regex Placeholder = new(@"\{\d+\}");

    private static string StringsFolder()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "src", "AutoLayout", "Strings")))
        {
            folder = folder.Parent;
        }
        Assert.IsNotNull(folder, "Strings folder not found");
        return Path.Combine(folder.FullName, "src", "AutoLayout", "Strings");
    }

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    [TestMethod]
    public void EveryLanguage_MatchesTheEnglishStrings()
    {
        string folder = StringsFolder();
        Dictionary<string, string> english = Load(Path.Combine(folder, "en-US", "Resources.resw"));
        List<string> problems = new();

        foreach (string file in Directory.GetFiles(folder, "Resources.resw", SearchOption.AllDirectories))
        {
            string language = Path.GetFileName(Path.GetDirectoryName(file)!);
            Dictionary<string, string> strings;
            try
            {
                strings = Load(file);
            }
            catch (System.Xml.XmlException error)
            {
                problems.Add($"{language}: invalid XML ({error.Message})");
                continue;
            }

            foreach (string key in english.Keys.Except(strings.Keys))
            {
                problems.Add($"{language}: missing {key}");
            }
            foreach (string key in strings.Keys.Except(english.Keys))
            {
                problems.Add($"{language}: unknown {key}");
            }
            foreach ((string key, string value) in strings)
            {
                if (!english.TryGetValue(key, out string? reference))
                {
                    continue;
                }
                string[] expected = Placeholder.Matches(reference).Select(m => m.Value).OrderBy(v => v).ToArray();
                string[] actual = Placeholder.Matches(value).Select(m => m.Value).OrderBy(v => v).ToArray();
                if (!expected.SequenceEqual(actual))
                {
                    problems.Add($"{language}: {key} has placeholders [{string.Join(",", actual)}], expected [{string.Join(",", expected)}]");
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"{language}: {key} is empty");
                }
            }

            string direction = strings.GetValueOrDefault("App_FlowDirection", string.Empty);
            bool rightToLeft = language.StartsWith("ar", StringComparison.Ordinal) || language.StartsWith("he", StringComparison.Ordinal);
            if (direction != (rightToLeft ? "RightToLeft" : "LeftToRight"))
            {
                problems.Add($"{language}: App_FlowDirection is '{direction}'");
            }
            foreach (string key in new[] { "Tray_Tip_Attention", "Tray_Tip_Error" })
            {
                if (strings.TryGetValue(key, out string? tip) && tip.Length > 120)
                {
                    problems.Add($"{language}: {key} is {tip.Length} characters (max 120)");
                }
            }
        }

        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }
}
