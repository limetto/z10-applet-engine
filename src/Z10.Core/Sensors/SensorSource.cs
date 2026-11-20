namespace Z10.Core.Sensors;

/// <summary>Beschreibung eines Sensors. Id ist der LibreHardwareMonitor-Identifier, z. B. "/amdcpu/0/temperature/2".</summary>
public sealed record SensorInfo(
    string Id,
    string Hardware,
    string HardwareKind,   // cpu, gpu, memory, motherboard, network, storage, other
    string Name,
    string Type,           // Temperature, Load, Clock, Fan, Data, SmallData, Throughput, Power, …
    string Unit,
    double? Value);

public interface ISensorSource : IDisposable
{
    /// <summary>Liest alle Sensoren neu ein (teuer, max. etwa 1× pro Sekunde aufrufen).</summary>
    void Update();

    IReadOnlyList<SensorInfo> Sensors { get; }

    /// <summary>Wert zu einer Id oder Auto-Abfrage ("auto:cpu/Temperature/CPU Package||Core (Tctl/Tdie)").</summary>
    double? GetValue(string id);

    string? Error { get; }
}

/// <summary>Gemeinsame Logik für Sensorquellen: Wertetabelle und Auflösung von Auto-Abfragen.</summary>
public abstract class SensorSourceBase : ISensorSource
{
    private readonly object _lock = new();
    private IReadOnlyList<SensorInfo> _sensors = Array.Empty<SensorInfo>();
    private Dictionary<string, SensorInfo> _byId = new();
    private readonly Dictionary<string, string?> _autoCache = new();

    public IReadOnlyList<SensorInfo> Sensors { get { lock (_lock) return _sensors; } }
    public string? Error { get; protected set; }

    public void Update()
    {
        IReadOnlyList<SensorInfo> fresh;
        try
        {
            Error = null;              // ReadSensors darf selbst einen Fehler setzen
            fresh = ReadSensors();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return;
        }
        fresh = MakeIdsUnique(fresh, out var aliases);
        lock (_lock)
        {
            _sensors = fresh;
            _byId = fresh.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            // Die ursprüngliche (doppelte) Id zeigt weiter auf den ersten Sensor, damit bestehende Applets funktionieren.
            foreach (var (plain, target) in aliases) _byId.TryAdd(plain, target);
        }
    }

    /// <summary>
    /// LibreHardwareMonitor vergibt manchmal dieselbe Id für mehrere Sensoren (z. B. NVIDIA "GPU Bus" und
    /// "GPU Memory" als /gpu-nvidia/0/load/3). Solche Ids bekommen den Sensornamen angehängt:
    /// /gpu-nvidia/0/load/3#gpu-bus und /gpu-nvidia/0/load/3#gpu-memory.
    /// </summary>
    public static IReadOnlyList<SensorInfo> MakeIdsUnique(IReadOnlyList<SensorInfo> sensors,
        out List<(string Plain, SensorInfo Target)> aliases)
    {
        aliases = new();
        var dupes = sensors.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        if (dupes.Count == 0) return sensors;

        var result = new List<SensorInfo>(sensors.Count);
        var used = new HashSet<string>(sensors.Where(s => !dupes.Contains(s.Id)).Select(s => s.Id));
        foreach (var s in sensors)
        {
            if (!dupes.Contains(s.Id)) { result.Add(s); continue; }
            var baseId = s.Id + "#" + Slug(s.Name);
            var id = baseId;
            for (int n = 2; !used.Add(id); n++) id = $"{baseId}-{n}";
            var renamed = s with { Id = id };
            result.Add(renamed);
            if (!aliases.Any(a => a.Plain == s.Id)) aliases.Add((s.Id, renamed));
        }
        return result;
    }

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "sensor" : slug;
    }

    protected abstract IReadOnlyList<SensorInfo> ReadSensors();

    /// <summary>Sensorbeschreibung zu einer Id oder Auto-Abfrage (für Einheit und Anzeige).</summary>
    public SensorInfo? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_lock)
        {
            if (id.StartsWith(AutoQuery.Prefix, StringComparison.OrdinalIgnoreCase))
                return AutoQuery.Resolve(id, _sensors);
            return _byId.TryGetValue(id, out var s) ? s : null;
        }
    }

    public double? GetValue(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_lock)
        {
            if (id.StartsWith(AutoQuery.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!_autoCache.TryGetValue(id, out var resolved) || resolved is null || !_byId.ContainsKey(resolved))
                    _autoCache[id] = resolved = AutoQuery.Resolve(id, _sensors)?.Id;
                id = resolved ?? "";
            }
            return _byId.TryGetValue(id, out var s) ? s.Value : null;
        }
    }

    public virtual void Dispose() { }
}

