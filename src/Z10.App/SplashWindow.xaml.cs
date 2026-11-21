using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Z10.Core.Rendering;

namespace Z10.App;

/// <summary>Startfenster mit derselben Logo-Animation wie auf der Tastatur.</summary>
public partial class SplashWindow : Window
{
    private readonly IReadOnlyList<Framebuffer> _frames = BootAnimation.Frames();
    private readonly WriteableBitmap _bitmap = LcdImage.Create();
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;
    private bool _closing;

    /// <summary>Wird ausgelöst, wenn das Fenster ausgeblendet ist.</summary>
    public event Action? Finished;

    public SplashWindow()
    {
        InitializeComponent();
        Lcd.Source = _bitmap;
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = v is null ? "" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        LcdImage.Update(_bitmap, _frames[0]);

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(BootAnimation.FrameMs) };
        _timer.Tick += (_, _) => Step();
        Loaded += (_, _) => _timer.Start();
    }

    private void Step()
    {
        int idx = (int)((DateTime.UtcNow - _start).TotalMilliseconds / BootAnimation.FrameMs);
        if (idx >= _frames.Count)
        {
            FadeOut();
            return;
        }
        LcdImage.Update(_bitmap, _frames[idx]);
        if (idx > _frames.Count / 2) Status.Text = "Sensoren werden gelesen …";
    }

    private void Skip_Click(object sender, MouseButtonEventArgs e) => FadeOut();

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        LcdImage.Update(_bitmap, _frames[^1]);
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280)) { BeginTime = TimeSpan.FromMilliseconds(150) };
        anim.Completed += (_, _) =>
        {
            Close();
            Finished?.Invoke();
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
