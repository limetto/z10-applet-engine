using Z10.Core.Rendering;

namespace Z10.Core.Model;

/// <summary>Vorlagen für den ersten Start. Nutzen Auto-Abfragen, damit sie auf jedem PC funktionieren.</summary>
public static class DefaultApplets
{
    private static TextStyle Px(int size) => new() { Size = size };

    public const string CpuTemp = "auto:cpu/Temperature/CPU Package||Core (Tctl/Tdie)||Core (Tctl)||Core Average||Core Max||*";
    public const string CpuLoad = "auto:cpu/Load/CPU Total||*";
    public const string CpuClock = "auto:cpu/Clock/Cores (Average)||Core #1||*";
    public const string GpuTemp = "auto:gpu/Temperature/GPU Core||GPU Hot Spot||*";
    public const string GpuLoad = "auto:gpu/Load/GPU Core||D3D 3D||*";
    public const string GpuVram = "auto:gpu/SmallData/GPU Memory Used||D3D Dedicated Memory Used||*";
    public const string RamUsed = "auto:memory/Data/Memory Used||Used Memory";
    public const string RamFree = "auto:memory/Data/Memory Available||Available Memory";
    public const string RamLoad = "auto:memory/Load/Memory||*";

    public static List<Applet> Create() => new()
    {
        new Applet
        {
            Name = "CPU", Order = 0, DurationSeconds = 8, ShowOnWarning = true,
            Widgets =
            {
                new IconWidget { Name = "Symbol", X = 0, Y = 0, W = 8, H = 8, Icon = "cpu" },
                new TextWidget { Name = "Titel", X = 11, Y = 1, W = 30, H = 7, Text = "CPU" },
                new SensorWidget { Name = "Temperatur", X = 0, Y = 11, W = 76, H = 21, Sensor = CpuTemp, Format = "{value:0}°C", Style = Px(3),
                                   Warn = new WarnRule { Above = 85, Blink = true } },
                new SensorWidget { Name = "Last", X = 82, Y = 1, W = 78, H = 7, Sensor = CpuLoad, Format = "Last {value:0}%" },
                new BarWidget { Name = "Last-Balken", X = 82, Y = 11, W = 78, H = 7, Sensor = CpuLoad },
                new SensorWidget { Name = "Takt", X = 82, Y = 23, W = 78, H = 7, Sensor = CpuClock, Format = "{value/1000:0.0} GHz" },
            }
        },
        new Applet
        {
            Name = "GPU", Order = 1, DurationSeconds = 8, ShowOnWarning = true,
            Widgets =
            {
                new IconWidget { Name = "Symbol", X = 0, Y = 0, W = 8, H = 8, Icon = "gpu" },
                new TextWidget { Name = "Titel", X = 11, Y = 1, W = 30, H = 7, Text = "GPU" },
                new SensorWidget { Name = "Temperatur", X = 0, Y = 11, W = 76, H = 21, Sensor = GpuTemp, Format = "{value:0}°C", Style = Px(3),
                                   Warn = new WarnRule { Above = 85, Blink = true } },
                new SensorWidget { Name = "Last", X = 82, Y = 1, W = 78, H = 7, Sensor = GpuLoad, Format = "Last {value:0}%" },
                new BarWidget { Name = "Last-Balken", X = 82, Y = 11, W = 78, H = 7, Sensor = GpuLoad },
                new SensorWidget { Name = "VRAM", X = 82, Y = 23, W = 78, H = 7, Sensor = GpuVram, Format = "VRAM {value/1024:0.0} GB" },
            }
        },
        new Applet
        {
            Name = "Uhr & Datum", Order = 2, DurationSeconds = 15, RefreshMs = 500,
            Widgets =
            {
                new ClockWidget { Name = "Uhrzeit", X = 0, Y = 1, W = 160, H = 21, Format = "HH:mm:ss", Style = Px(3), Align = TextAlign.Center },
                new ClockWidget { Name = "Datum", X = 0, Y = 25, W = 160, H = 7, Format = "dddd, dd. MMMM yyyy", Align = TextAlign.Center },
            }
        },
        new Applet
        {
            Name = "Arbeitsspeicher", Order = 3, DurationSeconds = 5,
            Widgets =
            {
                new IconWidget { Name = "Symbol", X = 0, Y = 0, W = 8, H = 8, Icon = "ram" },
                new SensorWidget { Name = "Belegt", X = 11, Y = 1, W = 149, H = 7, Sensor = RamUsed, Format = "RAM {value:0.0} GB belegt" },
                new BarWidget { Name = "Auslastung", X = 0, Y = 12, W = 160, H = 9, Sensor = RamLoad,
                                Warn = new WarnRule { Above = 90 } },
                new SensorWidget { Name = "Frei", X = 0, Y = 25, W = 160, H = 7, Sensor = RamFree, Format = "frei: {value:0.0} GB" },
            }
        },
        new Applet
        {
            Name = "CPU-Verlauf", Order = 4, DurationSeconds = 8, Enabled = false,
            Widgets =
            {
                new SensorWidget { Name = "Last", X = 0, Y = 0, W = 40, H = 7, Sensor = CpuLoad, Format = "{value:0}%" },
                new TextWidget { Name = "Titel", X = 120, Y = 0, W = 40, H = 7, Text = "CPU 60s", Align = TextAlign.Right },
                new GraphWidget { Name = "Verlauf", X = 0, Y = 9, W = 160, H = 23, Sensor = CpuLoad, Min = 0, Max = 100, Seconds = 60 },
            }
        },
    };
}
