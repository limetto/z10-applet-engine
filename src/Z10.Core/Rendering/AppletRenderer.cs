using System.Globalization;
using Z10.Core.Model;
using Z10.Core.Sensors;

namespace Z10.Core.Rendering;

/// <summary>Zeichnet ein Applet in einen Framebuffer.</summary>
public sealed class AppletRenderer
{
    private readonly ITextRasterizer _text;
    private readonly ISensorSource _sensors;
    private readonly SensorHistory _history;

    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Importierte Grafiken für Symbol-Bausteine ("img:Name").</summary>
    public ImageLibrary? Images { get; set; }

    /// <summary>Enthält das Applet animierte Grafiken (dann häufiger neu zeichnen)?</summary>
    public bool IsAnimated(Applet applet) =>
        Images is not null && applet.Widgets.OfType<IconWidget>().Any(i =>
            i.Icon.StartsWith(Icons.ImagePrefix, StringComparison.OrdinalIgnoreCase)
            && Images.Get(i.Icon[Icons.ImagePrefix.Length..])?.IsAnimated == true);

    public AppletRenderer(ITextRasterizer text, ISensorSource sensors, SensorHistory history)
    {
        _text = text;
        _sensors = sensors;
        _history = history;
    }

    /// <summary>Wert eines Sensor-Bausteins, auf Wunsch geglättet (benötigt aufgezeichneten Verlauf).</summary>
    public double? ValueOf(ISensorWidget w)
    {
        var raw = _sensors.GetValue(w.Sensor);
        if (w.Smoothing == SmoothingMode.None || w.SmoothingSeconds <= 1) return raw;
        return _history.Smoothed(w.Sensor, w.Smoothing, w.SmoothingSeconds) ?? raw;
    }

    /// <param name="blinkPhase">Wechselt bei jedem Takt, steuert blinkende Warnungen.</param>
    public void Render(Applet applet, Framebuffer fb, DateTime now, bool blinkPhase = false)
    {
        fb.Clear();
        foreach (var w in applet.Widgets)
        {
            // Normal direkt zeichnen, sonst auf eigener Ebene und dann pixelweise verrechnen
            var target = w.Blend == BlendMode.Normal ? fb : new Framebuffer();
            try
            {
                Draw(w, target, now);
            }
            catch (Exception)
            {
                // Ein fehlerhafter Baustein soll nicht das ganze Applet verhindern.
                target.DrawRect(w.X, w.Y, Math.Max(1, w.W), Math.Max(1, w.H));
            }
            if (!ReferenceEquals(target, fb)) fb.Combine(target, w.Blend);

            bool warn = w.Warn is not null && w is ISensorWidget sw && w.Warn.IsTriggered(ValueOf(sw));
            bool invert = w.Invert ^ (warn && (!w.Warn!.Blink || blinkPhase));
            if (invert) fb.Invert(w.X, w.Y, w.W, w.H);
        }
    }

    /// <summary>Ist in diesem Applet gerade ein Warnwert aktiv?</summary>
    public bool HasWarning(Applet applet) =>
        applet.Widgets.Any(w => w.Warn is not null && w is ISensorWidget sw && w.Warn.IsTriggered(ValueOf(sw)));

    private void Draw(Widget w, Framebuffer fb, DateTime now)
    {
        switch (w)
        {
            case TextWidget t:
                DrawText(fb, t, t.Text, t.Style, t.Align);
                break;
            case SensorWidget s:
                DrawText(fb, s, ValueFormatter.Format(s.Format, ValueOf(s), Culture), s.Style, s.Align);
                break;
            case ClockWidget c:
                var text = SafeDate(now, c.Format);
                DrawText(fb, c, c.UpperCase ? text.ToUpper(Culture) : text, c.Style, c.Align);
                break;
            case BarWidget b:
                DrawBar(fb, b, ValueOf(b));
                break;
            case GraphWidget g:
                DrawGraph(fb, g);
                break;
            case ShapeWidget sh:
                DrawShape(fb, sh);
                break;
            case IconWidget i:
                fb.Blit(Icons.Get(i.Icon, Images, new DateTimeOffset(now).ToUnixTimeMilliseconds()), i.X, i.Y, i.X, i.Y, i.W, i.H);
                break;
        }
    }

    private static void DrawShape(Framebuffer fb, ShapeWidget s)
    {
        int t = Math.Clamp(s.Thickness, 1, 16);
        switch (s.Shape)
        {
            case ShapeKind.Line:
                int off = (t - 1) / 2;
                int x0 = s.X + off, x1 = s.X + s.W - 1 - (t - 1 - off);
                int yTop = s.Y + off, yBottom = s.Y + s.H - 1 - (t - 1 - off);
                if (x1 < x0) x1 = x0;
                if (yBottom < yTop) yBottom = yTop;
                if (s.Direction == LineDirection.Rising) fb.DrawThickLine(x0, yBottom, x1, yTop, t);
                else fb.DrawThickLine(x0, yTop, x1, yBottom, t);
                break;
            case ShapeKind.Ellipse:
                fb.DrawEllipse(s.X, s.Y, s.W, s.H, s.Filled, t);
                break;
            default:
                fb.DrawRoundRect(s.X, s.Y, s.W, s.H, s.Radius, s.Filled, t);
                break;
        }
    }

