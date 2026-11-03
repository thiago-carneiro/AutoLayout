// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Microsoft.Win32;

namespace AutoLayout.Core;

/// <summary>
/// "Start with Windows", through the current user's Run key (listed in Task Manager's
/// Startup apps). The state is read from the registry, so it is never out of date.
/// </summary>
public static class StartupRegistration
{
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoLayout";

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path");

    private static string Command => "\"" + ExecutablePath + "\" " + BackgroundArgument;

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// True when the app runs from a place where others can easily drop files next to it
    /// (Downloads, temporary folders, removable or network drives): starting it at every
    /// sign-in from there is risky.
    /// </summary>
    public static bool IsRiskyLocation
    {
        get
        {
            string folder = Path.GetDirectoryName(ExecutablePath) ?? string.Empty;
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string temp = Path.GetTempPath();
            if (IsUnder(folder, downloads) || IsUnder(folder, temp))
            {
                return true;
            }
            try
            {
                DriveInfo drive = new(Path.GetPathRoot(folder) ?? folder);
                return drive.DriveType is DriveType.Removable or DriveType.Network or DriveType.CDRom;
            }
            catch (ArgumentException)
            {
                // UNC paths have no drive letter: a network location
                return true;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static bool IsUnder(string path, string folder)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        string parent = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
