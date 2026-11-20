using AVPC.Core;

namespace AVPC.Tests;

public sealed class PlaybackQueueTests
{
    private static PlaybackScheme Scheme(string name) => new() { Name = name };

    [Fact]
    public void Advance_EmptyQueue_StaysStopped()
    {
        var queue = new PlaybackQueue([]);
        Assert.Null(queue.Advance());
        Assert.False(queue.IsPlaying);
        Assert.Null(queue.CurrentScheme);
    }

    [Fact]
    public void Advance_VisitsSchemesInOrder_ThenStops()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);

        Assert.Same(first, queue.Advance());
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Same(second, queue.Advance());
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Null(queue.Advance());
        Assert.False(queue.IsPlaying);
        Assert.Null(queue.CurrentScheme);
        Assert.Same(first, queue.Advance());
    }

    [Fact]
    public void Stop_ResetsNextRunToFirstScheme()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);
        queue.Advance();
        queue.Stop();
        Assert.Same(first, queue.Advance());
    }

    [Fact]
    public void Move_ChangesPlaybackOrder()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);
        queue.Move(0, 1);
        Assert.Same(second, queue.Advance());
    }

    [Fact]
    public void Move_KeepsCurrentSchemeActive()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);
        queue.Advance();
        queue.Move(0, 1);
        Assert.Same(first, queue.CurrentScheme);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Null(queue.Advance());
    }
    [Fact]
    public void Previous_BeforeStartOrAtFirstScheme_StaysPut()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);

        Assert.Null(queue.Previous());
        Assert.Null(queue.CurrentIndex);
        Assert.Same(first, queue.Advance());
        Assert.Null(queue.Previous());
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Same(first, queue.CurrentScheme);
    }

    [Fact]
    public void Previous_ReturnsToPreviousScheme()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var queue = new PlaybackQueue([first, second]);
        queue.Advance();
        queue.Advance();

        Assert.Same(first, queue.Previous());
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Same(second, queue.Advance());
    }
    [Fact]
    public void Select_JumpsToRequestedSchemeAndPreservesNavigationOrder()
    {
        var first = Scheme("第一套");
        var second = Scheme("第二套");
        var third = Scheme("第三套");
        var queue = new PlaybackQueue([first, second, third]);

        Assert.Same(second, queue.Select(1));
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(third, queue.Advance());
        Assert.Same(second, queue.Previous());
    }

    [Fact]
    public void Select_InvalidIndex_ThrowsWithoutChangingCurrentScheme()
    {
        var first = Scheme("第一套");
        var queue = new PlaybackQueue([first]);
        queue.Advance();

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Select(1));
        Assert.Same(first, queue.CurrentScheme);
        Assert.Equal(0, queue.CurrentIndex);
    }
}
