using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;

namespace Z10.App;

/// <summary>Allgemeine Einstellungen: Display/Programm, Sensoren und Datenquellen, Bildschirmschoner, E-Tasten.</summary>
public partial class OptionsView : UserControl
{
    private static readonly int[] Delays = { 10, 20, 50, 100 };
    private static readonly int[] SensorIntervals = { 250, 500, 1000, 2000 };
    private static readonly int[] SaverMinutes = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };
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

    public OptionsView(AppState state)
    {
        _state = state;
        InitializeComponent();
        var s = _state.Settings;

        foreach (var d in Delays) DelayBox.Items.Add(d == 20 ? "20 ms (empfohlen)" : d == 10 ? "10 ms (getestetes Minimum)" : $"{d} ms");
        DelayBox.SelectedIndex = Math.Max(0, Array.IndexOf(Delays, s.PageDelayMs));
        ReleaseOnExitBox.IsChecked = s.ReleaseOnExit;
        BootBox.IsChecked = s.BootAnimation;
        AutoStartBox.IsChecked = AutoStart.IsEnabled();

        foreach (var ms in SensorIntervals)
            SensorIntervalBox.Items.Add(ms < 1000 ? $"{ms / 1000.0:0.##} Sekunden" : ms == 1000 ? "1 Sekunde (Standard)" : $"{ms / 1000} Sekunden");
        int si = Array.IndexOf(SensorIntervals, s.SensorIntervalMs);
        SensorIntervalBox.SelectedIndex = si < 0 ? Array.IndexOf(SensorIntervals, 1000) : si;
        RtssBox.IsChecked = s.RtssEnabled;
        AfterburnerBox.IsChecked = s.AfterburnerEnabled;
        AudioBox.IsChecked = s.AudioEnabled;

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
        _loading = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
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
    }

    private void Delay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DelayBox.SelectedIndex < 0) return;
        _state.Settings.PageDelayMs = Delays[DelayBox.SelectedIndex];
        _state.SaveSettings();
    }

    private void SensorInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SensorIntervalBox.SelectedIndex < 0) return;
        _state.Settings.SensorIntervalMs = SensorIntervals[SensorIntervalBox.SelectedIndex];
        _state.SaveSettings();
    }

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
}
