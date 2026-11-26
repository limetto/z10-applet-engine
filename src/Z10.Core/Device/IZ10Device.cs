using Z10.Core.Rendering;

namespace Z10.Core.Device;

public interface IZ10Device : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Kurzbeschreibung für die Oberfläche.</summary>
    string Description { get; }

    /// <summary>Pause nach jeder Seite. 10 ms ist das getestete Minimum.</summary>
    TimeSpan PageDelay { get; set; }

    /// <summary>Sendet einen Report (264 Byte). Wirft IOException bei Fehler.</summary>
    void SendFeature(byte[] report);

    /// <summary>Liest Feature-Report 7 (264 Byte inkl. Report-ID). Wirft IOException bei Fehler.</summary>
    byte[] GetFeature();
}

/// <summary>Schickt Framebuffer an die Tastatur und überspringt unveränderte Seiten.</summary>
public sealed class Z10Display
{
    private readonly IZ10Device _device;
    private byte[][]? _last;

    public Z10Display(IZ10Device device) => _device = device;

    public IZ10Device Device => _device;

    /// <summary>Erzwingt beim nächsten Show() das Senden aller Seiten.</summary>
    public void Invalidate() => _last = null;

    /// <summary>Kleinste Pause zwischen zwei Seiten, die die Firmware sicher verkraftet (Benchmark: 10 ms).</summary>
    public static readonly TimeSpan MinPageDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Erstes Bild nach dem Verbinden: mit der kürzesten sicheren Pause senden, damit der alte Inhalt im
    /// Speicher der Tastatur möglichst kurz sichtbar ist, danach noch einmal normal (falls Seiten verloren gingen).
    /// Früher ganz ohne Pause – das hat die Firmware vermutlich gelegentlich zum Absturz gebracht.
    /// </summary>
    public void ShowFirst(Framebuffer fb)
    {
        var delay = _device.PageDelay;
        if (delay > MinPageDelay) _device.PageDelay = MinPageDelay;
        try { _last = null; Show(fb); }
        finally { _device.PageDelay = delay; }
        _last = null;
        Show(fb);
    }

    public void Show(Framebuffer fb)
    {
        var pages = fb.ToPages();
        // Geräte-Reihenfolge 0..3 (unten → oben). Logische Seite 0 (oben) = Geräteseite 3 muss
        // zuletzt und immer gesendet werden, weil erst sie die Anzeige auslöst.
        for (int devicePage = 0; devicePage < Z10Protocol.Pages; devicePage++)
        {
            int logical = Z10Protocol.ToDevicePage(devicePage);
            bool isCommitPage = devicePage == Z10Protocol.Pages - 1;
            if (!isCommitPage && _last is not null && _last[logical].AsSpan().SequenceEqual(pages[logical]))
                continue;
            _device.SendFeature(Z10Protocol.BuildPageReport(devicePage, pages[logical]));
            if (_device.PageDelay > TimeSpan.Zero) Thread.Sleep(_device.PageDelay);
        }
        _last = pages;
    }

    /// <summary>
    /// Gibt das Display an die Firmware zurück. Vorher wird der Speicher geleert: Die Tastatur behält den
    /// letzten Inhalt und zeigt ihn beim nächsten Übernehmen kurz an – so ist das dann ein leeres Bild.
    /// </summary>
    public void Release()
    {
        try { _last = null; Show(new Framebuffer()); } catch (IOException) { }
        _device.SendFeature(Z10Protocol.BuildReleaseReport());
        _last = null;
    }

    public void SetBacklight(int brightness, int contrast)
    {
        _device.SendFeature(Z10Protocol.BuildBacklightReport(brightness, contrast));
        Thread.Sleep(100);
    }
}
