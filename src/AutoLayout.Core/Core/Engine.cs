// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace AutoLayout.Core;

public enum ProblemKind
{
    /// <summary>A keyboard's layout is no longer installed.</summary>
    LayoutMissing,

    /// <summary>The default layout is no longer installed.</summary>
    DefaultMissing,

    /// <summary>Windows refused to change the default input method.</summary>
    ApplyFailed,
}

/// <param name="Kind">What went wrong.</param>
/// <param name="Keyboard">The keyboard concerned, or null for the default layout.</param>
/// <param name="LayoutName">Name of the layout that could not be used.</param>
public sealed record Problem(ProblemKind Kind, KeyboardRecord? Keyboard, string LayoutName);

/// <param name="Layout">The layout now applied.</param>
/// <param name="Keyboard">The keyboard whose layout it is, or null for the default layout.</param>
/// <param name="Disconnected">When going back to the default layout, the keyboard that was disconnected.</param>
public sealed record LayoutChange(InputProfile Layout, KeyboardRecord? Keyboard, KeyboardRecord? Disconnected);

/// <summary>
/// Decides which layout to use from the keyboards connected and applies it. Not thread-safe:
/// use it from one thread (the UI thread).
/// </summary>
public sealed class Engine
{
    private readonly AppSettings settings;
    private readonly IDeviceSource devices;
    private readonly IInputSystem inputs;
    private readonly ISettingsStore store;

    private readonly Dictionary<string, ConnectedKeyboard> connected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> connectionOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LayoutSuggestion> suggestions = new(StringComparer.Ordinal);
    private readonly HashSet<string> problemsShown = new(StringComparer.Ordinal);
    private readonly List<string> setUpQueue = new();
    private IReadOnlyList<InputProfile> profiles = Array.Empty<InputProfile>();

    private long nextOrder;
    private string? appliedProfileId;
    private bool remoteSession;
    private bool started;
    private bool busy;
    private bool refreshAgain;

    public Engine(AppSettings settings, IDeviceSource devices, IInputSystem inputs, ISettingsStore store)
    {
        this.settings = settings;
        this.devices = devices;
        this.inputs = inputs;
        this.store = store;
    }

    /// <summary>Something shown in the UI changed.</summary>
    public event EventHandler? Changed;

    /// <summary>A keyboard that is not set up was connected.</summary>
    public event EventHandler<KeyboardRecord>? NewKeyboard;

    /// <summary>A keyboard was forgotten: windows showing it should close.</summary>
    public event EventHandler<string>? KeyboardForgotten;

    public event EventHandler<LayoutChange>? LayoutApplied;

    public event EventHandler<Problem>? ProblemOccurred;

    /// <summary>Windows could not list the keyboards now: check again shortly.</summary>
    public event EventHandler? RetryNeeded;

    public AppSettings Settings => settings;

    public IReadOnlyList<KeyboardRecord> Keyboards => settings.Keyboards;

    public IReadOnlyList<InputProfile> InstalledProfiles => profiles;

    /// <summary>Key of the keyboard whose layout applies, or null for the default layout.</summary>
    public string? ActiveKeyboardKey { get; private set; }

    /// <summary>True while a Remote Desktop session leaves the layout to Windows.</summary>
    public bool IsRemoteAutomatic { get; private set; }

    /// <summary>The current problem, or null when the right layout is applied.</summary>
    public Problem? CurrentProblem { get; private set; }

    public InputProfile? AppliedProfile => FindProfile(appliedProfileId);

    public InputProfile? DefaultProfile => FindProfile(settings.DefaultLayoutProfileId);

    /// <summary>Keyboards waiting to be set up, in the order they were connected.</summary>
    public IReadOnlyList<KeyboardRecord> SetUpQueue => setUpQueue.ConvertAll(Find).FindAll(r => r != null)!;

