namespace Z10.Core.Device;

/// <summary>Eine Taste auf der Tastaturgrafik. Maße in Tastenbreiten (1 = normale Taste).</summary>
public sealed record KeyDef(string Label, int Position, double X, double Y, double W = 1, double H = 1, bool HasLed = true);

/// <summary>Eine Tastengruppe zum schnellen Auswählen (wie die Gruppen in Unleash).</summary>
public sealed record KeyGroup(string Name, IReadOnlyList<int> Positions);

/// <summary>
/// Physisches Layout der Z10 (deutsch, ISO) mit der Zuordnung zu den Positionen der Beleuchtungsmaske.
/// Die Positionen ergeben sich aus der Tastenbelegung (<see cref="Z10Lighting.KeyTable"/>).
/// &lt;&gt; und #' haben keine LED (am Gerät geprüft). Die Reihenfolge der E-Tasten ist noch nicht geprüft.
/// </summary>
public static class Z10KeyLayout
{
    /// <summary>Positionen der sieben E-Tasten (HID-Code 0xD8 in der Tabelle).</summary>
    public static readonly int[] EKeyPositions = { 73, 74, 76, 78, 80, 125, 127 };

    public static IReadOnlyList<KeyDef> Keys { get; } = Build();

    public static IReadOnlyList<KeyGroup> Groups { get; } = BuildGroups();

    /// <summary>Alle Positionen mit LED.</summary>
    public static IReadOnlyList<int> AllLit { get; } = Keys.Where(k => k.HasLed).Select(k => k.Position).OrderBy(p => p).ToList();

    public static double TotalWidth => Keys.Max(k => k.X + k.W);
    public static double TotalHeight => Keys.Max(k => k.Y + k.H);

    /// <summary>Position einer Taste über ihren HID-Code (erste Fundstelle im Bereich 0..127), -1 wenn unbekannt.</summary>
    public static int PositionOf(byte usage)
    {
        for (int i = 0; i < Z10Lighting.Positions; i++)
            if (Z10Lighting.KeyTable[i] == usage) return i;
        return -1;
    }

    private static List<KeyDef> Build()
    {
        var keys = new List<KeyDef>();
        void K(string label, byte usage, double x, double y, double w = 1, double h = 1, bool led = true)
        {
            int pos = PositionOf(usage);
            if (pos < 0) throw new InvalidOperationException($"HID-Code 0x{usage:X2} fehlt in der Tastenbelegung.");
            keys.Add(new KeyDef(label, pos, x, y, w, h, led));
        }

        // E-Tasten (eigene Zeile, Reihenfolge ungeprüft)
        for (int i = 0; i < EKeyPositions.Length; i++)
            keys.Add(new KeyDef($"E{i + 1}", EKeyPositions[i], i * 1.25, 0));

        double y = 1.5;
        // Funktionsreihe
        K("Esc", 0x29, 0, y);
        for (int i = 0; i < 4; i++) K($"F{i + 1}", (byte)(0x3A + i), 2 + i, y);
        for (int i = 0; i < 4; i++) K($"F{i + 5}", (byte)(0x3E + i), 6.5 + i, y);
        for (int i = 0; i < 4; i++) K($"F{i + 9}", (byte)(0x42 + i), 11 + i, y);
        K("Druck", 0x46, 15.25, y); K("Rollen", 0x47, 16.25, y); K("Pause", 0x48, 17.25, y);

        // Zahlenreihe
        y += 1.5;
        K("^", 0x35, 0, y);
        string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++) K(digits[i], (byte)(0x1E + i), 1 + i, y);
        K("ß", 0x2D, 11, y); K("´", 0x2E, 12, y); K("⌫", 0x2A, 13, y, 2);
        K("Einfg", 0x49, 15.25, y); K("Pos1", 0x4A, 16.25, y); K("Bild↑", 0x4B, 17.25, y);
        K("Num", 0x53, 18.5, y); K("/", 0x54, 19.5, y); K("*", 0x55, 20.5, y); K("−", 0x56, 21.5, y);

