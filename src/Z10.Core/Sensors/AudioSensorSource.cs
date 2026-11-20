using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Z10.Core.Sensors;

/// <summary>
/// Lautstärke und Stummschaltung der Audiogeräte über die Windows Core Audio API (MMDevice / IAudioEndpointVolume).
/// Lautstärke in Prozent (0–100), Stumm = 1, sonst 0.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AudioSensorSource : SensorSourceBase
{
    public const string DefaultOutVolume = "/audio/default/volume";
    public const string DefaultOutMute = "/audio/default/mute";
    public const string DefaultInVolume = "/audio/default-mic/volume";
    public const string DefaultInMute = "/audio/default-mic/mute";

    public bool Enabled { get; set; } = true;

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        if (!Enabled) return Array.Empty<SensorInfo>();
        var list = new List<SensorInfo>();
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            AddDefault(enumerator, EDataFlow.Render, DefaultOutVolume, DefaultOutMute, "Standard-Wiedergabegerät", list);
            AddDefault(enumerator, EDataFlow.Capture, DefaultInVolume, DefaultInMute, "Standard-Aufnahmegerät", list);
            AddAll(enumerator, EDataFlow.Render, "out", "Wiedergabe", list);
            AddAll(enumerator, EDataFlow.Capture, "in", "Aufnahme", list);
        }
        finally
        {
            Release(enumerator);
        }
        return list;
    }

    private static void AddDefault(IMMDeviceEnumerator en, EDataFlow flow, string volId, string muteId, string label, List<SensorInfo> list)
    {
        IMMDevice? dev = null;
        try
        {
            if (en.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out dev) != 0 || dev is null)
            {
                list.Add(new SensorInfo(volId, label + " (keins)", "audio", "Lautstärke", "Level", "%", null));
                list.Add(new SensorInfo(muteId, label + " (keins)", "audio", "Stumm", "Control", "", null));
                return;
            }
            var (vol, mute) = ReadVolume(dev);
            var name = FriendlyName(dev) ?? label;
            list.Add(new SensorInfo(volId, $"{label} · {name}", "audio", "Lautstärke", "Level", "%", vol));
            list.Add(new SensorInfo(muteId, $"{label} · {name}", "audio", "Stumm", "Control", "", mute));
        }
        catch (COMException) { }
        finally { Release(dev); }
    }

    private static void AddAll(IMMDeviceEnumerator en, EDataFlow flow, string prefix, string label, List<SensorInfo> list)
    {
        IMMDeviceCollection? coll = null;
        try
        {
            if (en.EnumAudioEndpoints(flow, DeviceStateActive, out coll) != 0 || coll is null) return;
            coll.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? dev = null;
                try
                {
                    if (coll.Item(i, out dev) != 0 || dev is null) continue;
                    var name = FriendlyName(dev) ?? $"Gerät {i + 1}";
                    var (vol, mute) = ReadVolume(dev);
                    var slug = Slug(name);
                    list.Add(new SensorInfo($"/audio/{prefix}/{slug}/volume", $"{label} · {name}", "audio", "Lautstärke", "Level", "%", vol));
                    list.Add(new SensorInfo($"/audio/{prefix}/{slug}/mute", $"{label} · {name}", "audio", "Stumm", "Control", "", mute));
                }
                catch (COMException) { }
                finally { Release(dev); }
            }
        }
        catch (COMException) { }
        finally { Release(coll); }
    }

    private static (double? Volume, double? Mute) ReadVolume(IMMDevice dev)
    {
        object? obj = null;
        try
        {
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out obj) != 0 || obj is not IAudioEndpointVolume vol)
                return (null, null);
            double? level = vol.GetMasterVolumeLevelScalar(out float scalar) == 0 ? Math.Round(scalar * 100, 1) : null;
            double? mute = vol.GetMute(out bool muted) == 0 ? (muted ? 1 : 0) : null;
            return (level, mute);
        }
        finally { Release(obj); }
    }

    private static string? FriendlyName(IMMDevice dev)
    {
        IPropertyStore? store = null;
        var pv = new PropVariant();
        try
        {
            if (dev.OpenPropertyStore(StgmRead, out store) != 0 || store is null) return null;
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out pv) != 0) return null;
            return pv.vt == VtLpwstr && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null;
        }
        finally
        {
            PropVariantClear(ref pv);
            Release(store);
        }
    }

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "geraet" : slug;
    }

    private static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
    }

    // ---------- COM-Definitionen (mmdeviceapi.h, endpointvolume.h) ----------

    private const uint DeviceStateActive = 0x1;
    private const uint ClsCtxAll = 0x17;
    private const uint StgmRead = 0;
    private const ushort VtLpwstr = 31;
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private enum EDataFlow { Render = 0, Capture = 1 }
    private enum ERole { Console = 0, Multimedia = 1 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p;
        public IntPtr p2;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
