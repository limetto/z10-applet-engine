using System.Globalization;

namespace Z10.Core.Media;

public enum MediaStatus { None, Stopped, Paused, Playing }

/// <summary>
/// Stand der Medienwiedergabe (Windows: System Media Transport Controls). Viele Apps melden die Position nur bei
/// Titelwechsel, Pause oder Spulen; <see cref="PositionAt"/> rechnet deshalb ab dem Meldezeitpunkt weiter.
/// </summary>
public sealed record MediaInfo(
    string Title,
    string Artist,
    string Album,
    string App,
    MediaStatus Status,
    TimeSpan? Position,
    TimeSpan? Duration,
    DateTime PositionUpdatedUtc,
    double Rate = 1)
{
    public bool IsPlaying => Status == MediaStatus.Playing;

    /// <summary>Position zum Zeitpunkt <paramref name="utcNow"/>: beim Abspielen hochgezählt, auf die Dauer begrenzt.</summary>
    public TimeSpan? PositionAt(DateTime utcNow)
    {
        if (Position is not TimeSpan p) return null;
        if (IsPlaying && utcNow > PositionUpdatedUtc)
            p += TimeSpan.FromTicks((long)((utcNow - PositionUpdatedUtc).Ticks * (Rate > 0 ? Rate : 1)));
        if (Duration is TimeSpan d && d > TimeSpan.Zero && p > d) p = d;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p;
    }

    /// <summary>Fortschritt 0..100 oder null ohne bekannte Dauer.</summary>
    public double? ProgressAt(DateTime utcNow) =>
        Duration is TimeSpan d && d > TimeSpan.Zero && PositionAt(utcNow) is TimeSpan p
            ? Math.Clamp(p.TotalSeconds / d.TotalSeconds * 100, 0, 100)
            : null;
}

/// <summary>Liefert die aktuelle Medienwiedergabe (null = keine).</summary>
public interface IMediaProvider
{
    MediaInfo? Current { get; }
}

/// <summary>Feste Wiedergabe, für Tests und Vorschau.</summary>
public sealed class StaticMediaProvider : IMediaProvider
{
    public MediaInfo? Current { get; set; }
}

public enum MediaField { Title, Artist, ArtistTitle, Album, App, Time, Position, Duration, Remaining }

public static class MediaText
{
    /// <summary>Zeit als m:ss bzw. h:mm:ss.</summary>
    public static string FormatTime(TimeSpan? t)
    {
        if (t is not TimeSpan v) return "-:--";
        if (v < TimeSpan.Zero) v = TimeSpan.Zero;
        return v.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)v.TotalHours, v.Minutes, v.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)v.TotalMinutes, v.Seconds);
    }

    /// <summary>Text eines Felds; leer, wenn nichts läuft oder die App das Feld nicht liefert.</summary>
    public static string Get(MediaInfo? m, MediaField field, DateTime utcNow)
    {
        if (m is null || m.Status == MediaStatus.None) return "";
        var pos = m.PositionAt(utcNow);
        return field switch
        {
            MediaField.Title => m.Title,
            MediaField.Artist => m.Artist,
            MediaField.ArtistTitle => string.IsNullOrWhiteSpace(m.Artist) ? m.Title
                                    : string.IsNullOrWhiteSpace(m.Title) ? m.Artist : $"{m.Artist} – {m.Title}",
            MediaField.Album => m.Album,
            MediaField.App => m.App,
            MediaField.Position => FormatTime(pos),
            MediaField.Duration => FormatTime(m.Duration),
            MediaField.Remaining => m.Duration is TimeSpan d && pos is TimeSpan p ? "-" + FormatTime(d - p) : "-:--",
            _ => m.Duration is TimeSpan ? $"{FormatTime(pos)} / {FormatTime(m.Duration)}" : FormatTime(pos),
        };
    }

    /// <summary>App-Kennung, die nur ein Hash ist (z. B. Firefox: "308046B0AF4A39CB").</summary>
    public static bool IsHashId(string id) =>
        id.Length >= 8 && id.All(Uri.IsHexDigit);

    /// <summary>
    /// Lesbarer App-Name aus der App-Kennung von Windows ("Spotify.exe", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "MSEdge").
    /// </summary>
    public static string FriendlyApp(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var s = id.Trim();
        // Reine Hex-Kennung (Firefox & Co. leiten sie aus dem Installationspfad ab) ist kein lesbarer Name → ausblenden
        if (IsHashId(s)) return "";
        int bang = s.LastIndexOf('!');
        if (bang >= 0 && bang < s.Length - 1) s = s[(bang + 1)..];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        s = s.Replace('\\', '/');
        if (s.Contains('/')) s = s[(s.LastIndexOf('/') + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        int dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];   // "Microsoft.ZuneMusic" → "ZuneMusic"
        return s.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            "spotify" => "Spotify",
            "zunemusic" => "Media Player",
            "vlc" => "VLC",
            _ => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s,
        };
    }
}
