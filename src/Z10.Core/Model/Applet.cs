using static Z10.Core.Localization.Loc;
using System.Text.Json.Serialization;
using Z10.Core.Rendering;

namespace Z10.Core.Model;

public sealed class Applet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Neues Applet";
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }

    /// <summary>Anzeigedauer in der Rotation. 0 = nur manuell.</summary>
    public int DurationSeconds { get; set; } = 8;

    /// <summary>Aktualisierungsintervall der Anzeige.</summary>
    public int RefreshMs { get; set; } = 1000;

    /// <summary>Bei aktivem Warnwert dieses Applet sofort anzeigen.</summary>
    public bool ShowOnWarning { get; set; }

    /// <summary>Beim Durchwechseln nur zeigen, wenn gerade Medien abgespielt werden.</summary>
    public bool OnlyWhenMediaPlaying { get; set; }

    public List<Widget> Widgets { get; set; } = new();

    public IEnumerable<string> SensorIds =>
        Widgets.OfType<ISensorWidget>().Select(w => w.Sensor).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
}

public enum TextAlign { Left, Center, Right }

public sealed class WarnRule
{
    /// <summary>Warnung ab diesem Wert (größer oder gleich).</summary>
    public double? Above { get; set; }

    /// <summary>Warnung bis zu diesem Wert (kleiner oder gleich).</summary>
    public double? Below { get; set; }

    /// <summary>Bei Warnung blinken statt dauerhaft invertieren.</summary>
    public bool Blink { get; set; }

    public bool IsTriggered(double? value) =>
        value is double v && ((Above is double a && v >= a) || (Below is double b && v <= b));
}

public enum SmoothingMode { None, Average, Median }

public interface ISensorWidget
{
    string Sensor { get; set; }

    /// <summary>Glättung über die letzten Sekunden (Mittelwert oder Median).</summary>
    SmoothingMode Smoothing { get; set; }

    int SmoothingSeconds { get; set; }
}

/// <summary>Gemeinsame Basis aller Bausteine, die einen Sensor anzeigen.</summary>
public abstract class SensorBoundWidget : Widget, ISensorWidget
{
    public string Sensor { get; set; } = "";
    public SmoothingMode Smoothing { get; set; }
    public int SmoothingSeconds { get; set; } = 5;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextWidget), "text")]
[JsonDerivedType(typeof(SensorWidget), "sensor")]
[JsonDerivedType(typeof(BarWidget), "bar")]
[JsonDerivedType(typeof(GraphWidget), "graph")]
[JsonDerivedType(typeof(ClockWidget), "clock")]
[JsonDerivedType(typeof(IconWidget), "icon")]
[JsonDerivedType(typeof(ShapeWidget), "shape")]
[JsonDerivedType(typeof(MediaWidget), "media")]
public abstract class Widget
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 40;
    public int H { get; set; } = 7;

    /// <summary>Bereich invertiert darstellen (hell auf dunkel ↔ dunkel auf hell).</summary>
    public bool Invert { get; set; }

    /// <summary>Überlagerung mit darunterliegenden Bausteinen (Normal, Invertieren, Ausstanzen).</summary>
    public BlendMode Blend { get; set; }

    public WarnRule? Warn { get; set; }

    [JsonIgnore]
    public abstract string KindName { get; }

    public Widget Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize<Widget>(this, Storage.JsonDefaults.Options);
        return System.Text.Json.JsonSerializer.Deserialize<Widget>(json, Storage.JsonDefaults.Options)!;
    }
}

public sealed class TextWidget : Widget
{
    public string Text { get; set; } = "Text";
    public TextStyle Style { get; set; } = new();
    public TextAlign Align { get; set; }
    public override string KindName => L("Text");
}

public sealed class SensorWidget : SensorBoundWidget
{

    /// <summary>Platzhalter: {value}, {value:0.0}, {value/1000:0.0}, {value*8:0}.</summary>
    public string Format { get; set; } = "{value:0}";

    public TextStyle Style { get; set; } = new();
    public TextAlign Align { get; set; }
    public override string KindName => L("Sensorwert");
}

public sealed class BarWidget : SensorBoundWidget
{
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public bool Border { get; set; } = true;
    public bool Vertical { get; set; }
    public override string KindName => L("Balken");
}

public sealed class GraphWidget : SensorBoundWidget
{

    /// <summary>Feste Skala; null = automatisch aus den angezeigten Werten.</summary>
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>Wie viele Sekunden der Verlauf zeigt (ein Wert pro Aktualisierung).</summary>
    public int Seconds { get; set; } = 60;

    public bool Border { get; set; }
    public bool Filled { get; set; } = true;
    public override string KindName => L("Verlauf");
}

public sealed class ClockWidget : Widget
{
    /// <summary>.NET-Datumsformat, z. B. "HH:mm:ss" oder "ddd dd.MM.yyyy".</summary>
    public string Format { get; set; } = "HH:mm:ss";
    public TextStyle Style { get; set; } = new();
    public TextAlign Align { get; set; }
    public bool UpperCase { get; set; }
    public override string KindName => L("Uhr / Datum");
}

public sealed class IconWidget : Widget
{
    /// <summary>Name eines eingebauten Symbols (cpu, gpu, ram, fan, temp, …) oder 8 Hex-Bytes.</summary>
    public string Icon { get; set; } = "cpu";
    public override string KindName => L("Symbol");
}

public enum ShapeKind { Line, Rectangle, Ellipse }

/// <summary>Richtung einer Linie in ihrem Rahmen.</summary>
public enum LineDirection
{
    /// <summary>von links oben nach rechts unten (bei Höhe 1 waagerecht, bei Breite 1 senkrecht)</summary>
    Falling,
    /// <summary>von links unten nach rechts oben</summary>
    Rising,
}

/// <summary>Grundform: Linie, Rechteck oder Oval im Rahmen X/Y/W/H.</summary>
public sealed class ShapeWidget : Widget
{
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;
    public bool Filled { get; set; }

    /// <summary>Linien- bzw. Randstärke in Pixeln.</summary>
    public int Thickness { get; set; } = 1;

    /// <summary>Eckenradius für Rechtecke.</summary>
    public int Radius { get; set; }

    public LineDirection Direction { get; set; }

    public override string KindName => Shape switch
    {
        ShapeKind.Line => L("Linie"),
        ShapeKind.Ellipse => L("Oval"),
        _ => L("Rechteck"),
    };
}

/// <summary>Angaben zur laufenden Medienwiedergabe (Titel, Interpret, Zeit …), lange Texte auf Wunsch als Lauftext.</summary>
public sealed class MediaWidget : Widget
{
    public Media.MediaField Field { get; set; } = Media.MediaField.ArtistTitle;
    public TextStyle Style { get; set; } = new();
    public TextAlign Align { get; set; }

    /// <summary>Zu lange Texte durchlaufen lassen (sonst abgeschnitten).</summary>
    public bool Scroll { get; set; } = true;

    /// <summary>Laufgeschwindigkeit in Pixeln pro Sekunde.</summary>
    public int ScrollSpeed { get; set; } = 20;

    /// <summary>Text, wenn nichts läuft oder die App das Feld nicht liefert.</summary>
    public string Placeholder { get; set; } = "";

    public override string KindName => L("Medien");
}
