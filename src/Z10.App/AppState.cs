using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Z10.Core.Device;
using Z10.Core.Engine;
using Z10.Core.Model;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;
using static Z10.Core.Localization.Loc;

namespace Z10.App;

/// <summary>Gemeinsamer Zustand der Oberfläche: Applets, Einstellungen, Engine.</summary>
public sealed class AppState : IDisposable
{
    public AppStore Store { get; }
    public AppSettings Settings { get; }
    public ObservableCollection<Applet> Applets { get; }
    public SensorHub Sensors { get; }
    public LhmSensorSource Lhm { get; }
    public RtssSensorSource Rtss { get; }
    public AfterburnerSensorSource Afterburner { get; }
    public AudioSensorSource Audio { get; }
    public WindowsMediaProvider Media { get; }
    public MediaSensorSource MediaSensors { get; }
    public WpfTextRasterizer Text { get; } = new();
    public AppletEngine Engine { get; }
    public ImageLibrary Images { get; }
    public Z10KeyListener Keys { get; }

    public event Action? AppletsChanged;

    private bool _profileRead;

    /// <summary>Nach dem Verbinden einmal das aktive Profil lesen (mit etwas Abstand, damit der Start nicht stockt).</summary>
    private void OnEngineStatus()
    {
        if (Engine.State != EngineState.Connected) { _profileRead = false; return; }
        if (_profileRead) return;
        _profileRead = true;
        _ = ReadProfileLaterAsync();
    }