/// <summary>
/// Hardwareunabhängige Sensorabfrage, damit Vorlagen auf jedem PC funktionieren.
/// Format: auto:&lt;Art&gt;/&lt;Sensortyp&gt;/&lt;Name 1&gt;||&lt;Name 2&gt;||…
/// Namen werden der Reihe nach probiert (exakt, dann "beginnt mit", dann "enthält"); "*" = erster Sensor des Typs.
/// </summary>
public static class AutoQuery
{
    public const string Prefix = "auto:";

    public static SensorInfo? Resolve(string query, IEnumerable<SensorInfo> sensors)
    {
        var parts = query[Prefix.Length..].Split('/', 3);
        if (parts.Length < 3) return null;
        string kind = parts[0], type = parts[1];
        var candidates = sensors
            .Where(s => s.HardwareKind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                        && s.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var raw in parts[2].Split("||"))
        {
            var name = raw.Trim();
            if (name == "*") return candidates.FirstOrDefault();
            var hit = candidates.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault(s => s.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                   ?? candidates.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }
}

/// <summary>Werteverlauf je Sensor für Verlaufs-Bausteine.</summary>
public sealed class SensorHistory
{
    private readonly Dictionary<string, Queue<double?>> _data = new();
    private readonly object _lock = new();
    public int Capacity { get; }

    public SensorHistory(int capacity = 600) => Capacity = capacity;

    public void Record(string id, double? value)
    {
        lock (_lock)
        {
            if (!_data.TryGetValue(id, out var q)) _data[id] = q = new Queue<double?>();
            q.Enqueue(value);
            while (q.Count > Capacity) q.Dequeue();
        }
    }

    /// <summary>Geglätteter Wert über die letzten <paramref name="samples"/> Messungen; null ohne Daten.</summary>
    public double? Smoothed(string id, Model.SmoothingMode mode, int samples)
    {
        var vals = Last(id, Math.Max(1, samples)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (vals.Length == 0) return null;
        return Smooth(vals, mode);
    }

    public static double Smooth(IReadOnlyList<double> vals, Model.SmoothingMode mode)
    {
        if (mode == Model.SmoothingMode.Median)
        {
            var sorted = vals.OrderBy(v => v).ToArray();
            int m = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[m] : (sorted[m - 1] + sorted[m]) / 2;
        }
        return mode == Model.SmoothingMode.Average ? vals.Average() : vals[^1];
    }

    /// <summary>Die letzten <paramref name="count"/> Werte, ältester zuerst.</summary>
    public double?[] Last(string id, int count)
    {
        lock (_lock)
        {
            if (!_data.TryGetValue(id, out var q)) return Array.Empty<double?>();
            return q.Skip(Math.Max(0, q.Count - count)).ToArray();
        }
    }
}

/// <summary>Feste Werte, für Tests und die Vorschau ohne Hardware.</summary>
public sealed class StaticSensorSource : SensorSourceBase
{
    private readonly List<SensorInfo> _list;
    public StaticSensorSource(IEnumerable<SensorInfo> sensors) { _list = sensors.ToList(); Update(); }
    protected override IReadOnlyList<SensorInfo> ReadSensors() => _list;

    public static StaticSensorSource Demo() => new(new[]
    {
        new SensorInfo("/demo/cpu/temperature/0", "Demo CPU", "cpu", "CPU Package", "Temperature", "°C", 54),
        new SensorInfo("/demo/cpu/load/0", "Demo CPU", "cpu", "CPU Total", "Load", "%", 23),
        new SensorInfo("/demo/cpu/clock/1", "Demo CPU", "cpu", "Core #1", "Clock", "MHz", 4210),
        new SensorInfo("/demo/gpu/temperature/0", "Demo GPU", "gpu", "GPU Core", "Temperature", "°C", 61),
        new SensorInfo("/demo/gpu/load/0", "Demo GPU", "gpu", "GPU Core", "Load", "%", 47),
        new SensorInfo("/demo/gpu/smalldata/0", "Demo GPU", "gpu", "GPU Memory Used", "SmallData", "MB", 5120),
        new SensorInfo("/demo/ram/data/0", "Demo RAM", "memory", "Memory Used", "Data", "GB", 11.8),
        new SensorInfo("/demo/ram/data/1", "Demo RAM", "memory", "Memory Available", "Data", "GB", 20.2),
        new SensorInfo("/demo/ram/load/0", "Demo RAM", "memory", "Memory", "Load", "%", 37),
        new SensorInfo("/demo/mb/fan/0", "Demo Board", "motherboard", "Fan #1", "Fan", "RPM", 1180),
        new SensorInfo("/demo/mb/fan/1", "Demo Board", "motherboard", "Fan #2", "Fan", "RPM", 720),
        new SensorInfo("/demo/net/throughput/0", "Demo LAN", "network", "Download Speed", "Throughput", "B/s", 12_400_000),
        new SensorInfo("/demo/net/throughput/1", "Demo LAN", "network", "Upload Speed", "Throughput", "B/s", 800_000),
    });
}
