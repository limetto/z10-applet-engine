using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Z10.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _pauseItem;
    private MainWindow? _window;
    private bool _exiting;

    public AppState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "Z10AppletEngine.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Die Z10 Applet Engine läuft bereits (Symbol im Infobereich).", "Z10 Applet Engine");
            Shutdown();
            return;
        }

        State = new AppState();
        State.Engine.Start();

        _window = new MainWindow(State);
        _window.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;          // Schließen = in den Infobereich
            _window.Hide();
        };

        CreateTray();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        bool minimized = e.Args.Contains("--minimized") || (State.Settings.StartMinimized && e.Args.Contains("--autostart"));
        if (minimized) return;
        if (State.Settings.BootAnimation)
        {
            var splash = new SplashWindow();
            splash.Finished += () => _window?.Show();
            splash.Show();
        }
        else
        {
            _window.Show();
        }
    }

    // Nach Ruhezustand/Energiesparen kann die Tastatur ihren Displayspeicher verloren haben: alles neu senden.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) State.Engine.InvalidateDisplay();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            State.Engine.InvalidateDisplay();
    }

    private void CreateTray()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
        _tray = new Forms.NotifyIcon
        {
            Text = "Z10 Applet Engine",
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream),
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowWindow());
        menu.Items.Add("Nächstes Applet", null, (_, _) => State.Engine.Next());
        _pauseItem = new Forms.ToolStripMenuItem("Rotation anhalten", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        var releaseItem = new Forms.ToolStripMenuItem("Display freigeben", null, (_, _) => State.Engine.SetSuspended(!State.Engine.Suspended));
        menu.Items.Add(releaseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            _pauseItem.Text = State.Engine.Paused ? "Rotation fortsetzen" : "Rotation anhalten";
            releaseItem.Text = State.Engine.Suspended ? "Display übernehmen" : "Display freigeben";
        };
        _tray.ContextMenuStrip = menu;
    }

    private void TogglePause()
    {
        State.Engine.Paused = !State.Engine.Paused;
        State.SaveSettings();
    }

    public void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        State.Engine.SetPreview(null);
        State.Dispose();                 // gibt das Display frei, wenn so eingestellt
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _window?.Close();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }
}
