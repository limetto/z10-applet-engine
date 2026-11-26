using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;

namespace Z10.App;

public partial class DeviceView : UserControl
{
    private static readonly int[] Delays = { 10, 20, 50, 100 };
    private static readonly (KeyAction Action, string Label)[] KeyActions =
    {
        (KeyAction.None, "nichts"),
        (KeyAction.Previous, "vorheriges Applet"),
        (KeyAction.Next, "nächstes Applet"),
        (KeyAction.TogglePause, "Rotation anhalten / fortsetzen"),
        (KeyAction.ToggleDisplay, "Display freigeben / übernehmen"),
    };
    private readonly AppState _state;
    private readonly DispatcherTimer _timer;
    private bool _loading = true;
    private bool _customLoading;
    private readonly ObservableCollection<CustomSensor> _custom = new();

    public DeviceView(AppState state)
    {
        _state = state;
        InitializeComponent();

        foreach (var d in Delays) DelayBox.Items.Add(d == 20 ? "20 ms (empfohlen)" : d == 10 ? "10 ms (getestetes Minimum)" : $"{d} ms");
        var s = _state.Settings;
        BrightnessSlider.Value = s.Brightness;
        ContrastSlider.Value = s.Contrast;
        DelayBox.SelectedIndex = Math.Max(0, Array.IndexOf(Delays, s.PageDelayMs));
        ReleaseOnExitBox.IsChecked = s.ReleaseOnExit;
        BootBox.IsChecked = s.BootAnimation;
        foreach (var m in SaverMinutes) SaverMinutesBox.Items.Add(m == 1 ? "1 Minute" : $"{m} Minuten");
        foreach (var k in Enum.GetValues<ScreensaverKind>()) SaverKindBox.Items.Add(Screensavers.DisplayName(k));
        SaverBox.IsChecked = s.ScreensaverEnabled;
        int mi = Array.IndexOf(SaverMinutes, s.ScreensaverMinutes);
        SaverMinutesBox.SelectedIndex = mi < 0 ? Array.IndexOf(SaverMinutes, 5) : mi;
        SaverKindBox.SelectedIndex = (int)s.ScreensaverKind;
        SaverMouseBox.IsChecked = s.ScreensaverIncludeMouse;
        foreach (var k in KeyActions) { E6Box.Items.Add(k.Label); E7Box.Items.Add(k.Label); }
        KeysBox.IsChecked = s.KeysEnabled;
        E6Box.SelectedIndex = Math.Max(0, Array.FindIndex(KeyActions, k => k.Action == s.E6Action));
        E7Box.SelectedIndex = Math.Max(0, Array.FindIndex(KeyActions, k => k.Action == s.E7Action));
        _state.Engine.KeyPressed += (_, _) => Dispatcher.BeginInvoke(Refresh);
        AutoStartBox.IsChecked = AutoStart.IsEnabled();
        RtssBox.IsChecked = s.RtssEnabled;
        AfterburnerBox.IsChecked = s.AfterburnerEnabled;
        AudioBox.IsChecked = s.AudioEnabled;
        UpdateBacklightLabels();
        foreach (var c in s.CustomSensors) _custom.Add(Copy(c));
        CustomList.ItemsSource = _custom;
        CustomEditor.IsEnabled = false;
        if (_custom.Count > 0) CustomList.SelectedIndex = 0;
        _loading = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        DeviceInfo.Text = _state.Engine.DeviceInfo ?? "–";
        DeviceStatus.Text = _state.Engine.StatusText;
        var err = _state.Sensors.Error;
        SensorError.Visibility = err is null ? Visibility.Collapsed : Visibility.Visible;
        SensorError.Text = err is null ? "" : "Sensorfehler: " + err;
        var c = CultureInfo.CurrentCulture;
        RtssStatus.Text = !_state.Settings.RtssEnabled ? "ausgeschaltet"
            : !_state.Rtss.IsAvailable
                ? (_state.Afterburner.IsAvailable ? "RTSS läuft nicht – Afterburner allein liefert keine Framerate" : "RTSS läuft nicht")
            : _state.Rtss.ActiveApp is string app
                ? $"aktiv: {app} · {_state.Sensors.GetValue(RtssSensorSource.FpsId)?.ToString("0", c) ?? "–"} FPS"
                : "RTSS läuft, aber kein Spiel erkannt";
        int abCount = _state.Afterburner.Sensors.Count;
        AfterburnerStatus.Text = !_state.Settings.AfterburnerEnabled ? "ausgeschaltet"
            : _state.Afterburner.IsAvailable ? $"verbunden · {abCount} Werte" : "Afterburner läuft nicht";
        var vol = _state.Sensors.GetValue(AudioSensorSource.DefaultOutVolume);
        var muted = _state.Sensors.GetValue(AudioSensorSource.DefaultOutMute) == 1;
        AudioStatus.Text = !_state.Settings.AudioEnabled ? "ausgeschaltet"
            : _state.Audio.Error is string ae ? "Fehler: " + ae
            : vol is double volPct ? $"Standardgerät: {volPct.ToString("0", c)} %{(muted ? " (stumm)" : "")} · Sensoren unter /audio/…" : "kein Wiedergabegerät gefunden";
        KeysStatus.Text = !_state.Settings.KeysEnabled ? "ausgeschaltet"
            : !_state.Keys.IsConnected ? "Tastenkanal nicht verbunden"
            : _state.Engine.LastKey is string last ? $"bereit · zuletzt gedrückt: {last}" : "bereit · drück E6 oder E7 zum Testen";
        UpdateCustomResult();
        if (!IsVisible) return;

        var filter = FilterBox.Text.Trim();
        var selectedId = (SensorGrid.SelectedItem as Row)?.Id;
        var rows = _state.Sensors.Sensors
            .Where(x => filter.Length == 0
                        || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || x.Hardware.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || x.Type.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || x.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => new Row(x.Hardware, x.Name, x.Type,
                x.Value is double v ? $"{v.ToString("0.##", CultureInfo.CurrentCulture)} {x.Unit}" : "–", x.Id))
            .ToList();
        SensorGrid.ItemsSource = rows;
        if (selectedId is not null) SensorGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Refresh();

    private void CopyId_Click(object sender, RoutedEventArgs e)
    {
        if (SensorGrid.SelectedItem is Row r) Clipboard.SetText(r.Id);
    }

    private void Backlight_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _state.Settings.Brightness = (int)BrightnessSlider.Value;
        _state.Settings.Contrast = (int)ContrastSlider.Value;
        UpdateBacklightLabels();
        _state.SaveSettings();
    }

    private void UpdateBacklightLabels()
    {
        BrightnessValue.Text = $"Stufe {(int)BrightnessSlider.Value} von 5";
        ContrastValue.Text = $"Stufe {(int)ContrastSlider.Value} von 5";
    }

    private void Delay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DelayBox.SelectedIndex < 0) return;
        _state.Settings.PageDelayMs = Delays[DelayBox.SelectedIndex];
        _state.SaveSettings();
    }

