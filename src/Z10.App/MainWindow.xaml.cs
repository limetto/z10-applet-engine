using System;
using System.Linq;
using System.Windows;
using static Z10.Core.Localization.Loc;
using System.Windows.Media;
using System.Windows.Threading;
using Z10.Core.Engine;
using Z10.Core.Model;
using Z10.Core.Sensors;

namespace Z10.App;

public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly AppletsView _applets;
    private readonly EditorView _editor;
    private readonly DeviceView _device;
    private readonly OptionsView _options;
    private readonly LightingView _lighting;
    private readonly DispatcherTimer _statusTimer;

    public MainWindow(AppState state)
    {
        _state = state;
        InitializeComponent();
        UiTranslator.Apply(this);

        _applets = new AppletsView(state);
        _editor = new EditorView(state);
        _device = new DeviceView(state);
        _options = new OptionsView(state);
        _lighting = new LightingView(state);
        _applets.EditRequested += OpenEditor;
        ContentHost.Children.Add(_applets);
        ContentHost.Children.Add(_editor);
        ContentHost.Children.Add(_device);
        ContentHost.Children.Add(_options);
        ContentHost.Children.Add(_lighting);
        ShowView(_applets);

        _state.Engine.StatusChanged += () => Dispatcher.BeginInvoke(UpdateStatus);
        for (int p = 1; p <= 5; p++)
        {
            var b = new System.Windows.Controls.Button
            {
                Content = p.ToString(), Tag = p, Width = 24, Height = 22, MinHeight = 0, Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0), FontSize = 12,
            };
            b.Click += Profile_Click;
            ProfilePanel.Children.Add(b);
        }
        _state.Engine.ProfileChanged += _ => Dispatcher.BeginInvoke(UpdateProfile);
        UpdateProfile();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    private void OpenEditor(Applet applet)
    {
        _editor.Load(applet);
        NavEditor.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_applets is null) return;    // während InitializeComponent
        if (sender == NavApplets) ShowView(_applets);
        else if (sender == NavEditor) ShowView(_editor);
        else if (sender == NavLighting) ShowView(_lighting);
        else if (sender == NavOptions) ShowView(_options);
        else ShowView(_device);
    }

    private void ShowView(UIElement view)
    {
        foreach (UIElement child in ContentHost.Children)
            child.Visibility = child == view ? Visibility.Visible : Visibility.Collapsed;
        if (view != _editor) _editor.LeaveLive();
        else _editor.EnterLive();
        if (view == _lighting) _lighting.OnShown();
    }

    private bool _switching;

    private void UpdateProfile()
    {
        var engine = _state.Engine;
        bool usable = engine.State == Z10.Core.Engine.EngineState.Connected && !_switching;
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        foreach (var b in ProfilePanel.Children.OfType<System.Windows.Controls.Button>())
        {
            bool active = engine.ActiveProfile == (int)b.Tag;
            b.Background = active ? accent : System.Windows.Media.Brushes.Transparent;
            b.Foreground = active ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("MutedBrush");
            b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            b.IsEnabled = usable;
        }
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (_switching || sender is not System.Windows.Controls.Button { Tag: int profile } || _state.Engine.ActiveProfile == profile) return;
        _switching = true;
        UpdateProfile();
        try
        {
            await _state.SwitchProfileAsync(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Profilwechsel fehlgeschlagen:") + " " + ex.Message, L("Profil"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _switching = false;
            UpdateProfile();
        }
    }

    private void UpdateStatus()
    {
        UpdateProfile();
        var engine = _state.Engine;
        DeviceText.Text = engine.Suspended ? L("Display freigegeben") : engine.StatusText;
        ReleaseButton.Content = engine.Suspended ? L("Display übernehmen") : L("Display freigeben");
        bool ok = engine.State == EngineState.Connected && !engine.Suspended;
        bool warn = engine.State == EngineState.Conflict || engine.Suspended;
        SetChip(DeviceChip, DeviceDot, DeviceText, ok ? 0 : warn ? 1 : 2);

        var err = _state.Sensors.Error;
        var game = _state.Rtss.ActiveApp is string app && _state.Sensors.GetValue(RtssSensorSource.FpsId) is double fps
            ? $" · {app} {fps:0} FPS" : "";
        SensorText.Text = (err is null ? L("{0} Sensoren", _state.Sensors.Sensors.Count) : L("Sensorfehler")) + game;
        SensorChip.ToolTip = err;
        SetChip(SensorChip, SensorDot, SensorText, err is null ? 0 : 2);
    }

    private static void SetChip(System.Windows.Controls.Border chip, System.Windows.Shapes.Ellipse dot,
        System.Windows.Controls.TextBlock text, int level)
    {
        (string bg, string fg, string d) = level switch
        {
            0 => ("#1F2A22", "#9BE3AE", "#4CC970"),
            1 => ("#2A2416", "#F3D48A", "#E2B53E"),
            _ => ("#2A1C1D", "#FFB3B5", "#E5484D"),
        };
        chip.Background = (Brush)new BrushConverter().ConvertFromString(bg)!;
        text.Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!;
        dot.Fill = (Brush)new BrushConverter().ConvertFromString(d)!;
    }

    private void Release_Click(object sender, RoutedEventArgs e)
    {
        _state.Engine.SetSuspended(!_state.Engine.Suspended);
        UpdateStatus();
    }
}
