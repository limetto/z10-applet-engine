using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>Lädt Bilder und (animierte) GIFs und wandelt sie in 1-Bit-Bilder für das Display.</summary>
public sealed class ImageImporter
{
    public const int MaxFrames = 300;

    /// <summary>Quellbilder in Originalgröße (Bgra32), bei GIFs fertig zusammengesetzt.</summary>
    public IReadOnlyList<(BitmapSource Image, int DelayMs)> Frames { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public bool Truncated { get; }

    private ImageImporter(List<(BitmapSource, int)> frames, bool truncated)
    {
        Frames = frames;
        SourceWidth = frames[0].Item1.PixelWidth;
        SourceHeight = frames[0].Item1.PixelHeight;
        Truncated = truncated;
    }

    public static ImageImporter Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder is GifBitmapDecoder && decoder.Frames.Count > 1)
            return LoadGif(decoder);
        var single = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        single.Freeze();
        return new ImageImporter(new List<(BitmapSource, int)> { (single, 0) }, false);
    }

    /// <summary>GIF-Bilder sind oft nur Teilflächen: auf einer Leinwand zusammensetzen, Entsorgungsart beachten.</summary>
    private static ImageImporter LoadGif(BitmapDecoder decoder)
    {
        int w = Query<ushort>(decoder.Metadata, "/logscrdesc/Width") is ushort lw && lw > 0 ? lw : decoder.Frames[0].PixelWidth;
        int h = Query<ushort>(decoder.Metadata, "/logscrdesc/Height") is ushort lh && lh > 0 ? lh : decoder.Frames[0].PixelHeight;
        var canvas = new uint[w * h];
        var result = new List<(BitmapSource, int)>();
        bool truncated = false;

        foreach (var frame in decoder.Frames)
        {
            if (result.Count >= MaxFrames) { truncated = true; break; }
            var meta = frame.Metadata as BitmapMetadata;
            int left = Query<ushort>(meta, "/imgdesc/Left") ?? 0;
            int top = Query<ushort>(meta, "/imgdesc/Top") ?? 0;
            int delay = (Query<ushort>(meta, "/grctlext/Delay") ?? 10) * 10;
            if (delay < 20) delay = 100;            // wie Browser: 0/10 ms gelten als 100 ms
            int disposal = Query<byte>(meta, "/grctlext/Disposal") ?? 0;

            uint[]? previous = disposal == 3 ? (uint[])canvas.Clone() : null;

            var src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int fw = src.PixelWidth, fh = src.PixelHeight;
            var px = new uint[fw * fh];
            src.CopyPixels(px, fw * 4, 0);
            for (int y = 0; y < fh; y++)
            {
                int cy = top + y;
                if (cy < 0 || cy >= h) continue;
                for (int x = 0; x < fw; x++)
                {
                    int cx = left + x;
                    if (cx < 0 || cx >= w) continue;
                    uint p = px[y * fw + x];
                    if ((p >> 24) > 0) canvas[cy * w + cx] = p;
                }
            }

            var snapshot = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, canvas, w * 4);
            snapshot.Freeze();
            result.Add((snapshot, delay));

            if (disposal == 2)
            {
                for (int y = Math.Max(0, top); y < Math.Min(h, top + fh); y++)
                    for (int x = Math.Max(0, left); x < Math.Min(w, left + fw); x++)
                        canvas[y * w + x] = 0;
            }
            else if (previous is not null)
            {
                canvas = previous;
            }
        }
        return new ImageImporter(result, truncated);
    }

    private static T? Query<T>(BitmapMetadata? meta, string query) where T : struct
    {
        try
        {
            return meta?.ContainsQuery(query) == true && meta.GetQuery(query) is T v ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public sealed record Options(int Width, int Height, int Threshold, bool Dither, bool Invert);

    /// <summary>Alle Bilder in Zielgröße als 1-Bit. Transparente Bereiche bleiben aus.</summary>
    public List<(MonoBitmap Bitmap, int DelayMs)> Convert(Options o) =>
        Frames.Select(f => (ToMono(f.Image, o), f.DelayMs)).ToList();

    public MonoBitmap ConvertFrame(int index, Options o) => ToMono(Frames[index].Image, o);

    private static MonoBitmap ToMono(BitmapSource src, Options o)
    {
        int w = Math.Clamp(o.Width, 1, 160), h = Math.Clamp(o.Height, 1, 32);
        BitmapSource scaled = src;
        if (src.PixelWidth != w || src.PixelHeight != h)
        {
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
            group.Children.Add(new ImageDrawing(src, new Rect(0, 0, w, h)));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawDrawing(group);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            scaled = rtb;
        }
        var px = new uint[w * h];
        new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0).CopyPixels(px, w * 4, 0);

        // Helligkeit (vor schwarzem Hintergrund, also Transparenz = dunkel)
        var lum = new double[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            uint p = px[i];
            double a = (p >> 24) / 255.0;
            double r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
            double l = 0.299 * r + 0.587 * g + 0.114 * b;
            lum[i] = o.Invert ? (255 - l) * a : l * a;
        }

        var bmp = new MonoBitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool on = lum[i] >= o.Threshold;
                bmp[x, y] = on;
                if (!o.Dither) continue;
                // Floyd-Steinberg
                double err = lum[i] - (on ? 255 : 0);
                if (x + 1 < w) lum[i + 1] += err * 7 / 16;
                if (y + 1 < h)
                {
                    if (x > 0) lum[i + w - 1] += err * 3 / 16;
                    lum[i + w] += err * 5 / 16;
                    if (x + 1 < w) lum[i + w + 1] += err * 1 / 16;
                }
            }
        }
        return bmp;
    }
}
