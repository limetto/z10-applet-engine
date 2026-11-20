namespace Z10.Core.Device;

/// <summary>
/// LCD-Protokoll der EVGA Z10 (Nicht-RGB, USB 3842:2601).
/// Abgeleitet aus Mitschnitten und dem Verhalten von EVGA Unleash, am Gerät verifiziert.
///
/// Alle Befehle sind HID-Feature-Reports mit ID 7 (Usage Page 0xFF01), 264 Byte inkl. Report-ID.
///
/// Seite schreiben:  07 21 &lt;Seite&gt; 00 00 00 00 00 &lt;160 Byte Spalten&gt; 00…
///   - Seite 0 = UNTERSTE 8-Pixel-Zeile, Seite 3 = oberste.
///   - Ein Byte pro Spalte, Bit 7 = oberstes Pixel der Seite.
///   - Die Firmware zeigt den Puffer erst an, wenn Seite 3 geschrieben wird
///     → immer in der Reihenfolge 0,1,2,3 senden, Seite 3 zuletzt.
/// Display freigeben: 07 21 FE …
/// Helligkeit/Kontrast: 07 20 &lt;Kontrast-Tabellenwert&gt; &lt;Helligkeit-1&gt; …
/// </summary>
public static class Z10Protocol
{
    public const ushort VendorId = 0x3842;
    public const ushort ProductId = 0x2601;
    public const ushort VendorUsagePage = 0xFF01;

    public const int Width = 160;
    public const int Height = 32;
    public const int Pages = Height / 8;

    public const byte ReportId = 0x07;
    public const int ReportLength = 264;
    public const int PayloadOffset = 8;

    public const byte CmdWritePage = 0x21;
    public const byte CmdBacklight = 0x20;
    public const byte PageRelease = 0xFE;

    public const int BacklightLevels = 5;
    private static readonly byte[] ContrastTable = { 0x00, 0x0C, 0x18, 0x24, 0x30 };

    /// <summary>Report für eine Seite. <paramref name="devicePage"/> ist die Geräte-Seite (0 = unten).</summary>
    public static byte[] BuildPageReport(int devicePage, ReadOnlySpan<byte> columns)
    {
        if (devicePage is < 0 or >= Pages) throw new ArgumentOutOfRangeException(nameof(devicePage));
        if (columns.Length != Width) throw new ArgumentException($"Eine Seite braucht {Width} Bytes.", nameof(columns));
        var buf = NewReport(CmdWritePage, (byte)devicePage);
        columns.CopyTo(buf.AsSpan(PayloadOffset));
        return buf;
    }

    public static byte[] BuildReleaseReport() => NewReport(CmdWritePage, PageRelease);

    /// <summary>Helligkeit und Kontrast in Stufen 1..5.</summary>
    public static byte[] BuildBacklightReport(int brightness, int contrast)
    {
        if (brightness is < 1 or > BacklightLevels) throw new ArgumentOutOfRangeException(nameof(brightness));
        if (contrast is < 1 or > BacklightLevels) throw new ArgumentOutOfRangeException(nameof(contrast));
        var buf = NewReport(CmdBacklight, ContrastTable[contrast - 1]);
        buf[3] = (byte)(brightness - 1);
        return buf;
    }

    /// <summary>Logische Zeile (0 = oben) → Geräte-Seite (0 = unten).</summary>
    public static int ToDevicePage(int logicalPage) => Pages - 1 - logicalPage;

    private static byte[] NewReport(byte cmd, byte arg)
    {
        var buf = new byte[ReportLength];
        buf[0] = ReportId;
        buf[1] = cmd;
        buf[2] = arg;
        return buf;
    }
}
