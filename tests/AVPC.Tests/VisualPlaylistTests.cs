using AVPC.Core;

namespace AVPC.Tests;

public sealed class VisualPlaylistTests
{
    private static readonly MediaAsset FirstImage = new("first.png", MediaKind.Image);
    private static readonly MediaAsset Video = new("video.mp4", MediaKind.Video);
    private static readonly MediaAsset LastImage = new("last.png", MediaKind.Image);

    [Fact]
    public void Playlist_MovesThroughMixedItemsInOrder()
    {
        var scheme = CreateScheme();
        var playlist = new VisualPlaylist(scheme);

        Assert.Same(FirstImage, playlist.Current);
        Assert.Same(Video, playlist.MoveNext());
        Assert.Same(LastImage, playlist.MoveNext());
        Assert.Null(playlist.MoveNext());
    }

    [Fact]
    public void Playlist_WrapsWholeQueueWhenLooping()
    {
        var scheme = CreateScheme();
        scheme.IsLooping = true;
        var playlist = new VisualPlaylist(scheme);
        playlist.MoveNext();

        Assert.Same(LastImage, playlist.MoveNext());
        Assert.Same(FirstImage, playlist.MoveNext());
    }

    [Fact]
    public void Playlist_ExposesLastItemAndImageInterval()
    {
        var scheme = CreateScheme();
        scheme.ImageDurationSeconds = 8;
        var playlist = new VisualPlaylist(scheme);

        Assert.False(playlist.IsLast);
        playlist.MoveNext();
        playlist.MoveNext();

        Assert.True(playlist.IsLast);
        Assert.Equal(TimeSpan.FromSeconds(8), playlist.Interval);
    }

    [Fact]
    public void Playlist_MovesToSelectedIndex()
    {
        var playlist = new VisualPlaylist(CreateScheme());

        playlist.MoveTo(1);

        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Same(Video, playlist.Current);
    }

    [Fact]
    public void SingleVideoLoop_RestartsCurrentPlayerAtEnd()
    {
        var scheme = new PlaybackScheme { IsLooping = true };
        scheme.AppendVisual(Video);

        var playlist = new VisualPlaylist(scheme);

        Assert.True(playlist.ShouldReplayCurrentAtEnd);
    }

    private static PlaybackScheme CreateScheme()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(FirstImage);
        scheme.AppendVisual(Video);
        scheme.AppendVisual(LastImage);
        return scheme;
    }
}
