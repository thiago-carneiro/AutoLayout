// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using AutoLayout.Core;
using AutoLayout.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AutoLayout.Services;

/// <summary>
/// With --demo --capture &lt;folder&gt;: renders every window, page state, menu and dialog,
/// and the tray icons, to PNG files (for the README, the Store listing and design reviews),
/// and exits. Add --theme light for the light theme and --lang for another language.
/// </summary>
internal static class Capture
{
    public const string Argument = "--capture";

    private const int ShortWait = 700;
    private const int LongWait = 1500;

    public static async Task RunAsync(App app, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string Path(string name) => System.IO.Path.Combine(folder, name + ".png");

            // Keyboards page: top, end of the list, and the problem state
            app.ShowMainWindow();
            await Task.Delay(2000);
            UIElement main = app.MainWindowForCapture!.Content;
            await SaveAsync(main, Path("keyboards"));

            ScrollPage(main, toEnd: true);
            await Task.Delay(ShortWait);
            await SaveAsync(main, Path("keyboards-end"));

            // Choosing a Remote Desktop layout reaches the engine (checks the item tags under Native AOT)
            if (Descendants(main).OfType<ComboBox>().FirstOrDefault(c => c.Name == "RemoteCombo") is ComboBox remote && remote.Items.Count > 1)
            {
                remote.SelectedIndex = remote.Items.Count - 1;
                await Task.Delay(ShortWait);
                Log.Write("Capture: Remote Desktop layout chosen: " + app.Engine.Find(ExternalKeyboards.RemoteDesktopKey)?.LayoutProfileId);
                app.Engine.SetRemoteLayout(null);
                await Task.Delay(ShortWait);
            }
            ScrollPage(main, toEnd: false);
            await Task.Delay(ShortWait);

            // The "…" menu of the first keyboard (its card's context menu), then the Forget dialog it opens
            List<UIElement> withMenus = Descendants(main).OfType<UIElement>().Where(e => e.ContextFlyout != null).ToList();
            Log.Write("Capture: " + withMenus.Count + " elements with a context menu: "
                + string.Join(", ", withMenus.Select(e => e.GetType().Name + "/" + e.ContextFlyout.GetType().Name).Distinct()));
            // (Native AOT gives flyouts back as FlyoutBase: no tests on the derived types here)
            if (withMenus.OfType<Border>().FirstOrDefault(b => Descendants(b).OfType<Button>().Any()) is Border card
                && Descendants(card).OfType<Button>().LastOrDefault() is Button more)
            {
                FlyoutBase menu = card.ContextFlyout;
                // Screenshots can't capture acrylic: a solid background of the same tone
                Style solid = new(typeof(MenuFlyoutPresenter));
                solid.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.Resources["SolidBackgroundFillColorQuarternaryBrush"]));
                WinRT.CastExtensions.As<MenuFlyout>(menu).MenuFlyoutPresenterStyle = solid;
                menu.ShowAt(more);
                await Task.Delay(ShortWait);
                await SavePopupsAsync(main.XamlRoot, Path("keyboard-menu"));

