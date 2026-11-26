namespace Z10.Core.Sensors;

/// <summary>Eigener Sensor, berechnet aus anderen Sensoren.</summary>
public sealed class CustomSensor
{
    /// <summary>Kurzname, ergibt die Id "custom:&lt;Key&gt;".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Formula { get; set; } = "";
    public string Unit { get; set; } = "";

    public string Id => SensorHub.CustomPrefix + Key;
}

/// <summary>
/// Fasst mehrere Sensorquellen (LibreHardwareMonitor, RTSS, …) zusammen und berechnet eigene Sensoren.
/// </summary>
public sealed class SensorHub : SensorSourceBase, IWantedSensors
{
    public const string CustomPrefix = "custom:";

    private readonly List<ISensorSource> _sources;
    private readonly object _customLock = new();
    private List<(CustomSensor Def, Formula? Formula, string? Error)> _custom = new();

    public IReadOnlyList<ISensorSource> Sources => _sources;

    public SensorHub(params ISensorSource[] sources) => _sources = sources.ToList();

    /// <summary>Setzt die eigenen Sensoren. Fehlerhafte Formeln liefern keinen Wert, die Meldung steht in CustomErrors.</summary>
    public void SetCustomSensors(IEnumerable<CustomSensor> sensors)
    {
        var list = new List<(CustomSensor, Formula?, string?)>();
        foreach (var c in sensors.Where(c => !string.IsNullOrWhiteSpace(c.Key)))
        {
            Formula.TryParse(c.Formula, out var f, out var err);
            list.Add((c, f, err));
        }
        lock (_customLock) _custom = list;
    }

    /// <summary>Gebrauchte Sensoren inkl. der Eingänge eigener Sensoren an die Quellen weiterreichen.</summary>
    public void SetWanted(IReadOnlyCollection<string> ids)
    {
        List<(CustomSensor Def, Formula? Formula, string? Error)> custom;
        lock (_customLock) custom = _custom;
        var byId = custom.ToDictionary(c => c.Def.Id, c => c.Formula, StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(ids);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!result.Add(id)) continue;
            if (byId.TryGetValue(id, out var f) && f is not null)
                foreach (var r in f.SensorRefs) stack.Push(r);
        }
        foreach (var src in _sources.OfType<IWantedSensors>()) src.SetWanted(result);
    }

    public IReadOnlyDictionary<string, string> CustomErrors
    {
        get
        {
            lock (_customLock)
                return _custom.Where(c => c.Error is not null).ToDictionary(c => c.Def.Key, c => c.Error!);
        }
    }

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        var errors = new List<string>();
        var merged = new List<SensorInfo>();
        foreach (var src in _sources)
        {
            src.Update();
            if (src.Error is not null) errors.Add(src.Error);
            merged.AddRange(src.Sensors);
        }
        Error = errors.Count == 0 ? null : string.Join(" · ", errors);

        merged = SensorSourceBase.MakeIdsUnique(merged, out var aliases).ToList();
        var byId = merged.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var (plain, target) in aliases) byId.TryAdd(plain, target);
        var autoCache = new Dictionary<string, SensorInfo?>();
        var customValues = new Dictionary<string, double?>();

        double? Lookup(string id)
        {
            if (id.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
                return customValues.TryGetValue(id, out var cv) ? cv : null;
            if (id.StartsWith(AutoQuery.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!autoCache.TryGetValue(id, out var hit)) autoCache[id] = hit = AutoQuery.Resolve(id, merged);
                return hit?.Value;
            }
            return byId.TryGetValue(id, out var s) ? s.Value : null;
        }

        List<(CustomSensor Def, Formula? Formula, string? Error)> custom;
        lock (_customLock) custom = _custom;
        foreach (var (def, formula, _) in custom)
        {
            // In Reihenfolge berechnen: ein eigener Sensor darf frühere eigene Sensoren verwenden.
            var value = formula?.Evaluate(Lookup);
            customValues[def.Id] = value;
            merged.Add(new SensorInfo(def.Id, "Eigene Sensoren", "custom",
                string.IsNullOrWhiteSpace(def.Name) ? def.Key : def.Name, "Custom", def.Unit, value));
        }
        return merged;
    }

    public override void Dispose()
    {
        foreach (var s in _sources) s.Dispose();
    }
}
