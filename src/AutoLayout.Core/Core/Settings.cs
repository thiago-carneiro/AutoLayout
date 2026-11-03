// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoLayout.Core;

public enum KeyboardState
{
    NotSetUp,
    Configured,
    Ignored,
}

/// <summary>
/// A keyboard AutoLayout has seen, saved between sessions.
/// </summary>
public sealed class KeyboardRecord
{
    public string Key { get; set; } = string.Empty;

    public int VendorId { get; set; }

    public int ProductId { get; set; }

    /// <summary>Name Windows has for the device; may be null.</summary>
    public string? ProductName { get; set; }

    /// <summary>Name given by the user; null to use the product name.</summary>
    public string? FriendlyName { get; set; }

    public KeyboardConnection Connection { get; set; }

    public KeyboardState State { get; set; }

    /// <summary>Input profile to apply ("0416:00010416"), or <see cref="SettingsStore.AutomaticProfileId"/>.</summary>
    public string? LayoutProfileId { get; set; }

    /// <summary>Layout identifier of that profile, to name it if it is uninstalled later.</summary>
    public string? LayoutKlid { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    [JsonIgnore]
    public string VidPid => string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", VendorId, ProductId);

    /// <summary>The user's name, the device's name, or VID:PID.</summary>
    [JsonIgnore]
    public string DisplayName => FriendlyName ?? ProductName ?? VidPid;
}

public sealed class AppSettings
{
    public string? DefaultLayoutProfileId { get; set; }

    public bool NotifyOnSwitch { get; set; } = true;

    /// <summary>Off by default: the log lists the devices connected and when.</summary>
    public bool LogEnabled { get; set; }

    public bool KeepBacklight { get; set; } = true;

    public BacklightLevel BacklightLevel { get; set; } = BacklightLevel.High;

    public int MainWindowWidth { get; set; }

    public int MainWindowHeight { get; set; }

    public bool FirstRunDone { get; set; }

    public List<KeyboardRecord> Keyboards { get; set; } = new();
}

public interface ISettingsStore
{
    void Save(AppSettings settings);
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the app's data folder. The file is
/// only trusted as far as its schema: values are validated after loading.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    /// <summary>
    /// Layout of the Remote Desktop entry that leaves the layout to Windows, which by default
    /// uses the layout of the keyboard of the computer the user connects from.
    /// </summary>
    public const string AutomaticProfileId = "AUTO";

    public const int MaxKeyboards = 500;

    private const int MaxFileSize = 4 * 1024 * 1024;

    private static readonly Regex KeyPattern = new(@"^((BT:)?[0-9A-F]{4}:[0-9A-F]{4}|RDP)$", RegexOptions.CultureInvariant);
    private readonly string path;

    /// <param name="path">Settings file; by default settings.json in the app's data folder.</param>
    public SettingsStore(string? path = null)
    {
        this.path = path ?? DefaultFilePath;
    }

    public static string DefaultFilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > MaxFileSize)
            {
                throw new InvalidDataException("Settings file is too large");
            }
            return Sanitize(JsonSerializer.Deserialize(stream, SettingsJson.Default.AppSettings));
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException
            or InvalidDataException or UnauthorizedAccessException)
        {
            // Keep the unreadable file for inspection and start over
            Log.Write("Settings could not be read (" + error.Message + "); starting with defaults");
            try
            {
                File.Copy(path, path + ".bad", true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ToJson(settings));

            // Replace in one step so a crash never leaves a half-written file
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write("Settings could not be saved: " + error.Message);
            try
            {
                File.Delete(temporary);
            }
            catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static byte[] ToJson(AppSettings settings) => JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.AppSettings);

    /// <summary>Reads settings as <see cref="Load"/> does, without the file; throws <see cref="JsonException"/>.</summary>
    internal static AppSettings FromJson(byte[] json) => Sanitize(JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings));

    internal static AppSettings Sanitize(AppSettings? settings)
    {
        if (settings == null)
        {
            return new AppSettings();
        }

        if (!InputProfiles.IsValidId(settings.DefaultLayoutProfileId))
        {
            settings.DefaultLayoutProfileId = null;
        }
        if (!Enum.IsDefined(settings.BacklightLevel))
        {
            settings.BacklightLevel = BacklightLevel.High;
        }
        settings.MainWindowWidth = Math.Clamp(settings.MainWindowWidth, 0, 10000);
        settings.MainWindowHeight = Math.Clamp(settings.MainWindowHeight, 0, 10000);

        List<KeyboardRecord> keyboards = new();
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (KeyboardRecord? record in settings.Keyboards ?? new List<KeyboardRecord>())
        {
            if (keyboards.Count >= MaxKeyboards)
            {
                break;
            }
            if (record == null || !KeyPattern.IsMatch(record.Key ?? string.Empty) || !keys.Add(record.Key!)
                || !Enum.IsDefined(record.State) || !Enum.IsDefined(record.Connection))
            {
                continue;
            }

            record.VendorId &= 0xFFFF;
            record.ProductId &= 0xFFFF;
            record.ProductName = TextSanitizer.CleanName(record.ProductName);
            record.FriendlyName = TextSanitizer.CleanName(record.FriendlyName);
            if (!InputProfiles.IsValidKlid(record.LayoutKlid))
            {
                record.LayoutKlid = null;
            }

            bool automaticRemote = record.Key == ExternalKeyboards.RemoteDesktopKey && record.LayoutProfileId == AutomaticProfileId;
            if (!automaticRemote && !InputProfiles.IsValidId(record.LayoutProfileId))
            {
                record.LayoutProfileId = null;
                record.LayoutKlid = null;
                if (record.State == KeyboardState.Configured)
                {
                    record.State = KeyboardState.NotSetUp;
                }
            }
            keyboards.Add(record);
        }
        settings.Keyboards = keyboards;
        return settings;
    }
}

/// <summary>
/// JSON contract for the settings file, generated at compile time (no reflection, so the app
/// can be trimmed). Enums are written by name; unknown properties are ignored.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext
{
}
