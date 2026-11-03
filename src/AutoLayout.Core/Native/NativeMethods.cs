// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AutoLayout.Core.Native;

/// <summary>
/// Win32 declarations. Nothing here reads, hooks or injects keystrokes.
/// </summary>
internal static class NativeMethods
{
    // ---- Device interfaces and properties ----------------------------------------------

    public static readonly Guid GuidDevInterfaceKeyboard = new("884B96C3-56EF-11D1-BC8C-00A0C91405DD");

    public const int CR_SUCCESS = 0;
    public const int CR_BUFFER_SMALL = 0x1A;
    public const int CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const int CM_LOCATE_DEVNODE_NORMAL = 0;
    public const uint DEVPROP_TYPE_STRING = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    public struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public DevPropKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    public static readonly DevPropKey DevPKeyDeviceInstanceId = new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);
    public static readonly DevPropKey DevPKeyDeviceBusReportedDeviceDesc = new(new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), 4);
    public static readonly DevPropKey DevPKeyDeviceFriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List_Size(out int length, ref Guid interfaceClassGuid, string? deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List(ref Guid interfaceClassGuid, string? deviceId, [Out] char[] buffer, int bufferLength, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_Property(string deviceInterface, ref DevPropKey propertyKey, out uint propertyType,
        [Out] byte[]? buffer, ref int bufferSize, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNode(out int devInst, string deviceId, int flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out int parent, int devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_Property(int devInst, ref DevPropKey propertyKey, out uint propertyType,
        [Out] byte[]? buffer, ref int bufferSize, int flags);

    // ---- Files and devices ---------------------------------------------------------------

    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    /// <summary>Copies memory of this process without crashing on a bad address.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr bytesRead);

    // ---- Windows, messages and timers --------------------------------------------------------

    public const int WM_DESTROY = 0x0002;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_INPUTLANGCHANGE = 0x0051;
    public const int WM_TIMER = 0x0113;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_APP = 0x8000;

    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    public const int NOTIFY_FOR_THIS_SESSION = 0;

    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    // Power setting broadcast when the console display turns off (0), on (1) or dims (2)
    public static readonly Guid GuidConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    public delegate IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClassEx
    {
        public int Size;
        public int Style;
        public WindowProcedure WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y,
        int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint milliseconds, IntPtr timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(IntPtr hwnd, UIntPtr id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DevBroadcastDeviceInterface filter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    public const int SM_REMOTESESSION = 0x1000;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    // ---- Keyboard layouts and input methods ------------------------------------------------

    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint SPI_GETDEFAULTINPUTLANG = 0x0059;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint BSF_POSTMESSAGE = 0x00000010;
    public const uint BSM_APPLICATIONS = 0x00000008;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref IntPtr value, uint winIni);

    /// <summary>Sets the user's default input method, e.g. "0416:00020409" or "0411:{clsid}{profile}".</summary>
    [DllImport("input.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDefaultLayoutOrTip(string inputMethod, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int BroadcastSystemMessage(uint flags, ref uint recipients, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, [Out] char[] output, int outputLength, IntPtr reserved);

    // ---- Text Services Framework (input profiles, including IMEs) -------------------------------

    public static readonly Guid ClsidInputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");

    public const int TF_PROFILETYPE_INPUTPROCESSOR = 1;
    public const int TF_PROFILETYPE_KEYBOARDLAYOUT = 2;
    public const int TF_IPP_FLAG_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct TfInputProcessorProfile
    {
        public int ProfileType;
        public ushort LanguageId;
        public Guid Clsid;
        public Guid Profile;
        public Guid Category;
        public IntPtr SubstituteLayout;
        public int Caps;
        public IntPtr Layout;
        public int Flags;
    }

    [ComImport]
    [Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITfInputProcessorProfileMgr
    {
        // Only EnumProfiles is used; the other slots keep the vtable order
        [PreserveSig] int ActivateProfile(int profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, int flags);
        [PreserveSig] int DeactivateProfile(int profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, int flags);
        [PreserveSig] int GetProfile(int profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, out TfInputProcessorProfile result);
        [PreserveSig] int EnumProfiles(ushort langid, out IEnumTfInputProcessorProfiles profiles);
    }

    [ComImport]
    [Guid("71C6E74D-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumTfInputProcessorProfiles
    {
        [PreserveSig] int Clone(out IEnumTfInputProcessorProfiles clone);
        [PreserveSig] int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] TfInputProcessorProfile[] profiles, out int fetched);
        [PreserveSig] int Reset();
        [PreserveSig] int Skip(int count);
    }

    // ---- DLL loading -------------------------------------------------------------------

    public const int LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDefaultDllDirectories(int directoryFlags);
}
