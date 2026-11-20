namespace Z10.Core.Rendering;

/// <summary>
/// Eingebaute 8×8-Symbole. Jede Zeile ist ein Hex-Byte, Bit 7 = linkes Pixel.
/// Eigene Symbole: 8 Hex-Bytes durch Leerzeichen/Komma getrennt, z. B. "18 3C 7E FF FF 7E 3C 18".
/// </summary>
public static class Icons
{
    public static readonly IReadOnlyDictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = "54 FE 82 BA BA 82 FE 54",
        ["gpu"] = "00 FF 81 BD BD 81 FF 24",
        ["ram"] = "00 FF AB AB FF 81 FF 00",
        ["fan"] = "0E 8C D8 FF FF 1B 31 70",
        ["temp"] = "18 24 24 24 3C 7E 7E 3C",
        ["net"] = "24 74 24 24 24 24 2E 24",
        ["disk"] = "7E 81 81 81 FF 81 85 7E",
        ["clock"] = "3C 42 91 91 9D 81 42 3C",
        ["bolt"] = "0C 18 30 7E 0C 18 30 20",
        ["warn"] = "18 18 24 24 5A 42 99 FF",
        ["heart"] = "00 66 FF FF FF 7E 3C 18",
    };

    public static MonoBitmap Parse(string spec)
    {
        if (BuiltIn.TryGetValue(spec.Trim(), out var builtin)) spec = builtin;
        var parts = spec.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var bmp = new MonoBitmap(8, Math.Max(1, parts.Length));
        for (int y = 0; y < parts.Length; y++)
        {
            if (!byte.TryParse(parts[y].Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                    System.Globalization.NumberStyles.HexNumber, null, out var row)) continue;
            for (int x = 0; x < 8; x++)
                bmp[x, y] = (row & (0x80 >> x)) != 0;
        }
        return bmp;
    }
}
