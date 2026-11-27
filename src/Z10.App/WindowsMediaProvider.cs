using System;
using System.Collections.Concurrent;
using Microsoft.Win32;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Z10.Core.Media;

namespace Z10.App;

/// <summary>
/// Liest die aktuelle Medienwiedergabe über die System Media Transport Controls von Windows (dieselben Daten wie das
/// Lautstärke-Overlay): Spotify, Browser (YouTube …), Media Player, VLC usw. Wird einmal pro Sekunde abgefragt.
/// Bei mehreren Quellen gewinnt die, die gerade spielt, sonst die von Windows als aktuell gewählte.
/// </summary>
public sealed class WindowsMediaProvider : IMediaProvider, IDisposable
{
    private readonly Timer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private volatile MediaInfo? _current;
    private int _polling;
    private bool _enabled = true;

    public string? Error { get; private set; }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) _current = null; }
    }

    public MediaInfo? Current => _enabled ? _current : null;

    public WindowsMediaProvider() => _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));

    private async Task PollAsync()
    {
        if (!_enabled || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _manager.GetSessions();
            var session = sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus
                              == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                          ?? _manager.GetCurrentSession();
            _current = session is null ? null : await ReadAsync(session);
            Error = null;
        }
        catch (Exception ex)
        {
            // z. B. Sitzung gerade geschlossen; beim nächsten Durchlauf neu versuchen
            _current = null;
            Error = ex.Message;
            if (ex is not InvalidOperationException) _manager = null;
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static async Task<MediaInfo> ReadAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var status = playback?.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaStatus.Paused,
            _ => MediaStatus.Stopped,
        };

        TimeSpan? position = null, duration = null;
        var updated = DateTime.UtcNow;
        if (timeline is not null)
        {
            var length = timeline.EndTime - timeline.StartTime;
            if (length > TimeSpan.Zero)
            {
                duration = length;
                position = timeline.Position - timeline.StartTime;
                // Zeitpunkt der letzten Positionsmeldung; ohne gültigen Wert ab jetzt weiterzählen
                var last = timeline.LastUpdatedTime.UtcDateTime;
                if (last.Year > 2000 && last <= updated) updated = last;
            }
        }

        return new MediaInfo(
            Title: props?.Title ?? "",
            Artist: string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumArtist ?? "" : props!.Artist,
            Album: props?.AlbumTitle ?? "",
            App: AppName(session.SourceAppUserModelId),
            Status: status,
            Position: position,
            Duration: duration,
            PositionUpdatedUtc: updated,
            Rate: playback?.PlaybackRate is double r && r > 0 ? r : 1);
    }

    private static readonly ConcurrentDictionary<string, string> AppNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lesbarer Programmname. Firefox (und Thunderbird) melden sich mit einem Hash aus dem Installationspfad; den
    /// Namen dazu legt Mozilla unter Software\Mozilla\&lt;Programm&gt;\TaskBarIDs ab (Wert = Pfad, Daten = Hash).
    /// Sonst hilft die Registrierung für Benachrichtigungen (Software\Classes\AppUserModelId\&lt;Id&gt;, DisplayName).
    /// Unbekannte Hashes werden ausgeblendet.
    /// </summary>
    public static string AppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        return AppNames.GetOrAdd(aumid.Trim(), id => LookupRegistry(id) ?? MediaText.FriendlyApp(id));
    }

    private static string? LookupRegistry(string id)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using (var mozilla = root.OpenSubKey(@"Software\Mozilla"))
                {
                    if (mozilla is not null)
                        foreach (var product in mozilla.GetSubKeyNames())
                        {
                            using var ids = mozilla.OpenSubKey(product + @"\TaskBarIDs");
                            if (ids is null) continue;
                            foreach (var name in ids.GetValueNames())
                                if (string.Equals(ids.GetValue(name) as string, id, StringComparison.OrdinalIgnoreCase))
                                    return product.Replace("Mozilla ", "");
                        }
                }
                using var aumid = root.OpenSubKey(@"Software\Classes\AppUserModelId\" + id);
                if (aumid?.GetValue("DisplayName") is string display && display.Length > 0 && !display.StartsWith("@"))
                    return display;
            }
            catch (Exception) { /* fehlende Rechte o. Ä. */ }
        }
        // Bekannte Standardinstallation von Firefox (C:\Program Files\Mozilla Firefox)
        return id.Equals("308046B0AF4A39CB", StringComparison.OrdinalIgnoreCase) ? "Firefox" : null;
    }

    public void Dispose() => _timer.Dispose();
}
