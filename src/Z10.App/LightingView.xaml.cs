using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Z10.Core.Device;
using Z10.Core.Engine;
using static Z10.Core.Localization.Loc;

namespace Z10.App;

/// <summary>Konfiguration der Tastenbeleuchtung: Effekt, Helligkeit, Tastenauswahl, Schreiben ins aktive Profil.</summary>
public partial class LightingView : UserControl
{
    private const double Unit = 40;

    private static readonly (LightingEffect Effect, string Name)[] Effects =
    {
        (LightingEffect.Off, L("Aus")),
        (LightingEffect.Static, L("Statisch (alle Tasten)")),
        (LightingEffect.Breathing, L("Atmen (alle Tasten)")),
        (LightingEffect.Customize, L("Ausgewählte Tasten")),
        (LightingEffect.Trigger, L("Trigger (beim Drücken)")),
        (LightingEffect.Explosion, L("Explosion (beim Drücken)")),
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
    private bool _dirty;
    private readonly DispatcherTimer _previewTimer;
    private string? _pendingPreview;
    private bool _dragging;

    private static readonly (string Name, string Hex)[] Presets =
    {
        ("Rot", "#FF0000"), ("Orange", "#FF7F00"), ("Gelb", "#FFFF00"), ("Grün", "#00FF00"), ("Türkis", "#00FFFF"),
        ("Blau", "#0000FF"), ("Violett", "#8000FF"), ("Pink", "#FF00FF"), ("Weiß", "#FFFFFF"),
    };

    public LightingView(AppState state)
    {
        _state = state;
        _config = state.Settings.Lighting?.Clone() ?? new LightingConfig { Keys = Z10KeyLayout.AllLit.ToList() };
        // Der Slider meldet schon in InitializeComponent eine Wertänderung (Minimum 1), da gibt es die
        // übrigen Steuerelemente noch nicht → bis zum Ende des Konstruktors keine Änderungen verarbeiten.
        _loading = true;
        InitializeComponent();
        UiTranslator.Apply(this);

        foreach (var (_, name) in Effects) EffectBox.Items.Add(name);
        BuildGroupButtons();
        state.Engine.ProfileChanged += p => Dispatcher.BeginInvoke(() =>
        {
            if (_busy || p == _shownProfile) return;
            StatusText.Text = L("Profil {0} ist jetzt aktiv – „Von Tastatur lesen“ holt seine Einstellungen.", p);
        });
        state.KeyboardLayoutChanged += () => Dispatcher.BeginInvoke(() =>
        {
            KeyCanvas.Children.Clear();
            _keys.Clear();
            BuildGroupButtons();
            BuildKeyboard();
            Refresh();
        });

        foreach (var (name, hex) in Presets)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new Button
            {
                Width = 30, Height = 30, MinHeight = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(c), ToolTip = $"{L(name)} ({hex})", Tag = hex,
            };
            b.Click += (_, _) => SetColor(hex);
            PresetPanel.Children.Add(b);
        }
        // Jede Vorschau landet dauerhaft im Speicher der Tastatur (am Gerät geprüft) → nicht beim Ziehen senden,
        // sondern erst nach dem Loslassen bzw. nach einer kurzen Pause.
        foreach (var slider in new[] { RSlider, GSlider, BSlider })
        {
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
            {
                _dragging = false;
                if (_config.DisplayColor is not null) SchedulePreview(_config.DisplayColor);
            }));
        }
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _previewTimer.Tick += (_, _) => RunPreview();

        BuildKeyboard();
        _loading = false;
        ShowConfig();
        StatusText.Text = state.Settings.Lighting is null
            ? L("Noch nicht von der Tastatur gelesen.")
            : L("Zuletzt bearbeitete Einstellungen – mit „Von Tastatur lesen“ den aktuellen Stand holen.");
    }

    /// <summary>
    /// Früher wurde beim ersten Öffnen automatisch gelesen. Seit sich die Tastatur dabei einmal aufgehängt hat,
    /// wird nur noch auf Knopfdruck gelesen.
    /// </summary>
    public void OnShown() { }

    private void BuildGroupButtons()
    {
        GroupPanel.Children.Clear();
        foreach (var g in Z10KeyLayout.Groups)
        {
            var b = new Button { Content = g.Name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), Tag = g };
            b.Click += Group_Click;
            GroupPanel.Children.Add(b);
        }
        var none = new Button { Content = L("Keine"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6) };
        none.Click += (_, _) => SetKeys(Array.Empty<int>());
        GroupPanel.Children.Add(none);
        var invert = new Button { Content = L("Umkehren"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6) };
        invert.Click += (_, _) => SetKeys(Z10KeyLayout.AllLit.Except(_config.Keys));
        GroupPanel.Children.Add(invert);
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
                ToolTip = k.HasLed ? $"{(k.Label == "" ? L("Leertaste") : k.Label)} · " + L("Position {0}", k.Position) : $"{k.Label} · " + L("keine LED"),
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
        var eLabel = new TextBlock { Text = L("E-Tasten"), Foreground = KeyOffText, FontSize = 12 };
        Canvas.SetLeft(eLabel, 7 * 1.25 * Unit + 8);
        Canvas.SetTop(eLabel, Unit / 2 - 8);
        KeyCanvas.Children.Add(eLabel);
    }

    private void ShowConfig()
    {
        _loading = true;
        EffectBox.SelectedIndex = Array.FindIndex(Effects, e => e.Effect == _config.Effect);
        BrightnessSlider.Value = Math.Clamp(_config.Brightness, 1, Z10Lighting.MaxBrightness);
        ShowColor();
        AllProfilesBox.IsChecked = _config.ColorAllProfiles;
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
        CountText.Text = L("{0} von {1} Tasten ausgewählt", selected.Count(p => _keys.ContainsKey(p)), _keys.Count);
        ModeHint.Text = custom
            ? L("Nur die ausgewählten Tasten leuchten.")
            : L("Die Auswahl wirkt nur beim Effekt „Ausgewählte Tasten“ (weiß umrandet). Ein Klick auf eine Taste wechselt dorthin.");
        ReadButton.IsEnabled = WriteButton.IsEnabled = !_busy;
        EffectBox.IsEnabled = GroupPanel.IsEnabled = KeyCanvas.IsEnabled = !_busy;
        RSlider.IsEnabled = GSlider.IsEnabled = BSlider.IsEnabled = HexBox.IsEnabled = PresetPanel.IsEnabled = !_busy;
        WriteButton.Content = L("Auf Tastatur schreiben") + (_dirty ? " •" : "");
    }

    private void ShowColor()
    {
        bool wasLoading = _loading;
        _loading = true;
        if (Z10Lighting.TryParseColor(_config.DisplayColor, out var rgb))
        {
            RSlider.Value = rgb[0]; GSlider.Value = rgb[1]; BSlider.Value = rgb[2];
            HexBox.Text = _config.DisplayColor!.ToUpperInvariant();
            var c = Color.FromRgb(rgb[0], rgb[1], rgb[2]);
            ColorSwatch.Background = new SolidColorBrush(c);
            ColorSwatchText.Text = "Z10";
        }
        else
        {
            HexBox.Text = "";
            ColorSwatch.Background = Brushes.Transparent;
            ColorSwatchText.Text = "?";
        }
        RValue.Text = ((int)RSlider.Value).ToString();
        GValue.Text = ((int)GSlider.Value).ToString();
        BValue.Text = ((int)BSlider.Value).ToString();
        _loading = wasLoading;
    }

    private void SetColor(string hex)
    {
        if (!Z10Lighting.TryParseColor(hex, out var rgb)) { ShowColor(); return; }
        _config.DisplayColor = $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
        LcdImage.SetColor(_config.DisplayColor);
        ShowColor();
        Changed();
        if (!_dragging) SchedulePreview(_config.DisplayColor);
    }

    private void Rgb_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || BValue is null) return;
        SetColor($"#{(int)RSlider.Value:X2}{(int)GSlider.Value:X2}{(int)BSlider.Value:X2}");
    }

    private void Hex_Commit(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (!string.Equals(text, _config.DisplayColor, StringComparison.OrdinalIgnoreCase)) SetColor(text);
    }

    private void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Hex_Commit(sender, e);
    }

    private void AllProfiles_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ColorAllProfiles = AllProfilesBox.IsChecked == true;
        Changed();
    }

    /// <summary>Vorschau: Farbe ohne die komplette Speichersequenz an die Tastatur (nach 0,6 s Ruhe bzw. nach dem Loslassen).</summary>
    private void SchedulePreview(string color)
    {
        if (_state.Engine.State != EngineState.Connected) return;
        _pendingPreview = color;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async void RunPreview()
    {
        _previewTimer.Stop();
        if (_pendingPreview is null) return;
        if (_busy || _state.Engine.DeviceBusy) { _previewTimer.Start(); return; }   // später nochmal
        var color = _pendingPreview;
        _pendingPreview = null;
        try
        {
            await _state.Engine.RunDeviceJob(dev => { Z10Lighting.PreviewColor(dev, color); return true; }, null);
        }
        catch (InvalidOperationException) { _pendingPreview ??= color; _previewTimer.Start(); }
        catch (Exception ex) { StatusText.Text = L("Vorschau fehlgeschlagen:") + " " + Describe(ex); }
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
        StatusText.Text = L("Lese von der Tastatur …");
        Refresh();
        try
        {
            var state = await _state.Engine.RunDeviceJob(Z10Lighting.Read, L("Lese Beleuchtung …"));
            ApplyState(state, L("Gelesen"));
            _dirty = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = L("Lesen fehlgeschlagen:") + " " + Describe(ex);
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
                L("Beleuchtung wird\ngespeichert …"));
            ApplyState(state, L("Gespeichert"));
            _dirty = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = L("Schreiben fehlgeschlagen:") + " " + Describe(ex);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private int? _shownProfile;

    private void ApplyState(LightingState state, string verb)
    {
        _shownProfile = state.Profile;
        _state.Engine.NotifyProfile(state.Profile);
        var read = state.ToConfig();
        _config.Effect = read.Effect;
        _config.Brightness = read.Brightness;
        // Leere Maske (z. B. nach "alle Tasten") nicht übernehmen, damit die eigene Auswahl erhalten bleibt
        if (read.Keys.Count > 0 || read.Effect == LightingEffect.Customize) _config.Keys = read.Keys;
        _config.DisplayColor = read.DisplayColor;
        LcdImage.SetColor(_config.DisplayColor);
        _state.Settings.Lighting = _config.Clone();
        _state.SaveSettings();
        ShowConfig();
        StatusText.Text = $"{verb} · " + L("Profil {0}", state.Profile) + $" · {DateTime.Now:HH:mm:ss}";
    }

    private static string Describe(Exception ex) =>
        ex is IOException or InvalidOperationException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
}
