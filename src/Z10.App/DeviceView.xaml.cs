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
    private readonly AppState _state;
    private readonly DispatcherTimer _timer;
    private bool _loading = true;
    private bool _customLoading;
    private readonly ObservableCollection<CustomSensor> _custom = new();

    public DeviceView(AppState state)
    {
        _state = state;
        InitializeComponent();

        var s = _state.Settings;
        BrightnessSlider.Value = s.Brightness;
        ContrastSlider.Value = s.Contrast;
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
