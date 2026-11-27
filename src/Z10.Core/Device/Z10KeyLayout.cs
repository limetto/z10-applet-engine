using static Z10.Core.Localization.Loc;

namespace Z10.Core.Device;

/// <summary>Eine Taste auf der Tastaturgrafik. Maße in Tastenbreiten (1 = normale Taste).</summary>
public sealed record KeyDef(string Label, int Position, double X, double Y, double W = 1, double H = 1, bool HasLed = true);

/// <summary>Eine Tastengruppe zum schnellen Auswählen (wie die Gruppen in Unleash).</summary>
public sealed record KeyGroup(string Name, IReadOnlyList<int> Positions);

/// <summary>Ein Tastaturlayout (Beschriftung und Bauform ANSI/ISO).</summary>
public sealed record KeyboardLayoutInfo(string Id, string Name, bool Iso);

/// <summary>
/// Physisches Layout der Z10 mit der Zuordnung zu den Positionen der Beleuchtungsmaske. Die Positionen ergeben sich
/// aus der Tastenbelegung (<see cref="Z10Lighting.KeyTable"/>) und sind für alle Layouts gleich – nur Beschriftung und
/// Bauform (ANSI: eine Zeile Enter und „\“ darüber; ISO: hohes Enter, zusätzliche Tasten „#“ und „&lt;“) unterscheiden sich.
/// Auf der deutschen Z10 haben die beiden ISO-Zusatztasten keine LED (am Gerät geprüft); für die übrigen ISO-Layouts
/// wird dasselbe angenommen.
/// </summary>
public static class Z10KeyLayout
{
    /// <summary>Positionen der sieben E-Tasten E1..E7 (HID-Code 0xD8 in der Tabelle, Reihenfolge am Gerät geprüft).</summary>
    public static readonly int[] EKeyPositions = { 73, 74, 76, 78, 80, 125, 127 };

    public static IReadOnlyList<KeyboardLayoutInfo> Layouts { get; } = new KeyboardLayoutInfo[]
    {
        new("us", "English (US, ANSI)", false),
        new("uk", "English (UK, ISO)", true),
        new("de", "Deutsch (ISO)", true),
        new("fr", "Français (AZERTY, ISO)", true),
        new("nordic", "Nordisk (SE/FI/NO/DK, ISO)", true),
    };

    private static readonly Dictionary<string, (List<KeyDef> Keys, List<KeyGroup> Groups)> Cache = new();
    private static string _current = "de";

    /// <summary>Aktives Layout ("us", "uk", "de", "fr", "nordic").</summary>
    public static string Current => _current;

    public static void Use(string? id)
    {
        id = (id ?? "").ToLowerInvariant();
        _current = Layouts.Any(l => l.Id == id) ? id : "us";
    }

    /// <summary>Passendes Layout zu einer Windows-Kultur (z. B. "de-DE", "en-GB").</summary>
    public static string FromCulture(string? culture)
    {
        var c = (culture ?? "").ToLowerInvariant();
        if (c.StartsWith("de")) return "de";
        if (c == "en-gb" || c == "en-ie") return "uk";
        if (c.StartsWith("fr")) return "fr";
        if (c.StartsWith("sv") || c.StartsWith("fi") || c.StartsWith("nb") || c.StartsWith("nn") || c.StartsWith("no") || c.StartsWith("da"))
            return "nordic";
        return "us";
    }

    public static IReadOnlyList<KeyDef> Keys => Get(_current).Keys;
    public static IReadOnlyList<KeyGroup> Groups => Get(_current).Groups;

    /// <summary>Alle Positionen mit LED.</summary>
    public static IReadOnlyList<int> AllLit => Keys.Where(k => k.HasLed).Select(k => k.Position).OrderBy(p => p).ToList();

    /// <summary>Verschiebung des Hauptblocks nach rechts, um Platz für die E-Tasten-Spalte zu lassen.</summary>
    public const double MainOffset = 1.5;

    public static double TotalWidth => Keys.Max(k => k.X + k.W);
    public static double TotalHeight => Keys.Max(k => k.Y + k.H);

