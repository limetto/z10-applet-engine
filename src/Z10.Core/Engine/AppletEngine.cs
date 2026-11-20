using Z10.Core.Device;
using Z10.Core.Model;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;

namespace Z10.Core.Engine;

public enum EngineState { Searching, Connected, Conflict, Error }

/// <summary>
/// Steuert Rotation, Sensorabfrage, Rendering und das Senden an die Tastatur in einem Hintergrund-Thread.
/// </summary>
public sealed class AppletEngine : IDisposable
{
    private readonly ISensorSource _sensors;
    private readonly Func<IZ10Device?> _deviceFactory;
    private readonly Func<bool> _conflictCheck;
    private readonly AppletRenderer _renderer;
    private readonly SensorHistory _history = new();
    private readonly object _lock = new();

    private List<Applet> _applets = new();
    private AppSettings _settings;
    private Thread? _thread;
    private volatile bool _running;

    private Z10Display? _display;
    private DateTime _nextDeviceTry, _nextSensorUpdate, _nextRender, _switchAt;
    private string? _currentId;
    private Applet? _previewOverride;
    private Applet? _lastShown;
    private bool _blink;
    private bool _backlightDirty = true;
    private DateTime _nextFullRefresh;

    /// <summary>
    /// Abstand, in dem alle Seiten neu gesendet werden, auch wenn sich nichts geändert hat. Fängt ab, dass die
    /// Tastatur ihren Displayspeicher verliert (Ruhezustand, Neuverbindung), ohne dass die Engine es bemerkt.
    /// </summary>
    public TimeSpan FullRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    public event Action<Framebuffer, Applet>? FrameRendered;
    public event Action? StatusChanged;

    public EngineState State { get; private set; } = EngineState.Searching;
    public string StatusText { get; private set; } = "Suche Tastatur …";
    public string? DeviceInfo { get; private set; }
    public SensorHistory History => _history;
    public AppletRenderer Renderer => _renderer;
    public ISensorSource Sensors => _sensors;

    public AppletEngine(ISensorSource sensors, ITextRasterizer text, Func<IZ10Device?> deviceFactory,
        Func<bool> conflictCheck, AppSettings settings)
    {
        _sensors = sensors;
        _deviceFactory = deviceFactory;
        _conflictCheck = conflictCheck;
        _settings = settings;
        _renderer = new AppletRenderer(text, sensors, _history);
    }

    public bool Paused
    {
        get => _settings.RotationPaused;
        set { _settings.RotationPaused = value; lock (_lock) _switchAt = DateTime.UtcNow.AddSeconds(CurrentDuration()); }
    }

    public Applet? Current
    {
        get { lock (_lock) return _lastShown; }
    }

    public void SetApplets(IEnumerable<Applet> applets)
    {
        lock (_lock)
        {
            _applets = applets.OrderBy(a => a.Order).ToList();
            _nextRender = DateTime.MinValue;
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            _backlightDirty = true;
            if (_display is not null) _display.Device.PageDelay = TimeSpan.FromMilliseconds(Math.Max(10, settings.PageDelayMs));
        }
    }

    private string[] _extraSensors = Array.Empty<string>();

    /// <summary>Zusätzliche Sensoren, deren Verlauf aufgezeichnet wird (z. B. aus dem Layout-Editor ohne Live-Anzeige).</summary>
    public void TrackSensors(IEnumerable<string> ids)
    {
        var arr = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToArray();
        lock (_lock) _extraSensors = arr;
    }

    /// <summary>Zeigt ein (noch nicht gespeichertes) Applet dauerhaft an, z. B. aus dem Layout-Editor. null = zurück zur Rotation.</summary>
    public void SetPreview(Applet? applet)
    {
        lock (_lock) { _previewOverride = applet; _nextRender = DateTime.MinValue; }
    }