    private async Task ReadProfileLaterAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            try { await RefreshProfileAsync(); return; }
            catch (InvalidOperationException) { }          // gerade läuft ein anderer Gerätebefehl → später nochmal
            catch (Exception) { return; }
        }
    }

    /// <summary>Aktives Profil von der Tastatur lesen.</summary>
    public async Task<int> RefreshProfileAsync()
    {
        int p = await Engine.RunDeviceJob(Z10Lighting.ReadActiveProfile, null);
        Engine.NotifyProfile(p);
        return p;
    }

    /// <summary>Aktives Profil wechseln (1..5).</summary>
    public async Task SwitchProfileAsync(int profile)
    {
        int now = await Engine.RunDeviceJob(d => Z10Lighting.SwitchProfile(d, profile), null);
        Engine.NotifyProfile(now);
    }

    /// <summary>Das Tastaturlayout für die Beleuchtungsgrafik wurde geändert.</summary>
    public event Action? KeyboardLayoutChanged;

    /// <summary>Layout aus den Einstellungen übernehmen ("auto" = aktuelles Windows-Eingabegebietsschema).</summary>
    public void ApplyKeyboardLayout()
    {
        var id = Settings.KeyboardLayout;
        if (string.IsNullOrWhiteSpace(id) || id == "auto")
        {
            string? culture = null;
            try { culture = System.Windows.Forms.InputLanguage.CurrentInputLanguage?.Culture?.Name; } catch (Exception) { }
            id = Z10KeyLayout.FromCulture(culture ?? System.Globalization.CultureInfo.CurrentCulture.Name);
        }
        Z10KeyLayout.Use(id);
        KeyboardLayoutChanged?.Invoke();
    }

    public AppState()
    {
        Store = new AppStore();
        Settings = Store.LoadSettings();
        // Sprache vor allem anderen wählen (Standard-Applets werden in der aktiven Sprache angelegt)
        Z10.Core.Localization.Loc.UserFolder = System.IO.Path.Combine(Store.Root, "lang");
        Z10.Core.Localization.Loc.Use(Settings.Language);
        ApplyKeyboardLayout();
        var applets = Store.LoadApplets();
        // Wiedergabe-Applet für bestehende Installationen einmalig ergänzen
        if (!Settings.MediaAppletAdded)
        {
            if (DefaultApplets.AddMediaAppletOnce(applets, Settings) is Applet added) Store.SaveApplet(added);
            Store.SaveSettings(Settings);
        }
        Applets = new ObservableCollection<Applet>(applets);
        Lhm = new LhmSensorSource();
        Rtss = new RtssSensorSource { Enabled = Settings.RtssEnabled };
        Afterburner = new AfterburnerSensorSource { Enabled = Settings.AfterburnerEnabled };
        Audio = new AudioSensorSource { Enabled = Settings.AudioEnabled };
        Media = new WindowsMediaProvider { Enabled = Settings.MediaEnabled };
        MediaSensors = new MediaSensorSource(Media) { Enabled = Settings.MediaEnabled };
        Sensors = new SensorHub(Lhm, Rtss, Afterburner, Audio, MediaSensors);
        Sensors.SetCustomSensors(Settings.CustomSensors);
        Images = new ImageLibrary(System.IO.Path.Combine(Store.Root, "images"));
        Engine = new AppletEngine(Sensors, Text, () => Z10HidDevice.TryOpen(), UnleashDetector.IsRunning, Settings);
        Engine.Renderer.Images = Images;
        Engine.Renderer.Media = Media;
        Engine.SetApplets(Applets);
        Keys = new Z10KeyListener();
        Keys.KeyEvent += Engine.HandleKey;
        Keys.ProfileChanged += Engine.NotifyProfile;
        Engine.StatusChanged += OnEngineStatus;
    }

    public void SaveApplet(Applet applet)
    {
        int idx = IndexOf(applet.Id);
        if (idx < 0) Applets.Add(applet);
        else if (!ReferenceEquals(Applets[idx], applet)) Applets[idx] = applet;
        Store.SaveApplet(applet);
        Publish();
    }

    public Applet AddApplet(string name)
    {
        var a = new Applet { Name = name, Order = Applets.Count == 0 ? 0 : Applets.Max(x => x.Order) + 1 };
        a.Widgets.Add(new TextWidget { Name = "Text", X = 0, Y = 0, W = 160, H = 7, Text = name });
        SaveApplet(a);
        return a;
    }

    public Applet Duplicate(Applet src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src, JsonDefaults.Options);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Applet>(json, JsonDefaults.Options)!;
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        copy.Name = src.Name + " " + L("(Kopie)");
        copy.Order = Applets.Max(x => x.Order) + 1;
        SaveApplet(copy);
        return copy;
    }

    public void Delete(Applet applet)
    {
        Store.DeleteApplet(applet);
        int idx = IndexOf(applet.Id);
        if (idx >= 0) Applets.RemoveAt(idx);
        Publish();
    }

    public void Move(Applet applet, int delta)
    {
        int idx = IndexOf(applet.Id), target = idx + delta;
        if (idx < 0 || target < 0 || target >= Applets.Count) return;
        Applets.Move(idx, target);
        for (int i = 0; i < Applets.Count; i++)
        {
            if (Applets[i].Order == i) continue;
            Applets[i].Order = i;
            Store.SaveApplet(Applets[i]);
        }
        Publish();
    }

    public void SaveSettings()
    {
        Rtss.Enabled = Settings.RtssEnabled;
        Afterburner.Enabled = Settings.AfterburnerEnabled;
        Audio.Enabled = Settings.AudioEnabled;
        Media.Enabled = MediaSensors.Enabled = Settings.MediaEnabled;
        Sensors.SetCustomSensors(Settings.CustomSensors);
        Store.SaveSettings(Settings);
        Engine.UpdateSettings(Settings);
    }

    public Applet? Find(string id) => Applets.FirstOrDefault(a => a.Id == id);

    private int IndexOf(string id)
    {
        for (int i = 0; i < Applets.Count; i++) if (Applets[i].Id == id) return i;
        return -1;
    }

    private void Publish()
    {
        Engine.SetApplets(Applets.ToList());
        AppletsChanged?.Invoke();
    }

    public void Dispose()
    {
        Keys.Dispose();
        Engine.Dispose();
        Sensors.Dispose();
        Media.Dispose();
    }
}

/// <summary>Autostart über die Aufgabenplanung, damit beim Anmelden keine UAC-Abfrage kommt.</summary>
public static class AutoStart
{
    private const string TaskName = "Z10 Applet Engine";

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Set(bool enabled)
    {
        if (!enabled) return Run($"/Delete /TN \"{TaskName}\" /F") == 0;
        var exe = Environment.ProcessPath ?? "";
        return Run($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F") == 0;
    }

    private static int Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return -1;
            p.WaitForExit(10_000);
            return p.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
