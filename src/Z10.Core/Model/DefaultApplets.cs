using static Z10.Core.Localization.Loc;
using Z10.Core.Rendering;
using Z10.Core.Storage;

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

    /// <summary>Wiedergabe: Titel und Interpret als Lauftext, Fortschritt und Zeit. Nur sichtbar, wenn etwas läuft.</summary>
    public static Applet MediaApplet(int order) => new()
    {
        Name = L("Wiedergabe"), Order = order, DurationSeconds = 10, RefreshMs = 500, OnlyWhenMediaPlaying = true,
        Widgets =
        {
            new IconWidget { Name = L("Status"), X = 0, Y = 0, W = 8, H = 8, Icon = Icons.MediaStatus },
            new MediaWidget { Name = L("Titel"), X = 11, Y = 1, W = 149, H = 7, Field = Media.MediaField.Title, Placeholder = L("Keine Wiedergabe") },
            new MediaWidget { Name = L("Interpret"), X = 11, Y = 10, W = 149, H = 7, Field = Media.MediaField.Artist },
            new BarWidget { Name = L("Fortschritt"), X = 0, Y = 19, W = 160, H = 5, Sensor = Sensors.MediaSensorSource.ProgressId },
            new MediaWidget { Name = L("Position"), X = 0, Y = 25, W = 60, H = 7, Field = Media.MediaField.Position, Scroll = false },
            new MediaWidget { Name = L("Programm"), X = 40, Y = 25, W = 80, H = 7, Field = Media.MediaField.App, Scroll = false, Align = TextAlign.Center },
            new MediaWidget { Name = L("Dauer"), X = 100, Y = 25, W = 60, H = 7, Field = Media.MediaField.Duration, Scroll = false, Align = TextAlign.Right },
        }
    };

    public static List<Applet> Create() => new()
    {
        new Applet
        {
            Name = L("CPU"), Order = 0, DurationSeconds = 8, ShowOnWarning = true,
            Widgets =
            {
                new IconWidget { Name = L("Symbol"), X = 0, Y = 0, W = 8, H = 8, Icon = "cpu" },
                new TextWidget { Name = L("Titel"), X = 11, Y = 1, W = 30, H = 7, Text = L("CPU") },
                new SensorWidget { Name = L("Temperatur"), X = 0, Y = 11, W = 76, H = 21, Sensor = CpuTemp, Format = "{value:0}°C", Style = Px(3),
                                   Warn = new WarnRule { Above = 85, Blink = true } },
                new SensorWidget { Name = L("Last"), X = 82, Y = 1, W = 78, H = 7, Sensor = CpuLoad, Format = L("Last {value:0}%") },
                new BarWidget { Name = L("Last-Balken"), X = 82, Y = 11, W = 78, H = 7, Sensor = CpuLoad },
                new SensorWidget { Name = L("Takt"), X = 82, Y = 23, W = 78, H = 7, Sensor = CpuClock, Format = "{value/1000:0.0} GHz" },
            }
        },
        new Applet
        {
            Name = L("GPU"), Order = 1, DurationSeconds = 8, ShowOnWarning = true,
            Widgets =
            {
                new IconWidget { Name = L("Symbol"), X = 0, Y = 0, W = 8, H = 8, Icon = "gpu" },
                new TextWidget { Name = L("Titel"), X = 11, Y = 1, W = 30, H = 7, Text = L("GPU") },
                new SensorWidget { Name = L("Temperatur"), X = 0, Y = 11, W = 76, H = 21, Sensor = GpuTemp, Format = "{value:0}°C", Style = Px(3),
                                   Warn = new WarnRule { Above = 85, Blink = true } },
                new SensorWidget { Name = L("Last"), X = 82, Y = 1, W = 78, H = 7, Sensor = GpuLoad, Format = L("Last {value:0}%") },
                new BarWidget { Name = L("Last-Balken"), X = 82, Y = 11, W = 78, H = 7, Sensor = GpuLoad },
                new SensorWidget { Name = L("VRAM"), X = 82, Y = 23, W = 78, H = 7, Sensor = GpuVram, Format = L("VRAM {value/1024:0.0} GB") },
            }
        },
        new Applet
        {
            Name = L("Uhr & Datum"), Order = 2, DurationSeconds = 15, RefreshMs = 500,
            Widgets =
            {
                new ClockWidget { Name = L("Uhrzeit"), X = 0, Y = 1, W = 160, H = 21, Format = "HH:mm:ss", Style = Px(3), Align = TextAlign.Center },
                new ClockWidget { Name = L("Datum"), X = 0, Y = 25, W = 160, H = 7, Format = L("dddd, dd. MMMM yyyy"), Align = TextAlign.Center },
            }
        },
        new Applet
        {
            Name = L("Arbeitsspeicher"), Order = 3, DurationSeconds = 5,
            Widgets =
            {
                new IconWidget { Name = L("Symbol"), X = 0, Y = 0, W = 8, H = 8, Icon = "ram" },
                new SensorWidget { Name = L("Belegt"), X = 11, Y = 1, W = 149, H = 7, Sensor = RamUsed, Format = L("RAM {value:0.0} GB belegt") },
                new BarWidget { Name = L("Auslastung"), X = 0, Y = 12, W = 160, H = 9, Sensor = RamLoad,
                                Warn = new WarnRule { Above = 90 } },
                new SensorWidget { Name = L("Frei"), X = 0, Y = 25, W = 160, H = 7, Sensor = RamFree, Format = L("frei: {value:0.0} GB") },
            }
        },
        new Applet
        {
            Name = L("CPU-Verlauf"), Order = 4, DurationSeconds = 8, Enabled = false,
            Widgets =
            {
                new SensorWidget { Name = L("Last"), X = 0, Y = 0, W = 40, H = 7, Sensor = CpuLoad, Format = "{value:0}%" },
                new TextWidget { Name = L("Titel"), X = 120, Y = 0, W = 40, H = 7, Text = L("CPU 60s"), Align = TextAlign.Right },
                new GraphWidget { Name = L("Verlauf"), X = 0, Y = 9, W = 160, H = 23, Sensor = CpuLoad, Min = 0, Max = 100, Seconds = 60 },
            }
        },
        MediaApplet(5),
    };

    /// <summary>
    /// Für bestehende Installationen: Wiedergabe-Applet einmalig ergänzen, falls noch keins mit Medien-Bausteinen da ist.
    /// </summary>
    /// <returns>das ergänzte Applet oder null</returns>
    public static Applet? AddMediaAppletOnce(IList<Applet> applets, AppSettings settings)
    {
        if (settings.MediaAppletAdded) return null;
        settings.MediaAppletAdded = true;
        if (applets.Any(a => a.Widgets.OfType<MediaWidget>().Any())) return null;
        var a = MediaApplet(applets.Count == 0 ? 0 : applets.Max(x => x.Order) + 1);
        applets.Add(a);
        return a;
    }
}
