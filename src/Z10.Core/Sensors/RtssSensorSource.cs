using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace Z10.Core.Sensors;

/// <summary>
/// Framerate und Frametime aus RivaTuner Statistics Server (RTSS), gelesen aus dessen
/// Shared Memory "RTSSSharedMemoryV2". RTSS muss laufen und das Spiel erkannt haben.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RtssSensorSource : SensorSourceBase
{
    public const string FpsId = "/rtss/fps";
    public const string FrametimeId = "/rtss/frametime";
    private const string MapName = "RTSSSharedMemoryV2";

    public bool IsAvailable { get; private set; }
    public string? ActiveApp { get; private set; }

    /// <summary>Abgeschaltet liefert die Quelle keine Sensoren.</summary>
    public bool Enabled { get; set; } = true;

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        if (!Enabled)
        {
            IsAvailable = false;
            ActiveApp = null;
            return Array.Empty<SensorInfo>();
        }
        RtssReading? reading = null;
        var mem = SharedMemory.TryRead(MapName);
        IsAvailable = mem is not null;
        if (mem is not null) reading = RtssReader.Parse(mem, (uint)Environment.TickCount);

        ActiveApp = reading?.App;
        string hw = !IsAvailable ? "RTSS (nicht gestartet)" : reading is null ? "RTSS (kein Spiel aktiv)" : $"RTSS · {reading.App}";
        return new[]
        {
            new SensorInfo(FpsId, hw, "rtss", "Framerate", "Framerate", "FPS", reading?.Fps),
            new SensorInfo(FrametimeId, hw, "rtss", "Frametime", "Frametime", "ms", reading?.FrametimeMs),
        };
    }
}

public sealed record RtssReading(string App, double Fps, double FrametimeMs);

/// <summary>Liest die RTSS-Struktur (RTSS_SHARED_MEMORY v2) aus einem Speicherabbild.</summary>
public static class RtssReader
{
    public const uint Signature = 0x52545353;   // 'RTSS'
    private const int NameOffset = 4, NameLength = 260;
    private const int Time0Offset = 268, Time1Offset = 272, FramesOffset = 276, FrameTimeOffset = 280;

    /// <param name="nowTick">Aktueller GetTickCount-Wert, um veraltete Einträge zu verwerfen.</param>
    public static RtssReading? Parse(ReadOnlySpan<byte> mem, uint nowTick, uint maxAgeMs = 2000)
    {
        if (mem.Length < 20 || U32(mem, 0) != Signature) return null;
        uint version = U32(mem, 4);
        if (version < 0x00020000) return null;
        int entrySize = (int)U32(mem, 8);
        int arrOffset = (int)U32(mem, 12);
        int arrCount = (int)U32(mem, 16);
        if (entrySize < FrameTimeOffset + 4 || arrOffset <= 0 || arrCount <= 0 || arrCount > 4096) return null;

        RtssReading? best = null;
        uint bestTime = 0;
        for (int i = 0; i < arrCount; i++)
        {
            int e = arrOffset + i * entrySize;
            if (e + FrameTimeOffset + 4 > mem.Length) break;
            var entry = mem.Slice(e, entrySize);
            if (U32(entry, 0) == 0) continue;                       // Prozess-Id 0 = leer
            uint t0 = U32(entry, Time0Offset), t1 = U32(entry, Time1Offset), frames = U32(entry, FramesOffset);
            if (t1 <= t0 || frames == 0) continue;
            if (unchecked(nowTick - t1) > maxAgeMs) continue;       // Spiel läuft nicht mehr / im Hintergrund
            if (best is not null && t1 <= bestTime) continue;

            double fps = 1000.0 * frames / (t1 - t0);
            uint frameTimeUs = U32(entry, FrameTimeOffset);
            double ft = frameTimeUs > 0 ? frameTimeUs / 1000.0 : 1000.0 / fps;
            var nameBytes = entry.Slice(NameOffset, NameLength);
            int zero = nameBytes.IndexOf((byte)0);
            var path = Encoding.Latin1.GetString(zero < 0 ? nameBytes : nameBytes[..zero]);
            var app = Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/').Last());
            best = new RtssReading(app, fps, ft);
            bestTime = t1;
        }
        return best;
    }

    private static uint U32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(offset, 4));
}
