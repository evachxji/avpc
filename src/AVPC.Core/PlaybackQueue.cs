namespace AVPC.Core;

public sealed class PlaybackQueue
{
    private readonly List<PlaybackScheme> _schemes;

    public PlaybackQueue(IEnumerable<PlaybackScheme> schemes)
    {
        _schemes = schemes.ToList();
    }

    public IReadOnlyList<PlaybackScheme> Schemes => _schemes;
    public int? CurrentIndex { get; private set; }
    public PlaybackScheme? CurrentScheme => CurrentIndex is int index ? _schemes[index] : null;
    public bool IsPlaying => CurrentIndex.HasValue;

    public PlaybackScheme? Advance()
    {
        if (_schemes.Count == 0)
        {
            Stop();
            return null;
        }

        if (CurrentIndex is null)
            CurrentIndex = 0;
        else if (CurrentIndex.Value >= _schemes.Count - 1)
        {
            Stop();
            return null;
        }
        else
            CurrentIndex++;

        return CurrentScheme;
    }

    public void Stop() => CurrentIndex = null;

    public void Move(int oldIndex, int newIndex)
    {
        var current = CurrentScheme;
        var scheme = _schemes[oldIndex];
        _schemes.RemoveAt(oldIndex);
        _schemes.Insert(newIndex, scheme);
        if (current is not null)
            CurrentIndex = _schemes.IndexOf(current);
    }
}
