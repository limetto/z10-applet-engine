namespace Z10.Core.Rendering;

public enum ScreensaverKind
{
    /// <summary>App-Logo, wandert langsam hin und her, die Balken tanzen.</summary>
    Logo,
    /// <summary>Sternenflug wie der alte Windows-Bildschirmschoner „Starfield“.</summary>
    Starfield,
    /// <summary>Fallende Zeichen wie in „Matrix“.</summary>
    Matrix,
}

/// <summary>Ein Bildschirmschoner: zeichnet zu einer Zeit ein Bild. Zustand lebt im Objekt.</summary>
public interface IScreensaver
{
    /// <param name="fb">leeres Bild</param>
    /// <param name="t">Sekunden seit Start</param>
    /// <param name="dt">Sekunden seit dem letzten Bild (begrenzt)</param>
    void Render(Framebuffer fb, double t, double dt);
}

public static class Screensavers
{
    public static IScreensaver Create(ScreensaverKind kind, int? seed = null)
    {
        var rnd = seed is int s ? new Random(s) : new Random();
        return kind switch
        {
            ScreensaverKind.Starfield => new StarfieldSaver(rnd),
            ScreensaverKind.Matrix => new MatrixSaver(rnd),
            _ => new LogoSaver(),
        };
    }

    public static string DisplayName(ScreensaverKind kind) => kind switch
    {
        ScreensaverKind.Starfield => "Sternenflug",
        ScreensaverKind.Matrix => "Matrix",
        _ => "Logo",
    };
}

/// <summary>Logo pendelt langsam über das Display (sanft abgebremst an den Rändern), die Balken tanzen.</summary>
internal sealed class LogoSaver : IScreensaver
{
    private const int MarkW = 28, Gap = 8, TitleScale = 3;
    private readonly MonoBitmap _text;

    public LogoSaver()
    {
        var font = PixelFont.Default;
        int w = Math.Max(font.Measure("Z10", TitleScale) + 1, font.Measure("APPLET ENGINE"));
        _text = new MonoBitmap(w, Framebuffer.DefaultHeight);
        _text.Blit(font.Render("Z10", TitleScale), 0, 0);
        _text.Blit(font.Render("Z10", TitleScale), 1, 0);
        _text.Blit(font.Render("APPLET ENGINE"), 0, Framebuffer.DefaultHeight - PixelFont.GlyphHeight);
    }

    public void Render(Framebuffer fb, double t, double dt)
    {
        int total = MarkW + Gap + _text.Width;
        int range = Math.Max(0, fb.Width - total);
        // Pendel: ca. 20 s für hin und zurück, an den Rändern weich
        double phase = (1 - Math.Cos(t * 2 * Math.PI / 20)) / 2;
        int ox = (int)Math.Round(phase * range);
        int my = (fb.Height - 26) / 2;

        // Balken tanzen wie ein Equalizer (zwei überlagerte Sinus je Balken)
        var heights = new double[4];
        for (int i = 0; i < 4; i++)
            heights[i] = 0.55 + 0.35 * Math.Sin(t * (2.1 + i * 0.7) + i * 1.3) + 0.15 * Math.Sin(t * (5.3 + i) + i);
        DrawMark(fb, ox, my, heights);
        fb.Blit(_text, ox + MarkW + Gap, 0);
    }

    private static void DrawMark(MonoBitmap fb, int ox, int oy, double[] level)
    {
        int[] barX = { 4, 9, 14, 19 };
        const int barW = 3, barBottom = 23, maxH = 17;
        fb.FillRect(ox + 4, oy, MarkW - 8, 2);
        fb.DrawRect(ox, oy + 4, MarkW, 22);
        fb[ox, oy + 4] = false;
        fb[ox + MarkW - 1, oy + 4] = false;
        fb[ox, oy + 25] = false;
        fb[ox + MarkW - 1, oy + 25] = false;
        for (int i = 0; i < 4; i++)
        {
            int h = Math.Clamp((int)Math.Round(level[i] * maxH), 1, maxH);
            fb.FillRect(ox + barX[i], oy + barBottom - h + 1, barW, h);
        }
    }
}

