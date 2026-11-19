using AVPC.Core;

namespace AVPC.Tests;

public sealed class FadeTimingTests
{
    [Fact]
    public void GetFadeDuration_WaitsForMediaMetadata()
    {
        Assert.Null(FadeTiming.GetFadeDuration(TimeSpan.Zero, TimeSpan.Zero, false, VideoEndBehavior.FadeToBlack));
    }

    [Fact]
    public void GetFadeDuration_WaitsUntilLastTwoSeconds()
    {
        Assert.Null(FadeTiming.GetFadeDuration(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), false, VideoEndBehavior.FadeToBlack));
        Assert.Equal(TimeSpan.FromSeconds(2), FadeTiming.GetFadeDuration(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10), false, VideoEndBehavior.FadeToBlack));
        Assert.Equal(TimeSpan.FromSeconds(1), FadeTiming.GetFadeDuration(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), false, VideoEndBehavior.FadeToBlack));
    }

    [Fact]
    public void GetFadeDuration_DoesNotRunForLoopOrLastFrame()
    {
        Assert.Null(FadeTiming.GetFadeDuration(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), true, VideoEndBehavior.FadeToBlack));
        Assert.Null(FadeTiming.GetFadeDuration(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), false, VideoEndBehavior.HoldLastFrame));
    }

    [Fact]
    public void GetFadeDuration_ShortVideoUsesWholeDuration()
    {
        Assert.Equal(TimeSpan.FromSeconds(1.5), FadeTiming.GetFadeDuration(TimeSpan.Zero, TimeSpan.FromSeconds(1.5), false, VideoEndBehavior.FadeToBlack));
    }
}
