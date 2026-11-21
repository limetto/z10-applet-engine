using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>Wandelt einen Framebuffer in ein Bild im Look des roten Z10-Displays.</summary>
public static class LcdImage
{
    private const uint Off = 0xFF4A0B0D;   // dunkles Rot
    private const uint On = 0xFFFF4B4F;    // leuchtendes Rot

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
