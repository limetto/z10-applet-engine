using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Z10.Core.Model;
using Z10.Core.Rendering;

namespace Z10.App;

public partial class AppletsView : UserControl
{
    private static readonly (string Label, int Seconds)[] Durations =
        { ("3 Sekunden", 3), ("5 Sekunden", 5), ("8 Sekunden", 8), ("10 Sekunden", 10), ("15 Sekunden", 15), ("30 Sekunden", 30), ("60 Sekunden", 60), ("nur manuell", 0) };
    private static readonly (string Label, int Ms)[] Refreshes =
        { ("0,25 s", 250), ("0,5 s", 500), ("1 s", 1000), ("2 s", 2000), ("5 s", 5000) };

    private static readonly int[] TransitionDurations = { 200, 300, 400, 600, 800, 1000 };

    private readonly AppState _state;
    private readonly WriteableBitmap _bitmap = LcdImage.Create();
    private bool _loading;
    private bool _dirty;
    private Applet? _shown;     // Applet, dessen Einstellungen gerade im Formular stehen

    public event Action<Applet>? EditRequested;

    public AppletsView(AppState state)
    {
        _state = state;
        InitializeComponent();
        Preview.Source = _bitmap;
        foreach (var d in Durations) DurationBox.Items.Add(d.Label);
        foreach (var r in Refreshes) RefreshBox.Items.Add(r.Label);
        foreach (var ms in TransitionDurations) TransitionBox.Items.Add($"{ms / 1000.0:0.0#} s");
        _loading = true;
        TransitionCheck.IsChecked = state.Settings.TransitionEnabled;
        int ti = Array.IndexOf(TransitionDurations, state.Settings.TransitionMs);
        TransitionBox.SelectedIndex = ti < 0 ? 2 : ti;
        TransitionBox.IsEnabled = state.Settings.TransitionEnabled;
        _loading = false;

        List.ItemsSource = _state.Applets;
        _state.AppletsChanged += () => List.Items.Refresh();
        _state.Engine.FrameRendered += OnFrame;
        LcdImage.ColorChanged += () => Dispatcher.BeginInvoke(() => { if (_lastFrame is not null) LcdImage.Update(_bitmap, _lastFrame); });
        if (List.Items.Count > 0) List.SelectedIndex = 0;
        UpdateAutoRotate();
    }

    private Framebuffer? _lastFrame;

    private Applet? Selected => List.SelectedItem as Applet;

    private void OnFrame(Framebuffer fb, Applet applet)
    {
        var copy = fb.Clone();
        Dispatcher.BeginInvoke(() =>
        {
            _lastFrame = copy;
            LcdImage.Update(_bitmap, copy);
            if (AutoRotateBox.IsChecked != !_state.Engine.Paused) UpdateAutoRotate();   // z. B. per E-Taste umgeschaltet
            PreviewTitle.Text = "Auf der Tastatur: " + applet.Name;
        });
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Beim Wechsel zu einem anderen Applet nichts verlieren
        if (_dirty && _shown is not null && !ReferenceEquals(_shown, Selected)) SaveSettingsOf(_shown);
        var a = Selected;
        _shown = a;
        SettingsCard.IsEnabled = a is not null;
        if (a is null) return;
        _loading = true;
        SettingsTitle.Text = "Einstellungen: " + a.Name;
        NameBox.Text = a.Name;
        DurationBox.SelectedIndex = Math.Max(0, Array.FindIndex(Durations, d => d.Seconds == a.DurationSeconds));
        int r = Array.FindIndex(Refreshes, x => x.Ms == a.RefreshMs);
        RefreshBox.SelectedIndex = r < 0 ? 2 : r;
        WarnBox.IsChecked = a.ShowOnWarning;
        var ids = a.SensorIds.ToList();
        SensorList.Text = ids.Count == 0 ? "keine" : string.Join(Environment.NewLine, ids);
        _loading = false;
        SetDirty(false);
        if (a.Enabled) _state.Engine.Show(a.Id);
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _shown is null) return;
        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        SaveButton.IsEnabled = RevertButton.IsEnabled = dirty;
        DirtyText.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveSettingsOf(Applet a)
    {
        a.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? a.Name : NameBox.Text.Trim();
        if (DurationBox.SelectedIndex >= 0) a.DurationSeconds = Durations[DurationBox.SelectedIndex].Seconds;
        if (RefreshBox.SelectedIndex >= 0) a.RefreshMs = Refreshes[RefreshBox.SelectedIndex].Ms;
        a.ShowOnWarning = WarnBox.IsChecked == true;
        SetDirty(false);
        _state.SaveApplet(a);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_shown is null) return;
        var a = _shown;
        SaveSettingsOf(a);
        SettingsTitle.Text = "Einstellungen: " + a.Name;
        List.Items.Refresh();
        List.SelectedItem = a;
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        SetDirty(false);
        List_SelectionChanged(this, null!);
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _dirty) Save_Click(sender, e);
    }

    private void Enabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: Applet a } cb)
        {
            a.Enabled = cb.IsChecked == true;
            _state.SaveApplet(a);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var a = _state.AddApplet("Neues Applet");
        List.SelectedItem = a;
        EditRequested?.Invoke(a);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is Applet a) List.SelectedItem = _state.Duplicate(a);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not Applet a) return;
        if (MessageBox.Show($"Applet „{a.Name}“ löschen?", "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _state.Delete(a);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        if (Selected is not Applet a) return;
        _state.Move(a, delta);
        List.SelectedItem = a;
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _state.Engine.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _state.Engine.Next();

    private void Transition_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _state.Settings.TransitionEnabled = TransitionCheck.IsChecked == true;
        if (TransitionBox.SelectedIndex >= 0) _state.Settings.TransitionMs = TransitionDurations[TransitionBox.SelectedIndex];
        TransitionBox.IsEnabled = _state.Settings.TransitionEnabled;
        _state.SaveSettings();
    }

    private void AutoRotate_Click(object sender, RoutedEventArgs e)
    {
        _state.Engine.Paused = AutoRotateBox.IsChecked != true;
        _state.SaveSettings();
        UpdateAutoRotate();
    }

    private void UpdateAutoRotate()
    {
        AutoRotateBox.IsChecked = !_state.Engine.Paused;
        AutoRotateHint.Text = _state.Engine.Paused
            ? "Aus: Applets wechseln nur manuell über ◀ ▶ oder die Tasten E6/E7."
            : "Aktive Applets wechseln nach ihrer Anzeigedauer. Ein Applet mit Anzeigedauer „nur manuell“ bleibt stehen, bis du weiterschaltest.";
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not Applet a) return;
        if (_dirty) SaveSettingsOf(a);
        EditRequested?.Invoke(a);
    }
}
