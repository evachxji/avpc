using AVPC.Core;

namespace AVPC.Tests;

public sealed class PlaybackSchemeTests
{
    private static readonly MediaAsset Video = new("video.mp4", MediaKind.Video);
    private static readonly MediaAsset Image = new("image.png", MediaKind.Image);

    [Fact]
    public void Validate_RequiresOneVisualSource()
    {
        var scheme = new PlaybackScheme { Name = "空方案" };
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void Validate_RejectsVideoAndImagesTogether()
    {
        var scheme = new PlaybackScheme { Name = "混合方案", Video = Video };
        scheme.Images.Add(Image);
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidImageDuration()
    {
        var scheme = new PlaybackScheme { Name = "图片", ImageDurationSeconds = 0 };
        scheme.Images.Add(Image);
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void Validate_AcceptsVideoOrImages()
    {
        var video = new PlaybackScheme { Name = "视频", Video = Video };
        var images = new PlaybackScheme { Name = "图片" };
        images.Images.Add(Image);
        Assert.Null(video.Validate());
        Assert.Null(images.Validate());
    }

    [Fact]
    public void ImageDuration_DefaultsToFiveSeconds()
    {
        Assert.Equal(5, new PlaybackScheme().ImageDurationSeconds);
    }

    [Fact]
    public void DisplayName_RemovesGeneratedPrefix()
    {
        var asset = new MediaAsset("1795075486497-d4565b6a8d21-答谢宴主题屏幕动态.mp4", MediaKind.Video);
        Assert.Equal("答谢宴主题屏幕动态", asset.DisplayName);
    }
}