/// <summary>Sterne fliegen aus der Mitte auf den Betrachter zu; nahe Sterne werden größer und ziehen Spuren.</summary>
internal sealed class StarfieldSaver : IScreensaver
{
    private const int Count = 120;
    private const double Speed = 0.55;       // Tiefe pro Sekunde
    private const double Focal = 26;         // Pixel – klein, damit die Sterne auch zur Seite fliegen
    private readonly Random _rnd;
    private readonly double[] _x = new double[Count], _y = new double[Count], _z = new double[Count];

    public StarfieldSaver(Random rnd)
    {
        _rnd = rnd;
        for (int i = 0; i < Count; i++) Spawn(i, _rnd.NextDouble());
    }

    private void Spawn(int i, double z)
    {
        // breiter als hoch, passend zum 160×32-Display
        _x[i] = (_rnd.NextDouble() * 2 - 1) * 3.2;
        _y[i] = (_rnd.NextDouble() * 2 - 1) * 0.7;
        _z[i] = 0.05 + z * 0.95;
    }

    public void Render(Framebuffer fb, double t, double dt)
    {
        double cx = fb.Width / 2.0, cy = fb.Height / 2.0;
        for (int i = 0; i < Count; i++)
        {
            double zOld = _z[i];
            _z[i] -= Speed * dt;
            if (_z[i] <= 0.04) { Spawn(i, 1); continue; }
            int sx = (int)Math.Round(cx + _x[i] / _z[i] * Focal);
            int sy = (int)Math.Round(cy + _y[i] / _z[i] * Focal);
            if (sx < -2 || sy < -2 || sx > fb.Width + 1 || sy > fb.Height + 1) { Spawn(i, 1); continue; }

            if (_z[i] < 0.35)
            {
                // nahe: Spur vom vorigen Ort und 2×2-Punkt
                int px = (int)Math.Round(cx + _x[i] / zOld * Focal);
                int py = (int)Math.Round(cy + _y[i] / zOld * Focal);
                fb.DrawLine(px, py, sx, sy);
                fb.FillRect(sx, sy, 2, _z[i] < 0.18 ? 2 : 1);
            }
            else if (_z[i] < 0.75 || ((int)(t * 8) + i) % 3 != 0)
            {
                fb[sx, sy] = true;   // ferne Sterne funkeln leicht
            }
        }
    }
}

/// <summary>Fallende Zeichenketten: heller (invertierter) Kopf, flackernde Spur, ausgedünntes Ende.</summary>
internal sealed class MatrixSaver : IScreensaver
{
    private const int CellW = 4, CellH = 6;      // 3×5-Zeichen + 1 px Abstand
    private readonly Random _rnd;
    private readonly int _cols, _rows;
    private readonly byte[,] _glyph;             // Zeichen je Zelle
    private readonly Drop[] _drops;

    private sealed class Drop
    {
        public double Head;      // Kopfposition in Zellen (kann über dem Rand starten)
        public double Speed;     // Zellen pro Sekunde
        public int Length;       // Spurlänge in Zellen
        public double Pause;     // Wartezeit, bevor der Tropfen (neu) startet
    }

    // 3×5-Zeichen, Spalten von links nach rechts, Bit 0 = oben. Ziffern und erfundene „Katakana“-artige Formen.
    private static readonly byte[][] Glyphs =
    {
        new byte[] { 0x1F, 0x11, 0x1F }, new byte[] { 0x12, 0x1F, 0x10 }, new byte[] { 0x1D, 0x15, 0x17 },
        new byte[] { 0x15, 0x15, 0x1F }, new byte[] { 0x07, 0x04, 0x1F }, new byte[] { 0x17, 0x15, 0x1D },
        new byte[] { 0x1F, 0x15, 0x1D }, new byte[] { 0x01, 0x1D, 0x03 }, new byte[] { 0x1F, 0x15, 0x1F },
        new byte[] { 0x17, 0x15, 0x1F },
        new byte[] { 0x01, 0x1F, 0x05 }, new byte[] { 0x11, 0x0F, 0x01 }, new byte[] { 0x04, 0x1F, 0x04 },
        new byte[] { 0x19, 0x05, 0x03 }, new byte[] { 0x10, 0x0F, 0x02 }, new byte[] { 0x02, 0x1A, 0x06 },
        new byte[] { 0x15, 0x15, 0x0D }, new byte[] { 0x1E, 0x02, 0x1E }, new byte[] { 0x11, 0x0A, 0x04 },
        new byte[] { 0x09, 0x1D, 0x09 }, new byte[] { 0x03, 0x1D, 0x01 }, new byte[] { 0x1C, 0x04, 0x1F },
        new byte[] { 0x12, 0x15, 0x09 }, new byte[] { 0x0F, 0x14, 0x14 }, new byte[] { 0x01, 0x07, 0x19 },
        new byte[] { 0x0E, 0x15, 0x0E }, new byte[] { 0x18, 0x07, 0x18 }, new byte[] { 0x05, 0x1F, 0x14 },
    };

