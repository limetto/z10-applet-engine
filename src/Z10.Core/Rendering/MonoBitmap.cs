namespace Z10.Core.Rendering;

/// <summary>Wie ein Baustein mit dem bereits Gezeichneten darunter verrechnet wird.</summary>
public enum BlendMode
{
    /// <summary>Pixel setzen (ODER).</summary>
    Normal,
    /// <summary>Darunterliegende Pixel umkehren (XOR): Schrift bleibt auf Balken lesbar.</summary>
    Invert,
    /// <summary>Pixel löschen: dunkle Schrift auf hellem Grund.</summary>
    Erase,
}

/// <summary>Einfaches 1-Bit-Bild (an/aus pro Pixel).</summary>
public class MonoBitmap
{
    protected readonly bool[] Pixels;

    public int Width { get; }
    public int Height { get; }

    public MonoBitmap(int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException();
        Width = width;
        Height = height;
        Pixels = new bool[width * height];
    }

    public bool this[int x, int y]
    {
        get => x >= 0 && y >= 0 && x < Width && y < Height && Pixels[y * Width + x];
        set
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height) Pixels[y * Width + x] = value;
        }
    }

    public void Clear(bool on = false) => Array.Fill(Pixels, on);

    public void FillRect(int x, int y, int w, int h, bool on = true)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                Pixels[yy * Width + xx] = on;
    }

    public void DrawRect(int x, int y, int w, int h, bool on = true)
    {
        if (w <= 0 || h <= 0) return;
        FillRect(x, y, w, 1, on);
        FillRect(x, y + h - 1, w, 1, on);
        FillRect(x, y, 1, h, on);
        FillRect(x + w - 1, y, 1, h, on);
    }

    public void Invert(int x, int y, int w, int h)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                Pixels[yy * Width + xx] = !Pixels[yy * Width + xx];
    }

    /// <summary>Bresenham-Linie.</summary>
    public void DrawLine(int x0, int y0, int x1, int y1, bool on = true)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            this[x0, y0] = on;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Zeichnet die gesetzten Pixel von <paramref name="src"/>, beschnitten auf das Clip-Rechteck.</summary>
    public void Blit(MonoBitmap src, int x, int y, int clipX = int.MinValue, int clipY = int.MinValue,
        int clipW = int.MaxValue, int clipH = int.MaxValue)
    {
        long cx1 = clipW == int.MaxValue ? long.MaxValue : (long)clipX + clipW;
        long cy1 = clipH == int.MaxValue ? long.MaxValue : (long)clipY + clipH;
        for (int sy = 0; sy < src.Height; sy++)
        {
            int ty = y + sy;
            if (ty < clipY || ty >= cy1) continue;
            for (int sx = 0; sx < src.Width; sx++)
            {
                int tx = x + sx;
                if (tx < clipX || tx >= cx1) continue;
                if (src[sx, sy]) this[tx, ty] = true;
            }
        }
    }

    /// <summary>Verrechnet eine gleich große Ebene mit diesem Bild.</summary>
    public void Combine(MonoBitmap layer, BlendMode mode)
    {
        if (layer.Width != Width || layer.Height != Height) throw new ArgumentException("Ebene hat eine andere Größe.");
        for (int i = 0; i < Pixels.Length; i++)
        {
            if (!layer.Pixels[i]) continue;
            Pixels[i] = mode switch
            {
                BlendMode.Invert => !Pixels[i],
                BlendMode.Erase => false,
                _ => true,
            };
        }
    }

    public int CountSet() => Pixels.Count(p => p);
}