    /// <summary>Tasten und Gruppen eines bestimmten Layouts (für Tests).</summary>
    public static (IReadOnlyList<KeyDef> Keys, IReadOnlyList<KeyGroup> Groups) For(string id)
    {
        var g = Get(id);
        return (g.Keys, g.Groups);
    }

    /// <summary>Position einer Taste über ihren HID-Code (erste Fundstelle im Bereich 0..127), -1 wenn unbekannt.</summary>
    public static int PositionOf(byte usage)
    {
        for (int i = 0; i < Z10Lighting.Positions; i++)
            if (Z10Lighting.KeyTable[i] == usage) return i;
        return -1;
    }

    private static (List<KeyDef> Keys, List<KeyGroup> Groups) Get(string id)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(id, out var v))
            {
                var keys = Build(id);
                Cache[id] = v = (keys, BuildGroups(keys));
            }
            return v;
        }
    }

    // ---------- Beschriftungen ----------

    private static Dictionary<byte, string> Labels(string id)
    {
        bool german = id == "de";
        var l = new Dictionary<byte, string>();
        for (int i = 0; i < 26; i++) l[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
        for (int i = 0; i < 10; i++) l[(byte)(0x1E + i)] = ((i + 1) % 10).ToString();
        for (int i = 0; i < 12; i++) l[(byte)(0x3A + i)] = $"F{i + 1}";
        // Sondertasten: auf deutschen Tasten deutsch beschriftet, sonst englisch
        void Both(byte u, string de, string en) => l[u] = german ? de : en;
        Both(0x29, "Esc", "Esc"); Both(0x2A, "⌫", "⌫"); Both(0x2B, "Tab", "Tab"); Both(0x28, "Enter", "Enter");
        Both(0x39, "Caps", "Caps"); Both(0x2C, "", "");
        Both(0x46, "Druck", "PrtSc"); Both(0x47, "Rollen", "ScrLk"); Both(0x48, "Pause", "Pause");
        Both(0x49, "Einfg", "Ins"); Both(0x4A, "Pos1", "Home"); Both(0x4B, "Bild↑", "PgUp");
        Both(0x4C, "Entf", "Del"); Both(0x4D, "Ende", "End"); Both(0x4E, "Bild↓", "PgDn");
        Both(0x4F, "→", "→"); Both(0x50, "←", "←"); Both(0x51, "↓", "↓"); Both(0x52, "↑", "↑");
        Both(0xE0, "Strg", "Ctrl"); Both(0xE4, "Strg", "Ctrl"); Both(0xE1, "⇧", "⇧"); Both(0xE5, "⇧", "⇧");
        Both(0xE2, "Alt", "Alt"); Both(0xE6, "AltGr", id == "us" ? "Alt" : "AltGr"); Both(0xE7, "Win", "Win");
        Both(0x65, "Menü", "Menu"); Both(0xAD, "Fn", "Fn");
        Both(0x53, "Num", "Num"); Both(0x54, "/", "/"); Both(0x55, "*", "*"); Both(0x56, "−", "−"); Both(0x57, "+", "+");
        Both(0x58, "Enter", "Enter"); Both(0x63, german ? "," : ".", ".");
        for (int i = 0; i < 9; i++) l[(byte)(0x59 + i)] = (i + 1).ToString();
        l[0x62] = "0";

        // Zeichen-Tasten je Layout
        (byte, string)[] chars = id switch
        {
            "de" => new (byte, string)[] { (0x35, "^"), (0x2D, "ß"), (0x2E, "´"), (0x2F, "Ü"), (0x30, "+"), (0x32, "#"),
                (0x33, "Ö"), (0x34, "Ä"), (0x36, ","), (0x37, "."), (0x38, "-"), (0x64, "<"), (0x1C, "Z"), (0x1D, "Y") },
            "uk" => new (byte, string)[] { (0x35, "`"), (0x2D, "-"), (0x2E, "="), (0x2F, "["), (0x30, "]"), (0x32, "#"),
                (0x33, ";"), (0x34, "'"), (0x36, ","), (0x37, "."), (0x38, "/"), (0x64, "\\") },
            "fr" => new (byte, string)[] { (0x35, "²"), (0x2D, ")"), (0x2E, "="), (0x2F, "^"), (0x30, "$"), (0x32, "*"),
                (0x33, "M"), (0x34, "ù"), (0x10, ","), (0x36, ";"), (0x37, ":"), (0x38, "!"), (0x64, "<"),
                (0x14, "A"), (0x04, "Q"), (0x1A, "Z"), (0x1D, "W"),
                (0x1E, "&"), (0x1F, "é"), (0x20, "\""), (0x21, "'"), (0x22, "("), (0x23, "-"), (0x24, "è"), (0x25, "_"),
                (0x26, "ç"), (0x27, "à") },
            "nordic" => new (byte, string)[] { (0x35, "§"), (0x2D, "+"), (0x2E, "´"), (0x2F, "Å"), (0x30, "¨"), (0x32, "'"),
                (0x33, "Ö"), (0x34, "Ä"), (0x36, ","), (0x37, "."), (0x38, "-"), (0x64, "<") },
            _ => new (byte, string)[] { (0x35, "`"), (0x2D, "-"), (0x2E, "="), (0x2F, "["), (0x30, "]"), (0x31, "\\"),
                (0x33, ";"), (0x34, "'"), (0x36, ","), (0x37, "."), (0x38, "/") },
        };
        foreach (var (u, s) in chars) l[u] = s;
        return l;
    }

    // ---------- Aufbau ----------

    private static List<KeyDef> Build(string id)
    {
        bool iso = Layouts.FirstOrDefault(l => l.Id == id)?.Iso ?? false;
        var labels = Labels(id);
        var keys = new List<KeyDef>();
        void K(byte usage, double x, double y, double w = 1, double h = 1, bool led = true)
        {
            int pos = PositionOf(usage);
            if (pos < 0) throw new InvalidOperationException($"HID-Code 0x{usage:X2} fehlt in der Tastenbelegung.");
            keys.Add(new KeyDef(labels.TryGetValue(usage, out var lab) ? lab : $"0x{usage:X2}", pos, x + MainOffset, y, w, h, led));
        }

        // E-Tasten wie am Gerät: E1..E5 als Spalte links neben dem Hauptblock (E1 oben, ab der Zahlenreihe),
        // E6/E7 links und rechts neben „Pfeil hoch“ (über Pfeil links bzw. Pfeil rechts)
        for (int i = 0; i < 5; i++)
            keys.Add(new KeyDef($"E{i + 1}", EKeyPositions[i], 0, 1.5 + i));

        double y = 0;
        // Funktionsreihe
        K(0x29, 0, y);
        for (int i = 0; i < 4; i++) K((byte)(0x3A + i), 2 + i, y);
        for (int i = 0; i < 4; i++) K((byte)(0x3E + i), 6.5 + i, y);
        for (int i = 0; i < 4; i++) K((byte)(0x42 + i), 11 + i, y);
        K(0x46, 15.25, y); K(0x47, 16.25, y); K(0x48, 17.25, y);

        // Zahlenreihe
        y += 1.5;
        K(0x35, 0, y);
        for (int i = 0; i < 10; i++) K((byte)(0x1E + i), 1 + i, y);
        K(0x2D, 11, y); K(0x2E, 12, y); K(0x2A, 13, y, 2);
        K(0x49, 15.25, y); K(0x4A, 16.25, y); K(0x4B, 17.25, y);
        K(0x53, 18.5, y); K(0x54, 19.5, y); K(0x55, 20.5, y); K(0x56, 21.5, y);

        // obere Buchstabenreihe
        y += 1;
        K(0x2B, 0, y, 1.5);
        byte[] row2 = { 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13, 0x2F, 0x30 };
        for (int i = 0; i < row2.Length; i++) K(row2[i], 1.5 + i, y);
        if (iso) K(0x28, 13.75, y, 1.25, 2);
        else K(0x31, 13.5, y, 1.5);
        K(0x4C, 15.25, y); K(0x4D, 16.25, y); K(0x4E, 17.25, y);
        K(0x5F, 18.5, y); K(0x60, 19.5, y); K(0x61, 20.5, y); K(0x57, 21.5, y, 1, 2);

        // mittlere Buchstabenreihe
        y += 1;
        K(0x39, 0, y, 1.75);
        byte[] row3 = { 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F, 0x33, 0x34 };
        for (int i = 0; i < row3.Length; i++) K(row3[i], 1.75 + i, y);
        if (iso) K(0x32, 12.75, y, 1, 1, led: false);
        else K(0x28, 12.75, y, 2.25);
        K(0x5C, 18.5, y); K(0x5D, 19.5, y); K(0x5E, 20.5, y);

        // untere Buchstabenreihe
        y += 1;
        double x0;
        if (iso) { K(0xE1, 0, y, 1.25); K(0x64, 1.25, y, 1, 1, led: false); x0 = 2.25; }
        else { K(0xE1, 0, y, 2.25); x0 = 2.25; }
        byte[] row4 = { 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10, 0x36, 0x37, 0x38 };
        for (int i = 0; i < row4.Length; i++) K(row4[i], x0 + i, y);
        K(0xE5, 12.25, y, 2.75);
        K(0x52, 16.25, y);
        keys.Add(new KeyDef("E6", EKeyPositions[5], MainOffset + 15.25, y));
        keys.Add(new KeyDef("E7", EKeyPositions[6], MainOffset + 17.25, y));
        K(0x59, 18.5, y); K(0x5A, 19.5, y); K(0x5B, 20.5, y); K(0x58, 21.5, y, 1, 2);

        // unterste Reihe (Z10: Fn links statt Windows-Taste)
        y += 1;
        K(0xE0, 0, y, 1.25); K(0xAD, 1.25, y, 1.25); K(0xE2, 2.5, y, 1.25);
        K(0x2C, 3.75, y, 6.25);
        K(0xE6, 10, y, 1.25); K(0xE7, 11.25, y, 1.25); K(0x65, 12.5, y, 1.25);
        K(0xE4, 13.75, y, 1.25);
        K(0x50, 15.25, y); K(0x51, 16.25, y); K(0x4F, 17.25, y);
        K(0x62, 18.5, y, 2); K(0x63, 20.5, y);
        return keys;
    }

    private static List<KeyGroup> BuildGroups(List<KeyDef> keys)
    {
        var lit = keys.Where(k => k.HasLed).ToList();
        int[] U(params byte[] usages) => usages.Select(PositionOf).Where(p => lit.Any(k => k.Position == p)).ToArray();
        string LabelOf(int pos) => keys.FirstOrDefault(k => k.Position == pos)?.Label ?? "?";

        // Gruppen nach Lage, benannt nach der Beschriftung im gewählten Layout (z. B. AZERTY: ZQSD)
        var wasd = U(0x1A, 0x04, 0x16, 0x07);
        var qwer = U(0x14, 0x1A, 0x08, 0x15);
        var main = lit.Where(k => k.Y >= 1.5 && k.X < MainOffset + 15 && !EKeyPositions.Contains(k.Position)).Select(k => k.Position);
        var numpad = Enumerable.Range(0x53, 0x63 - 0x53 + 1).Select(u => PositionOf((byte)u));
        return new List<KeyGroup>
        {
            new(L("Alle"), lit.Select(k => k.Position).ToList()),
            new(string.Concat(wasd.Select(LabelOf)), wasd),
            new(string.Concat(qwer.Select(LabelOf)), qwer),
            new(L("F-Tasten"), Enumerable.Range(0x3A, 12).Select(u => PositionOf((byte)u)).ToList()),
            new(L("E-Tasten"), EKeyPositions),
            new(L("Ziffernblock"), numpad.ToList()),
            new(L("Pfeiltasten"), U(0x4F, 0x50, 0x51, 0x52)),
            new(L("Hauptblock"), main.ToList()),
        };
    }
}
