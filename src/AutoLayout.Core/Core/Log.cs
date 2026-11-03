// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoLayout.Core;

/// <summary>
/// Plain-text log in the app's data folder, off unless the user turns it on, and kept under a
/// few hundred KB. Only device ids, device names and layout ids are logged — never input.
/// </summary>
public static class Log
{
    private const long MaxSize = 256 * 1024;
    private static readonly object Sync = new();
    private static volatile bool enabled;

    public static bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "autolayout.log");

    public static void Write(string message)
    {
        if (!enabled)
        {
            return;
        }
        try
        {
            lock (Sync)
            {
                string path = FilePath;
                FileInfo info = new(path);
                if (info.Exists && info.Length > MaxSize)
                {
                    // Keep the previous log as a single backup
                    string backup = path + ".1";
                    File.Delete(backup);
                    File.Move(path, backup);
                }

                // One event per line: names from devices cannot fake other lines
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  "
                    + TextSanitizer.ForLog(message) + Environment.NewLine;
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Logging must never break the app
        }
    }
}
