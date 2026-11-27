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
using static Z10.Core.Localization.Loc;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Z10.Core.Media;
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
    private TextBlock? _valueHint;
    private ISensorWidget? _valueWidget;

    public EditorView(AppState state)
    {
        _state = state;
        InitializeComponent();
        UiTranslator.Apply(this);
        _renderer = new AppletRenderer(state.Text, state.Sensors, state.Engine.History) { Images = state.Images, Media = state.Media };
        EditorImage.Source = _bitmap;
        Layers.ItemsSource = _layers;
        AppletPicker.ItemsSource = _state.Applets;
        _state.AppletsChanged += () => AppletPicker.Items.Refresh();
        ApplyZoom();

        // 100 ms, damit animierte Grafiken in der Vorschau laufen; Blinken und Wertanzeige jede Sekunde
        LcdImage.ColorChanged += () => Dispatcher.BeginInvoke(() => RenderPreview(pushLive: false));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        int ticks = 0;
        _timer.Tick += (_, _) =>
        {
            bool second = ++ticks % 10 == 0;
            if (second) { _blink = !_blink; UpdateValueHint(); }
            if (second || (_work is not null && _renderer.NeedsFastRefresh(_work, DateTime.Now))) RenderPreview(pushLive: second);
        };
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
            MessageBox.Show(L("Ungespeicherte Änderungen verwerfen?"), L("Layout-Editor"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            SelectPicker(_work.Id);
            return;
        }
        _work = Clone(applet);
        _layers.Clear();
        foreach (var w in _work.Widgets) _layers.Add(w);
        SelectPicker(applet.Id);
        _state.Engine.TrackSensors(_work.SensorIds);
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
        if (_work is not null) _state.Engine.TrackSensors(_work.SensorIds);
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

    private void RenderPreview() => RenderPreview(pushLive: true);

    private void RenderPreview(bool pushLive)
    {
        if (_work is null) return;
        var fb = new Framebuffer();
        _renderer.Render(_work, fb, DateTime.Now, _blink);
        LcdImage.Update(_bitmap, fb);
        UpdateSelection();
        if (_visible && pushLive) PushLive();
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
        // Sonst holt sich der umgebende ScrollViewer beim Weiterreichen des Klicks den Fokus
        // und die Pfeiltasten scrollen statt zu verschieben.
        e.Handled = true;
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
        if (Layers.SelectedItem is not Widget w) return;
        // In Eingabefeldern und Auswahllisten behalten die Pfeiltasten ihre normale Funktion
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or ComboBoxItem or Slider or PasswordBox) return;
        if (Keyboard.FocusedElement is DependencyObject d && IsInside(d, PropsPanel)) return;
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

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var cur = d; cur is not null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, container)) return true;
        return false;
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
        copy.Name = w.Name + " " + L("(Kopie)");
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
            "text" => new TextWidget { Name = L("Text"), W = 60, H = 7, Text = L("Text") },
            "sensor" => new SensorWidget { Name = L("Sensorwert"), W = 60, H = 7, Sensor = DefaultApplets.CpuLoad, Format = "{value:0}%" },
            "bar" => new BarWidget { Name = L("Balken"), W = 80, H = 7, Sensor = DefaultApplets.CpuLoad },
            "graph" => new GraphWidget { Name = L("Verlauf"), W = 80, H = 16, Sensor = DefaultApplets.CpuLoad, Min = 0, Max = 100 },
            "clock" => new ClockWidget { Name = L("Uhr"), W = 60, H = 7, Format = "HH:mm:ss" },
            "media" => new MediaWidget { Name = L("Medien"), W = 100, H = 7, Field = MediaField.ArtistTitle },
            "line" => new ShapeWidget { Name = L("Linie"), Shape = ShapeKind.Line, W = 40, H = 1 },
            "rect" => new ShapeWidget { Name = L("Rechteck"), Shape = ShapeKind.Rectangle, W = 30, H = 12 },
            "ellipse" => new ShapeWidget { Name = L("Oval"), Shape = ShapeKind.Ellipse, W = 16, H = 16 },
            _ => new IconWidget { Name = L("Symbol"), W = 8, H = 8, Icon = "cpu" },
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
        _valueHint = null;
        _valueWidget = null;
        if (w is null)
        {
            PropsTitle.Text = L("EIGENSCHAFTEN");
            PropsPanel.Children.Add(new TextBlock { Text = "Kein Baustein ausgewählt.", Style = (Style)FindResource("Hint") });
            return;
        }
        PropsTitle.Text = L("EIGENSCHAFTEN") + " · " + L(w.KindName).ToUpperInvariant();

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
                AddIconPicker(i);
                break;
            case ShapeWidget sh:
                AddShapeProps(sh);
                break;
            case MediaWidget m:
                AddMediaProps(m);
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

        AddLabel("Überlagerung mit Bausteinen darunter");
        var blend = new ComboBox
        {
            ItemsSource = new[] { L("Normal (Pixel setzen)"), L("Invertieren (XOR, z. B. Schrift auf Balken)"), L("Ausstanzen (Pixel löschen)") },
            SelectedIndex = (int)w.Blend,
        };
        blend.SelectionChanged += (_, _) => { w.Blend = (BlendMode)Math.Max(0, blend.SelectedIndex); Changed(); };
        PropsPanel.Children.Add(blend);
        AddHint("Wirkt auf Bausteine, die in der Ebenenliste weiter oben stehen. Der Baustein muss also unter dem Balken stehen.");

        AddCheck("Ganzen Bereich invertieren", w.Invert, v => w.Invert = v);

        if (w is ISensorWidget)
        {
            AddLabel("Warnwert");
            AddRow(("ab (≥)", () => w.Warn?.Above?.ToString(CultureInfo.CurrentCulture) ?? "", v => { EnsureWarn(w).Above = TryDouble(v, out var d) ? d : null; CleanWarn(w); }),
                   ("bis (≤)", () => w.Warn?.Below?.ToString(CultureInfo.CurrentCulture) ?? "", v => { EnsureWarn(w).Below = TryDouble(v, out var d) ? d : null; CleanWarn(w); }));
            AddCheck("Blinken statt dauerhaft invertieren", w.Warn?.Blink ?? false, v => { EnsureWarn(w).Blink = v; CleanWarn(w); });
        }
    }

    private static readonly (MediaField Field, string Label)[] MediaFields =
    {
        (MediaField.Title, "Titel"),
        (MediaField.Artist, "Interpret"),
        (MediaField.ArtistTitle, "Interpret – Titel"),
        (MediaField.Album, "Album"),
        (MediaField.App, "Programm (Spotify, Edge …)"),
        (MediaField.Time, "Zeit (1:23 / 3:45)"),
        (MediaField.Position, "Position (1:23)"),
        (MediaField.Duration, "Dauer (3:45)"),
        (MediaField.Remaining, "Restzeit (-2:22)"),
    };

    private void AddMediaProps(MediaWidget m)
    {
        AddLabel("Anzeige");
        var field = new ComboBox
        {
            ItemsSource = MediaFields.Select(f => L(f.Label)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(MediaFields, f => f.Field == m.Field)),
        };
        field.SelectionChanged += (_, _) => { if (field.SelectedIndex >= 0) m.Field = MediaFields[field.SelectedIndex].Field; Changed(); };
        PropsPanel.Children.Add(field);
        AddText("Text, wenn nichts läuft", m.Placeholder, v => m.Placeholder = v);
        AddFont(m.Style, s => m.Style = s);
        AddAlign(m.Align, a => m.Align = a);
        AddCheck("Lauftext, wenn der Text nicht passt", m.Scroll, v => m.Scroll = v);
        AddRow(("Geschwindigkeit (Pixel/s)", () => m.ScrollSpeed.ToString(), v => { if (int.TryParse(v, out var i) && i >= 1) m.ScrollSpeed = Math.Min(i, 200); }));
        AddHint("Quelle ist die Medienwiedergabe von Windows (Spotify, Browser, Media Player …). Für einen Fortschrittsbalken einen Balken mit dem Sensor „Medien · Fortschritt“ verwenden.");
    }

    private void AddShapeProps(ShapeWidget sh)
    {
        AddLabel("Form");
        var shape = new ComboBox { ItemsSource = new[] { L("Linie"), L("Rechteck"), L("Oval") }, SelectedIndex = (int)sh.Shape };
        shape.SelectionChanged += (_, _) =>
        {
            sh.Shape = (ShapeKind)Math.Max(0, shape.SelectedIndex);
            Changed();
            Layers.Items.Refresh();
            Dispatcher.BeginInvoke(() => BuildProps(sh));   // passende Felder für die neue Form
        };
        PropsPanel.Children.Add(shape);

        if (sh.Shape == ShapeKind.Line)
        {
            AddLabel("Richtung");
            var dir = new ComboBox
            {
                ItemsSource = new[] { L("↘ links oben nach rechts unten"), L("↗ links unten nach rechts oben") },
                SelectedIndex = (int)sh.Direction,
            };
            dir.SelectionChanged += (_, _) => { sh.Direction = (LineDirection)Math.Max(0, dir.SelectedIndex); Changed(); };
            PropsPanel.Children.Add(dir);
            AddHint("Die Linie verläuft diagonal durch den Rahmen. Höhe 1 = waagerecht, Breite 1 = senkrecht.");
            AddRow(("Stärke", () => sh.Thickness.ToString(), v => { if (int.TryParse(v, out var i) && i >= 1) sh.Thickness = Math.Min(i, 16); }));
        }
        else
        {
            AddCheck("Gefüllt", sh.Filled, v => sh.Filled = v);
            if (sh.Shape == ShapeKind.Rectangle)
                AddRow(("Randstärke", () => sh.Thickness.ToString(), v => { if (int.TryParse(v, out var i) && i >= 1) sh.Thickness = Math.Min(i, 16); }),
                       ("Eckenradius", () => sh.Radius.ToString(), v => { if (int.TryParse(v, out var i) && i >= 0) sh.Radius = Math.Min(i, 16); }));
            else
                AddRow(("Randstärke", () => sh.Thickness.ToString(), v => { if (int.TryParse(v, out var i) && i >= 1) sh.Thickness = Math.Min(i, 16); }));
            if (sh.Shape == ShapeKind.Ellipse) AddHint("Gleiche Breite und Höhe ergibt einen Kreis.");
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
        PropsPanel.Children.Add(new TextBlock { Text = L(text), Style = (Style)FindResource("Label") });

    private void AddHint(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = L(text), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 0) });

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
            sp.Children.Add(new TextBlock { Text = L(fields[i].Label), Style = (Style)FindResource("Label") });
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
        var cb = new CheckBox { Content = L(label), IsChecked = value };
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

    // ---------- Symbolauswahl mit Vorschau ----------

    public sealed record IconChoice(string Spec, string Label, ImageSource Preview);

    private List<IconChoice> IconChoices(string current)
    {
        var list = new List<IconChoice>();
        foreach (var (category, names) in Icons.Categories)
            foreach (var n in names)
                list.Add(new IconChoice(n, $"{L(category)} · {L(n)}", LcdImage.ToImage(Icons.Get(n))));
        foreach (var a in _state.Images.All)
            list.Add(new IconChoice(Icons.ImagePrefix + a.Name,
                L("Eigene Grafik") + $" · {a.Name} ({a.Width}×{a.Height}{(a.IsAnimated ? ", " + L("{0} Bilder", a.Frames.Count) : "")})",
                LcdImage.ToImage(a.Frame(0))));
        if (!string.IsNullOrWhiteSpace(current) && list.All(c => !c.Spec.Equals(current, StringComparison.OrdinalIgnoreCase)))
            list.Insert(0, new IconChoice(current, L("Eigene Hex-Zeilen"), LcdImage.ToImage(Icons.Get(current, _state.Images))));
        return list;
    }

    private void AddIconPicker(IconWidget w)
    {
        AddLabel("Symbol");
        var choices = IconChoices(w.Icon);
        var cb = new ComboBox { ItemsSource = choices, MaxDropDownHeight = 420 };
        cb.ItemTemplate = IconTemplate();
        cb.SelectedItem = choices.FirstOrDefault(c => c.Spec.Equals(w.Icon, StringComparison.OrdinalIgnoreCase));
        PropsPanel.Children.Add(cb);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var import = new Button { Content = L("Grafik importieren …"), Margin = new Thickness(0, 0, 6, 6) };
        var delete = new Button { Content = L("Grafik löschen"), Margin = new Thickness(0, 0, 6, 6),
                                  IsEnabled = w.Icon.StartsWith(Icons.ImagePrefix, StringComparison.OrdinalIgnoreCase) };
        buttons.Children.Add(import);
        buttons.Children.Add(delete);
        PropsPanel.Children.Add(buttons);
        AddHint("Eigene Bilder und animierte GIFs werden beim Import auf 1 Bit umgerechnet. Animationen laufen auf der Tastatur mit bis zu 10 Bildern pro Sekunde.");

        AddLabel("Eigene Hex-Zeilen (für Fortgeschrittene)");
        var hex = new TextBox { Text = Icons.IsBuiltIn(w.Icon) || w.Icon.StartsWith(Icons.ImagePrefix, StringComparison.OrdinalIgnoreCase) ? "" : w.Icon,
                                FontFamily = new FontFamily("Consolas") };
        PropsPanel.Children.Add(hex);
        AddHint("Je Zeile 2 Hex-Ziffern pro 8 Pixel, z. B. 18 3C 7E FF FF 7E 3C 18");

        void Apply(string spec)
        {
            w.Icon = spec;
            var bmp = Icons.Get(spec, _state.Images);
            w.W = bmp.Width;
            w.H = bmp.Height;
            delete.IsEnabled = spec.StartsWith(Icons.ImagePrefix, StringComparison.OrdinalIgnoreCase);
            RefreshGeometryBoxes();
            Changed();
        }

        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is IconChoice c && c.Spec != w.Icon) Apply(c.Spec); };
        hex.TextChanged += (_, _) => { if (hex.Text.Trim().Length > 0) Apply(hex.Text.Trim()); };
        import.Click += (_, _) =>
        {
            var dlg = new ImportImageWindow(_state.Images) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.ImportedName is string name)
            {
                Apply(Icons.ImagePrefix + name);
                BuildProps(w);
            }
        };
        delete.Click += (_, _) =>
        {
            if (!w.Icon.StartsWith(Icons.ImagePrefix, StringComparison.OrdinalIgnoreCase)) return;
            var name = w.Icon[Icons.ImagePrefix.Length..];
            if (MessageBox.Show(L("Grafik „{0}“ löschen? Applets, die sie verwenden, zeigen dann ein Ersatzsymbol.", name),
                    L("Grafik löschen"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _state.Images.Delete(name);
            Apply("cpu");
            BuildProps(w);
        };
    }

    private static DataTemplate IconTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x4A, 0x0B, 0x0D)));
        border.SetValue(Border.PaddingProperty, new Thickness(3));
        border.SetValue(Border.MarginProperty, new Thickness(0, 1, 8, 1));
        var img = new FrameworkElementFactory(typeof(Image));
        img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(IconChoice.Preview)));
        img.SetValue(FrameworkElement.WidthProperty, 32.0);
        img.SetValue(FrameworkElement.HeightProperty, 32.0);
        img.SetValue(Image.StretchProperty, Stretch.Uniform);
        img.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
        border.AppendChild(img);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(IconChoice.Label)));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(border);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private void AddAlign(TextAlign value, Action<TextAlign> set)
    {
        AddLabel("Ausrichtung im Rahmen");
        var names = new[] { L("Links"), L("Mitte"), L("Rechts") };
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
            UpdateValueHint();
        }
        cb.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Apply);
        cb.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Apply()));
        PropsPanel.Children.Add(cb);

        // Aktueller Wert, wird jede Sekunde neu angezeigt
        _valueWidget = w;
        _valueHint = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 0) };
        PropsPanel.Children.Add(_valueHint);
        UpdateValueHint();

        // Glättung
        AddLabel("Glättung (gegen stark schwankende Werte)");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mode = new ComboBox { ItemsSource = new[] { L("Aus"), L("Mittelwert"), L("Median (gegen Ausreißer)") }, SelectedIndex = (int)w.Smoothing };
        var secs = new TextBox { Text = w.SmoothingSeconds.ToString(), Margin = new Thickness(6, 0, 0, 0), IsEnabled = w.Smoothing != SmoothingMode.None };
        var unit = new TextBlock { Text = " Sekunden", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("MutedBrush") };
        Grid.SetColumn(secs, 1);
        Grid.SetColumn(unit, 2);
        row.Children.Add(mode);
        row.Children.Add(secs);
        row.Children.Add(unit);
        mode.SelectionChanged += (_, _) =>
        {
            w.Smoothing = (SmoothingMode)Math.Max(0, mode.SelectedIndex);
            secs.IsEnabled = w.Smoothing != SmoothingMode.None;
            Changed();
            UpdateValueHint();
        };
        secs.TextChanged += (_, _) =>
        {
            if (int.TryParse(secs.Text, out var n) && n >= 2 && n <= 600) { w.SmoothingSeconds = n; Changed(); UpdateValueHint(); }
        };
        PropsPanel.Children.Add(row);
        AddHint("Mittelwert bzw. Median über die eingestellten Sekunden. Warnwerte nutzen den geglätteten Wert.");
    }

    private void UpdateValueHint()
    {
        if (_valueHint is null || _valueWidget is not ISensorWidget w) return;
        var info = _state.Sensors.Find(w.Sensor);
        var raw = _state.Sensors.GetValue(w.Sensor);
        if (raw is not double v)
        {
            _valueHint.Text = info is null ? L("Kein Wert – Sensor nicht gefunden?") : $"{info.Hardware} · {info.Name}: " + L("zurzeit kein Wert");
            return;
        }
        var c = CultureInfo.CurrentCulture;
        string unit = info?.Unit is { Length: > 0 } u ? " " + u : "";
        var text = L("Aktuell") + $": {v.ToString("0.##", c)}{unit}";
        if (w.Smoothing != SmoothingMode.None && _renderer.ValueOf(w) is double sm)
            text += " · " + L("geglättet") + $": {sm.ToString("0.##", c)}{unit}";
        if (info is not null) text += $"{Environment.NewLine}{info.Hardware} · {info.Name}";
        _valueHint.Text = text;
    }

    private List<SensorChoice> SensorChoices()
    {
        var list = new List<SensorChoice>
        {
            new(DefaultApplets.CpuTemp, L("Automatisch · CPU-Temperatur")),
            new(DefaultApplets.CpuLoad, L("Automatisch · CPU-Last")),
            new(DefaultApplets.CpuClock, L("Automatisch · CPU-Takt")),
            new(DefaultApplets.GpuTemp, L("Automatisch · GPU-Temperatur")),
            new(DefaultApplets.GpuLoad, L("Automatisch · GPU-Last")),
            new(DefaultApplets.GpuVram, L("Automatisch · VRAM belegt (MB)")),
            new(DefaultApplets.RamUsed, L("Automatisch · RAM belegt (GB)")),
            new(DefaultApplets.RamFree, L("Automatisch · RAM frei (GB)")),
            new(DefaultApplets.RamLoad, L("Automatisch · RAM-Auslastung")),
            new(RtssSensorSource.FpsId, L("RTSS · Framerate (FPS)")),
            new(RtssSensorSource.FrametimeId, L("RTSS · Frametime (ms)")),
            new(AudioSensorSource.DefaultOutVolume, L("Audio · Lautstärke Standardgerät (%)")),
            new(AudioSensorSource.DefaultOutMute, L("Audio · Standardgerät stumm (1/0)")),
            new(AudioSensorSource.DefaultInVolume, L("Audio · Mikrofon-Lautstärke (%)")),
            new(AudioSensorSource.DefaultInMute, L("Audio · Mikrofon stumm (1/0)")),
            new(MediaSensorSource.ProgressId, L("Medien · Fortschritt (%)")),
            new(MediaSensorSource.PositionId, L("Medien · Position (s)")),
            new(MediaSensorSource.DurationId, L("Medien · Dauer (s)")),
            new(MediaSensorSource.RemainingId, L("Medien · Restzeit (s)")),
            new(MediaSensorSource.PlayingId, L("Medien · spielt (1/0)")),
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
            sp.Children.Add(new TextBlock { Text = L(label), Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 2) });
            var box = new TextBox { Text = value.ToString() };
            box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out var v)) { set(v); Changed?.Invoke(); } };
            sp.Children.Add(box);
            Grid.SetColumn(sp, col);
            Panel.Children.Add(sp);
            return box;
        }
    }
}
