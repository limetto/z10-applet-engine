using System;
using System.Linq;
using System.Threading;
using System.Windows;
using static Z10.Core.Localization.Loc;
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
    private InputIdle? _idle;

    /// <summary>Programm neu starten (z. B. nach einem Sprachwechsel).</summary>
    public void Restart()
    {
        _restart = true;
        ExitApp();
    }

    private bool _restart;

    public AppState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { ReportCrash(args.Exception); args.Handled = true; Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportCrash(args.ExceptionObject as Exception);

        Z10.Core.Localization.Loc.Use("auto");   // vorläufig, bis die Einstellungen geladen sind
        _singleInstance = new Mutex(true, "Z10AppletEngine.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(L("Die Z10 Applet Engine läuft bereits (Symbol im Infobereich)."), "Z10 Applet Engine");
            Shutdown();
            return;
        }

        State = new AppState();
        LcdImage.SetColor(State.Settings.Lighting?.DisplayColor);
        var idle = new InputIdle();
        _idle = idle;
        State.Engine.IdleTime = () => idle.Idle(State.Settings.ScreensaverIncludeMouse);
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

    /// <summary>Unerwarteten Fehler in crash.log schreiben und anzeigen, statt kommentarlos zu verschwinden.</summary>
    private static void ReportCrash(Exception? ex)
    {
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}";
        string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Z10AppletEngine", "crash.log");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, text + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception) { }
        try
        {
            MessageBox.Show(L("Unerwarteter Fehler:") + $"\n\n{ex?.Message}\n\n" + L("Details stehen in") + $"\n{path}", "Z10 Applet Engine",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception) { }
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
        menu.Items.Add(L("Öffnen"), null, (_, _) => ShowWindow());
        menu.Items.Add(L("Nächstes Applet"), null, (_, _) => State.Engine.Next());
        _pauseItem = new Forms.ToolStripMenuItem(L("Rotation anhalten"), null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        var releaseItem = new Forms.ToolStripMenuItem(L("Display freigeben"), null, (_, _) => State.Engine.SetSuspended(!State.Engine.Suspended));
        menu.Items.Add(releaseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L("Beenden"), null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            _pauseItem.Text = State.Engine.Paused ? L("Rotation fortsetzen") : L("Rotation anhalten");
            releaseItem.Text = State.Engine.Suspended ? L("Display übernehmen") : L("Display freigeben");
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
        _idle?.Dispose();
        State.Dispose();                 // gibt das Display frei, wenn so eingestellt
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _window?.Close();
        try { _singleInstance?.ReleaseMutex(); } catch (Exception) { }
        _singleInstance?.Dispose();
        _singleInstance = null;
        if (_restart && Environment.ProcessPath is string exe)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch (Exception) { }
        }
        Shutdown();
    }
}