    private static readonly int[] SaverMinutes = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };

    private void Saver_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.ScreensaverEnabled = SaverBox.IsChecked == true;
        if (SaverMinutesBox.SelectedIndex >= 0) s.ScreensaverMinutes = SaverMinutes[SaverMinutesBox.SelectedIndex];
        if (SaverKindBox.SelectedIndex >= 0) s.ScreensaverKind = (ScreensaverKind)SaverKindBox.SelectedIndex;
        s.ScreensaverIncludeMouse = SaverMouseBox.IsChecked == true;
        _state.SaveSettings();
    }

    private void SaverPreview_Click(object sender, RoutedEventArgs e)
    {
        Saver_Changed(sender, e);
        _state.Engine.PreviewScreensaver(TimeSpan.FromSeconds(10));
    }

    private void Behavior_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.ReleaseOnExit = ReleaseOnExitBox.IsChecked == true;
        _state.Settings.BootAnimation = BootBox.IsChecked == true;
        _state.SaveSettings();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        bool want = AutoStartBox.IsChecked == true;
        if (!AutoStart.Set(want))
        {
            MessageBox.Show("Die Aufgabe für den Autostart konnte nicht geändert werden.", "Autostart",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        AutoStartBox.IsChecked = AutoStart.IsEnabled();
    }

    private void Keys_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _state.Settings.KeysEnabled = KeysBox.IsChecked == true;
        if (E6Box.SelectedIndex >= 0) _state.Settings.E6Action = KeyActions[E6Box.SelectedIndex].Action;
        if (E7Box.SelectedIndex >= 0) _state.Settings.E7Action = KeyActions[E7Box.SelectedIndex].Action;
        _state.SaveSettings();
        Refresh();
    }

    private void Sources_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.RtssEnabled = RtssBox.IsChecked == true;
        _state.Settings.AfterburnerEnabled = AfterburnerBox.IsChecked == true;
        _state.Settings.AudioEnabled = AudioBox.IsChecked == true;
        _state.SaveSettings();
        Refresh();
    }

    // ---------- Eigene Sensoren ----------

    private static CustomSensor Copy(CustomSensor c) => new() { Key = c.Key, Name = c.Name, Formula = c.Formula, Unit = c.Unit };

    private CustomSensor? SelectedCustom => CustomList.SelectedItem as CustomSensor;

    private void CustomList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var c = SelectedCustom;
        CustomEditor.IsEnabled = c is not null;
        _customLoading = true;
        CustomName.Text = c?.Name ?? "";
        CustomKey.Text = c?.Key ?? "";
        CustomUnit.Text = c?.Unit ?? "";
        CustomFormula.Text = c?.Formula ?? "";
        _customLoading = false;
        UpdateCustomResult();
    }

    private void Custom_Changed(object sender, TextChangedEventArgs e)
    {
        if (_customLoading || SelectedCustom is not CustomSensor c) return;
        c.Name = CustomName.Text;
        c.Key = Regex.Replace(CustomKey.Text.Trim().ToLowerInvariant(), "[^a-z0-9_-]", "");
        c.Unit = CustomUnit.Text;
        c.Formula = CustomFormula.Text;
        CustomSaved.Text = "ungespeichert";
        UpdateCustomResult();
    }

    private void UpdateCustomResult()
    {
        if (SelectedCustom is not CustomSensor c) { CustomResult.Text = ""; return; }
        if (string.IsNullOrEmpty(c.Key)) { CustomResult.Text = "Bitte einen Kurznamen vergeben (a–z, 0–9, _ und -)."; return; }
        if (_custom.Count(x => x.Key == c.Key) > 1) { CustomResult.Text = $"Kurzname „{c.Key}“ ist doppelt."; return; }
        if (!Formula.TryParse(c.Formula, out var f, out var err)) { CustomResult.Text = "Fehler: " + err; return; }
        var missing = f!.SensorRefs.Where(r => _state.Sensors.Find(r) is null && !r.StartsWith(SensorHub.CustomPrefix)).ToList();
        var value = f.Evaluate(_state.Sensors.GetValue);
        var text = value is double v ? $"Ergebnis: {v.ToString("0.##", CultureInfo.CurrentCulture)} {c.Unit}".TrimEnd() : "Ergebnis: kein Wert";
        if (missing.Count > 0) text += " · nicht gefunden: " + string.Join(", ", missing);
        CustomResult.Text = text + $"{Environment.NewLine}Verwenden als Sensor: {c.Id}";
    }

    private void CustomAdd_Click(object sender, RoutedEventArgs e)
    {
        int n = 1;
        while (_custom.Any(c => c.Key == $"sensor{n}")) n++;
        var c = new CustomSensor { Key = $"sensor{n}", Name = $"Eigener Sensor {n}", Formula = "max([auto:cpu/Temperature/*], [auto:gpu/Temperature/*])", Unit = "°C" };
        _custom.Add(c);
        CustomList.SelectedItem = c;
        CustomSaved.Text = "ungespeichert";
    }

    private void CustomDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCustom is not CustomSensor c) return;
        _custom.Remove(c);
        CustomSaved.Text = "ungespeichert";
    }

    private void InsertId_Click(object sender, RoutedEventArgs e)
    {
        if (SensorGrid.SelectedItem is not Row r || SelectedCustom is null) return;
        int pos = CustomFormula.CaretIndex;
        var insert = $"[{r.Id}]";
        CustomFormula.Text = CustomFormula.Text.Insert(Math.Clamp(pos, 0, CustomFormula.Text.Length), insert);
        CustomFormula.CaretIndex = pos + insert.Length;
        CustomFormula.Focus();
    }

    private void CustomSave_Click(object sender, RoutedEventArgs e)
    {
        var keys = _custom.Select(c => c.Key).ToList();
        if (keys.Any(string.IsNullOrEmpty) || keys.Distinct().Count() != keys.Count)
        {
            MessageBox.Show("Jeder eigene Sensor braucht einen eindeutigen Kurznamen.", "Eigene Sensoren",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _state.Settings.CustomSensors = _custom.Select(Copy).ToList();
        _state.SaveSettings();
        CustomList.Items.Refresh();
        var errors = _state.Sensors.CustomErrors;
        CustomSaved.Text = errors.Count == 0 ? "gespeichert" : $"gespeichert · {errors.Count} Formel(n) fehlerhaft";
    }

    private sealed record Row(string Hardware, string Name, string Type, string ValueText, string Id);
}
