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
    public void Validate_AcceptsMixedVisualQueue()
    {
        var scheme = new PlaybackScheme { Name = "混合方案" };
        scheme.AppendVisual(Image);
        scheme.AppendVisual(Video);
        Assert.Null(scheme.Validate());
    }

    [Fact]
    public void Validate_RejectsAudioInVisualQueue()
    {
        var scheme = new PlaybackScheme();
        scheme.Visuals.Add(new MediaAsset("audio.mp3", MediaKind.Audio));
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void Validate_RejectsUnknownVisualKind()
    {
        var scheme = new PlaybackScheme();
        scheme.Visuals.Add(new MediaAsset("bad.bin", (MediaKind)99));
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidImageDuration()
    {
        var scheme = new PlaybackScheme { Name = "图片", ImageDurationSeconds = 0 };
        scheme.AppendVisual(Image);
        Assert.NotNull(scheme.Validate());
    }

    [Fact]
    public void ImageDuration_DefaultsToFiveSeconds()
    {
        Assert.Equal(5, new PlaybackScheme().ImageDurationSeconds);
    }

    [Fact]
    public void VolumeSettings_ClampToSupportedRanges()
    {
        var scheme = new PlaybackScheme
        {
            PlaybackVolume = 1.5,
            SystemVolume = -0.2
        };

        Assert.Equal(1, scheme.PlaybackVolume);
        Assert.Equal(0, scheme.SystemVolume);
    }

    [Fact]
    public void VisualQueue_RemovesOnlySelectedAsset()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Video);
        scheme.AppendVisual(Image);

        scheme.RemoveVisual(Video);

        Assert.Equal(Image.Path, Assert.Single(scheme.Visuals).Path);
        Assert.Equal("video.mp4", Video.Path);
    }

    [Fact]
    public void RemoveMedia_RemovesAssetFromVisualAndAudioQueues()
    {
        var audio = new MediaAsset("audio.mp3", MediaKind.Audio);
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Video);
        scheme.AppendVisual(Image);
        scheme.AssignMedia(audio);

        scheme.RemoveMedia(Video);
        scheme.RemoveMedia(audio);

        Assert.Equal(Image.Path, Assert.Single(scheme.Visuals).Path);
        Assert.Empty(scheme.Audios);
    }

    [Fact]
    public void VisualQueue_MovesItemToNewIndex()
    {
        var third = new MediaAsset("third.png", MediaKind.Image);
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(Video);
        scheme.AppendVisual(Image);
        scheme.AppendVisual(third);

        scheme.MoveVisual(0, 2);

        Assert.Equal([Image.Path, third.Path, Video.Path], scheme.Visuals.Select(item => item.Path));
    }

    [Fact]
    public void AudioQueue_MovesItemToNewIndex()
    {
        var first = new MediaAsset("first.mp3", MediaKind.Audio);
        var second = new MediaAsset("second.mp3", MediaKind.Audio);
        var third = new MediaAsset("third.mp3", MediaKind.Audio);
        var scheme = new PlaybackScheme();
        scheme.AssignMedia(first);
        scheme.AssignMedia(second);
        scheme.AssignMedia(third);

        scheme.MoveAudio(2, 0);

        Assert.Equal([third.Path, first.Path, second.Path], scheme.Audios.Select(item => item.Path));
    }

    [Fact]
    public void HasIndependentAudio_TracksWhetherAnyAudioIsAssigned()
    {
        var scheme = new PlaybackScheme();
        Assert.False(scheme.HasIndependentAudio);

        scheme.AssignMedia(new MediaAsset("audio.mp3", MediaKind.Audio));
        Assert.True(scheme.HasIndependentAudio);

        scheme.RemoveAudio(scheme.Audios[0]);
        Assert.False(scheme.HasIndependentAudio);
    }

    [Fact]
    public void DisplayName_RemovesGeneratedPrefix()
    {
        var asset = new MediaAsset("1795075486497-d4565b6a8d21-答谢宴主题屏幕动态.mp4", MediaKind.Video);
        Assert.Equal("答谢宴主题屏幕动态", asset.DisplayName);
    }
}
