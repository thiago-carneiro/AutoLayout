// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace AutoLayout.Core.Tests;

/// <summary>Keyboards "plugged in" by the test.</summary>
internal sealed class FakeDevices : IDeviceSource
{
    public List<ConnectedKeyboard> Connected { get; } = new();

    public Dictionary<string, string?> Names { get; } = new();

    public bool IsRemoteSession { get; set; }

    /// <summary>The next listing fails, as when Windows is busy enumerating.</summary>
    public bool FailNext { get; set; }

    public int Listings { get; private set; }

    /// <summary>Runs inside GetConnected, to change devices in the middle of an operation.</summary>
    public Action? DuringListing { get; set; }

    public static ConnectedKeyboard Usb(int vid, int pid) =>
        new($"{vid:X4}:{pid:X4}", vid, pid, KeyboardConnection.Usb, $@"\\?\HID#VID_{vid:X4}&PID_{pid:X4}#x#{{884b96c3-56ef-11d1-bc8c-00a0c91405dd}}");

    public static ConnectedKeyboard Bluetooth(int vid, int pid) =>
        new($"{vid:X4}:{pid:X4}", vid, pid, KeyboardConnection.Bluetooth, "bt");

    public void Plug(ConnectedKeyboard keyboard)
    {
        if (!Connected.Exists(k => k.Key == keyboard.Key))
        {
            Connected.Add(keyboard);
        }
    }

    public void Unplug(string key) => Connected.RemoveAll(k => k.Key == key);

    public IReadOnlyList<ConnectedKeyboard>? GetConnected()
    {
        Listings++;
        Action? during = DuringListing;
        DuringListing = null;
        during?.Invoke();
        if (FailNext)
        {
            FailNext = false;
            return null;
        }
        return new List<ConnectedKeyboard>(Connected);
    }

    public string? GetProductName(ConnectedKeyboard keyboard) =>
        Names.TryGetValue(keyboard.Key, out string? name) ? name : "Keyboard " + keyboard.Key;
}

/// <summary>Layouts "installed" by the test; records what Windows was asked to apply.</summary>
internal sealed class FakeInputs : IInputSystem
{
    public static readonly InputProfile UsIntl = Layout("0416", "00020409", "United States-International");
    public static readonly InputProfile Abnt2 = Layout("0416", "00010416", "Portuguese (Brazil ABNT2)");
    public static readonly InputProfile German = Layout("0407", "00000407", "German");
    public static readonly InputProfile JapaneseIme = new(
        "0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}", 0x0411, null, new IntPtr(0x04110411), "Microsoft IME", "Japanese");

    public List<InputProfile> Installed { get; } = new() { UsIntl, Abnt2, German, JapaneseIme };

    public string? DefaultId { get; set; } = UsIntl.Id;

    /// <summary>The profile Windows has as default now (what the user would type with).</summary>
    public string? CurrentId { get; private set; }

    public List<string> Applied { get; } = new();

    /// <summary>Windows refuses the next apply.</summary>
    public bool FailNext { get; set; }

    /// <summary>Runs while applying, as Windows dispatches messages then.</summary>
    public Action? DuringApply { get; set; }

    public static InputProfile Layout(string language, string klid, string name) =>
        new(language + ":" + klid, Convert.ToInt32(language, 16), klid, new IntPtr(Convert.ToInt32(language, 16)), name, null);

    public IReadOnlyList<InputProfile> GetInstalled() => new List<InputProfile>(Installed);

    public string? GetDefaultProfileId(IReadOnlyList<InputProfile> installed) => DefaultId;

    public bool Apply(InputProfile profile)
    {
        Action? during = DuringApply;
        DuringApply = null;
        during?.Invoke();
        if (FailNext)
        {
            FailNext = false;
            return false;
        }
        Applied.Add(profile.Id);
        CurrentId = profile.Id;
        return true;
    }
}

/// <summary>Saves to memory, through the real XML format.</summary>
internal sealed class FakeStore : ISettingsStore
{
    private static readonly XmlSerializer Serializer = new(typeof(AppSettings));

    public int Saves { get; private set; }

    public string? LastXml { get; private set; }

    public void Save(AppSettings settings)
    {
        Saves++;
        using StringWriter writer = new();
        Serializer.Serialize(writer, settings);
        LastXml = writer.ToString();
    }

    public AppSettings? Reload()
    {
        if (LastXml == null)
        {
            return null;
        }
        using StringReader reader = new(LastXml);
        return SettingsStore.Sanitize(Serializer.Deserialize(reader) as AppSettings);
    }
}

/// <summary>An engine with fakes and a log of everything it raised.</summary>
internal sealed class Rig
{
    public Rig(AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        Engine = new Engine(Settings, Devices, Inputs, Store);
        Engine.NewKeyboard += (_, k) => NewKeyboards.Add(k.Key);
        Engine.LayoutApplied += (_, c) => Announced.Add(c);
        Engine.ProblemOccurred += (_, p) => Problems.Add(p);
        Engine.KeyboardForgotten += (_, k) => Forgotten.Add(k);
        Engine.RetryNeeded += (_, _) => Retries++;
    }

    public AppSettings Settings { get; }

    public FakeDevices Devices { get; } = new();

    public FakeInputs Inputs { get; } = new();

    public FakeStore Store { get; } = new();

    public Engine Engine { get; }

    public List<string> NewKeyboards { get; } = new();

    public List<LayoutChange> Announced { get; } = new();

    public List<Problem> Problems { get; } = new();

    public List<string> Forgotten { get; } = new();

    public int Retries { get; set; }

    public Rig Start()
    {
        Engine.Start();
        return this;
    }

    /// <summary>Plugs in a keyboard and lets the engine notice.</summary>
    public ConnectedKeyboard Plug(int vid, int pid)
    {
        ConnectedKeyboard keyboard = FakeDevices.Usb(vid, pid);
        Devices.Plug(keyboard);
        Engine.Refresh();
        return keyboard;
    }

    public void Unplug(ConnectedKeyboard keyboard)
    {
        Devices.Unplug(keyboard.Key);
        Engine.Refresh();
    }

    public void SetUp(ConnectedKeyboard keyboard, InputProfile profile) => Engine.Configure(keyboard.Key, null, profile);
}
