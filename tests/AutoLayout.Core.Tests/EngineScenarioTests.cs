// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoLayout.Core.Tests;

[TestClass]
public sealed class EngineScenarioTests
{
    [TestMethod]
    public void FirstRun_KeepsTheUsersDefault_AndAppliesItWithoutAnnouncing()
    {
        Rig rig = new Rig().Start();

        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Settings.DefaultLayoutProfileId);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);
        Assert.IsEmpty(rig.Announced, "Startup is not news");
        Assert.IsNull(rig.Engine.ActiveKeyboardKey);
    }

    [TestMethod]
    public void UnknownKeyboard_ChangesNothing_AndAsksOnce()
    {
        Rig rig = new Rig().Start();
        int applies = rig.Inputs.Applied.Count;

        ConnectedKeyboard keyboard = rig.Plug(0x1A2C, 0x0002);
        rig.Engine.Refresh();
        rig.Engine.Refresh();

        Assert.AreEqual(applies, rig.Inputs.Applied.Count, "Nothing is applied before the user answers");
        CollectionAssert.AreEqual(new[] { keyboard.Key }, rig.NewKeyboards);
        Assert.HasCount(1, rig.Engine.SetUpQueue);
        Assert.HasCount(1, rig.Engine.AwaitingSetUp);
    }

    [TestMethod]
    public void LastConnectedWins_AndUnpluggingFallsBackInOrder()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard abnt = rig.Plug(0x1111, 0x0001);
        rig.SetUp(abnt, FakeInputs.Abnt2);
        ConnectedKeyboard german = rig.Plug(0x2222, 0x0002);
        rig.SetUp(german, FakeInputs.German);
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);

        rig.Unplug(german);
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Inputs.CurrentId);
        Assert.AreEqual(abnt.Key, rig.Engine.ActiveKeyboardKey);

        rig.Unplug(abnt);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);
        Assert.IsNull(rig.Engine.ActiveKeyboardKey);
        Assert.AreEqual(abnt.Key, rig.Announced[^1].Disconnected?.Key, "The notification says which keyboard left");
    }

    [TestMethod]
    public void KeyboardsArrivingTogether_HaveAStableWinner()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard a = FakeDevices.Usb(0x1111, 1);
        ConnectedKeyboard b = FakeDevices.Usb(0x2222, 2);
        rig.Devices.Plug(a);
        rig.Engine.Refresh();
        rig.SetUp(a, FakeInputs.Abnt2);
        rig.Devices.Plug(b);
        rig.Engine.Refresh();
        rig.SetUp(b, FakeInputs.German);
        rig.Unplug(a);
        rig.Unplug(b);

        // Both come back in one burst (docking, resume): the order they are listed decides, every time
        for (int i = 0; i < 5; i++)
        {
            rig.Devices.Plug(a);
            rig.Devices.Plug(b);
            rig.Engine.Refresh();
            Assert.AreEqual(b.Key, rig.Engine.ActiveKeyboardKey);
            rig.Engine.Refresh();
            Assert.AreEqual(b.Key, rig.Engine.ActiveKeyboardKey, "A refresh with no change keeps the winner");
            rig.Devices.Unplug(a.Key);
            rig.Devices.Unplug(b.Key);
            rig.Engine.Refresh();
        }
    }

    [TestMethod]
    public void EditingAnotherKeyboard_DoesNotChangeWhichOneApplies()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard a = rig.Plug(0x1111, 1);
        rig.SetUp(a, FakeInputs.Abnt2);
        ConnectedKeyboard b = rig.Plug(0x2222, 2);
        rig.SetUp(b, FakeInputs.German);

        rig.Engine.Configure(a.Key, "Desk keyboard", FakeInputs.Abnt2);
        rig.Engine.Rename(a.Key, "Desk");

        Assert.AreEqual(b.Key, rig.Engine.ActiveKeyboardKey);
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void ForgettingAConnectedKeyboard_KeepsTheSameRecord_AndRevertsTheLayout()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        KeyboardRecord record = rig.Engine.Find(keyboard.Key)!;

        rig.Engine.Forget(keyboard.Key);

        Assert.AreSame(record, rig.Engine.Find(keyboard.Key), "Open windows keep a valid record");
        Assert.AreEqual(KeyboardState.NotSetUp, record.State);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);
        CollectionAssert.Contains(rig.Forgotten, keyboard.Key);
        Assert.IsEmpty(rig.Engine.SetUpQueue, "Forgetting does not ask again right away");
        Assert.HasCount(1, rig.Engine.AwaitingSetUp);
    }

    [TestMethod]
    public void ForgettingADisconnectedKeyboard_RemovesIt_AndActionsOnItAfterwardsDoNothing()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        rig.Unplug(keyboard);

        rig.Engine.Forget(keyboard.Key);
        rig.Engine.Configure(keyboard.Key, "x", FakeInputs.German);   // a dialog that was still open
        rig.Engine.SetIgnored(keyboard.Key, true);
        rig.Engine.Rename(keyboard.Key, "y");
        rig.Engine.Postpone(keyboard.Key);

        Assert.IsNull(rig.Engine.Find(keyboard.Key));
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void OtherHidDevices_CanBeIgnored_AndUnignoringAsksAgain()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard mouse = rig.Plug(0x046D, 0xC52B);

        rig.Engine.SetIgnored(mouse.Key, true);
        rig.Unplug(mouse);
        rig.Devices.Plug(mouse);
        rig.Engine.Refresh();
        Assert.HasCount(1, rig.NewKeyboards, "An ignored device is not asked about again");

        rig.Engine.SetIgnored(mouse.Key, false);
        Assert.HasCount(1, rig.Engine.SetUpQueue, "Unignoring a connected device asks again");
    }

    [TestMethod]
    public void NotNow_StopsAsking_UntilTheKeyboardIsConnectedAgain()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);

        rig.Engine.Postpone(keyboard.Key);
        rig.Engine.Refresh();
        Assert.IsEmpty(rig.Engine.SetUpQueue);
        Assert.HasCount(1, rig.Engine.AwaitingSetUp, "Still offered in the tray menu");
        Assert.HasCount(1, rig.NewKeyboards);

        rig.Unplug(keyboard);
        rig.Devices.Plug(keyboard);
        rig.Engine.Refresh();
        Assert.HasCount(2, rig.NewKeyboards);
    }

    [TestMethod]
    public void FailedListing_IsNotTreatedAsEverythingUnplugged()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        int applies = rig.Inputs.Applied.Count;

        rig.Devices.FailNext = true;
        rig.Engine.Refresh();

        Assert.AreEqual(1, rig.Retries);
        Assert.AreEqual(keyboard.Key, rig.Engine.ActiveKeyboardKey);
        Assert.AreEqual(applies, rig.Inputs.Applied.Count);
        Assert.IsTrue(rig.Engine.IsConnected(rig.Engine.Find(keyboard.Key)!));
    }

    [TestMethod]
    public void MissingLayout_IsReportedOnce_ClearedWhenFixed_AndReportedAgainNextTime()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.German);
        rig.Unplug(keyboard);
        rig.Inputs.Installed.Remove(FakeInputs.German);
        rig.Engine.ReloadProfiles();

        rig.Devices.Plug(keyboard);
        rig.Engine.Refresh();
        rig.Engine.Refresh();
        Assert.HasCount(1, rig.Problems);
        Assert.AreEqual(ProblemKind.LayoutMissing, rig.Engine.CurrentProblem?.Kind);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId, "The layout is left as it was");

        rig.Unplug(keyboard);
        Assert.IsNull(rig.Engine.CurrentProblem, "Back on the default, which is applied: no problem");

        rig.Devices.Plug(keyboard);
        rig.Engine.Refresh();
        Assert.HasCount(2, rig.Problems, "A new occurrence is reported again");

        rig.Inputs.Installed.Add(FakeInputs.German);
        rig.Engine.ReloadProfiles();
        Assert.IsNull(rig.Engine.CurrentProblem);
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void MissingDefault_IsReported_AndClearedWhenAKeyboardLayoutApplies()
    {
        Rig rig = new Rig().Start();
        rig.Inputs.Installed.Remove(FakeInputs.UsIntl);
        rig.Engine.ReloadProfiles();
        Assert.AreEqual(ProblemKind.DefaultMissing, rig.Engine.CurrentProblem?.Kind);

        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        Assert.IsNull(rig.Engine.CurrentProblem);

        rig.Engine.SetDefaultLayout(FakeInputs.German);
        rig.Unplug(keyboard);
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);
        Assert.IsNull(rig.Engine.CurrentProblem);
    }

    [TestMethod]
    public void RefusedApply_IsAProblem_UntilTheNextSuccess()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.Inputs.FailNext = true;
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        Assert.AreEqual(ProblemKind.ApplyFailed, rig.Engine.CurrentProblem?.Kind);
        Assert.AreEqual(keyboard.Key, rig.Engine.ActiveKeyboardKey, "The intended keyboard is still shown as the one in use");

        rig.Engine.Refresh();
        Assert.IsNull(rig.Engine.CurrentProblem);
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void KeyboardPluggedWhileApplying_IsHandledRightAfter()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard a = rig.Plug(0x1111, 1);
        ConnectedKeyboard b = FakeDevices.Usb(0x2222, 2);
        rig.Engine.Configure(b.Key, null, FakeInputs.German);   // unknown key: ignored, no crash
        rig.Inputs.DuringApply = () =>
        {
            // Windows dispatches messages while switching: a device arrives and the engine is called back
            rig.Devices.Plug(b);
            rig.Engine.Refresh();
        };

        rig.SetUp(a, FakeInputs.Abnt2);

        Assert.IsTrue(rig.Engine.IsConnected(rig.Engine.Find(b.Key)!), "The nested refresh ran afterwards");
        Assert.AreEqual(a.Key, rig.Engine.ActiveKeyboardKey, "b is not set up, so a still applies");
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void KeyboardUnpluggedWhileListing_IsSeenOnTheNextCheck()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);

        rig.Devices.DuringListing = () => rig.Devices.Unplug(keyboard.Key);
        rig.Engine.Refresh();
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void SettingUpAKeyboardThatWasUnpluggedMeanwhile_SavesItWithoutApplying()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.Unplug(keyboard);

        rig.SetUp(keyboard, FakeInputs.Abnt2);

        Assert.AreEqual(KeyboardState.Configured, rig.Engine.Find(keyboard.Key)!.State);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);

        rig.Devices.Plug(keyboard);
        rig.Engine.Refresh();
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void RemoteDesktop_Automatic_LeavesTheLayoutToWindows_AndKeepsPhysicalKeyboards()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        rig.SetUp(keyboard, FakeInputs.Abnt2);
        int applies = rig.Inputs.Applied.Count;

        rig.Devices.IsRemoteSession = true;
        rig.Engine.Refresh();
        Assert.IsTrue(rig.Engine.IsRemoteAutomatic);
        Assert.AreEqual(ExternalKeyboards.RemoteDesktopKey, rig.Engine.ActiveKeyboardKey);
        Assert.AreEqual(applies, rig.Inputs.Applied.Count, "Nothing applied in the remote session");

        rig.Devices.IsRemoteSession = false;
        rig.Engine.Refresh();
        Assert.IsFalse(rig.Engine.IsRemoteAutomatic);
        Assert.AreEqual(keyboard.Key, rig.Engine.ActiveKeyboardKey);
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Inputs.CurrentId, "Applied again on return");
        Assert.HasCount(1, rig.NewKeyboards, "Physical keyboards are not asked about again");
    }

    [TestMethod]
    public void RemoteDesktop_FixedLayout_AppliesInTheSession_AndRevertsAfter()
    {
        Rig rig = new Rig().Start();
        rig.Devices.IsRemoteSession = true;
        rig.Engine.Refresh();
        rig.Engine.Configure(ExternalKeyboards.RemoteDesktopKey, null, FakeInputs.German);
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);

        rig.Devices.IsRemoteSession = false;
        rig.Engine.Refresh();
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId);

        rig.Engine.SetRemoteAutomatic(null);
        rig.Engine.SetIgnored(ExternalKeyboards.RemoteDesktopKey, true);
        Assert.AreEqual(KeyboardState.Configured, rig.Engine.Find(ExternalKeyboards.RemoteDesktopKey)!.State, "Remote Desktop cannot be ignored");
    }

    [TestMethod]
    public void RemoteDesktop_LayoutCanBeChosenBeforeTheFirstSession()
    {
        Rig rig = new Rig().Start();
        Assert.IsNull(rig.Engine.Find(ExternalKeyboards.RemoteDesktopKey), "No entry until a session or a choice");

        rig.Engine.SetRemoteLayout(FakeInputs.German);
        KeyboardRecord rdp = rig.Engine.Find(ExternalKeyboards.RemoteDesktopKey)!;
        Assert.AreEqual(FakeInputs.German.Id, rdp.LayoutProfileId);
        Assert.AreEqual(KeyboardState.Configured, rdp.State);
        Assert.AreEqual(FakeInputs.UsIntl.Id, rig.Inputs.CurrentId, "Nothing changes outside a remote session");

        rig.Devices.IsRemoteSession = true;
        rig.Engine.Refresh();
        Assert.AreEqual(FakeInputs.German.Id, rig.Inputs.CurrentId);

        rig.Engine.SetRemoteLayout(null);
        Assert.AreEqual(SettingsStore.AutomaticProfileId, rdp.LayoutProfileId);
        Assert.IsNull(rdp.LayoutKlid);
        Assert.IsTrue(rig.Engine.IsRemoteAutomatic);
        Assert.AreEqual(SettingsStore.AutomaticProfileId, rig.Store.Reload()!.Keyboards.Single(k => k.Key == ExternalKeyboards.RemoteDesktopKey).LayoutProfileId,
            "Saved");
    }

    [TestMethod]
    public void InputMethods_LikeJapaneseIme_AreApplied()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard keyboard = rig.Plug(0x05AC, 0x0222);
        Assert.AreEqual(FakeInputs.JapaneseIme.Id, rig.Engine.GetSuggestion(rig.Engine.Find(keyboard.Key)!)?.Profile?.Id,
            "A Japanese Apple keyboard suggests the Japanese input method");

        rig.SetUp(keyboard, FakeInputs.JapaneseIme);
        Assert.AreEqual(FakeInputs.JapaneseIme.Id, rig.Inputs.CurrentId);
    }

    [TestMethod]
    public void SuggestionsAreRecomputed_WhenTheSuggestedLayoutGetsInstalled()
    {
        Rig rig = new Rig().Start();
        rig.Inputs.Installed.Remove(FakeInputs.Abnt2);
        rig.Engine.ReloadProfiles();
        rig.Devices.Names["1111:0001"] = "ABNT2 Keyboard";
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);
        KeyboardRecord record = rig.Engine.Find(keyboard.Key)!;
        Assert.IsNull(rig.Engine.GetSuggestion(record)?.Profile, "Suggested but not installed");

        rig.Inputs.Installed.Add(FakeInputs.Abnt2);
        rig.Engine.ReloadProfiles();
        Assert.AreEqual(FakeInputs.Abnt2.Id, rig.Engine.GetSuggestion(record)?.Profile?.Id);
    }

    [TestMethod]
    public void ManyDevices_KeepTheListBounded_WithoutDroppingSetUpKeyboards()
    {
        Rig rig = new Rig().Start();
        ConnectedKeyboard mine = rig.Plug(0x1111, 1);
        rig.SetUp(mine, FakeInputs.Abnt2);
        rig.Unplug(mine);

        // A device cycling through product ids
        for (int pid = 0; pid < 700; pid++)
        {
            ConnectedKeyboard device = FakeDevices.Usb(0xBAD0, pid);
            rig.Devices.Plug(device);
            rig.Engine.Refresh();
            rig.Devices.Unplug(device.Key);
            rig.Engine.Refresh();
        }

        Assert.IsLessThanOrEqualTo(SettingsStore.MaxKeyboards, rig.Settings.Keyboards.Count);
        Assert.IsNotNull(rig.Engine.Find(mine.Key), "Keyboards the user set up are kept");
    }

    [TestMethod]
    public void HostileDeviceNames_AreCleaned_AndSettingsStillSave()
    {
        Rig rig = new Rig().Start();
        rig.Devices.Names["1111:0001"] = "Evil\u0001‮Keyboard\r\nWindows Security: call 555￿\uD800";
        ConnectedKeyboard keyboard = rig.Plug(0x1111, 1);

        KeyboardRecord record = rig.Engine.Find(keyboard.Key)!;
        Assert.AreEqual("Evil Keyboard Windows Security: call 555", record.ProductName, "Controls become spaces; bidi and invalid characters go");
        Assert.IsNotNull(rig.Store.Reload(), "The saved file stays valid");

        rig.Engine.Configure(keyboard.Key, "Name\u0000with\u0007controls" + new string('x', 500), FakeInputs.Abnt2);
        Assert.IsLessThanOrEqualTo(TextSanitizer.MaxNameLength, record.FriendlyName!.Length);
        Assert.IsNotNull(rig.Store.Reload());
    }

    [TestMethod]
    public void NothingChanged_NothingSaved()
    {
        Rig rig = new Rig().Start();
        rig.Plug(0x1111, 1);
        int saves = rig.Store.Saves;

        for (int i = 0; i < 10; i++)
        {
            rig.Engine.Refresh();
        }
        Assert.AreEqual(saves, rig.Store.Saves);
    }

    [TestMethod]
    public void SettingsSurviveARestart()
    {
        Rig first = new Rig().Start();
        ConnectedKeyboard keyboard = first.Plug(0x1111, 1);
        first.Engine.Configure(keyboard.Key, "Desk", FakeInputs.Abnt2);
        first.Engine.SetDefaultLayout(FakeInputs.German);

        AppSettings saved = first.Store.Reload()!;
        Rig second = new Rig(saved);
        second.Devices.Plug(keyboard);
        second.Start();

        Assert.AreEqual(FakeInputs.Abnt2.Id, second.Inputs.CurrentId);
        Assert.AreEqual("Desk", second.Engine.Find(keyboard.Key)!.FriendlyName);
        Assert.IsEmpty(second.NewKeyboards);
        Assert.AreEqual(FakeInputs.German.Id, second.Settings.DefaultLayoutProfileId);
    }

    [TestMethod]
    public void SourceSelector_PrefersTheLatestSetUpKeyboard()
    {
        KeyboardRecord a = new() { Key = "1111:0001", State = KeyboardState.Configured };
        KeyboardRecord b = new() { Key = "2222:0002", State = KeyboardState.Configured };
        KeyboardRecord c = new() { Key = "3333:0003", State = KeyboardState.NotSetUp };
        KeyboardRecord rdp = new() { Key = ExternalKeyboards.RemoteDesktopKey, State = KeyboardState.Configured };
        Dictionary<string, long> order = new() { [a.Key] = 1, [b.Key] = 2, [c.Key] = 3 };

        Assert.AreSame(b, SourceSelector.Choose(new[] { a, b, c, rdp }, order, false));
        Assert.AreSame(rdp, SourceSelector.Choose(new[] { a, b, c, rdp }, order, true));
        Assert.IsNull(SourceSelector.Choose(new[] { c }, order, false));
    }
}
