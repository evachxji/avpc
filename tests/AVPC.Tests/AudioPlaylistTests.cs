using AVPC.Core;

namespace AVPC.Tests;

public sealed class AudioPlaylistTests
{
    private static readonly MediaAsset First = new("first.mp3", MediaKind.Audio);
    private static readonly MediaAsset Second = new("second.mp3", MediaKind.Audio);

    [Fact]
    public void Playlist_PlaysInOrderThenStops()
    {
        var scheme = new PlaybackScheme();
        scheme.AssignMedia(First);
        scheme.AssignMedia(Second);
        var playlist = new AudioPlaylist(scheme);
        Assert.Same(First, playlist.Current);
        Assert.Same(Second, playlist.MoveNext());
        Assert.Null(playlist.MoveNext());
    }

    [Fact]
    public void Playlist_WrapsWhenUnifiedLoopingIsEnabled()
    {
        var scheme = new PlaybackScheme { IsLooping = true };
        scheme.AssignMedia(First);
        scheme.AssignMedia(Second);
        var playlist = new AudioPlaylist(scheme);
        playlist.MoveNext();
        Assert.Same(First, playlist.MoveNext());
    }

    [Fact]
    public void AssignAudio_AppendsWithoutDuplicates()
    {
        var scheme = new PlaybackScheme();
        scheme.AssignMedia(First);
        scheme.AssignMedia(First);
        Assert.Single(scheme.Audios);
    }
}
