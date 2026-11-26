using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Z10.Core.Localization;

/// <summary>Eine verfügbare Sprache.</summary>
public sealed record LanguageInfo(string Code, string Name, string? FilePath);

/// <summary>
/// Übersetzungen. Schlüssel ist der deutsche Originaltext – fehlt eine Übersetzung, erscheint der deutsche Text.
/// Sprachdateien sind JSON-Objekte { "Deutscher Text": "Übersetzung", … } mit optionalem Eintrag "@name"
/// (Anzeigename der Sprache). Englisch ist eingebaut; weitere Sprachen liegen als &lt;code&gt;.json im Ordner
/// <see cref="UserFolder"/> und können dort ohne Neubau ergänzt werden (überschreiben auch eingebaute Einträge).
/// </summary>
public static class Loc
{
    public const string German = "de";
    public const string English = "en";
    public const string NameKey = "@name";

    private static Dictionary<string, string> _table = new();

    /// <summary>Aktive Sprache ("de", "en", …).</summary>
    public static string Current { get; private set; } = German;

    /// <summary>Ordner für zusätzliche Sprachdateien.</summary>
    public static string? UserFolder { get; set; }

    /// <summary>Übersetzt einen deutschen Text in die aktive Sprache.</summary>
    public static string L(string german) =>
        Current != German && _table.TryGetValue(german, out var t) && !string.IsNullOrEmpty(t) ? t : german;

    /// <summary>Übersetzt und füllt Platzhalter {0}, {1}, … (Zahlen im aktuellen Format).</summary>
    public static string L(string german, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, L(german), args);

    /// <summary>Gibt es eine Übersetzung für diesen Text? (Für den Oberflächen-Übersetzer.)</summary>
    public static bool TryTranslate(string german, out string translated)
    {
        translated = german;
        if (Current == German || !_table.TryGetValue(german, out var t) || string.IsNullOrEmpty(t)) return false;
        translated = t;
        return true;
    }

    /// <summary>Sprache wählen. "auto" = Windows-Sprache (Deutsch, sonst Englisch bzw. passende Sprachdatei).</summary>
    public static void Use(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == "auto") code = Detect();
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        if (code != German)
        {
            Merge(table, LoadEmbedded(code));
            if (UserFolder is not null) Merge(table, LoadFile(Path.Combine(UserFolder, code + ".json")));
            if (table.Count == 0 && code != English) { code = English; Merge(table, LoadEmbedded(English)); }
        }
        _table = table;
        Current = code;
    }

    /// <summary>Sprache, die "auto" ergibt.</summary>
    public static string Detect()
    {
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (ui == German) return German;
        if (Available().Any(l => l.Code == ui)) return ui;
        return English;
    }

    /// <summary>Eingebaute und im Benutzerordner gefundene Sprachen.</summary>
    public static IReadOnlyList<LanguageInfo> Available()
    {
        var list = new List<LanguageInfo> { new(German, "Deutsch", null), new(English, "English", null) };
        if (UserFolder is not null && Directory.Exists(UserFolder))
        {
            foreach (var file in Directory.GetFiles(UserFolder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (code.StartsWith('_') || list.Any(l => l.Code == code)) continue;
                var data = LoadFile(file);
                if (data.Count == 0) continue;
                list.Add(new LanguageInfo(code, data.TryGetValue(NameKey, out var n) ? n : code, file));
            }
        }
        return list;
    }

    /// <summary>
    /// Schreibt eine Vorlage für eine neue Sprache: alle bekannten Texte mit der englischen Fassung als Ausgangspunkt.
    /// </summary>
    public static string ExportTemplate(string folder)
    {
        Directory.CreateDirectory(folder);
        var en = LoadEmbedded(English);
        var template = new SortedDictionary<string, string>(StringComparer.Ordinal) { [NameKey] = "Neue Sprache / New language" };
        foreach (var (k, v) in en) if (k != NameKey) template[k] = v;
        var path = Path.Combine(folder, "_template.json");
        File.WriteAllText(path, JsonSerializer.Serialize(template, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return path;
    }

    /// <summary>Alle eingebetteten Einträge einer Sprache (für Tests).</summary>
    public static IReadOnlyDictionary<string, string> Embedded(string code) => LoadEmbedded(code);

    private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var (k, v) in source) target[k] = v;
    }

    private static Dictionary<string, string> LoadEmbedded(string code)
    {
        var asm = typeof(Loc).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Localization.{code}.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return new();
        using var stream = asm.GetManifestResourceStream(name);
        return stream is null ? new() : Parse(stream);
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }
        catch (Exception) { return new(); }
    }

    private static Dictionary<string, string> Parse(Stream stream)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new();
        }
        catch (JsonException) { return new(); }
    }
}