        // QWERTZ
        y += 1;
        K("Tab", 0x2B, 0, y, 1.5);
        (string, byte)[] row2 = { ("Q", 0x14), ("W", 0x1A), ("E", 0x08), ("R", 0x15), ("T", 0x17), ("Z", 0x1C),
            ("U", 0x18), ("I", 0x0C), ("O", 0x12), ("P", 0x13), ("Ü", 0x2F), ("+", 0x30) };
        for (int i = 0; i < row2.Length; i++) K(row2[i].Item1, row2[i].Item2, 1.5 + i, y);
        K("Enter", 0x28, 13.75, y, 1.25, 2);
        K("Entf", 0x4C, 15.25, y); K("Ende", 0x4D, 16.25, y); K("Bild↓", 0x4E, 17.25, y);
        K("7", 0x5F, 18.5, y); K("8", 0x60, 19.5, y); K("9", 0x61, 20.5, y); K("+", 0x57, 21.5, y, 1, 2);

        // ASDF
        y += 1;
        K("Caps", 0x39, 0, y, 1.75);
        (string, byte)[] row3 = { ("A", 0x04), ("S", 0x16), ("D", 0x07), ("F", 0x09), ("G", 0x0A), ("H", 0x0B),
            ("J", 0x0D), ("K", 0x0E), ("L", 0x0F), ("Ö", 0x33), ("Ä", 0x34) };
        for (int i = 0; i < row3.Length; i++) K(row3[i].Item1, row3[i].Item2, 1.75 + i, y);
        K("#", 0x32, 12.75, y, 1, 1, led: false);
        K("4", 0x5C, 18.5, y); K("5", 0x5D, 19.5, y); K("6", 0x5E, 20.5, y);

        // YXCV
        y += 1;
        K("⇧", 0xE1, 0, y, 1.25);
        K("<", 0x64, 1.25, y, 1, 1, led: false);
        (string, byte)[] row4 = { ("Y", 0x1D), ("X", 0x1B), ("C", 0x06), ("V", 0x19), ("B", 0x05), ("N", 0x11),
            ("M", 0x10), (",", 0x36), (".", 0x37), ("-", 0x38) };
        for (int i = 0; i < row4.Length; i++) K(row4[i].Item1, row4[i].Item2, 2.25 + i, y);
        K("⇧", 0xE5, 12.25, y, 2.75);
        K("↑", 0x52, 16.25, y);
        K("1", 0x59, 18.5, y); K("2", 0x5A, 19.5, y); K("3", 0x5B, 20.5, y); K("Enter", 0x58, 21.5, y, 1, 2);

        // Unterste Reihe
        y += 1;
        K("Strg", 0xE0, 0, y, 1.25); K("Fn", 0xAD, 1.25, y, 1.25); K("Alt", 0xE2, 2.5, y, 1.25);
        K("", 0x2C, 3.75, y, 6.25);
        K("AltGr", 0xE6, 10, y, 1.25); K("Win", 0xE7, 11.25, y, 1.25); K("Menü", 0x65, 12.5, y, 1.25);
        K("Strg", 0xE4, 13.75, y, 1.25);
        K("←", 0x50, 15.25, y); K("↓", 0x51, 16.25, y); K("→", 0x4F, 17.25, y);
        K("0", 0x62, 18.5, y, 2); K(",", 0x63, 20.5, y);
        return keys;
    }

    private static List<KeyGroup> BuildGroups()
    {
        int[] U(params byte[] usages) => usages.Select(PositionOf).ToArray();
        var lit = Keys.Where(k => k.HasLed).ToList();
        // Hauptblock: Schreibmaschinenblock von der Zahlenreihe bis zur untersten Reihe
        var main = lit.Where(k => k.Y >= 3 && k.X < 15 && !EKeyPositions.Contains(k.Position)).Select(k => k.Position);
        var numpad = Enumerable.Range(0x53, 0x63 - 0x53 + 1).Select(u => PositionOf((byte)u));
        return new List<KeyGroup>
        {
            new("Alle", lit.Select(k => k.Position).ToList()),
            new("WASD", U(0x1A, 0x04, 0x16, 0x07)),
            new("QWER", U(0x14, 0x1A, 0x08, 0x15)),
            new("F-Tasten", Enumerable.Range(0x3A, 12).Select(u => PositionOf((byte)u)).ToList()),
            new("E-Tasten", EKeyPositions),
            new("Ziffernblock", numpad.ToList()),
            new("Pfeiltasten", U(0x4F, 0x50, 0x51, 0x52)),
            new("Hauptblock", main.ToList()),
        };
    }
}
