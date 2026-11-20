using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using Z10.Core.Sensors;

namespace Z10.App;

/// <summary>Sensoren über LibreHardwareMonitorLib (benötigt Adminrechte für vollständige Werte).</summary>
public sealed class LhmSensorSource : SensorSourceBase
{
    private Computer? _computer;

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

        var list = new List<SensorInfo>();
        foreach (var hw in _computer.Hardware)
            Collect(hw, Kind(hw.HardwareType.ToString()), list);
        return list;
    }

    private static void Collect(IHardware hw, string kind, List<SensorInfo> list)
    {
        hw.Update();
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
            Collect(sub, subKind, list);
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
