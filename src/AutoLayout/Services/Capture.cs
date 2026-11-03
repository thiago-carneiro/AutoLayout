// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using AutoLayout.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AutoLayout.Services;

/// <summary>
/// With --demo --capture &lt;folder&gt;: renders the windows to PNG files (for the README and the
/// Store listing) and exits.
/// </summary>
internal static class Capture
{
    public const string Argument = "--capture";

    public static async Task RunAsync(App app, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            app.ShowMainWindow();
            await Task.Delay(2000);
            await SaveAsync(app.MainWindowForCapture?.Content, Path.Combine(folder, "keyboards.png"));

            app.ShowMainWindow(settingsPage: true);
            await Task.Delay(1500);
            await SaveAsync(app.MainWindowForCapture?.Content, Path.Combine(folder, "settings.png"));

            app.ShowKeyboardWindow(Demo.WaitingKey);
            await Task.Delay(1500);
            await SaveAsync(app.KeyboardWindowForCapture(Demo.WaitingKey)?.Content, Path.Combine(folder, "new-keyboard.png"));
        }
        catch (Exception error)
        {
            // A tool for screenshots: report anything, and still exit
            Log.Write("Capture failed: " + error);
        }
        app.Quit();
    }

    private static async Task SaveAsync(UIElement? element, string path)
    {
        if (element == null)
        {
            return;
        }
        RenderTargetBitmap bitmap = new();
        await bitmap.RenderAsync(element);
        IBuffer pixels = await bitmap.GetPixelsAsync();

        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        double scale = element.XamlRoot?.RasterizationScale ?? 1.0;
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96 * scale, 96 * scale, pixels.ToArray());
        await encoder.FlushAsync();

        byte[] png = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, png);
    }
}
