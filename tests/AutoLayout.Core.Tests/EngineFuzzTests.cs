// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoLayout.Core.Tests;

/// <summary>
/// Random sequences of everything that can happen — keyboards, mice and receivers plugged and
/// unplugged in bursts, the user answering, ignoring, forgetting or postponing, layouts
/// uninstalled, Windows refusing a switch or failing to list devices, Remote Desktop sessions,
/// devices changing while the engine is busy — with the engine's rules checked after each step.
/// </summary>
[TestClass]
public sealed class EngineFuzzTests
{
    private const int Seeds = 40;
    private const int StepsPerSeed = 3000;

    private static readonly InputProfile[] Choices =
        { FakeInputs.UsIntl, FakeInputs.Abnt2, FakeInputs.German, FakeInputs.JapaneseIme };

    [TestMethod]
    public void RandomSequences_KeepTheEngineConsistent()
    {
        for (int seed = 1; seed <= Seeds; seed++)
        {
            Run(seed);
        }
    }

    private static void Run(int seed)
    {
        Random random = new(seed);
        Rig rig = new();

        // A pool of devices: real keyboards, a mouse and a receiver that also report as keyboards
        List<ConnectedKeyboard> pool = new();
        for (int i = 0; i < 12; i++)
        {
            pool.Add(i % 3 == 0 ? FakeDevices.Bluetooth(0x046D, 0xB300 + i) : FakeDevices.Usb(0x1000 + i, 0x0100 + i));
        }

        rig.Start();
        string step = "start";
        try
        {
            for (int i = 0; i < StepsPerSeed; i++)
            {
                step = Act(random, rig, pool);
                CheckInvariants(rig, step);
            }
        }
        catch (Exception error) when (error is not AssertFailedException)
        {
            Assert.Fail($"Seed {seed}, after '{step}': {error}");
        }
    }

    private static string Act(Random random, Rig rig, List<ConnectedKeyboard> pool)
    {
        Engine engine = rig.Engine;
        ConnectedKeyboard device = pool[random.Next(pool.Count)];
        KeyboardRecord? record = engine.Keyboards.Count > 0 ? engine.Keyboards[random.Next(engine.Keyboards.Count)] : null;
        InputProfile profile = Choices[random.Next(Choices.Length)];

        switch (random.Next(22))
        {
            case 0:
            case 1:
            case 2:
                rig.Devices.Plug(device);
                engine.Refresh();
                return "plug " + device.Key;
            case 3:
            case 4:
                rig.Devices.Unplug(device.Key);
                engine.Refresh();
                return "unplug " + device.Key;
            case 5:
                // A burst: several devices change before the engine looks
                for (int n = random.Next(2, 6); n > 0; n--)
                {
                    ConnectedKeyboard other = pool[random.Next(pool.Count)];
                    if (random.Next(2) == 0)
                    {
                        rig.Devices.Plug(other);
                    }
                    else
                    {
                        rig.Devices.Unplug(other.Key);
                    }
                }
                engine.Refresh();
                return "burst";
            case 6:
            case 7:
                if (record != null)
                {
                    engine.Configure(record.Key, random.Next(4) == 0 ? "Name " + random.Next(100) : null, profile);
                }
                return "configure " + record?.Key + " " + profile.Id;
            case 8:
                if (record != null)
                {
                    engine.SetIgnored(record.Key, random.Next(2) == 0);
                }
                return "ignore toggle " + record?.Key;
            case 9:
                if (record != null)
                {
                    engine.Forget(record.Key);
                }
                return "forget " + record?.Key;
            case 10:
                if (record != null)
                {
                    engine.Postpone(record.Key);
                }
                return "postpone " + record?.Key;
            case 11:
                engine.SetDefaultLayout(profile);
                return "default " + profile.Id;
            case 12:
                // The user removes or adds a layout in Windows Settings
                if (rig.Inputs.Installed.Contains(profile) && rig.Inputs.Installed.Count > 1)
                {
                    rig.Inputs.Installed.Remove(profile);
                }
                else if (!rig.Inputs.Installed.Contains(profile))
                {
                    rig.Inputs.Installed.Add(profile);
                }
                engine.ReloadProfiles();
                return "toggle installed " + profile.Id;
            case 13:
                rig.Inputs.FailNext = true;
                rig.Devices.Plug(device);
                engine.Refresh();
                return "apply fails, plug " + device.Key;
            case 14:
                rig.Devices.FailNext = true;
                engine.Refresh();
                return "listing fails";
            case 15:
                rig.Devices.IsRemoteSession = !rig.Devices.IsRemoteSession;
                engine.Refresh();
                return "remote " + rig.Devices.IsRemoteSession;
            case 16:
                if (random.Next(2) == 0)
                {
                    engine.SetRemoteAutomatic(null);
                }
                else
                {
                    engine.Configure(ExternalKeyboards.RemoteDesktopKey, null, profile);
                }
                return "remote layout";
            case 17:
                // A device arrives while Windows is applying a layout, and the engine is called back
                rig.Inputs.DuringApply = () =>
                {
                    rig.Devices.Plug(pool[random.Next(pool.Count)]);
                    rig.Devices.Unplug(pool[random.Next(pool.Count)].Key);
                    engine.Refresh();
                    if (record != null)
                    {
                        engine.Configure(record.Key, null, profile);
                    }
                };
                rig.Devices.Plug(device);
                engine.Refresh();
                if (record != null)
                {
                    engine.Configure(record.Key, null, profile);
                }
                rig.Inputs.DuringApply = null;
                return "re-entrant apply";
            case 18:
                // A device is unplugged while the engine is listing devices
                rig.Devices.DuringListing = () => rig.Devices.Unplug(device.Key);
                engine.Refresh();
                return "unplug during listing " + device.Key;
            case 19:
                if (record != null)
                {
                    engine.Rename(record.Key, random.Next(2) == 0 ? null : "‮hostile\r\nname" + random.Next(1000));
                }
                return "rename";
            case 20:
                engine.ReloadProfiles();
                return "reload";
            default:
                engine.Refresh();
                return "refresh";
        }
    }

