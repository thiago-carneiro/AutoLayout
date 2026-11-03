// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace AutoLayout.Core;

/// <summary>
/// Decides which keyboard's layout applies. Pure, so the rules can be tested on their own.
/// </summary>
public static class SourceSelector
{
    /// <summary>
    /// In a Remote Desktop session, the Remote Desktop entry when it is set up; otherwise the
    /// set-up keyboard connected last. Null means the default layout.
    /// </summary>
    /// <param name="keyboards">All known keyboards.</param>
    /// <param name="connectionOrder">Connected keyboards and when they were connected (higher = later).</param>
    /// <param name="isRemoteSession">True while the session is used through Remote Desktop.</param>
    public static KeyboardRecord? Choose(IEnumerable<KeyboardRecord> keyboards, IReadOnlyDictionary<string, long> connectionOrder, bool isRemoteSession)
    {
        KeyboardRecord? latest = null;
        long latestOrder = long.MinValue;
        foreach (KeyboardRecord keyboard in keyboards)
        {
            if (keyboard.State != KeyboardState.Configured)
            {
                continue;
            }
            if (keyboard.Key == ExternalKeyboards.RemoteDesktopKey)
            {
                if (isRemoteSession)
                {
                    return keyboard;
                }
                continue;
            }
            if (connectionOrder.TryGetValue(keyboard.Key, out long order) && order > latestOrder)
            {
                latest = keyboard;
                latestOrder = order;
            }
        }
        return latest;
    }
}
