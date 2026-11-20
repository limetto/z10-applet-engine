using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

    private readonly AppState _state;
    private readonly WriteableBitmap _bitmap = LcdImage.Create();
    private bool _loading;

    public event Action<Applet>? EditRequested;

    public AppletsView(AppState state)
    {
        _state = state;
        InitializeComponent();
        Preview.Source = _bitmap;
        foreach (var d in Durations) DurationBox.Items.Add(d.Label);
        foreach (var r in Refreshes) RefreshBox.Items.Add(r.Label);

        List.ItemsSource = _state.Applets;
        _state.AppletsChanged += () => List.Items.Refresh();
        _state.Engine.FrameRendered += OnFrame;
        if (List.Items.Count > 0) List.SelectedIndex = 0;
        UpdatePauseButton();
    }

    private Applet? Selected => List.SelectedItem as Applet;

    private void OnFrame(Framebuffer fb, Applet applet)
    {
        var copy = fb.Clone();
        Dispatcher.BeginInvoke(() =>
        {
            LcdImage.Update(_bitmap, copy);
            PreviewTitle.Text = "Auf der Tastatur: " + applet.Name;
        });
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var a = Selected;
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
        if (a.Enabled) _state.Engine.Show(a.Id);
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not Applet a) return;
        a.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? a.Name : NameBox.Text.Trim();
        if (DurationBox.SelectedIndex >= 0) a.DurationSeconds = Durations[DurationBox.SelectedIndex].Seconds;
        if (RefreshBox.SelectedIndex >= 0) a.RefreshMs = Refreshes[RefreshBox.SelectedIndex].Ms;
        a.ShowOnWarning = WarnBox.IsChecked == true;
        SettingsTitle.Text = "Einstellungen: " + a.Name;
        _state.SaveApplet(a);
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

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _state.Engine.Paused = !_state.Engine.Paused;
        _state.SaveSettings();
        UpdatePauseButton();
    }

    private void UpdatePauseButton() =>
        PauseButton.Content = _state.Engine.Paused ? "Rotation fortsetzen" : "Rotation anhalten";

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is Applet a) EditRequested?.Invoke(a);
    }
}
