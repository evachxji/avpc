using AVPC.Core;

namespace AVPC.Tests;

public sealed class WorkspacePersistenceTests
{
    [Fact]
    public void Workspace_RoundTrip_RestoresMediaAndSchemeSettings()
    {
        var video = new MediaAsset("video.mp4", MediaKind.Video);
        var image = new MediaAsset("image.png", MediaKind.Image);
        var firstAudio = new MediaAsset("first.mp3", MediaKind.Audio);
        var secondAudio = new MediaAsset("second.mp3", MediaKind.Audio);
        var scheme = new PlaybackScheme
        {
            Name = "欢迎方案",
            IsAudioLooping = true,
            ImageDurationSeconds = 8
        };
        scheme.AssignMedia(image);
        scheme.AssignMedia(firstAudio);
        scheme.AssignMedia(secondAudio);
        var workspace = new WorkspaceState
        {
            Media = [video, image, firstAudio, secondAudio],
            Schemes = [scheme]
        };

        var restored = WorkspacePersistence.Deserialize(WorkspacePersistence.Serialize(workspace));
        var restoredScheme = Assert.Single(restored.Schemes);
        Assert.Equal(4, restored.Media.Count);
        Assert.Equal("欢迎方案", restoredScheme.Name);
        Assert.Equal(image.Path, restoredScheme.Images[0].Path);
        Assert.Equal([firstAudio.Path, secondAudio.Path], restoredScheme.Audios.Select(audio => audio.Path).ToArray());
        Assert.True(restoredScheme.IsAudioLooping);
        Assert.Equal(8, restoredScheme.ImageDurationSeconds);
    }
}

