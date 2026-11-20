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

    public static WriteableBitmap Create() =>
        new(Framebuffer.DefaultWidth, Framebuffer.DefaultHeight, 96, 96, PixelFormats.Bgra32, null);

    public static void Update(WriteableBitmap target, MonoBitmap fb)
    {
        var data = new uint[fb.Width * fb.Height];
        for (int y = 0; y < fb.Height; y++)
            for (int x = 0; x < fb.Width; x++)
                data[y * fb.Width + x] = fb[x, y] ? On : Off;
        target.WritePixels(new Int32Rect(0, 0, fb.Width, fb.Height), data, fb.Width * 4, 0);
    }
}
