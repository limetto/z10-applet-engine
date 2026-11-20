using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Z10.App;

public partial class DeviceView : UserControl
{
    private static readonly int[] Delays = { 10, 20, 50, 100 };
    private readonly AppState _state;
    private readonly DispatcherTimer _timer;
    private bool _loading = true;

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
        AutoStartBox.IsChecked = AutoStart.IsEnabled();
        UpdateBacklightLabels();
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

    private void Behavior_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.ReleaseOnExit = ReleaseOnExitBox.IsChecked == true;
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

    private sealed record Row(string Hardware, string Name, string Type, string ValueText, string Id);
}
