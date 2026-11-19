namespace AVPC.Core;

public sealed class ImageSlideshow
{
    private readonly PlaybackScheme _scheme;
    private int _index;

    public ImageSlideshow(PlaybackScheme scheme)
    {
        if (scheme.Images.Count == 0)
            throw new ArgumentException("图片轮播至少需要一张图片。", nameof(scheme));
        _scheme = scheme;
    }

    public MediaAsset Current => _scheme.Images[_index];
    public TimeSpan Interval => TimeSpan.FromSeconds(_scheme.ImageDurationSeconds);

    public MediaAsset MoveNext()
    {
        _index = (_index + 1) % _scheme.Images.Count;
        return Current;
    }
}
