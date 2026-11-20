namespace Z10.Core.Rendering;

/// <summary>Schriftangabe eines Bausteins.</summary>
public sealed record TextStyle
{
    /// <summary>"pixel" = eingebaute 5×7-Schrift, sonst Name einer Windows-Schrift.</summary>
    public string Font { get; init; } = "pixel";

    /// <summary>Pixelschrift: Vergrößerung 1..4 (7, 14, 21, 28 px). Systemschrift: Höhe in Pixeln.</summary>
    public int Size { get; init; } = 1;

    public bool Bold { get; init; }

    public bool IsPixelFont => string.IsNullOrWhiteSpace(Font) || Font.Equals("pixel", StringComparison.OrdinalIgnoreCase);
}

public interface ITextRasterizer
{
    MonoBitmap Render(string text, TextStyle style);
}

/// <summary>Rendert nur die eingebaute Pixelschrift. Systemschriften liefert die App (WPF).</summary>
public sealed class PixelTextRasterizer : ITextRasterizer
{
    public static PixelTextRasterizer Instance { get; } = new();

    public MonoBitmap Render(string text, TextStyle style)
    {
        var bmp = PixelFont.Default.Render(text, style.IsPixelFont ? style.Size : Math.Max(1, style.Size / 7));
        if (!style.Bold || bmp.Width == 0) return bmp;
        var bold = new MonoBitmap(bmp.Width + 1, bmp.Height);
        bold.Blit(bmp, 0, 0);
        bold.Blit(bmp, 1, 0);
        return bold;
    }
}
