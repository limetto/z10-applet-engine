using Z10.Core.Device;

namespace Z10.Core.Rendering;

/// <summary>Bildspeicher in Displaygröße (160 × 32).</summary>
public sealed class Framebuffer : MonoBitmap
{
    public const int DefaultWidth = Z10Protocol.Width;
    public const int DefaultHeight = Z10Protocol.Height;

    public Framebuffer() : base(Z10Protocol.Width, Z10Protocol.Height) { }

    /// <summary>
    /// Wandelt in 4 logische Seiten (0 = oben) à 160 Byte um.
    /// Ein Byte pro Spalte, Bit 7 = oberstes Pixel der Seite.
    /// </summary>
    public byte[][] ToPages()
    {
        var pages = new byte[Z10Protocol.Pages][];
        for (int p = 0; p < Z10Protocol.Pages; p++)
        {
            var cols = new byte[Width];
            for (int x = 0; x < Width; x++)
            {
                int b = 0;
                for (int r = 0; r < 8; r++)
                    if (Pixels[(p * 8 + r) * Width + x]) b |= 0x80 >> r;
                cols[x] = (byte)b;
            }
            pages[p] = cols;
        }
        return pages;
    }

    public Framebuffer Clone()
    {
        var fb = new Framebuffer();
        Array.Copy(Pixels, fb.Pixels, Pixels.Length);
        return fb;
    }
}
