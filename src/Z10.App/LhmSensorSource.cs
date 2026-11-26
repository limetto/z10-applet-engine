using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using Z10.Core.Sensors;

namespace Z10.App;

/// <summary>Sensoren über LibreHardwareMonitorLib (benötigt Adminrechte für vollständige Werte).</summary>
public sealed class LhmSensorSource : SensorSourceBase, IWantedSensors
{
    private Computer? _computer;
    private volatile IReadOnlyCollection<string>? _wanted;
    private DateTime _nextFull = DateTime.MinValue;

    /// <summary>So oft wird auch Hardware abgefragt, deren Sensoren gerade kein Applet braucht (für Listen und Editor).</summary>
    public static TimeSpan FullInterval { get; set; } = TimeSpan.FromSeconds(5);

    public void SetWanted(IReadOnlyCollection<string> ids) => _wanted = ids;

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        if (_computer is null)
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
            };
            _computer.Open();
        }

        var now = DateTime.UtcNow;
        bool full = now >= _nextFull || _wanted is null;
        if (full) _nextFull = now + FullInterval;
        var wanted = _wanted ?? Array.Empty<string>();
        // Auto-Abfragen ("auto:cpu/…") brauchen alle Hardware dieser Art
        var autoKinds = wanted.Where(id => id.StartsWith(AutoQuery.Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(id => id[AutoQuery.Prefix.Length..].Split('/')[0].ToLowerInvariant()).ToHashSet();
        var plain = wanted.Where(id => id.StartsWith('/')).ToList();

        bool Wanted(IHardware hw, string kind)
        {
            if (full || autoKinds.Contains(kind)) return true;
            var prefix = hw.Identifier + "/";
            if (plain.Any(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return true;
            return hw.SubHardware.Any(sub => Wanted(sub, kind == "other" ? Kind(sub.HardwareType.ToString()) : kind));
        }

        var list = new List<SensorInfo>();
        foreach (var hw in _computer.Hardware)
            Collect(hw, Kind(hw.HardwareType.ToString()), list, Wanted);
        return list;
    }

    private static void Collect(IHardware hw, string kind, List<SensorInfo> list, Func<IHardware, string, bool> update)
    {
        // Nicht gebrauchte Hardware behält ihre letzten Werte bis zur nächsten vollständigen Abfrage
        if (update(hw, kind)) hw.Update();
        foreach (var s in hw.Sensors)
        {
            var type = s.SensorType.ToString();
            list.Add(new SensorInfo(s.Identifier.ToString(), hw.Name, kind, s.Name, type, Unit(type),
                s.Value is float v && !float.IsNaN(v) ? v : null));
        }
        foreach (var sub in hw.SubHardware)
        {
            // Unter-Hardware (z. B. Super-I/O-Chip des Mainboards) erbt die Art des Elternteils.
            var subKind = kind == "other" ? Kind(sub.HardwareType.ToString()) : kind;
            Collect(sub, subKind, list, update);
        }
    }

    private static string Kind(string hardwareType) => hardwareType switch
    {
        "Cpu" => "cpu",
        var t when t.StartsWith("Gpu", StringComparison.Ordinal) => "gpu",
        "Memory" => "memory",
        "Motherboard" or "SuperIO" or "EmbeddedController" or "Cooler" => "motherboard",
        "Network" => "network",
        "Storage" => "storage",
        _ => "other",
    };

    private static string Unit(string sensorType) => sensorType switch
    {
        "Temperature" => "°C",
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Clock" => "MHz",
        "Fan" => "RPM",
        "Data" => "GB",
        "SmallData" => "MB",
        "Throughput" => "B/s",
        "Power" => "W",
        "Voltage" => "V",
        "Current" => "A",
        "Frequency" => "Hz",
        "Flow" => "L/h",
        "Energy" => "mWh",
        _ => "",
    };

    public override void Dispose()
    {
        try { _computer?.Close(); } catch (Exception) { }
        _computer = null;
    }
}
