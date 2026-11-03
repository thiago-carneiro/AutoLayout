// Copyright 2026 The AutoLayout authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoLayout.Core.Native;

namespace AutoLayout.Core;

public enum KeyboardConnection
{
    Usb,
    Bluetooth,

    /// <summary>The keyboard of the computer this PC is used from through Remote Desktop.</summary>
    RemoteDesktop,
}

/// <summary>
/// An external (USB or Bluetooth) keyboard that is connected now.
/// </summary>
/// <param name="Key">"046D:C31C", or "BT:xxxx:yyyy" when the vendor id was assigned by the Bluetooth SIG. The same on any port.</param>
/// <param name="VendorId">USB or Bluetooth vendor id.</param>
/// <param name="ProductId">Product id.</param>
/// <param name="Connection">How it is connected.</param>
/// <param name="InterfacePath">Device interface path, used to read its name.</param>
public sealed record ConnectedKeyboard(string Key, int VendorId, int ProductId, KeyboardConnection Connection, string InterfacePath);

/// <summary>
/// The keyboards connected to this PC. Only device interfaces are listed; no input is read.
/// </summary>
public interface IDeviceSource
{
    /// <summary>The external keyboards connected, or null if Windows could not list them now.</summary>
    IReadOnlyList<ConnectedKeyboard>? GetConnected();

    /// <summary>True while this session is used through Remote Desktop.</summary>
    bool IsRemoteSession { get; }

    /// <summary>The name Windows has for the keyboard (e.g. "K380 Multi-Device Keyboard"), or null.</summary>
    string? GetProductName(ConnectedKeyboard keyboard);
}

public sealed class ExternalKeyboards : IDeviceSource
{
    /// <summary>Key of the Remote Desktop entry.</summary>
    public const string RemoteDesktopKey = "RDP";

    // Bluetooth HID service UUIDs: classic (HID profile) and Low Energy (HID over GATT)
    private const string BluetoothClassicHid = "{00001124-0000-1000-8000-00805F9B34FB}";
    private const string BluetoothLowEnergyHid = "{00001812-0000-1000-8000-00805F9B34FB}";

    // USB:          \\?\HID#VID_046D&PID_C31C&MI_00#...
    // Bluetooth:    \\?\HID#{00001124-...}_VID&0002046D_PID&B342#...  (2-byte id source + vendor id)
    // Bluetooth LE: \\?\HID#{00001812-...}_DEV_VID&02046D_PID&B35B...   (1-byte id source + vendor id)
    // Id source: 1 = Bluetooth SIG, 2 = USB-IF.
    private static readonly Regex IdPattern = new(
        @"VID[_&](?<vid>[0-9A-F]{4,8})_?&?PID[_&](?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public bool IsRemoteSession => NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;

    public IReadOnlyList<ConnectedKeyboard>? GetConnected()
    {
        List<string>? paths = GetKeyboardInterfaces();
        if (paths == null)
        {
            return null;
        }

        Dictionary<string, ConnectedKeyboard> keyboards = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            ConnectedKeyboard? keyboard = Parse(path);
            // A keyboard often has several interfaces (keys, media keys...): one entry per model
            if (keyboard != null && !keyboards.ContainsKey(keyboard.Key))
            {
                keyboards.Add(keyboard.Key, keyboard);
            }
        }
        return new List<ConnectedKeyboard>(keyboards.Values);
    }

    public string? GetProductName(ConnectedKeyboard keyboard)
    {
        // Read from the properties Windows already has (no I/O to the device): the name the
        // device reported to its bus, on the keyboard's node or one of its parents
        string? instanceId = GetInterfaceString(keyboard.InterfacePath, NativeMethods.DevPKeyDeviceInstanceId);
        if (instanceId == null || NativeMethods.CM_Locate_DevNode(out int node, instanceId, NativeMethods.CM_LOCATE_DEVNODE_NORMAL) != NativeMethods.CR_SUCCESS)
        {
            return null;
        }

        for (int level = 0; level < 4; level++)
        {
            string? name = TextSanitizer.CleanName(GetNodeString(node, NativeMethods.DevPKeyDeviceBusReportedDeviceDesc));
            if (name != null)
            {
                return name;
            }
            if (keyboard.Connection == KeyboardConnection.Bluetooth)
            {
                // Bluetooth devices carry the name the user sees in Settings
                name = TextSanitizer.CleanName(GetNodeString(node, NativeMethods.DevPKeyDeviceFriendlyName));
                if (name != null && level > 0)
                {
                    return name;
                }
            }
            if (NativeMethods.CM_Get_Parent(out int parent, node, 0) != NativeMethods.CR_SUCCESS)
            {
                break;
            }
            node = parent;
        }
        return null;
    }

