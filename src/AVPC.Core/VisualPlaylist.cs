namespace AVPC.Core;

public sealed class VisualPlaylist
{
    private readonly PlaybackScheme _scheme;
    private int _index;

    public VisualPlaylist(PlaybackScheme scheme)
    {
        if (scheme.Visuals.Count == 0)
            throw new ArgumentException("画面队列至少需要一个视频或图片。", nameof(scheme));
        _scheme = scheme;
    }

    public int CurrentIndex => _index;

    public MediaAsset Current => _scheme.Visuals[_index];
    public bool IsLast => _index == _scheme.Visuals.Count - 1;
    public bool ShouldReplayCurrentAtEnd =>
        _scheme.Visuals.Count == 1 && _scheme.IsLooping && Current.Kind == MediaKind.Video;
    public TimeSpan Interval => TimeSpan.FromSeconds(_scheme.ImageDurationSeconds);

    public void MoveTo(int index)
    {
        _ = _scheme.Visuals[index];
        _index = index;
    }

    public MediaAsset? MoveNext()
    {
        if (IsLast)
        {
            if (!_scheme.IsLooping) return null;
            _index = 0;
        }
        else
            _index++;
        return Current;
    }
}
