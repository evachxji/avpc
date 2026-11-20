namespace AVPC.Core;

public sealed record VisualTimelineLocation(
    int Index,
    MediaAsset Asset,
    TimeSpan ItemStart,
    TimeSpan ItemEnd,
    TimeSpan ItemPosition);

public sealed class VisualTimeline
{
    private readonly IReadOnlyList<MediaAsset> _visuals;
    private readonly int _imageDurationSeconds;

    public VisualTimeline(IReadOnlyList<MediaAsset> visuals, int imageDurationSeconds)
    {
        _visuals = visuals;
        _imageDurationSeconds = imageDurationSeconds;
    }

    public TimeSpan TotalDuration => TimeSpan.FromSeconds(Enumerable.Range(0, _visuals.Count).Sum(DurationAt));

    public bool HasCompleteDuration => _visuals.All(item =>
        item.Kind == MediaKind.Image || item.Kind == MediaKind.Video && item.DurationSeconds > 0);

    public TimeSpan GetItemEnd(int index) => TimeSpan.FromSeconds(
        Enumerable.Range(0, index + 1).Sum(DurationAt));

    public TimeSpan GetGlobalPosition(int index, TimeSpan itemPosition)
    {
        var start = index == 0 ? TimeSpan.Zero : GetItemEnd(index - 1);
        var localSeconds = Math.Clamp(itemPosition.TotalSeconds, 0, DurationAt(index));
        return start + TimeSpan.FromSeconds(localSeconds);
    }

    public VisualTimelineLocation Locate(TimeSpan position)
    {
        if (_visuals.Count == 0) throw new InvalidOperationException("画面时间轴为空。");
        var seconds = Math.Clamp(position.TotalSeconds, 0, TotalDuration.TotalSeconds);
        double start = 0;
        for (var index = 0; index < _visuals.Count; index++)
        {
            var end = start + DurationAt(index);
            if (seconds <= end || index == _visuals.Count - 1)
            {
                var local = Math.Clamp(seconds - start, 0, DurationAt(index));
                return new VisualTimelineLocation(
                    index,
                    _visuals[index],
                    TimeSpan.FromSeconds(start),
                    TimeSpan.FromSeconds(end),
                    TimeSpan.FromSeconds(local));
            }
            start = end;
        }
        throw new InvalidOperationException("无法定位画面时间轴。");
    }

    private double DurationAt(int index)
    {
        var item = _visuals[index];
        return item.Kind switch
        {
            MediaKind.Image => _imageDurationSeconds,
            MediaKind.Video => Math.Max(0, item.DurationSeconds),
            _ => throw new InvalidOperationException("画面时间轴只能包含图片和视频。")
        };
    }
}
