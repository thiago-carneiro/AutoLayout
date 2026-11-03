// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.RegularExpressions;
using AutoLayout.Core.Native;
using Microsoft.Win32;

namespace AutoLayout.Core;

/// <summary>
/// An input profile the user has in Windows: a keyboard layout or an input method (IME)
/// under a language, as listed in Settings › Time &amp; language › Language &amp; region.
/// </summary>
/// <param name="Id">"0416:00020409" (layout) or "0411:{clsid}{profile}" (IME), as Windows names it.</param>
/// <param name="LanguageId">Language the profile is installed under.</param>
/// <param name="Klid">Layout identifier (e.g. "00010416"), or null for an IME.</param>
/// <param name="Handle">Layout handle to send to windows when switching.</param>
/// <param name="LayoutName">Localized name of the layout or IME.</param>
/// <param name="LanguageName">Localized name of the language, or null.</param>
public sealed partial record InputProfile(string Id, int LanguageId, string? Klid, IntPtr Handle, string LayoutName, string? LanguageName);

/// <summary>
/// Reads the installed input profiles and switches the default one.
/// </summary>
public interface IInputSystem
{
    IReadOnlyList<InputProfile> GetInstalled();

    /// <summary>Id of the user's current default input profile, or null.</summary>
    string? GetDefaultProfileId(IReadOnlyList<InputProfile> installed);

    /// <summary>
    /// Makes the profile the default input method (new windows, and after the lock screen) and
    /// switches the foreground window. False if Windows refused to change the default.
    /// </summary>
    bool Apply(InputProfile profile);
}

