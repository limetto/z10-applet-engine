using System;
using System.IO;
using System.Windows;
using static Z10.Core.Localization.Loc;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>Importiert ein Bild oder animiertes GIF als Grafik für Symbol-Bausteine.</summary>
public partial class ImportImageWindow : Window
{
    private readonly ImageLibrary _library;
    private readonly WriteableBitmap _bitmap = LcdImage.Create();
    private readonly DispatcherTimer _timer = new();
    private ImageImporter? _source;
    private bool _updatingSize;
    private int _frameIndex;

    /// <summary>Name der importierten Grafik (ohne "img:"), wenn erfolgreich.</summary>
    public string? ImportedName { get; private set; }

    public ImportImageWindow(ImageLibrary library)
    {
        _library = library;
        InitializeComponent();
        UiTranslator.Apply(this);
        Preview.Source = _bitmap;
        LcdImage.Update(_bitmap, new Framebuffer());
        _timer.Tick += (_, _) => NextFrame();
        UpdateThresholdText();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L("Grafik für das Z10-Display wählen"),
            Filter = L("Bilder") + " (*.png;*.gif;*.jpg;*.jpeg;*.bmp)|*.png;*.gif;*.jpg;*.jpeg;*.bmp|" + L("Alle Dateien") + "|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _source = ImageImporter.Load(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Die Datei konnte nicht gelesen werden:") + "\n" + ex.Message, L("Grafik importieren"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        FileText.Text = dlg.FileName;
        NameBox.Text = ImageLibrary.SafeName(Path.GetFileNameWithoutExtension(dlg.FileName));
        OptionsPanel.IsEnabled = true;
        ImportButton.IsEnabled = true;
        SetFitSize(Math.Min(32, _source.SourceHeight));
        Restart();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || sender is not Button { Tag: string tag }) return;
        if (tag == "full")
        {
            _updatingSize = true;
            WidthBox.Text = "160";
            HeightBox.Text = "32";
            _updatingSize = false;
            KeepRatioBox.IsChecked = false;
        }
        else
        {
            KeepRatioBox.IsChecked = true;
            SetFitSize(int.Parse(tag));
        }
        Restart();
    }

    /// <summary>Höhe vorgeben, Breite nach Seitenverhältnis (begrenzt auf 160 × 32).</summary>
    private void SetFitSize(int height)
    {
        if (_source is null) return;
        double ratio = _source.SourceWidth / (double)_source.SourceHeight;
        int h = Math.Clamp(height, 1, 32);
        int w = (int)Math.Round(h * ratio);
        if (w > 160) { w = 160; h = Math.Max(1, (int)Math.Round(w / ratio)); }
        _updatingSize = true;
        WidthBox.Text = Math.Max(1, w).ToString();
        HeightBox.Text = h.ToString();
        _updatingSize = false;
    }

    private void Size_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingSize || _source is null) return;
        if (KeepRatioBox.IsChecked == true && sender == HeightBox && int.TryParse(HeightBox.Text, out var h))
            SetFitSize(h);
        else if (KeepRatioBox.IsChecked == true && sender == WidthBox && int.TryParse(WidthBox.Text, out var w))
        {
            double ratio = _source.SourceWidth / (double)_source.SourceHeight;
            _updatingSize = true;
            HeightBox.Text = Math.Clamp((int)Math.Round(Math.Clamp(w, 1, 160) / ratio), 1, 32).ToString();
            _updatingSize = false;
        }
        Restart();
    }

    private void Options_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateThresholdText();
        Restart();
    }

    private void Options_Click(object sender, RoutedEventArgs e) => Restart();

    private void UpdateThresholdText()
    {
        if (ThresholdText is not null) ThresholdText.Text = ((int)ThresholdSlider.Value).ToString();
    }

    private ImageImporter.Options CurrentOptions() => new(
        int.TryParse(WidthBox.Text, out var w) ? Math.Clamp(w, 1, 160) : 16,
        int.TryParse(HeightBox.Text, out var h) ? Math.Clamp(h, 1, 32) : 16,
        (int)ThresholdSlider.Value,
        DitherBox.IsChecked == true,
        InvertBox.IsChecked == true);

    private void Restart()
    {
        if (_source is null || !IsInitialized) return;
        _frameIndex = -1;
        _timer.Stop();
        var o = CurrentOptions();
        int count = _source.Frames.Count;
        InfoText.Text = count > 1
            ? L("Animiert: {0} Bilder", count) + (_source.Truncated ? " " + L("(auf {0} gekürzt)", ImageImporter.MaxFrames) : "") + " · " + L("Original {0} × {1} px · Ziel {2} × {3} px", _source.SourceWidth, _source.SourceHeight, o.Width, o.Height)
            : L("Original {0} × {1} px · Ziel {2} × {3} px", _source.SourceWidth, _source.SourceHeight, o.Width, o.Height);
        NextFrame();
    }

    private void NextFrame()
    {
        if (_source is null) return;
        _frameIndex = (_frameIndex + 1) % _source.Frames.Count;
        var bmp = _source.ConvertFrame(_frameIndex, CurrentOptions());
        var fb = new Framebuffer();
        fb.Blit(bmp, 0, 0);
        LcdImage.Update(_bitmap, fb);
        if (_source.Frames.Count > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _source.Frames[_frameIndex].DelayMs));
            _timer.Start();
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var name = ImageLibrary.SafeName(NameBox.Text);
        if (_library.Get(name) is not null &&
            MessageBox.Show(this, L("Eine Grafik „{0}“ gibt es schon. Ersetzen?", name), L("Grafik importieren"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var frames = _source.Convert(CurrentOptions());
        _library.Save(ImageAsset.FromBitmaps(name, frames));
        ImportedName = name;
        _timer.Stop();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = false;
    }
}
