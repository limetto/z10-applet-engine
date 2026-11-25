using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Z10.Core.Device;
using Z10.Core.Engine;

namespace Z10.App;

/// <summary>Konfiguration der Tastenbeleuchtung: Effekt, Helligkeit, Tastenauswahl, Schreiben ins aktive Profil.</summary>
public partial class LightingView : UserControl
{
    private const double Unit = 40;

    private static readonly (LightingEffect Effect, string Name)[] Effects =
    {
        (LightingEffect.Off, "Aus"),
        (LightingEffect.Static, "Statisch (alle Tasten)"),
        (LightingEffect.Breathing, "Atmen (alle Tasten)"),
        (LightingEffect.Customize, "Ausgewählte Tasten"),
        (LightingEffect.Trigger, "Trigger (beim Drücken)"),
        (LightingEffect.Explosion, "Explosion (beim Drücken)"),
    };

    private static readonly Brush KeyOn = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly Brush KeyOnText = Brushes.White;
    private static readonly Brush KeyOff = new SolidColorBrush(Color.FromRgb(0x24, 0x26, 0x2C));
    private static readonly Brush KeyOffText = new SolidColorBrush(Color.FromRgb(0xA0, 0xA4, 0xAD));
    private static readonly Brush KeyLine = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x46));

    private readonly AppState _state;
    private readonly LightingConfig _config;
    private readonly Dictionary<int, Border> _keys = new();
    private bool _loading;
    private bool _busy;
    private bool _readOnce;
    private bool _dirty;

    public LightingView(AppState state)
    {
        _state = state;
        _config = state.Settings.Lighting?.Clone() ?? new LightingConfig { Keys = Z10KeyLayout.AllLit.ToList() };
        InitializeComponent();

        foreach (var (_, name) in Effects) EffectBox.Items.Add(name);
        foreach (var g in Z10KeyLayout.Groups)
        {
            var b = new Button { Content = g.Name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), Tag = g };
            b.Click += Group_Click;
            GroupPanel.Children.Add(b);
        }
        var none = new Button { Content = "Keine", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6) };
        none.Click += (_, _) => SetKeys(Array.Empty<int>());
        GroupPanel.Children.Add(none);
        var invert = new Button { Content = "Umkehren", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6) };
        invert.Click += (_, _) => SetKeys(Z10KeyLayout.AllLit.Except(_config.Keys));
        GroupPanel.Children.Add(invert);

        BuildKeyboard();
        ShowConfig();
        StatusText.Text = state.Settings.Lighting is null
            ? "Noch nicht von der Tastatur gelesen."
            : "Zuletzt bearbeitete Einstellungen – mit „Von Tastatur lesen“ den aktuellen Stand holen.";
    }

    /// <summary>Beim ersten Öffnen den Stand der Tastatur lesen.</summary>
    public void OnShown()
    {
        if (_readOnce || _busy || _state.Engine.State != EngineState.Connected) return;
        _readOnce = true;
        if (!_dirty) Read_Click(this, new RoutedEventArgs());
    }

    private void BuildKeyboard()
    {
        KeyCanvas.Width = Z10KeyLayout.TotalWidth * Unit;
        KeyCanvas.Height = Z10KeyLayout.TotalHeight * Unit;
        foreach (var k in Z10KeyLayout.Keys)
        {
            var text = new TextBlock
            {
                Text = k.Label,
                FontSize = k.Label.Length > 3 ? 10 : 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var border = new Border
            {
                Width = k.W * Unit - 4,
                Height = k.H * Unit - 4,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = KeyLine,
                Background = KeyOff,
                Child = text,
                Tag = k,
                ToolTip = k.HasLed ? $"{(k.Label == "" ? "Leertaste" : k.Label)} · Position {k.Position}" : $"{k.Label} · keine LED",
                Cursor = k.HasLed ? Cursors.Hand : Cursors.Arrow,
            };
            if (!k.HasLed)
            {
                border.Background = Brushes.Transparent;
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4E, 0x56)) { };
                border.BorderThickness = new Thickness(1);
                text.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x5E, 0x66));
                border.Opacity = 0.6;
            }
            else
            {
                border.MouseLeftButtonDown += Key_Click;
                _keys[k.Position] = border;
            }
            Canvas.SetLeft(border, k.X * Unit + 2);
            Canvas.SetTop(border, k.Y * Unit + 2);
            KeyCanvas.Children.Add(border);
        }
        var eLabel = new TextBlock { Text = "E-Tasten", Foreground = KeyOffText, FontSize = 12 };
        Canvas.SetLeft(eLabel, 7 * 1.25 * Unit + 8);
        Canvas.SetTop(eLabel, Unit / 2 - 8);
        KeyCanvas.Children.Add(eLabel);
    }

    private void ShowConfig()
    {
        _loading = true;
        EffectBox.SelectedIndex = Array.FindIndex(Effects, e => e.Effect == _config.Effect);
        BrightnessSlider.Value = Math.Clamp(_config.Brightness, 1, Z10Lighting.MaxBrightness);
        _loading = false;
        Refresh();
    }

    private void Refresh()
    {
        BrightnessValue.Text = $"{_config.Brightness * 10} %";
        BrightnessSlider.IsEnabled = _config.Effect != LightingEffect.Off && !_busy;
        bool custom = _config.Effect == LightingEffect.Customize;
        var selected = _config.Keys.ToHashSet();
        foreach (var (pos, border) in _keys)
        {
            bool on = _config.Effect switch
            {
                LightingEffect.Off => false,
                LightingEffect.Customize => selected.Contains(pos),
                _ => true,
            };
            border.Background = on ? KeyOn : KeyOff;
            ((TextBlock)border.Child).Foreground = on ? KeyOnText : KeyOffText;
            // außerhalb von "Ausgewählte Tasten" die gespeicherte Auswahl andeuten
            border.BorderBrush = !custom && selected.Contains(pos) ? Brushes.White : KeyLine;
            border.Opacity = custom || _config.Effect == LightingEffect.Off ? 1 : 0.75;
        }
        CountText.Text = $"{selected.Count(p => _keys.ContainsKey(p))} von {_keys.Count} Tasten ausgewählt";
        ModeHint.Text = custom
            ? "Nur die ausgewählten Tasten leuchten."
            : "Die Auswahl wirkt nur beim Effekt „Ausgewählte Tasten“ (weiß umrandet). Ein Klick auf eine Taste wechselt dorthin.";
        ReadButton.IsEnabled = WriteButton.IsEnabled = !_busy;
        EffectBox.IsEnabled = GroupPanel.IsEnabled = KeyCanvas.IsEnabled = !_busy;
        WriteButton.Content = _dirty ? "Auf Tastatur schreiben •" : "Auf Tastatur schreiben";
    }

    private void Changed()
    {
        _dirty = true;
        _state.Settings.Lighting = _config.Clone();
        _state.SaveSettings();
        Refresh();
    }

    private void Effect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EffectBox.SelectedIndex < 0) return;
        _config.Effect = Effects[EffectBox.SelectedIndex].Effect;
        Changed();
    }

    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || BrightnessValue is null) return;
        _config.Brightness = (int)Math.Round(BrightnessSlider.Value);
        Changed();
    }

    private void Key_Click(object sender, MouseButtonEventArgs e)
    {
        if (_busy || sender is not Border { Tag: KeyDef k }) return;
        var set = _config.Keys.ToHashSet();
        if (!set.Remove(k.Position)) set.Add(k.Position);
        SetKeys(set);
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KeyGroup g }) return;
        var set = _config.Keys.ToHashSet();
        // Gruppe komplett an → aus, sonst alle der Gruppe an
        if (g.Positions.All(set.Contains)) set.ExceptWith(g.Positions);
        else set.UnionWith(g.Positions);
        SetKeys(set);
    }

    private void SetKeys(IEnumerable<int> keys)
    {
        _config.Keys = keys.Where(Z10KeyLayout.AllLit.Contains).Distinct().OrderBy(p => p).ToList();
        if (_config.Effect != LightingEffect.Customize)
        {
            _config.Effect = LightingEffect.Customize;
            _loading = true;
            EffectBox.SelectedIndex = Array.FindIndex(Effects, x => x.Effect == LightingEffect.Customize);
            _loading = false;
        }
        Changed();
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        StatusText.Text = "Lese von der Tastatur …";
        Refresh();
        try
        {
            var state = await _state.Engine.RunDeviceJob(Z10Lighting.Read, "Lese Beleuchtung …");
            ApplyState(state, "Gelesen");
            _dirty = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Lesen fehlgeschlagen: " + Describe(ex);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        var config = _config.Clone();
        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            var state = await _state.Engine.RunDeviceJob(dev => Z10Lighting.Write(dev, config, progress),
                "Beleuchtung wird\ngespeichert …");
            ApplyState(state, "Gespeichert");
            _dirty = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Schreiben fehlgeschlagen: " + Describe(ex);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void ApplyState(LightingState state, string verb)
    {
        var read = state.ToConfig();
        _config.Effect = read.Effect;
        _config.Brightness = read.Brightness;
        // Leere Maske (z. B. nach "alle Tasten") nicht übernehmen, damit die eigene Auswahl erhalten bleibt
        if (read.Keys.Count > 0 || read.Effect == LightingEffect.Customize) _config.Keys = read.Keys;
        _state.Settings.Lighting = _config.Clone();
        _state.SaveSettings();
        ShowConfig();
        StatusText.Text = $"{verb} · Profil {state.Profile} · {DateTime.Now:HH:mm:ss}";
    }

    private static string Describe(Exception ex) =>
        ex is IOException or InvalidOperationException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
}