    public void Show(string appletId)
    {
        lock (_lock)
        {
            _currentId = appletId;
            _switchAt = DateTime.UtcNow.AddSeconds(CurrentDuration());
            _nextRender = DateTime.MinValue;
        }
    }

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int dir)
    {
        lock (_lock)
        {
            var enabled = _applets.Where(a => a.Enabled).ToList();
            if (enabled.Count == 0) return;
            int idx = enabled.FindIndex(a => a.Id == _currentId);
            idx = idx < 0 ? 0 : (idx + dir + enabled.Count) % enabled.Count;
            _currentId = enabled[idx].Id;
            _switchAt = DateTime.UtcNow.AddSeconds(CurrentDuration());
            _nextRender = DateTime.MinValue;
        }
    }

    private int CurrentDuration()
    {
        var a = _applets.FirstOrDefault(x => x.Id == _currentId);
        return a is null || a.DurationSeconds <= 0 ? int.MaxValue / 4 : a.DurationSeconds;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Z10 Engine" };
        // STA, damit die App Systemschriften mit WPF rendern kann.
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        lock (_lock)
        {
            if (_display is not null)
            {
                try { if (_settings.ReleaseOnExit) _display.Release(); } catch (IOException) { }
                _display.Device.Dispose();
                _display = null;
            }
        }
    }

    /// <summary>true = Display an die Tastatur-Firmware zurückgegeben, die Engine sendet nichts mehr.</summary>
    public bool Suspended { get; private set; }

    /// <summary>Nach Ruhezustand o. Ä.: alle Seiten und Helligkeit/Kontrast sofort neu senden.</summary>
    public void InvalidateDisplay()
    {
        lock (_lock)
        {
            _display?.Invalidate();
            _backlightDirty = true;
            _nextRender = DateTime.MinValue;
            _nextDeviceTry = DateTime.MinValue;
        }
    }

    public void SetSuspended(bool suspended)
    {
        lock (_lock)
        {
            if (Suspended == suspended) return;
            Suspended = suspended;
            if (suspended) { try { _display?.Release(); } catch (IOException) { } }
            else { _display?.Invalidate(); _nextRender = DateTime.MinValue; }
        }
        StatusChanged?.Invoke();
    }

    public void Dispose() => Stop();

    /// <summary>Ein Durchlauf der Schleife. Öffentlich für Tests.</summary>
    public void Tick(DateTime utcNow)
    {
        lock (_lock)
        {
            HandleDevice(utcNow);
            HandleSensors(utcNow);
            HandleRotation(utcNow);
            HandleRender(utcNow);
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try { Tick(DateTime.UtcNow); }
            catch (Exception ex) { SetStatus(EngineState.Error, "Fehler: " + ex.Message); }
            Thread.Sleep(50);
        }
    }

    private void HandleDevice(DateTime now)
    {
        if (_conflictCheck())
        {
            if (_display is not null)
            {
                try { _display.Release(); } catch (IOException) { }
                _display.Device.Dispose();
                _display = null;
            }
            SetStatus(EngineState.Conflict, "EVGA Unleash läuft – Display freigegeben");
            return;
        }

        if (_display is null && now >= _nextDeviceTry)
        {
            _nextDeviceTry = now.AddSeconds(3);
            var dev = _deviceFactory();
            if (dev is null)
            {
                SetStatus(EngineState.Searching, "Tastatur nicht gefunden – suche weiter …");
                return;
            }
            dev.PageDelay = TimeSpan.FromMilliseconds(Math.Max(10, _settings.PageDelayMs));
            _display = new Z10Display(dev);
            _backlightDirty = true;
            _nextRender = DateTime.MinValue;
            DeviceInfo = dev.Description;
            SetStatus(EngineState.Connected, "Z10 verbunden");
        }

        if (_display is not null && _backlightDirty)
        {
            _backlightDirty = false;
            TryDevice(() => _display.SetBacklight(_settings.Brightness, _settings.Contrast));
        }
    }

    private void HandleSensors(DateTime now)
    {
        if (now < _nextSensorUpdate) return;
        _nextSensorUpdate = now.AddSeconds(1);
        _sensors.Update();
        // Verlauf für alle verwendeten Sensoren aufzeichnen (Verlaufs-Bausteine und Glättung)
        var used = _applets.Append(_previewOverride).Where(a => a is not null)
            .SelectMany(a => a!.SensorIds).Concat(_extraSensors).Distinct();
        foreach (var id in used) _history.Record(id, _sensors.GetValue(id));
    }

    private void HandleRotation(DateTime now)
    {
        if (_previewOverride is not null) return;
        var enabled = _applets.Where(a => a.Enabled).ToList();
        if (enabled.Count == 0) { _currentId = null; return; }
        if (_currentId is null || enabled.All(a => a.Id != _currentId))
        {
            _currentId = enabled[0].Id;
            _switchAt = now.AddSeconds(CurrentDuration());
            _nextRender = DateTime.MinValue;
        }
        else if (!Paused && now >= _switchAt)
        {
            Step(+1);
            _switchAt = now.AddSeconds(CurrentDuration());
        }
    }

    private void HandleRender(DateTime now)
    {
        var applet = _previewOverride
                     ?? _applets.FirstOrDefault(a => a.Enabled && a.ShowOnWarning && _renderer.HasWarning(a))
                     ?? _applets.FirstOrDefault(a => a.Id == _currentId);
        if (applet is null) return;
        if (!ReferenceEquals(applet, _lastShown)) _nextRender = DateTime.MinValue;
        if (now < _nextRender) return;
        _nextRender = now.AddMilliseconds(Math.Max(100, applet.RefreshMs));
        if (now >= _nextFullRefresh)
        {
            _display?.Invalidate();
            _nextFullRefresh = now + FullRefreshInterval;
        }
        _blink = !_blink;

        var fb = new Framebuffer();
        _renderer.Render(applet, fb, DateTime.Now, _blink);
        _lastShown = applet;
        FrameRendered?.Invoke(fb, applet);
        if (_display is not null && !Suspended) TryDevice(() => _display.Show(fb));
    }

    private void TryDevice(Action action)
    {
        try
        {
            action();
        }
        catch (IOException ex)
        {
            _display?.Device.Dispose();
            _display = null;
            _nextDeviceTry = DateTime.UtcNow.AddSeconds(3);
            SetStatus(EngineState.Error, "Verbindung verloren: " + ex.Message);
        }
    }

    private void SetStatus(EngineState state, string text)
    {
        if (State == state && StatusText == text) return;
        State = state;
        StatusText = text;
        StatusChanged?.Invoke();
    }
}
