// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoLayout.Core.Tests;

[TestClass]
public sealed class DevicePathTests
{
    [TestMethod]
    [DataRow(@"\\?\HID#VID_046D&PID_C31C&MI_00#8&2b2a5c3c&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", "046D:C31C", KeyboardConnection.Usb)]
    [DataRow(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b342&Col01#9&1f&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", "046D:B342", KeyboardConnection.Bluetooth)]
    [DataRow(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b35b_REV&0003#a&1&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", "046D:B35B", KeyboardConnection.Bluetooth)]
    [DataRow(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0267#x#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", "BT:004C:0267", KeyboardConnection.Bluetooth)]
    public void ExternalKeyboards_AreRecognized(string path, string key, KeyboardConnection connection)
    {
        ConnectedKeyboard? keyboard = ExternalKeyboards.Parse(path);
        Assert.IsNotNull(keyboard);
        Assert.AreEqual(key, keyboard.Key);
        Assert.AreEqual(connection, keyboard.Connection);
    }

    [TestMethod]
    [DataRow(@"\\?\ACPI#LEN0071#4&76a515d&0#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}")]   // built-in PS/2
    [DataRow(@"\\?\Root#RDP_KBD#0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}")]        // Remote Desktop
    [DataRow(@"\\?\HID#ELAN0001&Col01#5&1&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}")] // I2C, no vendor id
    [DataRow("")]
    [DataRow("HID#VID_046D&PID_C31C")]
    public void OtherDevices_AreNotExternalKeyboards(string path)
    {
        Assert.IsNull(ExternalKeyboards.Parse(path));
    }

    [TestMethod]
    public void RandomAndHostilePaths_NeverThrow_AndOnlyYieldValidKeys()
    {
        Regex validKey = new(@"^(BT:)?[0-9A-F]{4}:[0-9A-F]{4}$");
        Random random = new(7);
        string alphabet = @"\?#&_{}-VIDPHvidph0123456789ABCDEFabcdef" + "\u0000\u202E\uFFFF\uD800";
        for (int i = 0; i < 20000; i++)
        {
            StringBuilder path = new(random.Next(2) == 0 ? @"\\?\HID#" : string.Empty);
            int length = random.Next(0, 120);
            for (int c = 0; c < length; c++)
            {
                path.Append(alphabet[random.Next(alphabet.Length)]);
            }
            ConnectedKeyboard? keyboard = ExternalKeyboards.Parse(path.ToString());
            if (keyboard != null)
            {
                StringAssert.Matches(keyboard.Key, validKey);
            }
        }
    }
}

[TestClass]
public sealed class TextSanitizerTests
{
    [TestMethod]
    [DataRow("K380 Multi-Device Keyboard", "K380 Multi-Device Keyboard")]
    [DataRow("  Tabs\tand\r\nnew lines  ", "Tabs and new lines")]
    [DataRow("Bidi\u202Etxt.exe", "Biditxt.exe")]
    [DataRow("Zero\u200Bwidth", "Zerowidth")]
    [DataRow("Nul\u0000Bell\u0007", "Nul Bell")]
    [DataRow("Non\uFFFEchars\uFFFF", "Nonchars")]
    [DataRow("Teclado ç 日本語 😀", "Teclado ç 日本語 😀")]
    public void Names_AreCleaned(string input, string expected)
    {
        Assert.AreEqual(expected, TextSanitizer.CleanName(input));
    }

    [TestMethod]
    public void UnpairedSurrogates_AreDropped()
    {
        // Built in code: attribute strings cannot hold unpaired surrogates
        string input = "Unpaired" + '\uD800' + " high, " + '\uDC00' + "low";
        Assert.AreEqual("Unpaired high, low", TextSanitizer.CleanName(input));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("\u0000\u0001\u202E   ")]
    public void EmptyAfterCleaning_IsNull(string? input)
    {
        Assert.IsNull(TextSanitizer.CleanName(input));
    }

    [TestMethod]
    public void LongNames_AreCut_WithoutBreakingCharacters()
    {
        string name = string.Concat(System.Linq.Enumerable.Repeat("😀", 100));
        string clean = TextSanitizer.CleanName(name)!;
        Assert.IsLessThanOrEqualTo(TextSanitizer.MaxNameLength, clean.Length);
        Assert.IsFalse(char.IsHighSurrogate(clean[^1]), "A surrogate pair was split");

        string shortened = TextSanitizer.Shorten("Teclado da mesa com nome bem comprido", 12);
        Assert.IsLessThanOrEqualTo(12, shortened.Length);
        Assert.AreEqual("Teclado da…", shortened);
    }

    [TestMethod]
    public void LogText_CannotFakeLines()
    {
        Assert.AreEqual("a\\u000D\\u000Ab\\u202E", TextSanitizer.ForLog("a\r\nb\u202E"));
    }
}

[TestClass]
public sealed class SettingsFileTests
{
    private string folder = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        folder = Path.Combine(Path.GetTempPath(), "AutoLayoutTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(folder, true);

    private AppSettings LoadText(string json, out bool keptBadCopy)
    {
        string path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, json);
        AppSettings settings = new SettingsStore(path).Load();
        keptBadCopy = File.Exists(path + ".bad");
        return settings;
    }

    [TestMethod]
    public void SavedSettings_LoadBackTheSame()
    {
        string path = Path.Combine(folder, "settings.json");
        AppSettings settings = new() { DefaultLayoutProfileId = "0416:00020409", LogEnabled = true, BacklightLevel = BacklightLevel.Low };
        settings.Keyboards.Add(new KeyboardRecord { Key = "046D:C31C", ProductName = "K120 \"teclado\" ç 日本", State = KeyboardState.Configured, LayoutProfileId = "0416:00010416" });
        settings.Keyboards.Add(new KeyboardRecord { Key = "RDP", Connection = KeyboardConnection.RemoteDesktop, State = KeyboardState.Configured, LayoutProfileId = "AUTO" });
        new SettingsStore(path).Save(settings);
        new SettingsStore(path).Save(settings);   // replacing an existing file

        AppSettings loaded = new SettingsStore(path).Load();
        Assert.AreEqual("0416:00020409", loaded.DefaultLayoutProfileId);
        Assert.IsTrue(loaded.LogEnabled);
        Assert.AreEqual(BacklightLevel.Low, loaded.BacklightLevel);
        Assert.HasCount(2, loaded.Keyboards);
        Assert.AreEqual("K120 \"teclado\" ç 日本", loaded.Keyboards[0].ProductName);
        Assert.AreEqual("0416:00010416", loaded.Keyboards[0].LayoutProfileId);
        Assert.AreEqual(KeyboardConnection.RemoteDesktop, loaded.Keyboards[1].Connection);
        Assert.AreEqual("AUTO", loaded.Keyboards[1].LayoutProfileId);
        Assert.IsFalse(File.Exists(path + ".tmp"));

        string text = File.ReadAllText(path);
        StringAssert.Contains(text, "\"Configured\"", "Enums are saved by name");
        Assert.DoesNotContain("VidPid", text, "Computed properties are not saved");
    }

    [TestMethod]
    public void DeepNesting_IsRejected()
    {
        string json = "{\"Keyboards\":" + new string('[', 100000) + new string(']', 100000) + "}";
        AppSettings settings = LoadText(json, out bool kept);
        Assert.IsEmpty(settings.Keyboards);
        Assert.IsTrue(kept);
    }

    [TestMethod]
    public void HugeFiles_AreNotRead()
    {
        AppSettings settings = LoadText("{\"Keyboards\":[]" + new string(' ', 5 * 1024 * 1024) + "}", out bool kept);
        Assert.IsEmpty(settings.Keyboards);
        Assert.IsTrue(kept);
    }

    [TestMethod]
    [DataRow("{\"Keyboards\":[{\"Key\":")]                       // cut short
    [DataRow("{\"MainWindowWidth\":\"wide\"}")]                  // wrong type
    [DataRow("{\"Keyboards\":[{\"State\":\"Hacked\"}]}")]        // unknown enum name
    [DataRow("<AutoLayout />")]                                  // not JSON
    [DataRow("")]
    public void BrokenFiles_StartOverAndKeepACopy(string json)
    {
        AppSettings settings = LoadText(json, out bool kept);
        Assert.IsNotNull(settings);
        Assert.IsEmpty(settings.Keyboards);
        Assert.IsTrue(kept);
    }

    [TestMethod]
    public void TamperedValues_AreDropped()
    {
        AppSettings settings = LoadText(
            """
            {
              "DefaultLayoutProfileId": "0416:00020409; calc.exe",
              "BacklightLevel": "Low", "MainWindowWidth": 99999999, "Unknown": { "a": [1, 2] },
              "Keyboards": [
                { "Key": "..\\..\\evil", "State": "Configured" },
                { "Key": "046D:C31C", "State": "Configured", "LayoutProfileId": "AUTO" },
                { "Key": "046D:C31C", "State": "Configured" },
                { "Key": "1111:2222", "ProductName": "a\u202Eb", "State": "Ignored", "VendorId": 70000 },
                { "Key": "3333:4444", "State": 7 },
                null
              ]
            }
            """, out _);

        Assert.IsNull(settings.DefaultLayoutProfileId, "Only well-formed profile ids are kept");
        Assert.AreEqual(BacklightLevel.Low, settings.BacklightLevel);
        Assert.AreEqual(10000, settings.MainWindowWidth);
        Assert.HasCount(2, settings.Keyboards, "Invalid keys, undefined states, nulls and duplicates are dropped");
        Assert.AreEqual(KeyboardState.NotSetUp, settings.Keyboards[0].State, "AUTO is only valid for Remote Desktop");
        Assert.AreEqual("ab", settings.Keyboards[1].ProductName);
        Assert.AreEqual(70000 & 0xFFFF, settings.Keyboards[1].VendorId);
    }

    [TestMethod]
    public void TooManyRecords_AreCapped()
    {
        StringBuilder json = new("{\"Keyboards\":[");
        for (int i = 0; i < 800; i++)
        {
            json.Append(i > 0 ? "," : string.Empty).Append($"{{\"Key\":\"{0x1000 + i:X4}:0001\",\"State\":\"NotSetUp\"}}");
        }
        json.Append("]}");
        Assert.HasCount(SettingsStore.MaxKeyboards, LoadText(json.ToString(), out _).Keyboards);
    }
}

[TestClass]
public sealed class LayoutSuggesterTests
{
    private static readonly List<InputProfile> Installed = new() { FakeInputs.UsIntl, FakeInputs.Abnt2, FakeInputs.JapaneseIme };

    private static LayoutSuggestion? Suggest(string? name, int vid, int pid, string region) =>
        LayoutSuggester.Suggest(new KeyboardRecord { ProductName = name, VendorId = vid, ProductId = pid }, Installed, region);

    [TestMethod]
    public void NameWithAbnt_SuggestsAbnt2WithConfidence()
    {
        LayoutSuggestion? suggestion = Suggest("USB Keyboard ABNT2", 0x1234, 1, "US");
        Assert.AreEqual(SuggestionConfidence.High, suggestion?.Confidence);
        Assert.AreEqual(FakeInputs.Abnt2.Id, suggestion?.Profile?.Id);
    }

    [TestMethod]
    public void AppleJisModel_SuggestsTheJapaneseInputMethod()
    {
        Assert.AreEqual(FakeInputs.JapaneseIme.Id, Suggest("Apple Keyboard", 0x05AC, 0x0222, "BR")?.Profile?.Id);
    }

    [TestMethod]
    public void SuggestedLayoutNotInstalled_IsStillReported()
    {
        LayoutSuggestion? suggestion = Suggest("Korean keyboard", 0x1234, 1, "BR");
        Assert.AreEqual("00000412", suggestion?.Klid);
        Assert.IsNull(suggestion?.Profile);
    }

    [TestMethod]
    public void UnknownKeyboard_GetsARegionGuess_OnlyWhenInstalled()
    {
        Assert.AreEqual(SuggestionConfidence.Low, Suggest("USB Keyboard", 0x1A2C, 2, "BR")?.Confidence);
        Assert.IsNull(Suggest("USB Keyboard", 0x1A2C, 2, "DE"), "German is not installed: no weak guess");
        Assert.IsNull(Suggest(null, 0x1A2C, 2, "ZZ"));
    }
}