    private static void CheckInvariants(Rig rig, string step)
    {
        Engine engine = rig.Engine;
        IReadOnlyList<KeyboardRecord> keyboards = engine.Keyboards;

        // No duplicates, bounded list
        Assert.AreEqual(keyboards.Count, keyboards.Select(k => k.Key).Distinct().Count(), step + ": duplicate records");
        Assert.IsLessThanOrEqualTo(SettingsStore.MaxKeyboards, keyboards.Count, step);

        // The set-up queue only holds connected keyboards that are not set up, once each
        IReadOnlyList<KeyboardRecord> queue = engine.SetUpQueue;
        Assert.AreEqual(queue.Count, queue.Select(k => k.Key).Distinct().Count(), step + ": queue duplicates");
        foreach (KeyboardRecord queued in queue)
        {
            Assert.AreEqual(KeyboardState.NotSetUp, queued.State, step + ": queued keyboard is set up");
            Assert.IsTrue(engine.IsConnected(queued), step + ": queued keyboard is not connected");
        }

        // The keyboard shown as in use is the one the rules choose
        Dictionary<string, long> order = new();
        foreach (KeyboardRecord keyboard in keyboards)
        {
            if (rig.Devices.Connected.Exists(d => d.Key == keyboard.Key))
            {
                order[keyboard.Key] = engine.GetConnectionOrder(keyboard);
            }
        }
        KeyboardRecord? expected = SourceSelector.Choose(keyboards, order, rig.Devices.IsRemoteSession);
        if (!rig.Devices.FailNext)
        {
            Assert.AreEqual(expected?.Key, engine.ActiveKeyboardKey, step + ": wrong keyboard in use");
        }

        // With no problem reported, Windows has the layout the rules ask for
        string? wanted = expected?.LayoutProfileId ?? rig.Settings.DefaultLayoutProfileId;
        if (engine.CurrentProblem == null && !engine.IsRemoteAutomatic && wanted != null && wanted != SettingsStore.AutomaticProfileId)
        {
            Assert.AreEqual(wanted, rig.Inputs.CurrentId, step + ": Windows has another layout than the one applied");
        }

        // A problem always names a real situation
        if (engine.CurrentProblem?.Kind == ProblemKind.LayoutMissing)
        {
            Assert.IsNull(engine.FindProfile(engine.CurrentProblem.Keyboard?.LayoutProfileId), step + ": layout reported missing is installed");
        }

        // What is saved can always be read back, with every record intact
        AppSettings? reloaded = rig.Store.Reload();
        if (reloaded != null)
        {
            Assert.AreEqual(keyboards.Count, reloaded.Keyboards.Count, step + ": records lost when saving");
        }
    }
}
