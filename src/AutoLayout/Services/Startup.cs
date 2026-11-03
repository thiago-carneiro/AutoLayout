// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using AutoLayout.Core;
using AutoLayout.Platform;
using Windows.ApplicationModel;

namespace AutoLayout.Services;

internal enum StartupChange
{
    Done,

    /// <summary>The user turned it off in Task Manager or Settings; only they can turn it back on.</summary>
    BlockedByUser,

    /// <summary>A policy decides it.</summary>
    BlockedByPolicy,

    Failed,
}

/// <summary>
/// "Start with Windows": a startup task when installed from the Store (MSIX), the Run key otherwise.
/// </summary>
internal static class Startup
{
    private const string TaskId = "AutoLayoutStartup";

    public static bool IsPackaged => Shell.IsPackaged;

    /// <summary>True when the unpackaged app runs from Downloads, Temp, removable or network drives.</summary>
    public static bool IsRiskyLocation => !IsPackaged && StartupRegistration.IsRiskyLocation;

    public static async Task<bool> IsEnabledAsync()
    {
        if (!IsPackaged)
        {
            return StartupRegistration.IsEnabled;
        }
        StartupTask task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public static async Task<StartupChange> SetEnabledAsync(bool enabled)
    {
        try
        {
            if (!IsPackaged)
            {
                StartupRegistration.SetEnabled(enabled);
                return StartupChange.Done;
            }

            StartupTask task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                return StartupChange.Done;
            }
            StartupTaskState state = await task.RequestEnableAsync();
            return state switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupChange.Done,
                StartupTaskState.DisabledByUser => StartupChange.BlockedByUser,
                StartupTaskState.DisabledByPolicy => StartupChange.BlockedByPolicy,
                _ => StartupChange.Failed,
            };
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            Log.Write("Start with Windows could not be changed: " + error.Message);
            return StartupChange.Failed;
        }
    }
}
