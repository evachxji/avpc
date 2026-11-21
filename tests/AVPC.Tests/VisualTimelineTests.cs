using AVPC.Core;

namespace AVPC.Tests;

public sealed class VisualTimelineTests
{
    [Fact]
    public void Timeline_ComputesTotalAndItemEndForMixedQueue()
    {
        var scheme = CreateScheme();
        var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);

        Assert.Equal(TimeSpan.FromSeconds(250), timeline.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(60), timeline.GetItemEnd(0));
        Assert.Equal(TimeSpan.FromSeconds(65), timeline.GetItemEnd(1));
        Assert.Equal(TimeSpan.FromSeconds(245), timeline.GetItemEnd(2));
        Assert.True(timeline.HasCompleteDuration);
    }

    [Fact]
    public void Timeline_ConvertsBetweenLocalAndGlobalPositions()
    {
        var scheme = CreateScheme();
        var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);

        Assert.Equal(TimeSpan.FromSeconds(30), timeline.GetGlobalPosition(0, TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(125), timeline.GetGlobalPosition(2, TimeSpan.FromSeconds(60)));

        var located = timeline.Locate(TimeSpan.FromSeconds(125));
        Assert.Equal(2, located.Index);
        Assert.Same(scheme.Visuals[2], located.Asset);
        Assert.Equal(TimeSpan.FromSeconds(65), located.ItemStart);
        Assert.Equal(TimeSpan.FromSeconds(245), located.ItemEnd);
        Assert.Equal(TimeSpan.FromSeconds(60), located.ItemPosition);
    }

    [Fact]
    public void Timeline_ItemEndBoundaryBelongsToEndingItem()
    {
        var scheme = CreateScheme();
        var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);

        var located = timeline.Locate(TimeSpan.FromSeconds(65));

        Assert.Equal(1, located.Index);
        Assert.Equal(TimeSpan.FromSeconds(5), located.ItemPosition);
    }

    [Fact]
    public void Timeline_SeekWithinCurrentVisualDoesNotCrossBoundary()
    {
        var scheme = CreateScheme();
        var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);

        var sameVisual = timeline.Locate(TimeSpan.FromSeconds(30));
        var nextVisual = timeline.Locate(TimeSpan.FromSeconds(70));

        Assert.False(sameVisual.CrossesVisualBoundary(currentIndex: 0));
        Assert.True(nextVisual.CrossesVisualBoundary(currentIndex: 0));
    }

    [Fact]
    public void Timeline_ClampsGlobalPositionAndReportsUnknownVideoDuration()
    {
        var scheme = CreateScheme();
        scheme.Visuals[2].DurationSeconds = 0;
        var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);

        Assert.False(timeline.HasCompleteDuration);
        Assert.Equal(TimeSpan.Zero, timeline.Locate(TimeSpan.FromSeconds(-10)).ItemStart);
        var end = timeline.Locate(TimeSpan.FromDays(1));
        Assert.Equal(3, end.Index);
        Assert.Equal(timeline.TotalDuration, end.ItemEnd);
    }

    private static PlaybackScheme CreateScheme()
    {
        var scheme = new PlaybackScheme { ImageDurationSeconds = 5 };
        scheme.AppendVisual(new MediaAsset("opening.mp4", MediaKind.Video) { DurationSeconds = 60 });
        scheme.AppendVisual(new MediaAsset("forest.png", MediaKind.Image));
        scheme.AppendVisual(new MediaAsset("feature.mp4", MediaKind.Video) { DurationSeconds = 180 });
        scheme.AppendVisual(new MediaAsset("closing.png", MediaKind.Image));
        return scheme;
    }
}
