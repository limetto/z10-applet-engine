using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Z10.Core.Model;
using Z10.Core.Rendering;
using Z10.Core.Sensors;
using Z10.Core.Storage;

namespace Z10.App;

public partial class EditorView : UserControl
{
    private readonly AppState _state;
    private readonly AppletRenderer _renderer;
    private readonly WriteableBitmap _bitmap = LcdImage.Create();
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<Widget> _layers = new();

    private Applet? _work;
    private int _scale = 5;
    private bool _dirty, _blink, _visible, _loadingPicker;
    private Widget? _dragging;
    private Point _dragOffset;
    private TextBox? _xBox, _yBox, _wBox, _hBox;

    public EditorView(AppState state)
    {
        _state = state;
        InitializeComponent();
        _renderer = new AppletRenderer(state.Text, state.Sensors, state.Engine.History);
        EditorImage.Source = _bitmap;
        Layers.ItemsSource = _layers;
        AppletPicker.ItemsSource = _state.Applets;
        _state.AppletsChanged += () => AppletPicker.Items.Refresh();
        ApplyZoom();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { _blink = !_blink; RenderPreview(); };
        _timer.Start();

        if (_state.Applets.Count > 0) Load(_state.Applets[0]);
    }

    // ---------- Laden / Speichern ----------

