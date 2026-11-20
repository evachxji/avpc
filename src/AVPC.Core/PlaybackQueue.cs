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
    public bool IsPlaying { get; private set; }
    public bool IsPastEnd { get; private set; }

    public PlaybackScheme? Start()
    {
        if (_schemes.Count == 0)
        {
            Stop();
            return null;
        }

        if (CurrentIndex is null || IsPastEnd) CurrentIndex = 0;
        IsPastEnd = false;
        IsPlaying = true;
        return CurrentScheme;
    }

    public PlaybackScheme? Advance()
    {
        if (_schemes.Count == 0)
        {
            Stop();
            return null;
        }

        IsPlaying = false;
        IsPastEnd = false;
        if (CurrentIndex is null)
            CurrentIndex = 0;
        else if (CurrentIndex.Value >= _schemes.Count - 1)
        {
            CurrentIndex = null;
            IsPastEnd = true;
            return null;
        }
        else
            CurrentIndex++;

        return CurrentScheme;
    }

    public PlaybackScheme Select(int index)
    {
        if ((uint)index >= (uint)_schemes.Count) throw new ArgumentOutOfRangeException(nameof(index));
        CurrentIndex = index;
        IsPlaying = false;
        IsPastEnd = false;
        return CurrentScheme!;
    }
    public PlaybackScheme? Previous()
    {
        if (_schemes.Count == 0) return null;
        if (CurrentIndex is null && !IsPastEnd) return null;
        IsPlaying = false;
        IsPastEnd = false;
        if (CurrentIndex is not int index || index <= 0)
            CurrentIndex = _schemes.Count - 1;
        else
            CurrentIndex = index - 1;
        return CurrentScheme;
    }
    public void Stop()
    {
        CurrentIndex = null;
        IsPlaying = false;
        IsPastEnd = false;
    }

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
