using AVPC.Core;

namespace AVPC.Tests;

public sealed class WorkspacePersistenceTests
{
    [Fact]
    public void Workspace_RoundTrip_RestoresMixedVisualsAndSchemeSettings()
    {
        var video = new MediaAsset("video.mp4", MediaKind.Video) { DurationSeconds = 62.5 };
        var image = new MediaAsset("image.png", MediaKind.Image);
        var audio = new MediaAsset("audio.mp3", MediaKind.Audio) { DurationSeconds = 45 };
        var secondAudio = new MediaAsset("second.mp3", MediaKind.Audio) { DurationSeconds = 50 };
        var scheme = new PlaybackScheme
        {
            Name = "混合方案",
            IsLooping = true,
            ImageDurationSeconds = 8,
            EndBehavior = VideoEndBehavior.FadeToBlack,
            PlaybackVolume = 0.65,
            SystemVolume = 0.4
        };
        scheme.AppendVisual(image);
        scheme.AppendVisual(video);
        scheme.AssignMedia(audio);
        scheme.AssignMedia(secondAudio);
        var workspace = new WorkspaceState { Media = [video, image, audio, secondAudio], Schemes = [scheme] };

        var json = WorkspacePersistence.Serialize(workspace);
        var restoredWorkspace = WorkspacePersistence.Deserialize(json);
        var restored = Assert.Single(restoredWorkspace.Schemes);

        Assert.Equal([image.Path, video.Path], restored.Visuals.Select(item => item.Path));
        Assert.Equal([audio.Path, secondAudio.Path], restored.Audios.Select(item => item.Path));
        Assert.True(restored.IsLooping);
        Assert.Equal(62.5, restored.Visuals[1].DurationSeconds);
        Assert.Equal(45, restored.Audios[0].DurationSeconds);
        Assert.Equal(62.5, restoredWorkspace.Media.Single(item => item.Path == video.Path).DurationSeconds);
        Assert.Equal(8, restored.ImageDurationSeconds);
        Assert.Equal(VideoEndBehavior.FadeToBlack, restored.EndBehavior);
        Assert.Equal(0.65, restored.PlaybackVolume);
        Assert.Equal(0.4, restored.SystemVolume);
        Assert.Contains("\"Visuals\"", json);
        Assert.Contains("\"DurationSeconds\"", json);
        Assert.DoesNotContain("\"Video\"", json);
        Assert.DoesNotContain("\"Images\"", json);
        Assert.DoesNotContain("\"IsVideoLooping\"", json);
        Assert.DoesNotContain("\"IsVisualLooping\"", json);
        Assert.DoesNotContain("\"IsAudioLooping\"", json);
        Assert.DoesNotContain("\"HasIndependentAudio\"", json);
    }

    [Fact]
    public void LegacyVideoWorkspace_MigratesToVisualQueue()
    {
        const string json = """
        {"Media":[],"Schemes":[{"Name":"旧视频","Video":{"Path":"old.mp4","Kind":0},"Images":[],"Audios":[],"IsVideoLooping":true,"ImageDurationSeconds":5,"EndBehavior":1}]}
        """;

        var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

        var visual = Assert.Single(scheme.Visuals);
        Assert.Equal("old.mp4", visual.Path);
        Assert.True(scheme.IsLooping);
        Assert.Equal(VideoEndBehavior.FadeToBlack, scheme.EndBehavior);
    }

    [Fact]
    public void LegacyImageWorkspace_PreservesOrderAndSettings()
    {
        const string json = """
        {"Media":[],"Schemes":[{"Name":"旧图片","Video":null,"Images":[{"Path":"a.png","Kind":2},{"Path":"b.png","Kind":2}],"Audios":[{"Path":"a.mp3","Kind":1}],"IsVideoLooping":false,"IsAudioLooping":true,"ImageDurationSeconds":7,"EndBehavior":0}]}
        """;

        var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

        Assert.Equal(["a.png", "b.png"], scheme.Visuals.Select(item => item.Path));
        Assert.True(scheme.IsLooping);
        Assert.Equal("a.mp3", Assert.Single(scheme.Audios).Path);
        Assert.Equal(7, scheme.ImageDurationSeconds);
    }

    [Fact]
    public void LegacyImageWorkspace_KeepsLoopingWhenVideoLoopFieldComesFirst()
    {
        const string json = """
        {"Media":[],"Schemes":[{"Name":"旧图片乱序","IsVideoLooping":false,"Images":[{"Path":"a.png","Kind":2}],"Video":null}]}
        """;

        var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

        Assert.Equal("a.png", Assert.Single(scheme.Visuals).Path);
        Assert.True(scheme.IsLooping);
    }

    [Fact]
    public void LegacyVisualLoopWorkspace_MigratesToUnifiedLoop()
    {
        const string json = """
        {"Media":[],"Schemes":[{"Name":"旧画面循环","Visuals":[{"Path":"a.png","Kind":2}],"IsVisualLooping":true,"IsAudioLooping":false}]}
        """;

        var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

        Assert.True(scheme.IsLooping);
    }
}
