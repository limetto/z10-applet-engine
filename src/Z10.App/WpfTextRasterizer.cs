using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>
/// Rendert Windows-Schriften ohne Kantenglättung auf 1 Bit. Die Pixelschrift übernimmt der Core.
/// Thread-sicher: jeder Thread (UI, Engine) bekommt seinen eigenen Cache.
/// </summary>
public sealed class WpfTextRasterizer : ITextRasterizer
{
    [ThreadStatic] private static Dictionary<(string, string, int, bool), MonoBitmap>? _cache;

    public MonoBitmap Render(string text, TextStyle style)
    {
        if (style.IsPixelFont || string.IsNullOrEmpty(text))
            return PixelTextRasterizer.Instance.Render(text, style);

        _cache ??= new();
        var key = (text, style.Font, style.Size, style.Bold);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (_cache.Count > 500) _cache.Clear();

        MonoBitmap result;
        try
        {
            result = RenderSystemFont(text, style);
        }
        catch (Exception)
        {
            result = PixelTextRasterizer.Instance.Render(text, style with { Font = "pixel", Size = Math.Clamp(style.Size / 7, 1, 4) });
        }
        _cache[key] = result;
        return result;
    }

    private static MonoBitmap RenderSystemFont(string text, TextStyle style)
    {
        var typeface = new Typeface(new FontFamily(style.Font), FontStyles.Normal,
            style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            Math.Max(4, style.Size), Brushes.Black, 1.0);

        int w = Math.Max(1, (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace));
        int h = Math.Max(1, (int)Math.Ceiling(ft.Height));
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        using (var dc = visual.RenderOpen())
            dc.DrawText(ft, new Point(0, 0));

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var pixels = new int[w * h];
        rtb.CopyPixels(pixels, w * 4, 0);

        var bmp = new MonoBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp[x, y] = ((pixels[y * w + x] >> 24) & 0xFF) > 110;
        return bmp;
    }
}
