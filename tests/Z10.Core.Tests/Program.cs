using System.Globalization;
using System.Text.Json;
using Z10.Core.Device;
using Z10.Core.Engine;
using Z10.Core.Model;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;

// Kleiner Testlauf ohne externe Pakete. Rückgabewert != 0 bei Fehlern (für CI).
// "--dump <Ordner>" schreibt zusätzlich alle Vorlagen als PBM-Bilder (Vorschau ohne Tastatur).
if (args.Length == 2 && args[0] == "--dump")
{
    Directory.CreateDirectory(args[1]);
    var rd = new AppletRenderer(PixelTextRasterizer.Instance, StaticSensorSource.Demo(), new SensorHistory()) { Culture = CultureInfo.GetCultureInfo("de-DE") };
    foreach (var a in DefaultApplets.Create())
    {
        var fb = new Framebuffer();
        rd.Render(a, fb, new DateTime(2026, 11, 20, 10, 42, 17));
        var sb = new System.Text.StringBuilder($"P1\n{fb.Width} {fb.Height}\n");
        for (int y = 0; y < fb.Height; y++)
            sb.AppendLine(string.Join(" ", Enumerable.Range(0, fb.Width).Select(x => fb[x, y] ? "1" : "0")));
        File.WriteAllText(Path.Combine(args[1], a.Name.Replace(' ', '_').Replace('&', '+') + ".pbm"), sb.ToString());
    }
    return 0;
}

