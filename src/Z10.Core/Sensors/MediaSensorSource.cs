using static Z10.Core.Localization.Loc;
using Z10.Core.Media;

namespace Z10.Core.Sensors;

/// <summary>Medienwiedergabe als Sensoren: Position, Dauer, Restzeit (Sekunden), Fortschritt (%), spielt (1/0).</summary>
public sealed class MediaSensorSource : SensorSourceBase
{
    public const string PositionId = "/media/position";
    public const string DurationId = "/media/duration";
    public const string RemainingId = "/media/remaining";
    public const string ProgressId = "/media/progress";
    public const string PlayingId = "/media/playing";

    private readonly IMediaProvider _provider;
    public bool Enabled { get; set; } = true;

    /// <summary>Uhr, für Tests austauschbar.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public MediaSensorSource(IMediaProvider provider) => _provider = provider;

    protected override IReadOnlyList<SensorInfo> ReadSensors()
    {
        if (!Enabled) return Array.Empty<SensorInfo>();
        var m = _provider.Current;
        var now = UtcNow();
        var pos = m?.PositionAt(now);
        var hw = m is null || m.Status == MediaStatus.None ? L("Medienwiedergabe") : L("Medienwiedergabe") + " · " + m.App;
        double? Sec(TimeSpan? t) => t is TimeSpan v ? Math.Round(v.TotalSeconds, 1) : null;
        return new[]
        {
            new SensorInfo(PositionId, hw, "media", L("Position"), "Time", "s", Sec(pos)),
            new SensorInfo(DurationId, hw, "media", L("Dauer"), "Time", "s", Sec(m?.Duration)),
            new SensorInfo(RemainingId, hw, "media", L("Restzeit"), "Time", "s",
                m?.Duration is TimeSpan d && pos is TimeSpan p ? Sec(d - p) : null),
            new SensorInfo(ProgressId, hw, "media", L("Fortschritt"), "Load", "%", m?.ProgressAt(now)),
            new SensorInfo(PlayingId, hw, "media", L("Spielt"), "Control", "", m is null ? 0 : m.IsPlaying ? 1 : 0),
        };
    }
}
