namespace Z10.Core.Rendering;

/// <summary>
/// Startanimation mit dem Logo: ein kleines LCD mit Balken und der Schriftzug „Z10 / APPLET ENGINE“.
/// Läuft auf der Tastatur beim Verbinden und im Startfenster der App.
/// </summary>
public static class BootAnimation
{
    public const int FrameMs = 50;
    private const string Title = "Z10";
    private const string Subtitle = "APPLET ENGINE";
    private const int MarkW = 28, MarkH = 26, Gap = 8, TitleScale = 3;
    private static readonly int[] BarX = { 4, 9, 14, 19 };
    private static readonly int[] BarH = { 6, 11, 8, 15 };
    private const int BarW = 3, BarBottom = 23;

    /// <summary>Alle Bilder der Animation, je <see cref="FrameMs"/> ms (≈ 2,6 s).</summary>
    public static IReadOnlyList<Framebuffer> Frames()
    {
        var font = PixelFont.Default;
        int textW = Math.Max(font.Measure(Title, TitleScale), font.Measure(Subtitle));
        int total = MarkW + Gap + textW;
        int ox = (Framebuffer.DefaultWidth - total) / 2;
        int tx = ox + MarkW + Gap;
        int my = (Framebuffer.DefaultHeight - MarkH) / 2;

        var frames = new List<Framebuffer>();

        // 1) Scanlinie läuft von links nach rechts und deckt Gerät und „Z10“ auf
        const int sweep = 10;
        for (int i = 1; i <= sweep; i++)
        {
            var full = Compose(ox, my, tx, barProgress: 0, subtitleChars: 0);
            int cx = (int)Math.Round(i / (double)sweep * (Framebuffer.DefaultWidth + 2)) - 1;
            var fb = new Framebuffer();
            for (int y = 0; y < fb.Height; y++)
                for (int x = 0; x < Math.Min(cx, fb.Width); x++)
                    fb[x, y] = full[x, y];
            if (cx < fb.Width) fb.FillRect(cx, 0, 2, fb.Height);
            frames.Add(fb);
        }

        // 2) Balken wachsen mit leichtem Überschwingen
        const int rise = 12;
        for (int i = 1; i <= rise; i++)
            frames.Add(Compose(ox, my, tx, EaseOutBack(i / (double)rise), 0));

        // 3) Untertitel wird Buchstabe für Buchstabe geschrieben
        for (int n = 1; n <= Subtitle.Length; n++)
            frames.Add(Compose(ox, my, tx, 1, n));

        // 4) Kurz stehen lassen, einmal aufblitzen (invertieren), stehen lassen
        var final = Compose(ox, my, tx, 1, Subtitle.Length);
        for (int i = 0; i < 10; i++) frames.Add(final.Clone());
        for (int i = 0; i < 2; i++)
        {
            var flash = final.Clone();
            flash.Invert(0, 0, flash.Width, flash.Height);
            frames.Add(flash);
        }
        for (int i = 0; i < 16; i++) frames.Add(final.Clone());
        return frames;
    }

    /// <summary>Das fertige Logo als Standbild.</summary>
    public static Framebuffer Logo() => Frames()[^1];

    private static Framebuffer Compose(int ox, int my, int tx, double barProgress, int subtitleChars)
    {
        var fb = new Framebuffer();
        DrawMark(fb, ox, my, barProgress);
        var font = PixelFont.Default;
        fb.Blit(font.Render(Title, TitleScale), tx, 0);
        // fett: um 1 px versetzt nochmals zeichnen
        fb.Blit(font.Render(Title, TitleScale), tx + 1, 0);
        if (subtitleChars > 0)
            fb.Blit(font.Render(Subtitle[..Math.Min(subtitleChars, Subtitle.Length)]), tx, Framebuffer.DefaultHeight - PixelFont.GlyphHeight);
        return fb;
    }

    /// <summary>Kleines Display mit Alu-Leiste und Balken (wie die Z10 von oben).</summary>
    public static void DrawMark(MonoBitmap fb, int ox, int oy, double barProgress)
    {
        fb.FillRect(ox + 4, oy, MarkW - 8, 2);                  // Alu-Leiste
        fb.DrawRect(ox, oy + 4, MarkW, MarkH - 4);              // Display-Rahmen
        fb[ox, oy + 4] = false;                                  // abgerundete Ecken
        fb[ox + MarkW - 1, oy + 4] = false;
        fb[ox, oy + MarkH - 1] = false;
        fb[ox + MarkW - 1, oy + MarkH - 1] = false;
        for (int i = 0; i < BarX.Length; i++)
        {
            int h = (int)Math.Round(BarH[i] * Math.Clamp(barProgress, 0, 1.25));
            h = Math.Min(h, BarBottom - 6);
            if (h > 0) fb.FillRect(ox + BarX[i], oy + BarBottom - h + 1, BarW, h);
        }
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
