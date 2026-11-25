using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Z10.Core.Device;

/// <summary>
/// Zugriff auf das Vendor-Interface der Z10 über die Windows-HID-API (hid.dll / setupapi.dll).
/// Keine externen Abhängigkeiten.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Z10HidDevice : IZ10Device
{
    private SafeFileHandle? _handle;

    public string DevicePath { get; }
    public ushort FirmwareVersion { get; }
    public string Description => $"EVGA Z10 · Firmware 0x{FirmwareVersion:X4}";
    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };
    public TimeSpan PageDelay { get; set; } = TimeSpan.FromMilliseconds(20);

    private Z10HidDevice(SafeFileHandle handle, string path, ushort version)
    {
        _handle = handle;
        DevicePath = path;
        FirmwareVersion = version;
    }

    /// <summary>Sucht die Tastatur. Gibt null zurück, wenn sie nicht angeschlossen ist.</summary>
    public static Z10HidDevice? TryOpen()
    {
        var (handle, path, version) = Find(caps =>
            caps.UsagePage == Z10Protocol.VendorUsagePage && caps.FeatureReportByteLength == Z10Protocol.ReportLength);
        return handle is null ? null : new Z10HidDevice(handle, path!, version);
    }

    /// <summary>Öffnet eine HID-Collection der Z10 nach Usage Page, z. B. 0xFF00 für Tastenereignisse.</summary>
    public static (SafeFileHandle? Handle, int InputLength) OpenCollection(ushort usagePage, int minInputLength)
    {
        int length = 0;
        var (handle, _, _) = Find(caps =>
        {
            if (caps.UsagePage != usagePage || caps.InputReportByteLength < minInputLength) return false;
            length = caps.InputReportByteLength;
            return true;
        });
        return (handle, length);
    }

    private static (SafeFileHandle? Handle, string? Path, ushort Version) Find(Func<Native.HIDP_CAPS, bool> match)
    {
        foreach (var path in EnumerateHidPaths())
        {
            var handle = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); continue; }

            var attr = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
            if (!Native.HidD_GetAttributes(handle, ref attr)
                || attr.VendorID != Z10Protocol.VendorId || attr.ProductID != Z10Protocol.ProductId)
            {
                handle.Dispose();
                continue;
            }

            if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) { handle.Dispose(); continue; }
            try
            {
                if (Native.HidP_GetCaps(preparsed, out var caps) == Native.HIDP_STATUS_SUCCESS && match(caps))
                    return (handle, path, attr.VersionNumber);
            }
            finally
            {
                Native.HidD_FreePreparsedData(preparsed);
            }
            handle.Dispose();
        }
        return (null, null, 0);
    }

    public void SendFeature(byte[] report)
    {
        if (_handle is null || !IsOpen) throw new IOException("Z10 ist nicht verbunden.");
        if (report.Length != Z10Protocol.ReportLength) throw new ArgumentException("Falsche Reportlänge.", nameof(report));
        if (!Native.HidD_SetFeature(_handle, report, report.Length))
            throw new IOException("HidD_SetFeature fehlgeschlagen.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private static IEnumerable<string> EnumerateHidPaths()
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        var info = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (info == IntPtr.Zero || info == new IntPtr(-1)) yield break;
        try
        {
            var data = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, i, ref data); i++)
            {
                var path = GetDevicePath(info, ref data);
                if (path is not null) yield return path;
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(info);
        }
    }

    private static string? GetDevicePath(IntPtr info, ref Native.SP_DEVICE_INTERFACE_DATA data)
    {
        Native.SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
        if (required <= 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal(required);
        try
        {
            // cbSize der Struktur SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 unter x64, 6 unter x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!Native.SetupDiGetDeviceInterfaceDetail(info, ref data, buffer, required, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public const int DIGCF_PRESENT = 0x2;
        public const int DIGCF_DEVICEINTERFACE = 0x10;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    }
}
