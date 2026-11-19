using AVPC.Core;

namespace AVPC.Tests;

public sealed class ImageSlideshowTests
{
    private static readonly MediaAsset First = new("first.png", MediaKind.Image);
    private static readonly MediaAsset Second = new("second.png", MediaKind.Image);

    [Fact]
    public void SingleImage_RemainsFixed()
    {
        var scheme = new PlaybackScheme { Name = "单张" };
        scheme.Images.Add(First);
        var show = new ImageSlideshow(scheme);
        Assert.Same(First, show.Current);
        Assert.Same(First, show.MoveNext());
    }

    [Fact]
    public void MultipleImages_MoveInOrderAndWrap()
    {
        var scheme = new PlaybackScheme { Name = "轮播" };
        scheme.Images.Add(First);
        scheme.Images.Add(Second);
        var show = new ImageSlideshow(scheme);
        Assert.Same(First, show.Current);
        Assert.Same(Second, show.MoveNext());
        Assert.Same(First, show.MoveNext());
    }

    [Fact]
    public void Interval_UsesSchemeDuration()
    {
        var scheme = new PlaybackScheme { Name = "轮播", ImageDurationSeconds = 8 };
        scheme.Images.Add(First);
        var show = new ImageSlideshow(scheme);
        Assert.Equal(TimeSpan.FromSeconds(8), show.Interval);
    }
}
