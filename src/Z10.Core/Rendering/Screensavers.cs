namespace Z10.Core.Rendering;

public enum ScreensaverKind
{
    /// <summary>App-Logo, wandert langsam hin und her, die Balken tanzen.</summary>
    Logo,
    /// <summary>Sternenflug wie der alte Windows-Bildschirmschoner „Starfield“.</summary>
    Starfield,
    /// <summary>Fallende Zeichen wie in „Matrix“.</summary>
    Matrix,
    /// <summary>Lagerfeuer mit Funkenflug unter einem Sternenhimmel.</summary>
    Campfire,
    /// <summary>Aquarium mit Fischen, Luftblasen und Wasserpflanzen.</summary>
    Aquarium,
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
            ScreensaverKind.Campfire => new CampfireSaver(rnd),
            ScreensaverKind.Aquarium => new AquariumSaver(rnd),
            _ => new LogoSaver(),
        };
    }

    public static string DisplayName(ScreensaverKind kind) => kind switch
    {
        ScreensaverKind.Starfield => "Sternenflug",
        ScreensaverKind.Matrix => "Matrix",
        ScreensaverKind.Campfire => "Lagerfeuer",
        ScreensaverKind.Aquarium => "Aquarium",
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

/// <summary>
/// Lagerfeuer: Wärmefeld wie beim klassischen „Doom-Feuer“ (Glut unten, steigt auf, kühlt ab, flackert im Wind),
/// über ein 4×4-Bayer-Raster in Pixel umgesetzt. Dazu Holzscheite, aufsteigende Funken und funkelnde Sterne.
/// </summary>
internal sealed class CampfireSaver : IScreensaver
{
    private const int W = Framebuffer.DefaultWidth, H = Framebuffer.DefaultHeight;
    private const int FireLeft = 52, FireRight = 108, SourceY = 28;
    private const double StepSeconds = 1 / 40.0;

    private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    private readonly Random _rnd;
    private readonly double[] _heat = new double[W * H];
    private readonly List<(double X, double Y, double Vx, double Life)> _sparks = new();
    private readonly (int X, int Y, double Phase)[] _stars;
    private double _acc, _wind, _windTarget;

    public CampfireSaver(Random rnd)
    {
        _rnd = rnd;
        _stars = Enumerable.Range(0, 16).Select(_ =>
        {
            int x = _rnd.Next(2) == 0 ? _rnd.Next(2, 44) : _rnd.Next(116, 158);
            return (x, _rnd.Next(1, 18), _rnd.NextDouble() * 10);
        }).ToArray();
        for (int i = 0; i < 80; i++) Step();   // Feuer schon brennend starten
    }

    public void Render(Framebuffer fb, double t, double dt)
    {
        _acc += dt;
        while (_acc >= StepSeconds) { _acc -= StepSeconds; Step(); }

        // Sterne funkeln
        foreach (var (x, y, ph) in _stars)
            if (Math.Sin(t * 1.7 + ph) > -0.6) fb[x, y] = true;

        // Feuer
        for (int y = 0; y < H; y++)
            for (int x = FireLeft - 8; x <= FireRight + 8; x++)
            {
                double h = _heat[y * W + x];
                if (h <= 0.04) continue;
                double threshold = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0;
                if (h > 0.7 || h > threshold * 0.7 + 0.1) fb[x, y] = true;
            }

        // Funken
        foreach (var sp in _sparks)
            fb[(int)Math.Round(sp.X), (int)Math.Round(sp.Y)] = true;

        // Holzscheite (über den Flammen, mit ausgesparter Maserung)
        DrawLog(fb, FireLeft + 4, 31, FireRight - 4, 27);
        DrawLog(fb, FireLeft + 4, 27, FireRight - 4, 31);
        // Boden
        for (int x = 0; x < W; x += 3) fb[x, H - 1] = true;
    }

    private static void DrawLog(MonoBitmap fb, int x0, int y0, int x1, int y1)
    {
        fb.DrawThickLine(x0, y0, x1, y1, 3);
        fb.DrawLine(x0 + 3, y0, x1 - 3, y1, false);   // Maserung
    }

    private void Step()
    {
        // Wind ändert sich langsam
        if (_rnd.NextDouble() < 0.02) _windTarget = (_rnd.NextDouble() - 0.5) * 1.2;
        _wind += (_windTarget - _wind) * 0.05;

        // Glutquelle: in der Mitte heiß, zum Rand hin schwächer; einzelne Stellen lodern kurz auf
        double mid = (FireLeft + FireRight) / 2.0, half = (FireRight - FireLeft) / 2.0;
        for (int x = FireLeft; x <= FireRight; x++)
        {
            double env = Math.Pow(Math.Cos((x - mid) / half * Math.PI / 2), 1.4);
            double v = env * (1.0 + _rnd.NextDouble() * 0.3);
            if (_rnd.NextDouble() < 0.04) v *= 0.4;                        // kurz dunkle Stelle → Zungen
            _heat[SourceY * W + x] = v;
            _heat[(SourceY + 1) * W + x] = v;
        }

        // „Doom-Feuer“: jede Zelle übernimmt die Wärme von unten, leicht seitlich versetzt und zufällig abgekühlt.
        // Der zufällige Versatz pro Pixel erzeugt die flackernden Flammenzungen.
        int lo = FireLeft - 10, hi = FireRight + 10;
        for (int y = 0; y < SourceY; y++)
            for (int x = lo; x <= hi; x++)
            {
                double src = _heat[(y + 1) * W + x];
                int shift = _rnd.Next(3) - 1;                                    // symmetrisch, sonst kippt das Feuer
                if (_rnd.NextDouble() < Math.Abs(_wind) * 0.4) shift += Math.Sign(_wind);
                int dx = Math.Clamp(x + shift, lo, hi);
                _heat[y * W + dx] = Math.Max(0, src - _rnd.Next(2) * 0.068 - _rnd.NextDouble() * 0.01);
            }

        // Funken
        if (_rnd.NextDouble() < 0.12)
            _sparks.Add((FireLeft + half + (_rnd.NextDouble() - 0.5) * half, 12 + _rnd.NextDouble() * 6,
                (_rnd.NextDouble() - 0.5) * 0.6, 0.8 + _rnd.NextDouble() * 1.2));
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var sp = _sparks[i];
            sp.X += (sp.Vx + _wind) * 12 * StepSeconds + (_rnd.NextDouble() - 0.5) * 0.4;
            sp.Y -= 10 * StepSeconds;
            sp.Life -= StepSeconds;
            if (sp.Life <= 0 || sp.Y < 0) _sparks.RemoveAt(i); else _sparks[i] = sp;
        }
    }
}