    /// <summary>Connected keyboards that are not set up (including postponed ones).</summary>
    public IReadOnlyList<KeyboardRecord> AwaitingSetUp =>
        settings.Keyboards.FindAll(r => r.State == KeyboardState.NotSetUp && connected.ContainsKey(r.Key));

    public bool IsConnected(KeyboardRecord keyboard) =>
        connected.ContainsKey(keyboard.Key) || (keyboard.Key == ExternalKeyboards.RemoteDesktopKey && remoteSession);

    public long GetConnectionOrder(KeyboardRecord keyboard) =>
        connectionOrder.TryGetValue(keyboard.Key, out long order) ? order : 0;

    public LayoutSuggestion? GetSuggestion(KeyboardRecord keyboard) =>
        suggestions.TryGetValue(keyboard.Key, out LayoutSuggestion? suggestion) ? suggestion : null;

    public KeyboardRecord? Find(string key) => settings.Keyboards.Find(r => r.Key == key);

    public InputProfile? FindProfile(string? id) =>
        id == null ? null : ((List<InputProfile>)AsList(profiles)).Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads the installed layouts, picks up the keyboards connected and applies the layout.</summary>
    public void Start()
    {
        profiles = inputs.GetInstalled();
        if (FindProfile(settings.DefaultLayoutProfileId) == null && settings.DefaultLayoutProfileId == null)
        {
            // First run: keep the default the user already has
            settings.DefaultLayoutProfileId = inputs.GetDefaultProfileId(profiles);
            Save();
        }
        started = true;
        Refresh();
    }

