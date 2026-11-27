using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Z10.Core.Device;
using Z10.Core.Localization;
using Z10.Core.Rendering;
using Z10.Core.Media;
using Z10.Core.Sensors;
using Z10.Core.Storage;
using static Z10.Core.Localization.Loc;

namespace Z10.App;

/// <summary>Allgemeine Einstellungen: Display/Programm, Sensoren und Datenquellen, Bildschirmschoner, E-Tasten.</summary>
public partial class OptionsView : UserControl
{
    private static readonly int[] Delays = { 10, 20, 50, 100 };
    private static readonly int[] SensorIntervals = { 250, 500, 1000, 2000 };
    private static readonly int[] SaverMinutes = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };
    private static readonly (KeyAction Action, string Label)[] KeyActions =
    {
        (KeyAction.None, L("nichts")),
        (KeyAction.Previous, L("vorheriges Applet")),
        (KeyAction.Next, L("nächstes Applet")),
        (KeyAction.TogglePause, L("Rotation anhalten / fortsetzen")),
        (KeyAction.ToggleDisplay, L("Display freigeben / übernehmen")),
    };

    private readonly AppState _state;
    private readonly DispatcherTimer _timer;
    private bool _loading = true;

    public OptionsView(AppState state)
    {
        _state = state;
        InitializeComponent();
        UiTranslator.Apply(this);
        var s = _state.Settings;

        foreach (var d in Delays) DelayBox.Items.Add(d == 20 ? L("20 ms (empfohlen)") : d == 10 ? L("10 ms (getestetes Minimum)") : $"{d} ms");
        DelayBox.SelectedIndex = Math.Max(0, Array.IndexOf(Delays, s.PageDelayMs));
        ReleaseOnExitBox.IsChecked = s.ReleaseOnExit;
        BootBox.IsChecked = s.BootAnimation;
        AutoStartBox.IsChecked = AutoStart.IsEnabled();

        foreach (var ms in SensorIntervals)
            SensorIntervalBox.Items.Add(ms == 1000 ? L("1 Sekunde (Standard)") : L("{0} Sekunden", ms / 1000.0));
        int si = Array.IndexOf(SensorIntervals, s.SensorIntervalMs);
        SensorIntervalBox.SelectedIndex = si < 0 ? Array.IndexOf(SensorIntervals, 1000) : si;
        RtssBox.IsChecked = s.RtssEnabled;
        AfterburnerBox.IsChecked = s.AfterburnerEnabled;
        AudioBox.IsChecked = s.AudioEnabled;
        MediaBox.IsChecked = s.MediaEnabled;

        foreach (var m in SaverMinutes) SaverMinutesBox.Items.Add(m == 1 ? L("1 Minute") : L("{0} Minuten", m));
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

        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = "Z10 Applet Engine · " + L("Version {0}", v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}");

        _languages = Loc.Available().ToArray();
        LanguageBox.Items.Add(L("Automatisch (Windows-Sprache)"));
        foreach (var lang in _languages) LanguageBox.Items.Add(lang.Name);
        int li = Array.FindIndex(_languages, l => l.Code == s.Language);
        LanguageBox.SelectedIndex = li < 0 ? 0 : li + 1;
        LayoutBox.Items.Add(L("Automatisch (Windows-Eingabesprache)"));
        foreach (var layout in Z10KeyLayout.Layouts) LayoutBox.Items.Add(layout.Name);
        int ki = Z10KeyLayout.Layouts.ToList().FindIndex(l => l.Id == s.KeyboardLayout);
        LayoutBox.SelectedIndex = ki < 0 ? 0 : ki + 1;
        _loading = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var c = CultureInfo.CurrentCulture;
        RtssStatus.Text = !_state.Settings.RtssEnabled ? L("ausgeschaltet")
            : !_state.Rtss.IsAvailable
                ? (_state.Afterburner.IsAvailable ? L("RTSS läuft nicht – Afterburner allein liefert keine Framerate") : L("RTSS läuft nicht"))
            : _state.Rtss.ActiveApp is string app
                ? L("aktiv:") + $" {app} · {_state.Sensors.GetValue(RtssSensorSource.FpsId)?.ToString("0", c) ?? "–"} FPS"
                : L("RTSS läuft, aber kein Spiel erkannt");
        int abCount = _state.Afterburner.Sensors.Count;
        AfterburnerStatus.Text = !_state.Settings.AfterburnerEnabled ? L("ausgeschaltet")
            : _state.Afterburner.IsAvailable ? L("verbunden · {0} Werte", abCount) : L("Afterburner läuft nicht");
        var vol = _state.Sensors.GetValue(AudioSensorSource.DefaultOutVolume);
        var muted = _state.Sensors.GetValue(AudioSensorSource.DefaultOutMute) == 1;
        AudioStatus.Text = !_state.Settings.AudioEnabled ? L("ausgeschaltet")
            : _state.Audio.Error is string ae ? L("Fehler:") + " " + ae
            : vol is double volPct ? L("Standardgerät:") + $" {volPct.ToString("0", c)} %{(muted ? " " + L("(stumm)") : "")} · " + L("Sensoren unter /audio/…") : L("kein Wiedergabegerät gefunden");
        var media = _state.Media.Current;
        MediaStatusText.Text = !_state.Settings.MediaEnabled ? L("ausgeschaltet")
            : media is null ? (_state.Media.Error is string me ? L("Fehler:") + " " + me : L("keine Wiedergabe"))
            : $"{media.App}: " + (media.IsPlaying ? L("spielt") : L("angehalten")) + " · "
              + MediaText.Get(media, MediaField.ArtistTitle, DateTime.UtcNow) + " · " + L("Sensoren unter /media/…");
        KeysStatus.Text = !_state.Settings.KeysEnabled ? L("ausgeschaltet")
            : !_state.Keys.IsConnected ? L("Tastenkanal nicht verbunden")
            : _state.Engine.LastKey is string last ? L("bereit · zuletzt gedrückt: {0}", last) : L("bereit · drück E6 oder E7 zum Testen");
    }

    private void Link_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { }
        e.Handled = true;
    }

    private LanguageInfo[] _languages = Array.Empty<LanguageInfo>();

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0) return;
        _state.Settings.Language = LanguageBox.SelectedIndex == 0 ? "auto" : _languages[LanguageBox.SelectedIndex - 1].Code;
        _state.SaveSettings();
        string wanted = _state.Settings.Language == "auto" ? Loc.Detect() : _state.Settings.Language;
        RestartPanel.Visibility = wanted == Loc.Current ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Restart();

    private void LanguageTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Loc.UserFolder ?? System.IO.Path.Combine(_state.Store.Root, "lang");
            var path = Loc.ExportTemplate(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("Sprache"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LayoutBox.SelectedIndex < 0) return;
        _state.Settings.KeyboardLayout = LayoutBox.SelectedIndex == 0 ? "auto" : Z10KeyLayout.Layouts[LayoutBox.SelectedIndex - 1].Id;
        _state.SaveSettings();
        _state.ApplyKeyboardLayout();
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
            MessageBox.Show(L("Die Aufgabe für den Autostart konnte nicht geändert werden."), L("Autostart"),
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
        _state.Settings.MediaEnabled = MediaBox.IsChecked == true;
        _state.SaveSettings();
        Refresh();
    }
}
