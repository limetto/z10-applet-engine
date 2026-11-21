using System.Collections.Concurrent;
using System.Text.Json;
using Z10.Core.Storage;

namespace Z10.Core.Rendering;

/// <summary>Ein Bild einer importierten Grafik (1 Bit), Daten zeilenweise gepackt, höchstwertiges Bit links.</summary>
public sealed class ImageFrame
{
    public int DelayMs { get; set; } = 100;
    public string Data { get; set; } = "";
}

/// <summary>Importierte Grafik, ggf. animiert (aus GIF). Gespeichert als JSON in %APPDATA%\Z10AppletEngine\images.</summary>
public sealed class ImageAsset
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public List<ImageFrame> Frames { get; set; } = new();

    private MonoBitmap[]? _decoded;

    public bool IsAnimated => Frames.Count > 1;
    public int TotalMs => Frames.Sum(f => Math.Max(20, f.DelayMs));

    public static ImageAsset FromBitmaps(string name, IReadOnlyList<(MonoBitmap Bitmap, int DelayMs)> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("Keine Bilder.", nameof(frames));
        int w = frames[0].Bitmap.Width, h = frames[0].Bitmap.Height;
        var asset = new ImageAsset { Name = name, Width = w, Height = h };
        foreach (var (bmp, delay) in frames)
            asset.Frames.Add(new ImageFrame { DelayMs = delay, Data = Pack(bmp, w, h) });
        return asset;
    }

    public MonoBitmap Frame(int index)
    {
        _decoded ??= Frames.Select(f => Unpack(f.Data, Width, Height)).ToArray();
        return _decoded[Math.Clamp(index, 0, _decoded.Length - 1)];
    }

    /// <summary>Passendes Bild zur Zeit (Endlosschleife).</summary>
    public MonoBitmap FrameAt(long timeMs)
    {
        if (!IsAnimated) return Frame(0);
        long t = timeMs % TotalMs;
        for (int i = 0; i < Frames.Count; i++)
        {
            t -= Math.Max(20, Frames[i].DelayMs);
            if (t < 0) return Frame(i);
        }
        return Frame(Frames.Count - 1);
    }

    private static string Pack(MonoBitmap bmp, int w, int h)
    {
        int stride = (w + 7) / 8;
        var bytes = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (bmp[x, y]) bytes[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
        return Convert.ToBase64String(bytes);
    }

    private static MonoBitmap Unpack(string data, int w, int h)
    {
        var bmp = new MonoBitmap(w, h);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); } catch (FormatException) { return bmp; }
        int stride = (w + 7) / 8;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x / 8;
                if (i < bytes.Length && (bytes[i] & (0x80 >> (x % 8))) != 0) bmp[x, y] = true;
            }
        return bmp;
    }
}

/// <summary>Sammlung importierter Grafiken.</summary>
public sealed class ImageLibrary
{
    private readonly ConcurrentDictionary<string, ImageAsset> _assets = new(StringComparer.OrdinalIgnoreCase);
    public string? Directory { get; }

    public event Action? Changed;

    public ImageLibrary(string? directory = null)
    {
        Directory = directory;
        if (directory is null) return;
        System.IO.Directory.CreateDirectory(directory);
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var a = JsonSerializer.Deserialize<ImageAsset>(File.ReadAllText(file), JsonDefaults.Options);
                if (a is not null && a.Width > 0 && a.Height > 0 && a.Frames.Count > 0) _assets[a.Name] = a;
            }
            catch (JsonException) { }
        }
    }

    public IReadOnlyList<ImageAsset> All => _assets.Values.OrderBy(a => a.Name).ToList();

    public ImageAsset? Get(string name) => _assets.TryGetValue(name.Trim(), out var a) ? a : null;

    public static string SafeName(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-');
        return s.Length == 0 ? "bild" : s[..Math.Min(40, s.Length)];
    }

    public void Save(ImageAsset asset)
    {
        asset.Name = SafeName(asset.Name);
        _assets[asset.Name] = asset;
        if (Directory is not null)
            File.WriteAllText(Path.Combine(Directory, asset.Name + ".json"), JsonSerializer.Serialize(asset, JsonDefaults.Options));
        Changed?.Invoke();
    }

    public void Delete(string name)
    {
        _assets.TryRemove(name, out _);
        if (Directory is not null)
        {
            var path = Path.Combine(Directory, SafeName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        Changed?.Invoke();
    }
}
