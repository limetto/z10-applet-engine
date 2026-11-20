using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace Z10.Core.Sensors;

/// <summary>
/// Alle Überwachungswerte aus MSI Afterburner (Shared Memory "MAHMSharedMemory"):
/// GPU-Temperatur, Takt, Lüfter, Leistung … und die Framerate, sofern RTSS läuft.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AfterburnerSensorSource : SensorSourceBase
{
    private const string MapName = "MAHMSharedMemory";

    public bool Enabled { get; set; } = true;
    public bool IsAvailable { get; private set; }

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        if (!Enabled) { IsAvailable = false; return Array.Empty<SensorInfo>(); }
        var mem = SharedMemory.TryRead(MapName);
        var list = mem is null ? null : MahmReader.Parse(mem);
        IsAvailable = list is not null;
        return list ?? (IReadOnlyList<SensorInfo>)Array.Empty<SensorInfo>();
    }
}

/// <summary>Liest die Afterburner-Struktur (MAHM_SHARED_MEMORY, Version 2).</summary>
public static class MahmReader
{
    public const uint Signature = 0x4D41484D;   // 'MAHM'
    private const int StrLen = 260;
    private const int NameOffset = 0, UnitsOffset = 260, LocalizedNameOffset = 520, DataOffset = 1300, GpuOffset = 1316;

    public static List<SensorInfo>? Parse(ReadOnlySpan<byte> mem)
    {
        if (mem.Length < 20 || U32(mem, 0) != Signature) return null;
        int headerSize = (int)U32(mem, 8);
        int count = (int)U32(mem, 12);
        int entrySize = (int)U32(mem, 16);
        if (headerSize < 20 || entrySize < DataOffset + 4 || count < 0 || count > 1024) return null;

        var list = new List<SensorInfo>();
        for (int i = 0; i < count; i++)
        {
            int e = headerSize + i * entrySize;
            if (e + entrySize > mem.Length) break;
            var entry = mem.Slice(e, entrySize);
            var name = Str(entry.Slice(NameOffset, StrLen));
            if (name.Length == 0) continue;
            var localized = Str(entry.Slice(LocalizedNameOffset, StrLen));
            var units = Str(entry.Slice(UnitsOffset, StrLen));
            float raw = BinaryPrimitives.ReadSingleLittleEndian(entry.Slice(DataOffset, 4));
            double? value = float.IsNaN(raw) || Math.Abs(raw) >= 1e38f ? null : raw;
            uint gpu = entrySize >= GpuOffset + 4 ? U32(entry, GpuOffset) : uint.MaxValue;

            bool perGpu = gpu != uint.MaxValue;
            var id = "/afterburner/" + (perGpu ? $"gpu{gpu}/" : "") + Slug(name);
            var hardware = perGpu ? $"MSI Afterburner · GPU {gpu + 1}" : "MSI Afterburner";
            list.Add(new SensorInfo(id, hardware, "afterburner", localized.Length > 0 ? localized : name, TypeFromUnit(units), units, value));
        }
        return list;
    }

    private static string TypeFromUnit(string unit) => unit.Trim() switch
    {
        "°C" or "C" => "Temperature",
        "%" => "Load",
        "MHz" => "Clock",
        "RPM" => "Fan",
        "FPS" => "Framerate",
        "ms" => "Frametime",
        "MB" => "SmallData",
        "W" => "Power",
        "V" or "mV" => "Voltage",
        _ => "Afterburner",
    };

    private static string Str(ReadOnlySpan<byte> s)
    {
        int zero = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(zero < 0 ? s : s[..zero]).Trim();
    }

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static uint U32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(offset, 4));
}