    public void Load(Applet applet)
    {
        if (_dirty && _work is not null && _work.Id == applet.Id)
        {
            SelectPicker(_work.Id);      // laufende Bearbeitung behalten
            return;
        }
        if (_dirty && _work is not null &&
            MessageBox.Show("Ungespeicherte Änderungen verwerfen?", "Layout-Editor", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            SelectPicker(_work.Id);
            return;
        }
        _work = Clone(applet);
        _layers.Clear();
        foreach (var w in _work.Widgets) _layers.Add(w);
        SelectPicker(applet.Id);
        SetDirty(false);
        Layers.SelectedIndex = _layers.Count > 0 ? 0 : -1;
        if (_layers.Count == 0) BuildProps(null);
        RenderPreview();
    }

    private void SelectPicker(string id)
    {
        _loadingPicker = true;
        AppletPicker.SelectedItem = _state.Find(id);
        _loadingPicker = false;
    }

    private void AppletPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPicker || AppletPicker.SelectedItem is not Applet a || a.Id == _work?.Id) return;
        Load(a);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null) return;
        _state.SaveApplet(Clone(_work));
        SelectPicker(_work.Id);
        SetDirty(false);
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || _state.Find(_work.Id) is not Applet original) return;
        SetDirty(false);
        Load(original);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyMark.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Changed()
    {
        SetDirty(true);
        RenderPreview();
    }

    private static Applet Clone(Applet a) =>
        JsonSerializer.Deserialize<Applet>(JsonSerializer.Serialize(a, JsonDefaults.Options), JsonDefaults.Options)!;

    // ---------- Live-Anzeige ----------

    public void EnterLive()
    {
        _visible = true;
        PushLive();
    }

    public void LeaveLive()
    {
        _visible = false;
        _state.Engine.SetPreview(null);
    }

    private void Live_Click(object sender, RoutedEventArgs e) => PushLive();

    private void PushLive()
    {
        if (_visible && LiveBox.IsChecked == true && _work is not null)
            _state.Engine.SetPreview(Clone(_work));   // Kopie: Engine-Thread und Editor teilen keine Objekte
        else
            _state.Engine.SetPreview(null);
    }

    // ---------- Darstellung ----------

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string t } && int.TryParse(t, out var s)) { _scale = s; ApplyZoom(); }
    }

    private void ApplyZoom()
    {
        double w = Framebuffer.DefaultWidth * _scale, h = Framebuffer.DefaultHeight * _scale;
        Surface.Width = EditorImage.Width = Overlay.Width = w;
        Surface.Height = EditorImage.Height = Overlay.Height = h;
        var guides = new[] { Guide1, Guide2, Guide3 };
        for (int i = 0; i < 3; i++)
        {
            guides[i].X1 = 0; guides[i].X2 = w;
            guides[i].Y1 = guides[i].Y2 = (i + 1) * 8 * _scale + 0.5;
        }
        UpdateSelection();
    }

    private void RenderPreview()
    {
        if (_work is null) return;
        var fb = new Framebuffer();
        _renderer.Render(_work, fb, DateTime.Now, _blink);
        LcdImage.Update(_bitmap, fb);
        UpdateSelection();
        if (_visible) PushLive();
    }

    private void UpdateSelection()
    {
        if (Layers?.SelectedItem is not Widget w)
        {
            if (Selection is not null) Selection.Visibility = Visibility.Collapsed;
            return;
        }
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, w.X * _scale - 2);
        Canvas.SetTop(Selection, w.Y * _scale - 2);
        Selection.Width = Math.Max(4, w.W * _scale + 4);
        Selection.Height = Math.Max(4, w.H * _scale + 4);
    }

    // ---------- Maus und Tastatur ----------

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Surface.Focus();
        var p = e.GetPosition(Surface);
        int px = (int)(p.X / _scale), py = (int)(p.Y / _scale);
        var hit = _layers.LastOrDefault(w => px >= w.X && px < w.X + Math.Max(1, w.W) && py >= w.Y && py < w.Y + Math.Max(1, w.H));
        Layers.SelectedItem = hit;
        if (hit is null) return;
        _dragging = hit;
        _dragOffset = new Point(px - hit.X, py - hit.Y);
        Surface.CaptureMouse();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null) return;
        var p = e.GetPosition(Surface);
        int nx = (int)Math.Floor(p.X / _scale - _dragOffset.X);
        int ny = (int)Math.Floor(p.Y / _scale - _dragOffset.Y);
        nx = Math.Clamp(nx, -_dragging.W + 1, Framebuffer.DefaultWidth - 1);
        ny = Math.Clamp(ny, -_dragging.H + 1, Framebuffer.DefaultHeight - 1);
        if (nx == _dragging.X && ny == _dragging.Y) return;
        _dragging.X = nx;
        _dragging.Y = ny;
        RefreshGeometryBoxes();
        Changed();
    }

    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = null;
        Surface.ReleaseMouseCapture();
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Surface.IsKeyboardFocused || Layers.SelectedItem is not Widget w) return;
        bool resize = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
        int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; return; }
        if (dx == 0 && dy == 0) return;
        if (resize) { w.W = Math.Max(1, w.W + dx); w.H = Math.Max(1, w.H + dy); }
        else { w.X += dx; w.Y += dy; }
        RefreshGeometryBoxes();
        Changed();
        e.Handled = true;
    }

    // ---------- Ebenen ----------

    private void Layers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BuildProps(Layers.SelectedItem as Widget);
        UpdateSelection();
    }

    private void SyncWidgets()
    {
        if (_work is null) return;
        _work.Widgets = _layers.ToList();
        Changed();
    }

    private void LayerUp_Click(object sender, RoutedEventArgs e) => MoveLayer(-1);
    private void LayerDown_Click(object sender, RoutedEventArgs e) => MoveLayer(+1);

    private void MoveLayer(int delta)
    {
        int i = Layers.SelectedIndex, t = i + delta;
        if (i < 0 || t < 0 || t >= _layers.Count) return;
        _layers.Move(i, t);
        Layers.SelectedIndex = t;
        SyncWidgets();
    }

    private void LayerDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Layers.SelectedItem is not Widget w) return;
        var copy = w.Clone();
        copy.Name = w.Name + " (Kopie)";
        copy.X = Math.Min(copy.X + 4, Framebuffer.DefaultWidth - 1);
        copy.Y = Math.Min(copy.Y + 4, Framebuffer.DefaultHeight - 1);
        _layers.Add(copy);
        Layers.SelectedItem = copy;
        SyncWidgets();
    }

    private void LayerDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        int i = Layers.SelectedIndex;
        if (i < 0) return;
        _layers.RemoveAt(i);
        Layers.SelectedIndex = Math.Min(i, _layers.Count - 1);
        SyncWidgets();
    }

    private void AddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || sender is not Button { Tag: string kind }) return;
        Widget w = kind switch
        {
            "text" => new TextWidget { Name = "Text", W = 60, H = 7, Text = "Text" },
            "sensor" => new SensorWidget { Name = "Sensorwert", W = 60, H = 7, Sensor = DefaultApplets.CpuLoad, Format = "{value:0}%" },
            "bar" => new BarWidget { Name = "Balken", W = 80, H = 7, Sensor = DefaultApplets.CpuLoad },
            "graph" => new GraphWidget { Name = "Verlauf", W = 80, H = 16, Sensor = DefaultApplets.CpuLoad, Min = 0, Max = 100 },
            "clock" => new ClockWidget { Name = "Uhr", W = 60, H = 7, Format = "HH:mm:ss" },
            _ => new IconWidget { Name = "Symbol", W = 8, H = 8, Icon = "cpu" },
        };
        _layers.Add(w);
        Layers.SelectedItem = w;
        SyncWidgets();
    }

    // ---------- Eigenschaften ----------

    private void BuildProps(Widget? w)
    {
        PropsPanel.Children.Clear();
        _xBox = _yBox = _wBox = _hBox = null;
        if (w is null)
        {
            PropsTitle.Text = "EIGENSCHAFTEN";
            PropsPanel.Children.Add(new TextBlock { Text = "Kein Baustein ausgewählt.", Style = (Style)FindResource("Hint") });
            return;
        }
        PropsTitle.Text = "EIGENSCHAFTEN · " + w.KindName.ToUpperInvariant();

        AddText("Name", w.Name, v => { w.Name = v; Layers.Items.Refresh(); });

        switch (w)
        {
            case TextWidget t:
                AddText("Text", t.Text, v => t.Text = v);
                AddFont(t.Style, s => t.Style = s);
                AddAlign(t.Align, a => t.Align = a);
                break;
            case SensorWidget s:
                AddSensor(s);
                AddText("Format", s.Format, v => s.Format = v,
                    "{value:0} → 54   {value:0.0} → 54,3   {value/1000:0.0} → MHz in GHz");
                AddFont(s.Style, st => s.Style = st);
                AddAlign(s.Align, a => s.Align = a);
                break;
            case BarWidget b:
                AddSensor(b);
                AddRow(("Minimum", () => b.Min.ToString(CultureInfo.CurrentCulture), v => { if (TryDouble(v, out var d)) b.Min = d; }),
                       ("Maximum", () => b.Max.ToString(CultureInfo.CurrentCulture), v => { if (TryDouble(v, out var d)) b.Max = d; }));
                AddCheck("Rahmen", b.Border, v => b.Border = v);
                AddCheck("Senkrecht", b.Vertical, v => b.Vertical = v);
                break;
            case GraphWidget g:
                AddSensor(g);
                AddRow(("Minimum (leer = auto)", () => g.Min?.ToString(CultureInfo.CurrentCulture) ?? "", v => g.Min = TryDouble(v, out var d) ? d : null),
                       ("Maximum (leer = auto)", () => g.Max?.ToString(CultureInfo.CurrentCulture) ?? "", v => g.Max = TryDouble(v, out var d) ? d : null));
                AddText("Zeitraum in Sekunden", g.Seconds.ToString(), v => { if (int.TryParse(v, out var i) && i > 1) g.Seconds = i; });
                AddCheck("Rahmen", g.Border, v => g.Border = v);
                AddCheck("Fläche füllen", g.Filled, v => g.Filled = v);
                break;
            case ClockWidget c:
                AddText("Format", c.Format, v => c.Format = v, "HH:mm:ss · HH:mm · ddd dd.MM. · dddd, d. MMMM yyyy");
                AddFont(c.Style, s => c.Style = s);
                AddAlign(c.Align, a => c.Align = a);
                AddCheck("Großbuchstaben", c.UpperCase, v => c.UpperCase = v);
                break;
            case IconWidget i:
                AddCombo("Symbol", Icons.BuiltIn.Keys.OrderBy(k => k), i.Icon, v => i.Icon = v, editable: true,
                    hint: "Eingebautes Symbol oder 8 Hex-Bytes, z. B. 18 3C 7E FF FF 7E 3C 18");
                break;
        }

        AddLabel("Position und Größe (Pixel)");
        var grid = new UniformGrid4();
        _xBox = grid.Add("X", w.X, v => w.X = v);
        _yBox = grid.Add("Y", w.Y, v => w.Y = v);
        _wBox = grid.Add("Breite", w.W, v => w.W = Math.Max(1, v));
        _hBox = grid.Add("Höhe", w.H, v => w.H = Math.Max(1, v));
        grid.Changed += Changed;
        PropsPanel.Children.Add(grid.Panel);

        AddCheck("Invertiert darstellen", w.Invert, v => w.Invert = v);

        if (w is ISensorWidget)
        {
            AddLabel("Warnwert");
            AddRow(("ab (≥)", () => w.Warn?.Above?.ToString(CultureInfo.CurrentCulture) ?? "", v => { EnsureWarn(w).Above = TryDouble(v, out var d) ? d : null; CleanWarn(w); }),
                   ("bis (≤)", () => w.Warn?.Below?.ToString(CultureInfo.CurrentCulture) ?? "", v => { EnsureWarn(w).Below = TryDouble(v, out var d) ? d : null; CleanWarn(w); }));
            AddCheck("Blinken statt dauerhaft invertieren", w.Warn?.Blink ?? false, v => { EnsureWarn(w).Blink = v; CleanWarn(w); });
        }
    }

    private static WarnRule EnsureWarn(Widget w) => w.Warn ??= new WarnRule();

    private static void CleanWarn(Widget w)
    {
        if (w.Warn is { Above: null, Below: null }) w.Warn = w.Warn.Blink ? w.Warn : null;
    }

    private void RefreshGeometryBoxes()
    {
        if (Layers.SelectedItem is not Widget w) return;
        void Set(TextBox? b, int v) { if (b is not null && b.Text != v.ToString()) b.Text = v.ToString(); }
        Set(_xBox, w.X); Set(_yBox, w.Y); Set(_wBox, w.W); Set(_hBox, w.H);
    }

    private void AddLabel(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Label") });

    private void AddHint(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 0) });

    private void AddText(string label, string value, Action<string> set, string? hint = null)
    {
        AddLabel(label);
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => { set(box.Text); Changed(); };
        PropsPanel.Children.Add(box);
        if (hint is not null) AddHint(hint);
    }

    private void AddRow(params (string Label, Func<string> Get, Action<string> Set)[] fields)
    {
        var grid = new Grid();
        for (int i = 0; i < fields.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var sp = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 6, 0, i == fields.Length - 1 ? 0 : 6, 0) };
            sp.Children.Add(new TextBlock { Text = fields[i].Label, Style = (Style)FindResource("Label") });
            var box = new TextBox { Text = fields[i].Get() };
            var f = fields[i];
            box.TextChanged += (_, _) => { f.Set(box.Text); Changed(); };
            sp.Children.Add(box);
            Grid.SetColumn(sp, i);
            grid.Children.Add(sp);
        }
        PropsPanel.Children.Add(grid);
    }

    private void AddCheck(string label, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = value };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Changed(); };
        PropsPanel.Children.Add(cb);
    }

    private void AddCombo(string label, IEnumerable<string> options, string value, Action<string> set, bool editable, string? hint = null)
    {
        AddLabel(label);
        var cb = new ComboBox { IsEditable = editable, ItemsSource = options.ToList(), Text = value };
        if (!editable) cb.SelectedItem = value;
        void Apply() { set(cb.Text); Changed(); }
        cb.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Apply);
        cb.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Apply()));
        PropsPanel.Children.Add(cb);
        if (hint is not null) AddHint(hint);
    }

    private void AddAlign(TextAlign value, Action<TextAlign> set)
    {
        AddLabel("Ausrichtung im Rahmen");
        var names = new[] { "Links", "Mitte", "Rechts" };
        var cb = new ComboBox { ItemsSource = names, SelectedIndex = (int)value };
        cb.SelectionChanged += (_, _) => { set((TextAlign)Math.Max(0, cb.SelectedIndex)); Changed(); };
        PropsPanel.Children.Add(cb);
    }

    private void AddFont(TextStyle style, Action<TextStyle> set)
    {
        var current = style;
        var fonts = new List<string> { "pixel" };
        fonts.AddRange(Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n));
        AddCombo("Schrift (pixel = eingebaute 5×7)", fonts, style.Font, v => { current = current with { Font = v }; set(current); }, editable: true);
        AddRow(("Größe", () => current.Size.ToString(), v => { if (int.TryParse(v, out var s) && s > 0) { current = current with { Size = s }; set(current); } }));
        AddHint("Pixelschrift: 1–4 (7, 14, 21, 28 px hoch). Windows-Schrift: Höhe in Pixeln.");
        AddCheck("Fett", style.Bold, v => { current = current with { Bold = v }; set(current); });
    }

    private void AddSensor(ISensorWidget w)
    {
        AddLabel("Sensor");
        var choices = SensorChoices();
        var cb = new ComboBox { IsEditable = true, ItemsSource = choices, IsTextSearchEnabled = false, Text = w.Sensor };
        cb.ItemTemplate = SensorTemplate();
        TextSearch.SetTextPath(cb, "Id");
        void Apply()
        {
            var text = (cb.Text ?? "").Trim();
            if (text == w.Sensor) return;
            w.Sensor = text;
            Changed();
        }
        cb.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Apply);
        cb.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Apply()));
        PropsPanel.Children.Add(cb);
        var value = _state.Sensors.GetValue(w.Sensor);
        AddHint(value is double v ? $"Aktueller Wert: {v.ToString("0.##", CultureInfo.CurrentCulture)}" : "Kein Wert – Sensor nicht gefunden?");
    }

    private List<SensorChoice> SensorChoices()
    {
        var list = new List<SensorChoice>
        {
            new(DefaultApplets.CpuTemp, "Automatisch · CPU-Temperatur"),
            new(DefaultApplets.CpuLoad, "Automatisch · CPU-Last"),
            new(DefaultApplets.CpuClock, "Automatisch · CPU-Takt"),
            new(DefaultApplets.GpuTemp, "Automatisch · GPU-Temperatur"),
            new(DefaultApplets.GpuLoad, "Automatisch · GPU-Last"),
            new(DefaultApplets.GpuVram, "Automatisch · VRAM belegt (MB)"),
            new(DefaultApplets.RamUsed, "Automatisch · RAM belegt (GB)"),
            new(DefaultApplets.RamFree, "Automatisch · RAM frei (GB)"),
            new(DefaultApplets.RamLoad, "Automatisch · RAM-Auslastung"),
        };
        foreach (var s in _state.Sensors.Sensors.OrderBy(s => s.Hardware).ThenBy(s => s.Type).ThenBy(s => s.Name))
        {
            var val = s.Value is double v ? $" = {v.ToString("0.##", CultureInfo.CurrentCulture)} {s.Unit}" : "";
            list.Add(new SensorChoice(s.Id, $"{s.Hardware} · {s.Name} ({s.Type}){val}"));
        }
        return list;
    }

    private static DataTemplate SensorTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SensorChoice.Display)));
        var id = new FrameworkElementFactory(typeof(TextBlock));
        id.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SensorChoice.Id)));
        id.SetValue(TextBlock.FontSizeProperty, 10.0);
        id.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        factory.AppendChild(name);
        factory.AppendChild(id);
        return new DataTemplate { VisualTree = factory };
    }

    private static bool TryDouble(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out d)
        || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    public sealed record SensorChoice(string Id, string Display)
    {
        public override string ToString() => Id;
    }

    /// <summary>Vier Zahlenfelder nebeneinander (X, Y, Breite, Höhe).</summary>
    private sealed class UniformGrid4
    {
        public Grid Panel { get; } = new();
        public event Action? Changed;

        public TextBox Add(string label, int value, Action<int> set)
        {
            int col = Panel.ColumnDefinitions.Count;
            Panel.ColumnDefinitions.Add(new ColumnDefinition());
            var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 4, 0, 0, 0) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 2) });
            var box = new TextBox { Text = value.ToString() };
            box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out var v)) { set(v); Changed?.Invoke(); } };
            sp.Children.Add(box);
            Grid.SetColumn(sp, col);
            Panel.Children.Add(sp);
            return box;
        }
    }
}
