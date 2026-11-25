using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>Wandelt einen Framebuffer in ein Bild im Look des Z10-Displays (in der eingestellten Displayfarbe).</summary>
public static class LcdImage
{
    private const uint DefaultOff = 0xFF4A0B0D;   // dunkles Rot
    private const uint DefaultOn = 0xFFFF4B4F;    // leuchtendes Rot
    private static uint Off = DefaultOff;
    private static uint On = DefaultOn;

    /// <summary>Aktuelle Displayfarbe der Vorschau ("#RRGGBB").</summary>
    public static string? CurrentColor { get; private set; }

    /// <summary>Wird ausgelöst, wenn sich die Vorschaufarbe ändert (Vorschauen neu zeichnen).</summary>
    public static event Action? ColorChanged;

    /// <summary>
    /// Vorschaufarbe setzen. Leuchtende Pixel = Displayfarbe (auf volle Helligkeit gezogen, damit auch dunkle
    /// Farbtöne lesbar bleiben), dunkle Pixel = gleiche Farbe stark abgedunkelt. Null oder Schwarz = Standardrot.
    /// </summary>
    public static void SetColor(string? hex)
    {
        uint on = DefaultOn, off = DefaultOff;
        if (Z10.Core.Device.Z10Lighting.TryParseColor(hex, out var rgb) && rgb.Max() > 0)
        {
            double scale = 255.0 / rgb.Max();
            byte r = (byte)(rgb[0] * scale), g = (byte)(rgb[1] * scale), b = (byte)(rgb[2] * scale);
            // Weiß/helle Töne etwas weicher, damit es nach Hintergrundbeleuchtung und nicht nach Papier aussieht
            on = Argb(Lerp(r, 0x40), Lerp(g, 0x40), Lerp(b, 0x40), 0.12);
            off = Argb((byte)(r * 0.22), (byte)(g * 0.22), (byte)(b * 0.22), 0);
        }
        string? normalized = hex is null ? null : hex.ToUpperInvariant();
        if (on == On && off == Off && normalized == CurrentColor) return;
        On = on; Off = off; CurrentColor = normalized;
        ColorChanged?.Invoke();

        static byte Lerp(byte v, byte floor) => (byte)Math.Max(v, (int)floor);
        static uint Argb(byte r, byte g, byte b, double soften)
        {
            r = (byte)(r - r * soften * (g + b) / 510.0);
            g = (byte)(g - g * soften * (r + b) / 510.0);
            b = (byte)(b - b * soften * (r + g) / 510.0);
            return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }

    public static WriteableBitmap Create() => Create(Framebuffer.DefaultWidth, Framebuffer.DefaultHeight);

    public static WriteableBitmap Create(int width, int height) =>
        new(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Bgra32, null);

    /// <summary>Einzelnes Bild (z. B. Symbolvorschau) als fertige, eingefrorene Grafik.</summary>
    public static BitmapSource ToImage(MonoBitmap bmp)
    {
        var wb = Create(bmp.Width, bmp.Height);
        if (bmp.Width > 0 && bmp.Height > 0) Update(wb, bmp);
        wb.Freeze();
        return wb;
    }

    public static void Update(WriteableBitmap target, MonoBitmap fb)
    {
        var data = new uint[fb.Width * fb.Height];
        for (int y = 0; y < fb.Height; y++)
            for (int x = 0; x < fb.Width; x++)
                data[y * fb.Width + x] = fb[x, y] ? On : Off;
        target.WritePixels(new Int32Rect(0, 0, fb.Width, fb.Height), data, fb.Width * 4, 0);
    }
}
