// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoLayout.Platform;

internal enum TrayState
{
    Normal,
    Attention,
    Error,
}

/// <summary>
/// Draws the tray icon pixel by pixel, aligned to whole pixels at every size, so it stays
/// crisp: a keyboard outline with keys, in the taskbar's text color, plus a round badge
/// (new keyboard waiting) or a triangular one (problem) — different shapes, not just colors.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>Creates the icon; the caller destroys it with Shell.DestroyIcon.</summary>
    public static IntPtr Create(TrayState state, int size)
    {
        bool lightTaskbar = IsTaskbarLight();
        uint ink = lightTaskbar ? 0xFF1C1C1C : 0xFFFFFFFF;
        uint[] pixels = new uint[size * size];

        DrawKeyboard(pixels, size, ink);
        if (state == TrayState.Attention)
        {
            DrawBadge(pixels, size, lightTaskbar ? 0xFF005FB8 : 0xFF4CC2FF, round: true);
        }
        else if (state == TrayState.Error)
        {
            DrawBadge(pixels, size, lightTaskbar ? 0xFF9D5D00 : 0xFFFCE100, round: false);
        }
        return ToIcon(pixels, size);
    }

    public static bool IsTaskbarLight()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private static void DrawKeyboard(uint[] pixels, int size, uint ink)
    {
        int unit = Math.Max(1, size / 16);
        int left = 0;
        int right = size - 1;
        int top = (int)Math.Round(size * 3 / 16.0);
        int bottom = size - 1 - (int)Math.Round(size * 3 / 16.0);

        // Outline with cut corners
        for (int t = 0; t < unit; t++)
        {
            for (int x = left + unit; x <= right - unit; x++)
            {
                Set(pixels, size, x, top + t, ink);
                Set(pixels, size, x, bottom - t, ink);
            }
            for (int y = top + unit; y <= bottom - unit; y++)
            {
                Set(pixels, size, left + t, y, ink);
                Set(pixels, size, right - t, y, ink);
            }
        }

        // Two rows of keys and a space bar, each key 2 units wide with 1-unit gaps
        int innerLeft = left + 2 * unit;
        int innerRight = right - 2 * unit;
        int rowY = top + 2 * unit;
        for (int row = 0; row < 2; row++)
        {
            for (int x = innerLeft; x + 2 * unit - 1 <= innerRight; x += 3 * unit)
            {
                Fill(pixels, size, x, rowY, 2 * unit, unit, ink);
            }
            rowY += 2 * unit;
        }
        int spaceWidth = (innerRight - innerLeft + 1) / 2;
        int spaceLeft = innerLeft + (innerRight - innerLeft + 1 - spaceWidth) / 2;
        Fill(pixels, size, spaceLeft, rowY, spaceWidth, unit, ink);
    }

    private static void DrawBadge(uint[] pixels, int size, uint color, bool round)
    {
        int badge = Math.Max(6, size * 7 / 16);
        int x0 = size - badge;
        int y0 = size - badge;

        // Clear a 1-pixel ring around the badge so it doesn't blend into the keys
        for (int y = y0 - 1; y < size; y++)
        {
            for (int x = x0 - 1; x < size; x++)
            {
                if (Inside(x - x0 + 0.5, y - y0 + 0.5, badge, round, grow: 1))
                {
                    Set(pixels, size, x, y, 0);
                }
            }
        }

        // Badge with 4x4 supersampling for smooth edges
        for (int y = y0; y < size; y++)
        {
            for (int x = x0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        if (Inside(x - x0 + (sx + 0.5) / 4, y - y0 + (sy + 0.5) / 4, badge, round, grow: 0))
                        {
                            hits++;
                        }
                    }
                }
                if (hits > 0)
                {
                    uint alpha = (uint)(255 * hits / 16);
                    Set(pixels, size, x, y, Premultiply(color, alpha));
                }
            }
        }
    }

    private static bool Inside(double x, double y, int badge, bool round, double grow)
    {
        if (round)
        {
            double r = badge / 2.0 + grow;
            double cx = badge / 2.0;
            double dx = x - cx;
            double dy = y - cx;
            return dx * dx + dy * dy <= r * r;
        }
        // Triangle pointing up
        double height = badge + grow;
        double top = -grow;
        if (y < top || y > height)
        {
            return false;
        }
        double half = (y - top) / (height - top) * (badge / 2.0 + grow);
        return Math.Abs(x - badge / 2.0) <= half;
    }

    private static uint Premultiply(uint color, uint alpha)
    {
        uint r = ((color >> 16) & 0xFF) * alpha / 255;
        uint g = ((color >> 8) & 0xFF) * alpha / 255;
        uint b = (color & 0xFF) * alpha / 255;
        return (alpha << 24) | (r << 16) | (g << 8) | b;
    }

    private static void Fill(uint[] pixels, int size, int x, int y, int width, int height, uint color)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                Set(pixels, size, x + dx, y + dy, color);
            }
        }
    }

    private static void Set(uint[] pixels, int size, int x, int y, uint color)
    {
        if (x >= 0 && y >= 0 && x < size && y < size)
        {
            pixels[y * size + x] = color;
        }
    }

    private static IntPtr ToIcon(uint[] pixels, int size)
    {
        Shell.BitmapInfoHeader header = new()
        {
            Size = Marshal.SizeOf<Shell.BitmapInfoHeader>(),
            Width = size,
            Height = -size,   // top-down
            Planes = 1,
            BitCount = 32,
        };
        IntPtr color = Shell.CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        IntPtr mask = Shell.CreateBitmap(size, size, 1, 1, null);
        try
        {
            int[] signed = new int[pixels.Length];
            Buffer.BlockCopy(pixels, 0, signed, 0, pixels.Length * sizeof(uint));
            Marshal.Copy(signed, 0, bits, signed.Length);

            Shell.IconInfo info = new() { IsIcon = true, Mask = mask, Color = color };
            IntPtr icon = Shell.CreateIconIndirect(ref info);
            if (icon == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return icon;
        }
        finally
        {
            Shell.DeleteObject(color);
            Shell.DeleteObject(mask);
        }
    }
}
