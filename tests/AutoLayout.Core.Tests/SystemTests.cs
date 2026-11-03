// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoLayout.Core.Tests;

/// <summary>
/// Read-only checks against the Windows this runs on: nothing is applied or changed.
/// </summary>
[TestClass]
[TestCategory("System")]
public sealed class SystemTests
{
    [TestMethod]
    public void InstalledProfiles_AreListedWithValidIds_AndTheDefaultIsAmongThem()
    {
        InputProfiles inputs = new();
        IReadOnlyList<InputProfile> installed = inputs.GetInstalled();

        Assert.IsNotEmpty(installed);
        foreach (InputProfile profile in installed)
        {
            Assert.IsTrue(InputProfiles.IsValidId(profile.Id), profile.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.LayoutName), profile.Id);
            if (profile.Klid != null)
            {
                Assert.EndsWith(profile.Klid, profile.Id);
                Assert.AreEqual(profile.Klid, InputProfiles.KlidFromHandle(profile.Handle), "Handle and layout id agree");
            }
        }

        string? defaultId = inputs.GetDefaultProfileId(installed);
        Assert.IsNotNull(defaultId);
        Assert.IsTrue(((List<InputProfile>)installed).Exists(p => p.Id == defaultId));
    }

    [TestMethod]
    [DataRow(0x04090409, "00000409")]
    [DataRow(0x04160416, "00000416")]
    [DataRow(0x04090416, "00000409")]
    public void PlainLayoutHandles_MapToTheirLayoutId(int handle, string klid)
    {
        Assert.AreEqual(klid, InputProfiles.KlidFromHandle(new IntPtr(handle)));
    }

    [TestMethod]
    public void LayoutNames_ComeFromWindows_EvenForLayoutsNotInstalled()
    {
        Assert.IsFalse(string.IsNullOrEmpty(InputProfiles.GetLayoutName("00000411")));
        Assert.IsFalse(string.IsNullOrEmpty(InputProfiles.GetLayoutName("00010416")));
        Assert.IsNull(InputProfiles.GetLayoutName("..\\..\\x"));
    }

    [TestMethod]
    public void ConnectedKeyboards_CanBeListed_WithoutTheBuiltInOne()
    {
        ExternalKeyboards devices = new();
        IReadOnlyList<ConnectedKeyboard>? keyboards = devices.GetConnected();

        Assert.IsNotNull(keyboards);
        foreach (ConnectedKeyboard keyboard in keyboards)
        {
            Assert.DoesNotContain("ACPI", keyboard.InterfacePath);
            _ = devices.GetProductName(keyboard);   // must not throw
        }
        Assert.IsFalse(devices.IsRemoteSession);
    }
}
