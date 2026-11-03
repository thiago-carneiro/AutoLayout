// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AutoLayout.Platform;

/// <summary>Win32 declarations used by the app's shell integration (tray icon, menus, windows).</summary>
internal static class Shell
{
    /// <summary>With an empty string, removes the current directory from the DLL search order.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDllDirectory(string path);

    public const uint MUI_LANGUAGE_NAME = 0x8;

    /// <summary>The UI language of the process's own resources, WinUI's included; languages ends in "\0".</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessPreferredUILanguages(uint flags, string languages, out uint count);

    // ---- Tray icon ---------------------------------------------------------------------

    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_APP = 0x8000;

    public const int NIM_ADD = 0;
    public const int NIM_MODIFY = 1;
    public const int NIM_DELETE = 2;
    public const int NIM_SETVERSION = 4;
    public const int NIF_MESSAGE = 0x01;
    public const int NIF_ICON = 0x02;
    public const int NIF_TIP = 0x04;
    public const int NIF_SHOWTIP = 0x80;
    public const int NIF_INFO = 0x10;
    public const int NIIF_INFO = 0x01;
    public const int NIIF_WARNING = 0x02;
    public const int NIN_BALLOONUSERCLICK = 0x0405;
    public const int NOTIFYICON_VERSION_4 = 4;
    public const int NIN_SELECT = 0x0400;
    public const int NIN_KEYSELECT = 0x0401;
    public const int WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public int State;
        public int StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public int VersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegisterWindowMessage(string name);

    // ---- Menus -------------------------------------------------------------------------

    public const int MF_STRING = 0x0000;
    public const int MF_GRAYED = 0x0001;
    public const int MF_SEPARATOR = 0x0800;
    public const int TPM_RIGHTBUTTON = 0x0002;
    public const int TPM_RETURNCMD = 0x0100;
    public const int TPM_NONOTIFY = 0x0080;
    public const int TPM_LAYOUTRTL = 0x8000;
    public const int WM_NULL = 0x0000;

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(IntPtr menu, int flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetMenuDefaultItem(IntPtr menu, int item, int byPosition);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    // ---- Icons -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, int usage, out IntPtr bits, IntPtr section, int offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateBitmap(int width, int height, int planes, int bitCount, byte[]? bits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    public const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, int dpi);

    [DllImport("user32.dll")]
    public static extern int GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern int GetDpiForWindow(IntPtr window);

    // ---- Packaging ---------------------------------------------------------------------

    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    /// <summary>True when installed from an MSIX package (e.g. the Microsoft Store).</summary>
    public static bool IsPackaged
    {
        get
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
        }
    }
}