/// <summary>Aquarium: Fische verschiedener Form schwimmen hin und her, Blasen steigen auf, Pflanzen wiegen sich.</summary>
internal sealed class AquariumSaver : IScreensaver
{
    private const int W = Framebuffer.DefaultWidth, H = Framebuffer.DefaultHeight;

    // Nach rechts schauend, '#' = Pixel, '.' = leer. Zwei Bilder je Fisch (Schwanzschlag).
    private static readonly string[][][] Shapes =
    {
        new[] {
            new[] { "....####..", "#.#####.#.", "##########", "#.#######.", "....####.." },
            new[] { "....####..", ".######.#.", "##########", ".########.", "....####.." },
        },
        new[] {
            new[] { "#.###.", "####.#", "#.###." },
            new[] { ".####.", "####.#", ".####." },
        },
        new[] {
            new[] { "...#...", "..###..", "#.####.", "####.##", "#.####.", "..###..", "...#..." },
            new[] { "...#...", "..###..", ".#####.", "####.##", ".#####.", "..###..", "...#..." },
        },
        new[] {
            new[] { "#...######...", "##.########..", "#############", "##.##########", "#...######..." },
            new[] { "....######...", "#..########..", "#############", "#..##########", "....######..." },
        },
    };

    private sealed class Fish
    {
        public int Shape;
        public double X, BaseY, Speed, Phase;
        public int Dir;
        public double NextTurn;
    }

    private readonly Random _rnd;
    private readonly List<Fish> _fish = new();
    private readonly List<(double X, double Y, int Size, double Phase)> _bubbles = new();
    private readonly (int X, int Height, double Phase)[] _plants;
    private readonly (int X, int W)[] _stones;
    private double _bubbleTimer;

    public AquariumSaver(Random rnd)
    {
        _rnd = rnd;
        int[] shapes = { 0, 1, 2, 3, 1, 0 };
        foreach (var shape in shapes)
        {
            var f = new Fish { Shape = shape, Dir = _rnd.Next(2) == 0 ? 1 : -1, Phase = _rnd.NextDouble() * 10 };
            int h = Shapes[shape][0].Length;
            f.BaseY = 2 + _rnd.NextDouble() * Math.Max(1, H - 9 - h);
            f.X = _rnd.NextDouble() * W;
            f.Speed = shape == 1 ? 14 + _rnd.NextDouble() * 8 : 6 + _rnd.NextDouble() * 8;
            f.NextTurn = 4 + _rnd.NextDouble() * 12;
            _fish.Add(f);
        }
        _plants = new[] { (8, 14, 0.0), (14, 9, 1.3), (70, 11, 2.1), (122, 16, 0.7), (129, 10, 2.9), (150, 12, 1.7) };
        _stones = new[] { (30, 5), (95, 7), (108, 4) };
    }