    /// <summary>Checks the keyboards connected and applies the layout that fits.</summary>
    public void Refresh()
    {
        if (!started)
        {
            return;
        }
        if (busy)
        {
            // Called back while applying (Windows dispatches messages then): do it right after
            refreshAgain = true;
            return;
        }

        busy = true;
        try
        {
            do
            {
                refreshAgain = false;
                RefreshOnce();
            }
            while (refreshAgain);
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Reads the installed layouts again (e.g. after the user added one in Settings).</summary>
    public void ReloadProfiles()
    {
        if (!started)
        {
            return;
        }
        IReadOnlyList<InputProfile> before = profiles;
        profiles = inputs.GetInstalled();
        if (SameIds(before, profiles))
        {
            return;
        }

        Log.Write("Installed layouts changed");
        foreach (KeyboardRecord keyboard in settings.Keyboards)
        {
            if (keyboard.State == KeyboardState.NotSetUp && connected.ContainsKey(keyboard.Key))
            {
                UpdateSuggestion(keyboard);
            }
        }
        RunUpdate(null);
    }

    public void SetDefaultLayout(InputProfile profile)
    {
        settings.DefaultLayoutProfileId = profile.Id;
        Save();
        Log.Write("Default layout set to " + profile.Id);
        RunUpdate(null);
    }

    /// <summary>Sets up or changes a keyboard's layout and name.</summary>
    public void Configure(string key, string? friendlyName, InputProfile profile)
    {
        KeyboardRecord? keyboard = Find(key);
        if (keyboard == null)
        {
            return;
        }

        bool wasSetUp = keyboard.State == KeyboardState.Configured;
        SetName(keyboard, friendlyName);
        keyboard.LayoutProfileId = profile.Id;
        keyboard.LayoutKlid = profile.Klid;
        keyboard.State = KeyboardState.Configured;
        setUpQueue.Remove(key);
        suggestions.Remove(key);

        // A keyboard set up now becomes the most recent one; editing one doesn't change which applies
        if (!wasSetUp && connected.ContainsKey(key))
        {
            connectionOrder[key] = ++nextOrder;
        }
        Save();
        Log.Write("Keyboard " + key + " set to " + profile.Id);
        RunUpdate(null);
    }

    /// <summary>Leaves the Remote Desktop layout to Windows (the other computer's layout).</summary>
    public void SetRemoteAutomatic(string? friendlyName)
    {
        KeyboardRecord keyboard = EnsureRemoteRecord(DateTime.UtcNow);
        SetName(keyboard, friendlyName);
        keyboard.LayoutProfileId = SettingsStore.AutomaticProfileId;
        keyboard.LayoutKlid = null;
        keyboard.State = KeyboardState.Configured;
        Save();
        RunUpdate(null);
    }

    public void Rename(string key, string? friendlyName)
    {
        KeyboardRecord? keyboard = Find(key);
        if (keyboard == null)
        {
            return;
        }
        SetName(keyboard, friendlyName);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetIgnored(string key, bool ignored)
    {
        KeyboardRecord? keyboard = Find(key);
        if (keyboard == null || keyboard.Key == ExternalKeyboards.RemoteDesktopKey)
        {
            return;
        }

        if (ignored)
        {
            keyboard.State = KeyboardState.Ignored;
            setUpQueue.Remove(key);
        }
        else
        {
            keyboard.State = keyboard.LayoutProfileId != null ? KeyboardState.Configured : KeyboardState.NotSetUp;
            if (keyboard.State == KeyboardState.NotSetUp && connected.ContainsKey(key) && !setUpQueue.Contains(key))
            {
                UpdateSuggestion(keyboard);
                setUpQueue.Add(key);
            }
        }
        Save();
        Log.Write("Keyboard " + key + (ignored ? " ignored" : " no longer ignored"));
        RunUpdate(null);
    }

    /// <summary>"Not now": stops asking until the keyboard is connected again. It stays in the tray menu.</summary>
    public void Postpone(string key)
    {
        if (setUpQueue.Remove(key))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Forgets a keyboard. A connected keyboard stays listed as not set up (without asking again
    /// now); the record object is kept, so open windows never act on a stale copy.
    /// </summary>
    public void Forget(string key)
    {
        KeyboardRecord? keyboard = Find(key);
        if (keyboard == null)
        {
            return;
        }

        setUpQueue.Remove(key);
        suggestions.Remove(key);
        problemsShown.Remove(key);
        if (IsConnected(keyboard))
        {
            keyboard.State = keyboard.Key == ExternalKeyboards.RemoteDesktopKey ? KeyboardState.Configured : KeyboardState.NotSetUp;
            keyboard.FriendlyName = null;
            keyboard.LayoutProfileId = keyboard.Key == ExternalKeyboards.RemoteDesktopKey ? SettingsStore.AutomaticProfileId : null;
            keyboard.LayoutKlid = null;
            keyboard.FirstSeenUtc = DateTime.UtcNow;
        }
        else
        {
            settings.Keyboards.Remove(keyboard);
        }
        Save();
        Log.Write("Keyboard " + key + " forgotten");
        KeyboardForgotten?.Invoke(this, key);
        RunUpdate(null);
    }

    private void RefreshOnce()
    {
        IReadOnlyList<ConnectedKeyboard>? current = devices.GetConnected();
        if (current == null)
        {
            // Not "everything was unplugged": just try again shortly
            Log.Write("Keyboards could not be listed; checking again");
            RetryNeeded?.Invoke(this, EventArgs.Empty);
            return;
        }

        DateTime now = DateTime.UtcNow;
        bool dirty = false;
        KeyboardRecord? disconnected = null;
        Dictionary<string, ConnectedKeyboard> present = new(StringComparer.Ordinal);
        foreach (ConnectedKeyboard device in current)
        {
            present[device.Key] = device;
        }

        foreach (string key in new List<string>(connected.Keys))
        {
            if (present.ContainsKey(key))
            {
                continue;
            }
            connected.Remove(key);
            connectionOrder.Remove(key);
            setUpQueue.Remove(key);
            KeyboardRecord? record = Find(key);
            if (record != null)
            {
                record.LastSeenUtc = now;
                dirty = true;
                if (key == ActiveKeyboardKey)
                {
                    disconnected = record;
                }
            }
            Log.Write("Keyboard disconnected: " + key);
        }

        List<KeyboardRecord> arrived = new();
        foreach (ConnectedKeyboard device in current)
        {
            if (connected.ContainsKey(device.Key))
            {
                continue;
            }
            connected[device.Key] = device;
            connectionOrder[device.Key] = ++nextOrder;

            KeyboardRecord? record = Find(device.Key);
            if (record == null)
            {
                record = CreateRecord(device, now);
                AddRecord(record);
            }
            record.LastSeenUtc = now;
            record.Connection = device.Connection;
            // Device names are untrusted: clean them whatever the source did
            record.ProductName ??= TextSanitizer.CleanName(devices.GetProductName(device));
            dirty = true;
            Log.Write("Keyboard connected: " + device.Key + " (" + (record.ProductName ?? "no name") + ", " + device.Connection + ")");

            if (record.State == KeyboardState.NotSetUp && !setUpQueue.Contains(record.Key))
            {
                UpdateSuggestion(record);
                setUpQueue.Add(record.Key);
                arrived.Add(record);
            }
        }

        bool remote = devices.IsRemoteSession;
        if (remote != remoteSession)
        {
            remoteSession = remote;
            EnsureRemoteRecord(now).LastSeenUtc = now;
            dirty = true;
            Log.Write(remote ? "Remote Desktop session started" : "Remote Desktop session ended");
        }

        if (dirty)
        {
            Save();
        }
        UpdateLayout(disconnected);

        foreach (KeyboardRecord record in arrived)
        {
            NewKeyboard?.Invoke(this, record);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies the layout, guarding against being called back while applying.</summary>
    private void RunUpdate(KeyboardRecord? disconnected)
    {
        if (busy)
        {
            refreshAgain = true;
            return;
        }
        busy = true;
        try
        {
            UpdateLayout(disconnected);
        }
        finally
        {
            busy = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (refreshAgain)
        {
            Refresh();
        }
    }

    private void UpdateLayout(KeyboardRecord? disconnected)
    {
        KeyboardRecord? source = SourceSelector.Choose(settings.Keyboards, connectionOrder, remoteSession);
        string? previousKey = ActiveKeyboardKey;
        ActiveKeyboardKey = source?.Key;

        if (source != null && source.LayoutProfileId == SettingsStore.AutomaticProfileId)
        {
            // Remote Desktop with the other computer's layout: Windows sets it. Apply again
            // whatever fits when the session is local again.
            IsRemoteAutomatic = true;
            appliedProfileId = null;
            CurrentProblem = null;
            return;
        }
        bool wasRemoteAutomatic = IsRemoteAutomatic;
        IsRemoteAutomatic = false;

        string? profileId = source != null ? source.LayoutProfileId : settings.DefaultLayoutProfileId;
        if (profileId == null)
        {
            return;
        }

        InputProfile? profile = FindProfile(profileId);
        if (profile == null)
        {
            // Leave the layout as it is, and tell the user once
            if (source != null)
            {
                Report(new Problem(ProblemKind.LayoutMissing, source, LayoutName(source.LayoutKlid, profileId)), source.Key);
            }
            else
            {
                Report(new Problem(ProblemKind.DefaultMissing, null, LayoutName(null, profileId)), "(default)");
            }
            return;
        }

        if (string.Equals(appliedProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            ClearProblem();
            return;
        }

        bool announce = appliedProfileId != null || wasRemoteAutomatic || previousKey != null;
        if (!inputs.Apply(profile))
        {
            Report(new Problem(ProblemKind.ApplyFailed, source, profile.LayoutName), "(apply)");
            return;
        }

        appliedProfileId = profile.Id;
        ClearProblem();
        Log.Write("Applied " + profile.Id + (source != null ? " for " + source.Key : " (default)"));
        if (announce)
        {
            LayoutApplied?.Invoke(this, new LayoutChange(profile, source, source == null ? disconnected : null));
        }
    }

    private void Report(Problem problem, string shownKey)
    {
        CurrentProblem = problem;
        Log.Write("Problem: " + problem.Kind + " (" + problem.LayoutName + ")");
        if (problemsShown.Add(shownKey))
        {
            ProblemOccurred?.Invoke(this, problem);
        }
    }

    private void ClearProblem()
    {
        // Solved: the next time it happens is news again
        CurrentProblem = null;
        problemsShown.Clear();
    }

    private static string LayoutName(string? klid, string profileId)
    {
        if (klid == null && profileId.Length >= 13 && profileId[4] == ':')
        {
            string candidate = profileId.Substring(5);
            klid = InputProfiles.IsValidKlid(candidate) ? candidate : null;
        }
        return (klid != null ? InputProfiles.GetLayoutName(klid) : null) ?? profileId;
    }

    private void UpdateSuggestion(KeyboardRecord keyboard)
    {
        if (keyboard.Connection == KeyboardConnection.RemoteDesktop)
        {
            return;
        }
        LayoutSuggestion? suggestion = LayoutSuggester.Suggest(keyboard, profiles);
        if (suggestion != null)
        {
            suggestions[keyboard.Key] = suggestion;
        }
        else
        {
            suggestions.Remove(keyboard.Key);
        }
    }

    private KeyboardRecord EnsureRemoteRecord(DateTime now)
    {
        KeyboardRecord? record = Find(ExternalKeyboards.RemoteDesktopKey);
        if (record == null)
        {
            // Nothing to ask: by default Windows uses the other computer's layout
            record = new KeyboardRecord
            {
                Key = ExternalKeyboards.RemoteDesktopKey,
                Connection = KeyboardConnection.RemoteDesktop,
                State = KeyboardState.Configured,
                LayoutProfileId = SettingsStore.AutomaticProfileId,
                FirstSeenUtc = now,
                LastSeenUtc = now,
            };
            AddRecord(record);
        }
        return record;
    }

    private void AddRecord(KeyboardRecord record)
    {
        if (settings.Keyboards.Count >= SettingsStore.MaxKeyboards)
        {
            // Keep the list bounded (a device cycling through ids could grow it forever):
            // drop the oldest disconnected keyboard that is not set up
            KeyboardRecord? oldest = null;
            foreach (KeyboardRecord candidate in settings.Keyboards)
            {
                if (candidate.State != KeyboardState.Configured && !connected.ContainsKey(candidate.Key)
                    && (oldest == null || candidate.LastSeenUtc < oldest.LastSeenUtc))
                {
                    oldest = candidate;
                }
            }
            if (oldest != null)
            {
                settings.Keyboards.Remove(oldest);
            }
        }
        settings.Keyboards.Add(record);
    }

    private static KeyboardRecord CreateRecord(ConnectedKeyboard device, DateTime now) => new()
    {
        Key = device.Key,
        VendorId = device.VendorId,
        ProductId = device.ProductId,
        Connection = device.Connection,
        State = KeyboardState.NotSetUp,
        FirstSeenUtc = now,
        LastSeenUtc = now,
    };

    private static void SetName(KeyboardRecord keyboard, string? friendlyName)
    {
        string? name = TextSanitizer.CleanName(friendlyName);
        keyboard.FriendlyName = name == keyboard.ProductName ? null : name;
    }

    private void Save() => store.Save(settings);

    private static bool SameIds(IReadOnlyList<InputProfile> a, IReadOnlyList<InputProfile> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (InputProfile profile in a)
        {
            ids.Add(profile.Id);
        }
        foreach (InputProfile profile in b)
        {
            if (!ids.Contains(profile.Id))
            {
                return false;
            }
        }
        return true;
    }

    private static IReadOnlyList<InputProfile> AsList(IReadOnlyList<InputProfile> profiles) =>
        profiles as List<InputProfile> ?? new List<InputProfile>(profiles);
}
