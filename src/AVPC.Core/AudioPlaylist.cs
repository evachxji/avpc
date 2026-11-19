namespace AVPC.Core;

public sealed class AudioPlaylist
{
    private readonly PlaybackScheme _scheme;
    private int _index;

    public AudioPlaylist(PlaybackScheme scheme)
    {
        if (scheme.Audios.Count == 0)
            throw new ArgumentException("音频播放列表至少需要一首音频。", nameof(scheme));
        _scheme = scheme;
    }

    public MediaAsset Current => _scheme.Audios[_index];

    public MediaAsset? MoveNext()
    {
        if (_index + 1 >= _scheme.Audios.Count)
        {
            if (!_scheme.IsAudioLooping) return null;
            _index = 0;
        }
        else
            _index++;
        return Current;
    }
}