    public void Render(Framebuffer fb, double t, double dt)
    {
        // Boden: Sand und Steine
        for (int x = 0; x < W; x++) if ((x * 7 + 3) % 5 < 2) fb[x, H - 1] = true;
        foreach (var (sx, sw) in _stones) fb.DrawEllipse(sx, H - 4, sw, 5, filled: true);

        // Pflanzen wiegen sich (oben mehr als unten)
        foreach (var (px, height, ph) in _plants)
        {
            int prevX = px, prevY = H - 1;
            for (int k = 1; k <= height; k++)
            {
                int y = H - 1 - k;
                int x = px + (int)Math.Round(Math.Sin(t * 1.1 + ph + k * 0.35) * k / 5.0);
                fb.DrawLine(prevX, prevY, x, y);
                if (k % 4 == 2) fb[x + (k % 8 == 2 ? 1 : -1), y] = true;   // Blättchen
                prevX = x; prevY = y;
            }
        }

        // Blasen aus dem Sprudelstein und ab und zu von einem Fisch
        _bubbleTimer -= dt;
        if (_bubbleTimer <= 0)
        {
            _bubbleTimer = 0.25 + _rnd.NextDouble() * 0.5;
            _bubbles.Add((100 + _rnd.NextDouble() * 2, H - 4, _rnd.Next(3) == 0 ? 2 : 1, _rnd.NextDouble() * 6));
        }
        if (_rnd.NextDouble() < dt * 0.3)
        {
            var f = _fish[_rnd.Next(_fish.Count)];
            int w = Shapes[f.Shape][0][0].Length;
            _bubbles.Add((f.Dir > 0 ? f.X + w : f.X - 1, f.BaseY, 1, _rnd.NextDouble() * 6));
        }
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            b.Y -= dt * (b.Size == 2 ? 9 : 7);
            if (b.Y < -2) { _bubbles.RemoveAt(i); continue; }
            _bubbles[i] = b;
            int bx = (int)Math.Round(b.X + Math.Sin(t * 3 + b.Phase) * 0.8), by = (int)Math.Round(b.Y);
            if (b.Size == 1) fb[bx, by] = true;
            else { fb[bx, by - 1] = fb[bx - 1, by] = fb[bx + 1, by] = fb[bx, by + 1] = true; }
        }

        // Fische
        foreach (var f in _fish)
        {
            var frames = Shapes[f.Shape];
            int w = frames[0][0].Length, h = frames[0].Length;
            f.X += f.Dir * f.Speed * dt;
            f.NextTurn -= dt;
            bool offRight = f.Dir > 0 && f.X > W + 4, offLeft = f.Dir < 0 && f.X < -w - 4;
            if (offRight || offLeft || (f.NextTurn <= 0 && f.X > 10 && f.X < W - w - 10))
            {
                if (f.NextTurn <= 0 || _rnd.Next(3) > 0) f.Dir = -f.Dir;    // meist umdrehen, manchmal …
                else { f.X = f.Dir > 0 ? -w - 2 : W + 2; f.BaseY = 2 + _rnd.NextDouble() * Math.Max(1, H - 9 - h); }
                f.NextTurn = 5 + _rnd.NextDouble() * 14;
            }
            int y = (int)Math.Round(f.BaseY + Math.Sin(t * 1.3 + f.Phase) * 1.2);
            var frame = frames[(int)(t * (f.Speed / 4) + f.Phase) % 2];
            DrawFish(fb, frame, (int)Math.Round(f.X), y, f.Dir < 0);
        }
    }

    private static void DrawFish(MonoBitmap fb, string[] rows, int x, int y, bool mirror)
    {
        int w = rows[0].Length;
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            int first = row.IndexOf('#'), last = row.LastIndexOf('#');
            if (first < 0) continue;
            for (int c = first; c <= last; c++)
            {
                // Innerhalb des Körpers auch Löcher (Auge) setzen, damit Pflanzen dahinter verdeckt werden
                int col = mirror ? w - 1 - c : c;
                fb[x + col, y + r] = row[c] == '#';
            }
        }
    }
}
