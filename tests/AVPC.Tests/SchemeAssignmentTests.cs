using AVPC.Core;

namespace AVPC.Tests;

public sealed class SchemeAssignmentTests
{
    private static readonly MediaAsset Video = new("video.mp4", MediaKind.Video);
    private static readonly MediaAsset Image = new("image.png", MediaKind.Image);
    private static readonly MediaAsset Audio = new("audio.mp3", MediaKind.Audio);

    [Fact]
    public void AppendVisual_PreservesMixedOrderAndIgnoresDuplicate()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Image);
        scheme.AppendVisual(Video);
        scheme.AppendVisual(Image);

        Assert.Equal([Image, Video], scheme.Visuals);
    }

    [Fact]
    public void ReplaceVisual_ClearsExistingQueue()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Image);
        scheme.AppendVisual(Video);
        var replacement = new MediaAsset("replacement.png", MediaKind.Image);

        scheme.ReplaceVisual(replacement);

        Assert.Same(replacement, Assert.Single(scheme.Visuals));
    }

    [Fact]
    public void AssignVisual_UsesThumbnailDropReplacementSemantics()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Image);

        scheme.AssignMedia(Video);

        Assert.Same(Video, Assert.Single(scheme.Visuals));
    }

    [Fact]
    public void RemoveAudio_RemovesOnlyRequestedAudio()
    {
        var scheme = new PlaybackScheme();
        var secondAudio = new MediaAsset("second.mp3", MediaKind.Audio);
        scheme.AssignMedia(Audio);
        scheme.AssignMedia(secondAudio);

        scheme.RemoveAudio(Audio);

        Assert.Same(secondAudio, Assert.Single(scheme.Audios));
    }

    [Fact]
    public void AssignAudio_DoesNotChangeVisualQueue()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Image);

        scheme.AssignMedia(Audio);

        Assert.Same(Audio, Assert.Single(scheme.Audios));
        Assert.Same(Image, Assert.Single(scheme.Visuals));
    }

    [Fact]
    public void AppendVisual_RejectsAudio()
    {
        var scheme = new PlaybackScheme();

        Assert.Throws<ArgumentException>(() => scheme.AppendVisual(Audio));
        Assert.Empty(scheme.Visuals);
    }
}