public sealed class InputProfiles : IInputSystem
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\";
    private const string TipKey = @"SOFTWARE\Microsoft\CTF\TIP\";

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Regex ProfileIdPattern = new(
        @"^[0-9A-F]{4}:([0-9A-F]{8}|\{[0-9A-F-]{36}\}\{[0-9A-F-]{36}\})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsValidId(string? id) => id != null && ProfileIdPattern.IsMatch(id);

    public static bool IsValidKlid(string? klid) =>
        klid != null && klid.Length == 8 && uint.TryParse(klid, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);

    public IReadOnlyList<InputProfile> GetInstalled()
    {
        List<InputProfile> profiles = new();
        NativeMethods.ITfInputProcessorProfileMgr? manager = null;
        NativeMethods.IEnumTfInputProcessorProfiles? enumerator = null;
        try
        {
            if (NativeMethods.CoCreateInstance(NativeMethods.ClsidInputProcessorProfiles, IntPtr.Zero, NativeMethods.CLSCTX_INPROC_SERVER,
                NativeMethods.IidInputProcessorProfileMgr, out IntPtr instance) != 0 || instance == IntPtr.Zero)
            {
                return profiles;
            }
            try
            {
                manager = (NativeMethods.ITfInputProcessorProfileMgr)ComWrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
            }
            finally
            {
                Marshal.Release(instance);
            }

            if (manager.EnumProfiles(0, out enumerator) != 0 || enumerator == null)
            {
                return profiles;
            }

            NativeMethods.TfInputProcessorProfile[] batch = new NativeMethods.TfInputProcessorProfile[16];
            while (enumerator.Next(batch.Length, batch, out int fetched) >= 0 && fetched > 0)
            {
                for (int i = 0; i < fetched; i++)
                {
                    InputProfile? profile = Describe(batch[i]);
                    if (profile != null && !profiles.Exists(p => p.Id == profile.Id))
                    {
                        profiles.Add(profile);
                    }
                }
            }
        }
        catch (COMException error)
        {
            Log.Write("Input profiles could not be listed: " + error.Message);
        }
        finally
        {
            // Release now rather than at the next garbage collection
            ((object?)enumerator as ComObject)?.FinalRelease();
            ((object?)manager as ComObject)?.FinalRelease();
        }
        return profiles;
    }

    public string? GetDefaultProfileId(IReadOnlyList<InputProfile> installed)
    {
        IntPtr handle = IntPtr.Zero;
        if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDEFAULTINPUTLANG, 0, ref handle, 0) || handle == IntPtr.Zero)
        {
            return null;
        }
        foreach (InputProfile profile in installed)
        {
            if (profile.Handle == handle)
            {
                return profile.Id;
            }
        }
        // An IME is the default: match its language
        int language = unchecked((ushort)handle.ToInt64());
        return installed.FirstOrDefaultId(p => p.Klid == null && p.LanguageId == language);
    }

    public bool Apply(InputProfile profile)
    {
        bool defaultChanged = NativeMethods.SetDefaultLayoutOrTip(profile.Id, 0);

        // Windows already open keep their layout until asked. Elevated windows ignore the
        // request, which is fine: Win+Space still works there.
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            NativeMethods.SendMessageTimeout(foreground, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, profile.Handle,
                NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        }
        uint recipients = NativeMethods.BSM_APPLICATIONS;
        _ = NativeMethods.BroadcastSystemMessage(NativeMethods.BSF_POSTMESSAGE, ref recipients,
            NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, profile.Handle);
        return defaultChanged;
    }

    /// <summary>Localized name of a keyboard layout, even one that is not installed for the user.</summary>
    public static string? GetLayoutName(string klid)
    {
        if (!IsValidKlid(klid))
        {
            return null;
        }
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(LayoutsKey + klid);
        if (key == null)
        {
            return null;
        }
        return LoadIndirect(key.GetValue("Layout Display Name") as string) ?? key.GetValue("Layout Text") as string;
    }

    public static string? GetLanguageName(int languageId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static InputProfile? Describe(NativeMethods.TfInputProcessorProfile profile)
    {
        if ((profile.Flags & NativeMethods.TF_IPP_FLAG_ENABLED) == 0)
        {
            return null;
        }

        int language = profile.LanguageId;
        string languageCode = language.ToString("X4", CultureInfo.InvariantCulture);
        string? languageName = GetLanguageName(language);

        if (profile.ProfileType == NativeMethods.TF_PROFILETYPE_KEYBOARDLAYOUT)
        {
            string? klid = KlidFromHandle(profile.Layout);
            if (klid == null)
            {
                return null;
            }
            string name = GetLayoutName(klid) ?? klid;
            return new InputProfile(languageCode + ":" + klid, language, klid, profile.Layout, name, languageName);
        }

        if (profile.ProfileType == NativeMethods.TF_PROFILETYPE_INPUTPROCESSOR)
        {
            string clsid = profile.Clsid.ToString("B").ToUpperInvariant();
            string guid = profile.Profile.ToString("B").ToUpperInvariant();
            string name = GetTipName(clsid, language, guid) ?? languageName ?? clsid;
            // Switching windows to the language selects its input method
            IntPtr handle = new((language << 16) | language);
            return new InputProfile(languageCode + ":" + clsid + guid, language, null, handle, name, languageName);
        }
        return null;
    }

    /// <summary>
    /// Layout identifier of a layout handle, from the registry: no layout is activated to find it.
    /// </summary>
    internal static string? KlidFromHandle(IntPtr handle)
    {
        int high = (int)((handle.ToInt64() >> 16) & 0xFFFF);
        if ((high & 0xF000) == 0xF000)
        {
            // A layout variant: its "Layout Id" is the low 12 bits
            string layoutId = (high & 0x0FFF).ToString("X4", CultureInfo.InvariantCulture);
            using RegistryKey? layouts = Registry.LocalMachine.OpenSubKey(LayoutsKey.TrimEnd('\\'));
            if (layouts == null)
            {
                return null;
            }
            foreach (string klid in layouts.GetSubKeyNames())
            {
                using RegistryKey? layout = layouts.OpenSubKey(klid);
                if (layout?.GetValue("Layout Id") is string id && string.Equals(id, layoutId, StringComparison.OrdinalIgnoreCase))
                {
                    return klid.ToUpperInvariant();
                }
            }
            return null;
        }
        if ((high & 0xF000) == 0xE000)
        {
            // Legacy IME layout handle
            return null;
        }
        return "0000" + high.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static string? GetTipName(string clsid, int language, string profile)
    {
        string path = TipKey + clsid + @"\LanguageProfile\0x" + language.ToString("x8", CultureInfo.InvariantCulture) + @"\" + profile;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
        if (key == null)
        {
            return null;
        }
        return LoadIndirect(key.GetValue("Display Description") as string) ?? key.GetValue("Description") as string;
    }

    private static string? LoadIndirect(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }
        if (!source.StartsWith('@'))
        {
            return source;
        }
        char[] text = new char[256];
        if (NativeMethods.SHLoadIndirectString(source, text, text.Length, IntPtr.Zero) != 0)
        {
            return null;
        }
        int end = Array.IndexOf(text, '\0');
        return new string(text, 0, end >= 0 ? end : text.Length);
    }
}

internal static class InputProfileListExtensions
{
    public static string? FirstOrDefaultId(this IReadOnlyList<InputProfile> profiles, Predicate<InputProfile> match)
    {
        foreach (InputProfile profile in profiles)
        {
            if (match(profile))
            {
                return profile.Id;
            }
        }
        return null;
    }
}