    private string SafeDate(DateTime now, string format)
    {
        try { return now.ToString(string.IsNullOrWhiteSpace(format) ? "HH:mm" : format, Culture); }
        catch (FormatException) { return "Format?"; }
    }

    private void DrawText(Framebuffer fb, Widget w, string text, TextStyle style, TextAlign align)
    {
        var bmp = _text.Render(text, style);
        int x = align switch
        {
            TextAlign.Center => w.X + (w.W - bmp.Width) / 2,
            TextAlign.Right => w.X + w.W - bmp.Width,
            _ => w.X
        };
        fb.Blit(bmp, x, w.Y, w.X, w.Y, w.W, w.H);
    }

    private static void DrawBar(Framebuffer fb, BarWidget b, double? value)
    {
        int inset = b.Border ? 2 : 0;
        if (b.Border) fb.DrawRect(b.X, b.Y, b.W, b.H);
        if (value is not double v || b.Max <= b.Min) return;
        double frac = Math.Clamp((v - b.Min) / (b.Max - b.Min), 0, 1);
        int innerW = b.W - 2 * inset, innerH = b.H - 2 * inset;
        if (innerW <= 0 || innerH <= 0) return;
        if (b.Vertical)
        {
            int h = (int)Math.Round(innerH * frac);
            fb.FillRect(b.X + inset, b.Y + inset + innerH - h, innerW, h);
        }
        else
        {
            fb.FillRect(b.X + inset, b.Y + inset, (int)Math.Round(innerW * frac), innerH);
        }
    }

    private void DrawGraph(Framebuffer fb, GraphWidget g)
    {
        int inset = g.Border ? 1 : 0;
        if (g.Border) fb.DrawRect(g.X, g.Y, g.W, g.H);
        int innerW = g.W - 2 * inset, innerH = g.H - 2 * inset;
        if (innerW <= 1 || innerH <= 1) return;

        // Zeiten in Messungen umrechnen (Abfrageintervall ist einstellbar)
        int count = Math.Max(2, _history.SamplesFor(g.Seconds));
        int smooth = _history.SamplesFor(g.SmoothingSeconds);
        var values = _history.Last(g.Sensor, count);
        if (g.Smoothing != SmoothingMode.None && g.SmoothingSeconds > 1 && smooth > 1)
        {
            var raw = _history.Last(g.Sensor, count + smooth - 1);
            int offset = raw.Length - values.Length;
            values = values.Select((_, i) =>
            {
                var window = raw.Skip(Math.Max(0, offset + i - smooth + 1)).Take(Math.Min(smooth, offset + i + 1))
                                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                return raw[offset + i] is null || window.Length == 0 ? (double?)null : SensorHistory.Smooth(window, g.Smoothing);
            }).ToArray();
        }
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (known.Length == 0) return;
        double min = g.Min ?? known.Min();
        double max = g.Max ?? known.Max();
        if (max - min < 1e-9) { max = min + 1; }

        int n = values.Length;
        int prevX = -1, prevY = -1;
        // Mit Rahmen dient die untere Rahmenlinie als Nulllinie: Werte am Minimum zeichnen nichts extra.
        // Ohne Rahmen bleibt am Minimum eine 1-Pixel-Linie sichtbar.
        int baseY = g.Border ? g.Y + g.H - 1 : g.Y + g.H;
        int span = g.Border ? innerH : innerH - 1;
        int minH = g.Border ? 0 : 1;
        for (int i = 0; i < n; i++)
        {
            if (values[i] is not double v) { prevX = -1; continue; }
            // neuester Wert rechts
            int x = g.X + inset + innerW - 1 - (int)Math.Round((n - 1 - i) * (innerW - 1) / (double)Math.Max(1, count - 1));
            if (x < g.X + inset) continue;
            double frac = Math.Clamp((v - min) / (max - min), 0, 1);
            int y = baseY - (minH + (int)Math.Round(frac * span));
            if (g.Filled)
                fb.FillRect(x, y, 1, baseY - y);
            if (prevX >= 0)
            {
                if (g.Filled)
                {
                    for (int xx = prevX + 1; xx < x; xx++)
                    {
                        int yy = prevY + (y - prevY) * (xx - prevX) / Math.Max(1, x - prevX);
                        fb.FillRect(xx, yy, 1, baseY - yy);
                    }
                }
                else
                {
                    fb.DrawLine(prevX, prevY, x, y);
                }
            }
            else if (!g.Filled)
            {
                fb[x, y] = true;
            }
            prevX = x;
            prevY = y;
        }
    }
}
