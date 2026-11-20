using System.Text.Json;
using System.Text.Json.Serialization;
using Z10.Core.Model;
using Z10.Core.Sensors;

namespace Z10.Core.Storage;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

public sealed class AppSettings
{
    public int Brightness { get; set; } = 5;
    public int Contrast { get; set; } = 3;
    public int PageDelayMs { get; set; } = 20;
    public bool ReleaseOnExit { get; set; } = true;
    public bool RotationPaused { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool RtssEnabled { get; set; } = true;
    public bool AfterburnerEnabled { get; set; } = true;
    public bool AudioEnabled { get; set; } = true;
    public List<CustomSensor> CustomSensors { get; set; } = new();
}

/// <summary>Speichert Applets als einzelne JSON-Dateien und die Einstellungen in settings.json.</summary>
public sealed class AppStore
{
    public string Root { get; }
    public string AppletDir => Path.Combine(Root, "applets");
    private string SettingsPath => Path.Combine(Root, "settings.json");

    public AppStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Z10AppletEngine");
        Directory.CreateDirectory(AppletDir);
    }

    public List<Applet> LoadApplets()
    {
        var list = new List<Applet>();
        foreach (var file in Directory.EnumerateFiles(AppletDir, "*.json"))
        {
            try
            {
                var a = JsonSerializer.Deserialize<Applet>(File.ReadAllText(file), JsonDefaults.Options);
                if (a is not null) list.Add(a);
            }
            catch (JsonException)
            {
                // kaputte Datei überspringen, nicht die ganze App lahmlegen
            }
        }
        if (list.Count == 0)
        {
            list = DefaultApplets.Create();
            foreach (var a in list) SaveApplet(a);
        }
        return list.OrderBy(a => a.Order).ThenBy(a => a.Name).ToList();
    }

    public void SaveApplet(Applet applet)
    {
        var path = Path.Combine(AppletDir, applet.Id + ".json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(applet, JsonDefaults.Options));
        File.Move(tmp, path, overwrite: true);
    }

    public void DeleteApplet(Applet applet)
    {
        var path = Path.Combine(AppletDir, applet.Id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonDefaults.Options) ?? new();
        }
        catch (JsonException) { }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings s) =>
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonDefaults.Options));
}