var tests = new List<(string Name, Action Body)>
{
    ("Bit 7 ist das oberste Pixel einer Seite", () =>
    {
        var fb = new Framebuffer();
        fb[0, 0] = true;      // Seite 0 oben, oberste Zeile
        fb[1, 7] = true;      // Seite 0, unterste Zeile
        fb[2, 8] = true;      // Seite 1, oberste Zeile
        var p = fb.ToPages();
        Eq(0x80, p[0][0]); Eq(0x01, p[0][1]); Eq(0x80, p[1][2]);
    }),
    ("Logische Seite 0 (oben) ist Geräteseite 3", () =>
    {
        Eq(3, Z10Protocol.ToDevicePage(0)); Eq(0, Z10Protocol.ToDevicePage(3));
    }),
    ("Report-Aufbau Seite und Freigabe", () =>
    {
        var cols = Enumerable.Range(0, 160).Select(i => (byte)i).ToArray();
        var r = Z10Protocol.BuildPageReport(2, cols);
        Eq(264, r.Length); Eq(0x07, r[0]); Eq(0x21, r[1]); Eq(2, r[2]); Eq(0, r[7]); Eq(0, r[8]); Eq(159, r[167]); Eq(0, r[168]);
        var rel = Z10Protocol.BuildReleaseReport();
        Eq(0xFE, rel[2]);
        var bl = Z10Protocol.BuildBacklightReport(5, 3);
        Eq(0x20, bl[1]); Eq(0x18, bl[2]); Eq(4, bl[3]);
    }),
    ("Display sendet 0,1,2,3 und Seite 3 immer zuletzt", () =>
    {
        var dev = new FakeDevice();
        var d = new Z10Display(dev);
        var fb = new Framebuffer();
        d.Show(fb);
        Seq(new[] { 0, 1, 2, 3 }, dev.Pages);
        dev.Pages.Clear();
        d.Show(fb);                       // nichts geändert → nur Seite 3
        Seq(new[] { 3 }, dev.Pages);
        dev.Pages.Clear();
        fb[0, 31] = true;                 // unterste Zeile = Geräteseite 0
        d.Show(fb);
        Seq(new[] { 0, 3 }, dev.Pages);
    }),
    ("Pixelschrift: Glyphen vollständig, Ziffern gleich breit", () =>
    {
        var f = PixelFont.Default;
        foreach (var c in Enumerable.Range(32, 95).Select(i => (char)i)) True(f.HasGlyph(c), $"Glyph fehlt: {c}");
        Eq(f.Measure("1"), f.Measure("8"));
        Eq(7, f.LineHeight()); Eq(21, f.LineHeight(3));
        var bmp = f.Render("_", 1);
        True(bmp[0, 6] && !bmp[0, 0], "Unterstrich unten");
        Eq(f.Measure("88", 2), 2 * f.Measure("88", 1));
    }),
    ("Formatierung von Sensorwerten", () =>
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Eq("54°C", ValueFormatter.Format("{value:0}°C", 54.4, de));
        Eq("4,2 GHz", ValueFormatter.Format("{value/1000:0.0} GHz", 4210, de));
        Eq("Last 23%", ValueFormatter.Format("Last {value:0}%", 23.2, de));
        Eq("--°C", ValueFormatter.Format("{value:0}°C", null, de));
        Eq("80", ValueFormatter.Format("{value*8:0}", 10, de));
        Eq("ohne Wert", ValueFormatter.Format("ohne Wert", 1, de));
    }),
    ("Auto-Abfragen finden Sensoren in Reihenfolge", () =>
    {
        var s = new StaticSensorSource(new[]
        {
            new SensorInfo("/a/1", "CPU", "cpu", "Core Max", "Temperature", "°C", 70),
            new SensorInfo("/a/2", "CPU", "cpu", "Core (Tctl/Tdie)", "Temperature", "°C", 60),
        });
        Eq(60.0, s.GetValue(DefaultApplets.CpuTemp));
        Eq(null, s.GetValue("auto:gpu/Temperature/GPU Core"));
        Eq(70.0, s.GetValue("/a/1"));
    }),
    ("Applets: JSON hin und zurück", () =>
    {
        var list = DefaultApplets.Create();
        var json = JsonSerializer.Serialize(list, JsonDefaults.Options);
        True(json.Contains("\"type\": \"sensor\""), "Typfeld fehlt");
        var back = JsonSerializer.Deserialize<List<Applet>>(json, JsonDefaults.Options)!;
        Eq(list.Count, back.Count);
        Eq(list[0].Widgets.Count, back[0].Widgets.Count);
        True(back[0].Widgets[2] is SensorWidget { Style.Size: 3 }, "Stil geht verloren");
    }),
    ("Speicher legt Vorlagen an und lädt sie", () =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "z10test-" + Guid.NewGuid().ToString("N"));
        var store = new AppStore(dir);
        var a = store.LoadApplets();
        Eq(DefaultApplets.Create().Count, a.Count);
        a[0].Name = "Geändert"; store.SaveApplet(a[0]);
        Eq("Geändert", new AppStore(dir).LoadApplets()[0].Name);
        Directory.Delete(dir, true);
    }),
    ("Alle Vorlagen rendern ohne Fehler und zeichnen etwas", () =>
    {
        var r = new AppletRenderer(PixelTextRasterizer.Instance, StaticSensorSource.Demo(), new SensorHistory());
        foreach (var a in DefaultApplets.Create())
        {
            var fb = new Framebuffer();
            r.Render(a, fb, new DateTime(2026, 11, 20, 10, 42, 17));
            True(fb.CountSet() > 20, $"{a.Name} ist leer");
        }
    }),
    ("Warnwert invertiert den Baustein", () =>
    {
        var src = new StaticSensorSource(new[] { new SensorInfo("/t", "x", "cpu", "T", "Temperature", "°C", 90) });
        var r = new AppletRenderer(PixelTextRasterizer.Instance, src, new SensorHistory());
        var a = new Applet { Widgets = { new BarWidget { X = 0, Y = 0, W = 10, H = 4, Sensor = "/t", Border = false, Warn = new WarnRule { Above = 85 } } } };
        var fb = new Framebuffer();
        r.Render(a, fb, DateTime.Now);
        True(r.HasWarning(a), "Warnung nicht erkannt");
        // 90 % gefüllt (9 Spalten) und dann invertiert → nur letzte Spalte an
        True(!fb[0, 0] && fb[9, 0], "Invertierung falsch");
    }),
    ("Verlauf zeichnet neuesten Wert rechts", () =>
    {
        var h = new SensorHistory();
        for (int i = 0; i < 10; i++) h.Record("/s", i == 9 ? 100 : 0);
        var r = new AppletRenderer(PixelTextRasterizer.Instance, StaticSensorSource.Demo(), h);
        var a = new Applet { Widgets = { new GraphWidget { X = 0, Y = 0, W = 20, H = 10, Sensor = "/s", Min = 0, Max = 100, Seconds = 10, Filled = true } } };
        var fb = new Framebuffer();
        r.Render(a, fb, DateTime.Now);
        True(fb[19, 0], "Spitze rechts oben fehlt");
        True(!fb[0, 0], "links oben darf nichts sein");
    }),
    ("Engine rotiert, sendet und gibt bei Unleash frei", () =>
    {
        var dev = new FakeDevice();
        bool conflict = false;
        var e = new AppletEngine(StaticSensorSource.Demo(), PixelTextRasterizer.Instance, () => dev, () => conflict, new AppSettings());
        var apps = DefaultApplets.Create();
        e.SetApplets(apps);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        e.Tick(t);
        Eq(EngineState.Connected, e.State);
        Eq("CPU", e.Current?.Name);
        True(dev.Reports.Any(r => r[1] == 0x20), "Helligkeit nicht gesetzt");
        True(dev.Pages.Count >= 4, "kein Bild gesendet");
        e.Tick(t.AddSeconds(9));
        Eq("GPU", e.Current?.Name);
        e.Previous(); e.Tick(t.AddSeconds(9.5));
        Eq("CPU", e.Current?.Name);
        conflict = true;
        e.Tick(t.AddSeconds(10));
        Eq(EngineState.Conflict, e.State);
        True(dev.Reports.Last()[2] == 0xFE, "nicht freigegeben");
    }),
};

int failed = 0;
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine($"  ok    {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"  FEHLER {name}: {ex.Message}"); }
}
Console.WriteLine(failed == 0 ? $"Alle {tests.Count} Tests bestanden." : $"{failed} von {tests.Count} Tests fehlgeschlagen.");
return failed == 0 ? 0 : 1;

static void Eq<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"erwartet {expected}, war {actual}");
}

static void Seq(int[] expected, List<int> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new Exception($"erwartet [{string.Join(",", expected)}], war [{string.Join(",", actual)}]");
}

static void True(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

sealed class FakeDevice : IZ10Device
{
    public List<byte[]> Reports { get; } = new();
    public List<int> Pages { get; } = new();
    public bool IsOpen => true;
    public string Description => "Testgerät";
    public TimeSpan PageDelay { get; set; } = TimeSpan.Zero;
    public void SendFeature(byte[] report)
    {
        Reports.Add(report);
        if (report[1] == 0x21 && report[2] < 4) Pages.Add(report[2]);
    }
    public void Dispose() { }
}