    public MatrixSaver(Random rnd)
    {
        _rnd = rnd;
        _cols = Framebuffer.DefaultWidth / CellW;
        _rows = (Framebuffer.DefaultHeight + CellH - 1) / CellH;
        _glyph = new byte[_cols, _rows];
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                _glyph[c, r] = (byte)_rnd.Next(Glyphs.Length);
        _drops = new Drop[_cols];
        for (int c = 0; c < _cols; c++)
        {
            _drops[c] = new Drop();
            Restart(_drops[c]);
            _drops[c].Head = _rnd.NextDouble() * (_rows + _drops[c].Length) - 2;   // schon mittendrin starten
        }
    }

    private void Restart(Drop d)
    {
        d.Speed = 3 + _rnd.NextDouble() * 7;
        d.Length = 2 + _rnd.Next(6);
        d.Head = -1;
        d.Pause = _rnd.NextDouble() * 2.5;
    }

    public void Render(Framebuffer fb, double t, double dt)
    {
        // einige Zeichen flackern
        int flicker = Math.Max(1, (int)(_cols * _rows * dt * 1.5));
        for (int i = 0; i < flicker; i++)
            _glyph[_rnd.Next(_cols), _rnd.Next(_rows)] = (byte)_rnd.Next(Glyphs.Length);

        for (int c = 0; c < _cols; c++)
        {
            var d = _drops[c];
            if (d.Pause > 0) { d.Pause -= dt; continue; }
            int oldHead = (int)Math.Floor(d.Head);
            d.Head += d.Speed * dt;
            int head = (int)Math.Floor(d.Head);
            for (int r = oldHead + 1; r <= head; r++)                       // neuer Kopf bekommt ein neues Zeichen
                if (r >= 0 && r < _rows) _glyph[c, r] = (byte)_rnd.Next(Glyphs.Length);
            if (head - d.Length >= _rows) { Restart(d); continue; }

            for (int k = 0; k <= d.Length; k++)
            {
                int r = head - k;
                if (r < 0 || r >= _rows) continue;
                int x = c * CellW, y = r * CellH - 1;                     // 32 px: oberste Zeile leicht angeschnitten
                if (k == 0) DrawHead(fb, x, y, _glyph[c, r]);
                else DrawGlyph(fb, x, y, _glyph[c, r], thin: k >= d.Length - 1 && d.Length > 2, odd: (r + c) % 2 == 0);
            }
        }
    }

    private static void DrawGlyph(MonoBitmap fb, int x, int y, byte g, bool thin, bool odd)
    {
        var cols = Glyphs[g];
        for (int cx = 0; cx < 3; cx++)
            for (int cy = 0; cy < 5; cy++)
                if ((cols[cx] >> cy & 1) != 0 && (!thin || ((cx + cy) % 2 == 0) == odd))
                    fb[x + cx, y + cy] = true;
    }

    /// <summary>Kopf: Zeichen ausgespart aus einem hellen Block – wirkt auf dem einfarbigen Display wie „leuchtend“.</summary>
    private static void DrawHead(MonoBitmap fb, int x, int y, byte g)
    {
        fb.FillRect(x, y - 1, CellW, CellH + 1);
        var cols = Glyphs[g];
        for (int cx = 0; cx < 3; cx++)
            for (int cy = 0; cy < 5; cy++)
                if ((cols[cx] >> cy & 1) != 0) fb[x + cx, y + cy] = false;
    }
}
