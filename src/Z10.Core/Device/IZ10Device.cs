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

    public void Release()
    {
        _device.SendFeature(Z10Protocol.BuildReleaseReport());
        _last = null;
    }

    public void SetBacklight(int brightness, int contrast)
    {
        _device.SendFeature(Z10Protocol.BuildBacklightReport(brightness, contrast));
        Thread.Sleep(100);
    }
}
