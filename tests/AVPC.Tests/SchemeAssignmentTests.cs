using AVPC.Core;

namespace AVPC.Tests;

public sealed class SchemeAssignmentTests
{
    private static readonly MediaAsset Video = new("video.mp4", MediaKind.Video);
    private static readonly MediaAsset Image = new("image.png", MediaKind.Image);
    private static readonly MediaAsset Audio = new("audio.mp3", MediaKind.Audio);

    [Fact]
    public void AssignVideo_ReplacesImageGroup()
    {
        var scheme = new PlaybackScheme();
        scheme.Images.Add(Image);
        scheme.AssignMedia(Video);
        Assert.Same(Video, scheme.Video);
        Assert.Empty(scheme.Images);
    }

    [Fact]
    public void AssignImage_ReplacesVideoAndIgnoresDuplicate()
    {
        var scheme = new PlaybackScheme { Video = Video };
        scheme.AssignMedia(Image);
        scheme.AssignMedia(Image);
        Assert.Null(scheme.Video);
        Assert.Single(scheme.Images);
        Assert.Same(Image, scheme.Images[0]);
    }

    [Fact]
    public void AssignAudio_DoesNotChangeVisualSource()
    {
        var scheme = new PlaybackScheme { Video = Video };
        scheme.AssignMedia(Audio);
        Assert.Same(Audio, Assert.Single(scheme.Audios));
        Assert.Same(Video, scheme.Video);
    }
}

