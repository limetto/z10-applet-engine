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
    ("Formeln: Rechnen, Funktionen, fehlende Sensoren", () =>
    {
        double? L(string id) => id switch { "a" => 10, "b" => 4, "auto:cpu/Load/*" => 50, _ => null };
        Eq(14.0, Formula.Parse("[a] + [b]").Evaluate(L));
        Eq(18.0, Formula.Parse("([a] - 1) * 2").Evaluate(L));
        Eq(1024.0, Formula.Parse("2^10").Evaluate(L));
        Eq(-6.0, Formula.Parse("-[a] + [b]").Evaluate(L));
        Eq(50.0, Formula.Parse("max([a], [auto:cpu/Load/*], [fehlt])").Evaluate(L));
        Eq(7.0, Formula.Parse("avg([a], [b], [fehlt])").Evaluate(L));
        Eq(null, Formula.Parse("[a] + [fehlt]").Evaluate(L));
        Eq(null, Formula.Parse("[a] / 0").Evaluate(L));
        Eq(3.3, Formula.Parse("round(3.333, 1)").Evaluate(L));
        Eq(5.0, Formula.Parse("clamp([a], 0, 5)").Evaluate(L));
        Seq2(new[] { "a", "b" }, Formula.Parse("[a]*[b]+[a]").SensorRefs);
        foreach (var bad in new[] { "", "[a] +", "foo([a])", "([a]", "[a", "round(1,2,3)", "1,5 + 2" })
            True(!Formula.TryParse(bad, out _, out var err) && err is not null, $"Fehler nicht erkannt: {bad}");
    }),
    ("Sensor-Hub: Quellen zusammenführen und eigene Sensoren berechnen", () =>
    {
        var cpu = new StaticSensorSource(new[] { new SensorInfo("/c/t", "CPU", "cpu", "CPU Package", "Temperature", "°C", 60) });
        var gpu = new StaticSensorSource(new[] { new SensorInfo("/g/t", "GPU", "gpu", "GPU Core", "Temperature", "°C", 70) });
        var hub = new SensorHub(cpu, gpu);
        hub.SetCustomSensors(new[]
        {
            new CustomSensor { Key = "hot", Name = "Heißester Chip", Formula = "max([auto:cpu/Temperature/*], [/g/t])", Unit = "°C" },
            new CustomSensor { Key = "diff", Formula = "[custom:hot] - [/c/t]" },
            new CustomSensor { Key = "kaputt", Formula = "[a] +" },
        });
        hub.Update();
        Eq(70.0, hub.GetValue("custom:hot"));
        Eq(10.0, hub.GetValue("custom:diff"));
        Eq(null, hub.GetValue("custom:kaputt"));
        True(hub.CustomErrors.ContainsKey("kaputt"), "Formelfehler fehlt");
        Eq(60.0, hub.GetValue(DefaultApplets.CpuTemp));
        True(hub.Sensors.Any(s => s.Id == "custom:hot" && s.HardwareKind == "custom"), "eigener Sensor nicht gelistet");
    }),
    ("Glättung: Mittelwert und Median", () =>
    {
        var h = new SensorHistory();
        foreach (var v in new double?[] { 10, 10, 100, 10, 10 }) h.Record("/s", v);
        Eq(28.0, h.Smoothed("/s", SmoothingMode.Average, 5));
        Eq(10.0, h.Smoothed("/s", SmoothingMode.Median, 5));
        Eq(10.0, h.Smoothed("/s", SmoothingMode.Average, 2));
        var src = new StaticSensorSource(new[] { new SensorInfo("/s", "x", "cpu", "S", "Load", "%", 10) });
        var r = new AppletRenderer(PixelTextRasterizer.Instance, src, h);
        Eq(28.0, r.ValueOf(new SensorWidget { Sensor = "/s", Smoothing = SmoothingMode.Average, SmoothingSeconds = 5 }));
        Eq(10.0, r.ValueOf(new SensorWidget { Sensor = "/s" }));
        Eq(10.0, r.ValueOf(new SensorWidget { Sensor = "/neu", Smoothing = SmoothingMode.Average }) ?? 10.0);
    }),
    ("RTSS: Shared Memory auslesen", () =>
    {
        const int entrySize = 0x1000, arrOffset = 0x100;
        var mem = new byte[arrOffset + 3 * entrySize];
        void U(int o, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(mem.AsSpan(o, 4), v);
        U(0, RtssReader.Signature); U(4, 0x00020013); U(8, entrySize); U(12, arrOffset); U(16, 3);
        void Entry(int i, uint pid, string name, uint t0, uint t1, uint frames, uint ftUs)
        {
            int e = arrOffset + i * entrySize;
            U(e, pid);
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(mem, e + 4);
            U(e + 268, t0); U(e + 272, t1); U(e + 276, frames); U(e + 280, ftUs);
        }
        Entry(0, 100, @"C:\Games\alt.exe", 1000, 2000, 60, 16000);     // veraltet
        Entry(1, 200, @"C:\Games\Cyberpunk2077.exe", 9000, 9500, 72, 6900);
        Entry(2, 0, "leer", 0, 0, 0, 0);
        var r = RtssReader.Parse(mem, 10_000);
        True(r is not null, "nichts gefunden");
        Eq("Cyberpunk2077", r!.App);
        Eq(144.0, r.Fps);
        Eq(6.9, r.FrametimeMs);
        Eq(null, RtssReader.Parse(mem, 20_000));                          // alles veraltet
        mem[0] = 0; Eq(null, RtssReader.Parse(mem, 10_000));              // falsche Signatur
    }),
    ("Doppelte Sensor-Ids werden eindeutig (NVIDIA load/3)", () =>
    {
        var src = new StaticSensorSource(new[]
        {
            new SensorInfo("/gpu-nvidia/0/load/3", "RTX", "gpu", "GPU Bus", "Load", "%", 5),
            new SensorInfo("/gpu-nvidia/0/load/3", "RTX", "gpu", "GPU Memory", "Load", "%", 40),
            new SensorInfo("/gpu-nvidia/0/load/0", "RTX", "gpu", "GPU Core", "Load", "%", 90),
        });
        Eq(5.0, src.GetValue("/gpu-nvidia/0/load/3#gpu-bus"));
        Eq(40.0, src.GetValue("/gpu-nvidia/0/load/3#gpu-memory"));
        Eq(5.0, src.GetValue("/gpu-nvidia/0/load/3"));          // alte Id: erster Sensor, wie bisher
        Eq(90.0, src.GetValue("/gpu-nvidia/0/load/0"));
        Eq(3, src.Sensors.Select(x => x.Id).Distinct().Count());
        var hub = new SensorHub(src);
        hub.SetCustomSensors(new[] { new CustomSensor { Key = "m", Formula = "[/gpu-nvidia/0/load/3#gpu-memory] * 2" } });
        hub.Update();
        Eq(80.0, hub.GetValue("custom:m"));
        Eq(40.0, hub.GetValue("auto:gpu/Load/GPU Memory"));
    }),
    ("Verlauf mit Rahmen: Minimum liegt auf der Rahmenlinie", () =>
    {
        var h = new SensorHistory();
        for (int i = 0; i < 10; i++) h.Record("/s", i == 9 ? 100 : 0);
        var r = new AppletRenderer(PixelTextRasterizer.Instance, StaticSensorSource.Demo(), h);
        foreach (var filled in new[] { true, false })
        {
            var a = new Applet { Widgets = { new GraphWidget { X = 0, Y = 0, W = 20, H = 10, Sensor = "/s", Min = 0, Max = 100, Seconds = 10, Filled = filled, Border = true } } };
            var fb = new Framebuffer();
            r.Render(a, fb, DateTime.Now);
            // Zeile direkt über dem unteren Rahmen (y = 8) bleibt bei Wert 0 leer
            True(!fb[5, 8], $"Linie über dem Rahmen (gefüllt={filled})");
            True(fb[5, 9], "Rahmen unten fehlt");
            True(fb[18, 1], $"Spitze fehlt (gefüllt={filled})");
        }
    }),
    ("Afterburner: Shared Memory auslesen", () =>
    {
        const int header = 32, entry = 1324;
        var mem = new byte[header + 3 * entry];
        void U(int o, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(mem.AsSpan(o, 4), v);
        void F(int o, float v) => System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(mem.AsSpan(o, 4), v);
        void S(int o, string v) => System.Text.Encoding.Latin1.GetBytes(v).CopyTo(mem, o);
        U(0, MahmReader.Signature); U(4, 0x00020000); U(8, header); U(12, 3); U(16, entry);
        void E(int i, string name, string units, string loc, float val, uint gpu)
        {
            int e = header + i * entry;
            S(e, name); S(e + 260, units); S(e + 520, loc); F(e + 1300, val); U(e + 1316, gpu);
        }
        E(0, "GPU temperature", "°C", "GPU-Temperatur", 61.5f, 0);
        E(1, "Framerate", "FPS", "Framerate", float.MaxValue, uint.MaxValue);   // kein RTSS → ungültig
        E(2, "CPU usage", "%", "", 23f, uint.MaxValue);
        var list = MahmReader.Parse(mem)!;
        Eq(3, list.Count);
        Eq("/afterburner/gpu0/gpu-temperature", list[0].Id);
        Eq("GPU-Temperatur", list[0].Name);
        Eq("Temperature", list[0].Type);
        Eq(61.5, list[0].Value);
        Eq(null, list[1].Value);
        Eq("Framerate", list[1].Type);
        Eq("CPU usage", list[2].Name);
        mem[0] = 0; Eq(null, MahmReader.Parse(mem));
    }),
    ("Format für Stummschaltung", () =>
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Eq("STUMM", ValueFormatter.Format("{value:STUMM;;laut}", 1, de));
        Eq("laut", ValueFormatter.Format("{value:STUMM;;laut}", 0, de));
    }),
    ("Engine sendet regelmäßig und nach InvalidateDisplay alle Seiten neu", () =>
    {
        var dev = new FakeDevice();
        var e = new AppletEngine(StaticSensorSource.Demo(), PixelTextRasterizer.Instance, () => dev, () => false, new AppSettings());
        e.SetApplets(new[] { new Applet { Name = "Statisch", DurationSeconds = 0, Widgets = { new TextWidget { Text = "FEST", W = 60 } } } });
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        e.Tick(t);
        Seq(new[] { 0, 1, 2, 3 }, dev.Pages);
        dev.Pages.Clear(); e.Tick(t.AddSeconds(1.1));
        Seq(new[] { 3 }, dev.Pages);                    // unverändert → nur Abschlussseite
        dev.Pages.Clear(); e.Tick(t.AddSeconds(6));
        Seq(new[] { 0, 1, 2, 3 }, dev.Pages);           // regelmäßige Vollauffrischung
        dev.Pages.Clear(); dev.Reports.Clear();
        e.InvalidateDisplay(); e.Tick(t.AddSeconds(6.2));
        Seq(new[] { 0, 1, 2, 3 }, dev.Pages);           // nach Aufwachen sofort komplett
        True(dev.Reports.Any(r => r[1] == 0x20), "Helligkeit nicht neu gesetzt");
    }),
    ("Überlagerung: Schrift auf vollem Balken bleibt sichtbar", () =>
    {
        var src = new StaticSensorSource(new[] { new SensorInfo("/l", "x", "cpu", "L", "Load", "%", 100) });
        var r = new AppletRenderer(PixelTextRasterizer.Instance, src, new SensorHistory());
        Framebuffer Draw(BlendMode mode)
        {
            var a = new Applet { Widgets =
            {
                new BarWidget { X = 0, Y = 0, W = 40, H = 9, Sensor = "/l", Border = false },
                new TextWidget { X = 1, Y = 1, W = 38, H = 7, Text = "I", Blend = mode },
            } };
            var fb = new Framebuffer();
            r.Render(a, fb, DateTime.Now);
            return fb;
        }
        // "I" (beschnitten auf 3 Spalten): mittlere Spalte x = 1 + 1 ist in Zeile 1..7 gesetzt
        True(Draw(BlendMode.Normal)[2, 4], "Normal: Balken voll");
        True(!Draw(BlendMode.Invert)[2, 4], "Invertieren: Buchstabe muss ausgespart sein");
        True(Draw(BlendMode.Invert)[20, 4], "Invertieren: Balken daneben bleibt");
        True(!Draw(BlendMode.Erase)[2, 4], "Ausstanzen: Buchstabe gelöscht");
        // Invertieren über leerem Grund zeichnet die Schrift normal
        var empty = new Framebuffer();
        var layer = new Framebuffer();
        layer.Blit(PixelFont.Default.Render("I"), 0, 0);
        empty.Combine(layer, BlendMode.Invert);
        True(empty.CountSet() > 0, "Invertieren auf leerem Grund");
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

static void Seq2(string[] expected, IReadOnlyList<string> actual)
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