                // The Forget command: the item whose text is "Forget", clicked as a user would
                string forgetText = Loc.Get("Btn_Forget");
                TextBlock? label = OpenPopups(main.XamlRoot).SelectMany(Descendants).OfType<TextBlock>().LastOrDefault(t => t.Text == forgetText);
                DependencyObject? forget = label;
                while (forget != null && (forget is not UIElement element
                    || FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetPattern(PatternInterface.Invoke) == null))
                {
                    forget = VisualTreeHelper.GetParent(forget);
                }
                Log.Write("Capture: Forget command " + (forget != null ? "found" : "not found"));
                if (forget is UIElement item
                    && FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke) is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
                {
                    invoke.Invoke();
                    await Task.Delay(LongWait);
                    await SavePopupsAsync(main.XamlRoot, Path("forget-dialog"));
                    foreach (ContentDialog dialog in OpenPopups(main.XamlRoot).SelectMany(Descendants).OfType<ContentDialog>().ToList())
                    {
                        dialog.Hide();
                    }
                    await Task.Delay(ShortWait);
                }
            }

            // The list is rebuilt here: the focus must stay in it (logged, to check)
            string FocusName() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(main.XamlRoot) is FrameworkElement f
                ? f.GetType().Name + (f.DataContext is Views.KeyboardItem k ? " in row " + k.Key : string.Empty) : "none";
            // As a keyboard user would be: on the "…" button of the first row
            if (Descendants(main).OfType<Border>().FirstOrDefault(b => b.ContextFlyout != null) is Border firstRow)
            {
                Descendants(firstRow).OfType<Button>().LastOrDefault()?.Focus(FocusState.Keyboard);
            }
            await Task.Delay(ShortWait);
            string before = FocusName();
            Demo.LayoutMissing = true;
            app.Engine.ReloadProfiles();
            app.Engine.Refresh();
            await Task.Delay(LongWait);
            Log.Write("Capture: focus before the list was rebuilt: " + before + "; after: " + FocusName());
            await SaveAsync(main, Path("keyboards-problem"));
            Demo.LayoutMissing = false;
            app.Engine.ReloadProfiles();
            app.Engine.Refresh();
            await Task.Delay(ShortWait);

            // Settings page: top and end
            app.ShowMainWindow(settingsPage: true);
            await Task.Delay(LongWait);
            await SaveAsync(main, Path("settings"));
            ScrollPage(main, toEnd: true);
            await Task.Delay(ShortWait);
            await SaveAsync(main, Path("settings-end"));

            // The keyboard window: a new keyboard, a known one and Remote Desktop
            foreach ((string key, string name) in new[]
            {
                (Demo.WaitingKey, "new-keyboard"),
                (Demo.ConfiguredKey, "edit-keyboard"),
                (ExternalKeyboards.RemoteDesktopKey, "remote-desktop"),
            })
            {
                app.ShowKeyboardWindow(key);
                await Task.Delay(LongWait);
                await SaveAsync(app.KeyboardWindowForCapture(key)?.Content, Path(name));
            }

            await SaveTrayIconsAsync(Path("tray-icons"));
        }
        catch (Exception error)
        {
            // A tool for screenshots: report anything, and still exit
            Log.Write("Capture failed: " + error);
        }
        app.Quit();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>What the open popups show, without the dimming layer behind a dialog.</summary>
    private static IEnumerable<DependencyObject> OpenPopups(XamlRoot root) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(p => p.Child is not null and not Microsoft.UI.Xaml.Shapes.Rectangle)
            .Select(p => (DependencyObject)p.Child);

    /// <summary>Scrolls the page shown in the main window to its top or its end.</summary>
    private static void ScrollPage(UIElement window, bool toEnd)
    {
        if (Descendants(window).OfType<Frame>().FirstOrDefault()?.Content is DependencyObject page
            && Descendants(page).OfType<ScrollViewer>().FirstOrDefault() is ScrollViewer scroller)
        {
            scroller.ChangeView(null, toEnd ? scroller.ScrollableHeight : 0, null, disableAnimation: true);
        }
    }

    /// <summary>Saves what the popups (menus, dialogs) show, one file per popup.</summary>
    private static async Task SavePopupsAsync(XamlRoot root, string path)
    {
        List<DependencyObject> popups = OpenPopups(root).ToList();
        for (int i = 0; i < popups.Count; i++)
        {
            string file = i == 0 ? path : System.IO.Path.ChangeExtension(path, null) + "-" + i + ".png";
            await SaveAsync(popups[i] as UIElement, file);
        }
    }

    private static async Task SaveAsync(UIElement? element, string path)
    {
        if (element == null)
        {
            return;
        }
        // No keyboard focus ring in screenshots (WinUI may give the window's first button keyboard
        // focus): the focus goes, without a ring, to the first control of the content
        DependencyObject content = Descendants(element).OfType<Frame>().FirstOrDefault()?.Content as DependencyObject ?? element;
        DependencyObject? focused = element.XamlRoot != null
            ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(element.XamlRoot) as DependencyObject : null;
        while (focused != null && focused != content)
        {
            focused = VisualTreeHelper.GetParent(focused);
        }
        if (focused == null
            && Descendants(content).OfType<Control>().FirstOrDefault(c => c.IsTabStop && c.IsEnabled && c.Visibility == Visibility.Visible) is Control first)
        {
            first.Focus(FocusState.Programmatic);
            await Task.Delay(100);
        }
        RenderTargetBitmap bitmap = new();
        await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return;
        }
        IBuffer pixels = await bitmap.GetPixelsAsync();
        double scale = element.XamlRoot?.RasterizationScale ?? 1.0;
        await SavePngAsync(pixels.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight, scale, BitmapAlphaMode.Premultiplied, path);
    }

    /// <summary>
    /// The tray icon in its three states, on a light and on a dark taskbar, at 16, 20, 24 and 32
    /// pixels (100% to 200% scaling), each pixel enlarged 6 times so it can be inspected.
    /// </summary>
    private static async Task SaveTrayIconsAsync(string path)
    {
        int[] sizes = { 16, 20, 24, 32 };
        const int Zoom = 6;
        const int Cell = 32 * Zoom + 24;
        TrayState[] states = { TrayState.Normal, TrayState.Attention, TrayState.Error };
        int width = sizes.Length * Cell;
        int height = 2 * states.Length * Cell;
        byte[] image = new byte[width * height * 4];

        for (int theme = 0; theme < 2; theme++)
        {
            bool light = theme == 0;
            uint background = light ? 0xFFF3F3F3 : 0xFF1C1C1C;
            for (int s = 0; s < states.Length; s++)
            {
                int top = (theme * states.Length + s) * Cell;
                for (int c = 0; c < sizes.Length; c++)
                {
                    int size = sizes[c];
                    uint[] icon = TrayIconRenderer.Render(states[s], size, light);
                    int left = c * Cell;
                    int offset = (Cell - size * Zoom) / 2;
                    for (int y = 0; y < Cell; y++)
                    {
                        for (int x = 0; x < Cell; x++)
                        {
                            int ix = (x - offset) / Zoom;
                            int iy = (y - offset) / Zoom;
                            bool inside = x >= offset && y >= offset && ix < size && iy < size;
                            uint color = inside ? Over(icon[iy * size + ix], background) : background;
                            int at = ((top + y) * width + left + x) * 4;
                            image[at] = (byte)color;
                            image[at + 1] = (byte)(color >> 8);
                            image[at + 2] = (byte)(color >> 16);
                            image[at + 3] = 0xFF;
                        }
                    }
                }
            }
        }
        await SavePngAsync(image, width, height, 1.0, BitmapAlphaMode.Ignore, path);
    }

    /// <summary>A premultiplied pixel over an opaque background.</summary>
    private static uint Over(uint pixel, uint background)
    {
        uint alpha = pixel >> 24;
        uint Channel(int shift) => ((pixel >> shift) & 0xFF) + (((background >> shift) & 0xFF) * (255 - alpha) / 255);
        return 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static async Task SavePngAsync(byte[] bgra, int width, int height, double scale, BitmapAlphaMode alpha, string path)
    {
        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)width, (uint)height, 96 * scale, 96 * scale, bgra);
        await encoder.FlushAsync();

        byte[] png = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, png);
    }
}
