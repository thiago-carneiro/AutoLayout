// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using AutoLayout.Core;

namespace AutoLayout.Services;

/// <summary>
/// Sample keyboards and layouts for screenshots (--demo): nothing is read from or applied to
/// the PC, and nothing is saved.
/// </summary>
internal static class Demo
{
    public const string Argument = "--demo";
    public const string WaitingKey = "046D:B342";

    public static AppSettings Settings()
    {
        DateTime now = DateTime.UtcNow;
        AppSettings settings = new() { DefaultLayoutProfileId = "0409:00020409", FirstRunDone = true, KeepBacklight = true };
        settings.Keyboards.Add(new KeyboardRecord
        {
            Key = "1A2C:0002", VendorId = 0x1A2C, ProductId = 0x0002, ProductName = "USB Keyboard", FriendlyName = "Desk keyboard",
            Connection = KeyboardConnection.Usb, State = KeyboardState.Configured,
            LayoutProfileId = "0416:00010416", LayoutKlid = "00010416", FirstSeenUtc = now.AddDays(-30), LastSeenUtc = now,
        });
        settings.Keyboards.Add(new KeyboardRecord
        {
            Key = WaitingKey, VendorId = 0x046D, ProductId = 0xB342, ProductName = "K380 Multi-Device Keyboard",
            Connection = KeyboardConnection.Bluetooth, State = KeyboardState.NotSetUp, FirstSeenUtc = now, LastSeenUtc = now,
        });
        settings.Keyboards.Add(new KeyboardRecord
        {
            Key = "05AC:024F", VendorId = 0x05AC, ProductId = 0x024F, ProductName = "Magic Keyboard",
            Connection = KeyboardConnection.Bluetooth, State = KeyboardState.Configured,
            LayoutProfileId = "0407:00000407", LayoutKlid = "00000407", FirstSeenUtc = now.AddDays(-12), LastSeenUtc = now.AddDays(-2),
        });
        settings.Keyboards.Add(new KeyboardRecord
        {
            Key = ExternalKeyboards.RemoteDesktopKey, Connection = KeyboardConnection.RemoteDesktop, State = KeyboardState.Configured,
            LayoutProfileId = SettingsStore.AutomaticProfileId, FirstSeenUtc = now.AddDays(-40), LastSeenUtc = now.AddDays(-5),
        });
        settings.Keyboards.Add(new KeyboardRecord
        {
            Key = "046D:C52B", VendorId = 0x046D, ProductId = 0xC52B, ProductName = "USB Receiver",
            Connection = KeyboardConnection.Usb, State = KeyboardState.Ignored, FirstSeenUtc = now.AddDays(-20), LastSeenUtc = now.AddDays(-1),
        });
        return settings;
    }

    public sealed class Devices : IDeviceSource
    {
        public bool IsRemoteSession => false;

        public IReadOnlyList<ConnectedKeyboard>? GetConnected() => new List<ConnectedKeyboard>
        {
            new("1A2C:0002", 0x1A2C, 0x0002, KeyboardConnection.Usb, "demo"),
            new(WaitingKey, 0x046D, 0xB342, KeyboardConnection.Bluetooth, "demo"),
        };

        public string? GetProductName(ConnectedKeyboard keyboard) => null;
    }

    public sealed class Inputs : IInputSystem
    {
        public IReadOnlyList<InputProfile> GetInstalled() => new List<InputProfile>
        {
            Profile(0x0409, "00020409"),
            Profile(0x0416, "00010416"),
            Profile(0x0407, "00000407"),
        };

        public string? GetDefaultProfileId(IReadOnlyList<InputProfile> installed) => "0409:00020409";

        /// <summary>Applies nothing.</summary>
        public bool Apply(InputProfile profile) => true;

        private static InputProfile Profile(int language, string klid) =>
            new(language.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ":" + klid, language, klid,
                new IntPtr(language), InputProfiles.GetLayoutName(klid) ?? klid, InputProfiles.GetLanguageName(language));
    }

    public sealed class Store : ISettingsStore
    {
        /// <summary>Saves nothing.</summary>
        public void Save(AppSettings settings)
        {
        }
    }
}
