using System;
using System.Windows;
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
    private readonly DispatcherTimer _statusTimer;

    public MainWindow(AppState state)
    {
        _state = state;
        InitializeComponent();

        _applets = new AppletsView(state);
        _editor = new EditorView(state);
        _device = new DeviceView(state);
        _applets.EditRequested += OpenEditor;
        ContentHost.Children.Add(_applets);
        ContentHost.Children.Add(_editor);
        ContentHost.Children.Add(_device);
        ShowView(_applets);

        _state.Engine.StatusChanged += () => Dispatcher.BeginInvoke(UpdateStatus);
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
        else ShowView(_device);
    }

    private void ShowView(UIElement view)
    {
        foreach (UIElement child in ContentHost.Children)
            child.Visibility = child == view ? Visibility.Visible : Visibility.Collapsed;
        if (view != _editor) _editor.LeaveLive();
        else _editor.EnterLive();
    }

    private void UpdateStatus()
    {
        var engine = _state.Engine;
        DeviceText.Text = engine.Suspended ? "Display freigegeben" : engine.StatusText;
        ReleaseButton.Content = engine.Suspended ? "Display übernehmen" : "Display freigeben";
        bool ok = engine.State == EngineState.Connected && !engine.Suspended;
        bool warn = engine.State == EngineState.Conflict || engine.Suspended;
        SetChip(DeviceChip, DeviceDot, DeviceText, ok ? 0 : warn ? 1 : 2);

        var err = _state.Sensors.Error;
        var game = _state.Rtss.ActiveApp is string app && _state.Sensors.GetValue(RtssSensorSource.FpsId) is double fps
            ? $" · {app} {fps:0} FPS" : "";
        SensorText.Text = (err is null ? $"{_state.Sensors.Sensors.Count} Sensoren" : "Sensorfehler") + game;
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