    internal static ConnectedKeyboard? Parse(string path)
    {
        // External keyboards are HID devices on USB or Bluetooth. Built-in keyboards are
        // PS/2 (ACPI) or I2C devices and remote desktop's is a root device: none of these
        // paths start with HID# and carry a vendor id.
        if (!path.StartsWith(@"\\?\HID#", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Match match = IdPattern.Match(path);
        if (!match.Success)
        {
            return null;
        }

        string vid = match.Groups["vid"].Value;
        string source = vid.Substring(0, vid.Length - 4);
        int vendorId = int.Parse(vid.AsSpan(vid.Length - 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        int productId = int.Parse(match.Groups["pid"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        KeyboardConnection connection =
            path.Contains(BluetoothClassicHid, StringComparison.OrdinalIgnoreCase)
            || path.Contains(BluetoothLowEnergyHid, StringComparison.OrdinalIgnoreCase)
                ? KeyboardConnection.Bluetooth
                : KeyboardConnection.Usb;

        bool bluetoothSigVendor = source.Length > 0 && source.TrimStart('0') == "1";
        string key = string.Format(CultureInfo.InvariantCulture, "{0}{1:X4}:{2:X4}", bluetoothSigVendor ? "BT:" : string.Empty, vendorId, productId);
        return new ConnectedKeyboard(key, vendorId, productId, connection, path);
    }

    private static List<string>? GetKeyboardInterfaces()
    {
        Guid keyboardGuid = NativeMethods.GuidDevInterfaceKeyboard;

        // The list can grow between the two calls when a device is connected: retry then
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.CM_Get_Device_Interface_List_Size(out int length, ref keyboardGuid, null,
                NativeMethods.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != NativeMethods.CR_SUCCESS)
            {
                return null;
            }

            char[] buffer = new char[length];
            int result = NativeMethods.CM_Get_Device_Interface_List(ref keyboardGuid, null, buffer, buffer.Length,
                NativeMethods.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            if (result == NativeMethods.CR_BUFFER_SMALL)
            {
                continue;
            }
            if (result != NativeMethods.CR_SUCCESS)
            {
                return null;
            }

            // Multi-string: entries separated by NUL, ending with an empty entry
            return new List<string>(new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
        return null;
    }

    private static string? GetInterfaceString(string path, NativeMethods.DevPropKey key)
    {
        int size = 0;
        int probe = NativeMethods.CM_Get_Device_Interface_Property(path, ref key, out _, null, ref size, 0);
        if (probe != NativeMethods.CR_BUFFER_SMALL || size <= 0 || size > 4096)
        {
            return null;
        }
        byte[] buffer = new byte[size];
        if (NativeMethods.CM_Get_Device_Interface_Property(path, ref key, out uint type, buffer, ref size, 0) != NativeMethods.CR_SUCCESS
            || type != NativeMethods.DEVPROP_TYPE_STRING)
        {
            return null;
        }
        return DecodeString(buffer, size);
    }

    private static string? GetNodeString(int node, NativeMethods.DevPropKey key)
    {
        int size = 0;
        int probe = NativeMethods.CM_Get_DevNode_Property(node, ref key, out _, null, ref size, 0);
        if (probe != NativeMethods.CR_BUFFER_SMALL || size <= 0 || size > 4096)
        {
            return null;
        }
        byte[] buffer = new byte[size];
        if (NativeMethods.CM_Get_DevNode_Property(node, ref key, out uint type, buffer, ref size, 0) != NativeMethods.CR_SUCCESS
            || type != NativeMethods.DEVPROP_TYPE_STRING)
        {
            return null;
        }
        return DecodeString(buffer, size);
    }

    private static string DecodeString(byte[] buffer, int size)
    {
        string text = Encoding.Unicode.GetString(buffer, 0, Math.Min(size, buffer.Length) & ~1);
        int end = text.IndexOf('\0');
        return end >= 0 ? text.Substring(0, end) : text;
    }
}
