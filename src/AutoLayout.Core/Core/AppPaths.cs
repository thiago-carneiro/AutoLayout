// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace AutoLayout.Core;

public static class AppPaths
{
    /// <summary>%LocalAppData%\AutoLayout, created on first use.</summary>
    public static string DataDirectory
    {
        get
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoLayout");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
