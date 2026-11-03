// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Security.Principal;
using System.Threading;
using AutoLayout.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AutoLayout;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Never load DLLs from the current directory (the app's own P/Invokes use System32 only).
        // A stricter search order is not possible: the Windows App SDK loads its resources by name.
        Shell.SetDllDirectory(string.Empty);
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One instance per user: a second launch (or a click on a notification while the app
        // runs) is handed to the running instance
        string user = WindowsIdentity.GetCurrent().User?.Value ?? "user";
        bool demo = Array.IndexOf(args, Services.Demo.Argument) >= 0;
        string mode = demo ? "demo-" : string.Empty;

        // Screenshots in another language: --demo --lang de-DE
        int language = Array.IndexOf(args, "--lang");
        if (demo && language >= 0 && language + 1 < args.Length)
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = args[language + 1];
        }
        AppInstance instance = AppInstance.FindOrRegisterForKey("AutoLayout-" + mode + user);
        if (!instance.IsCurrent)
        {
            AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Thread redirect = new(() => instance.RedirectActivationToAsync(activation).AsTask().Wait());
            redirect.Start();
            redirect.Join(TimeSpan.FromSeconds(5));
            return 0;
        }

        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args, instance);
        });
        return 0;
    }
}
