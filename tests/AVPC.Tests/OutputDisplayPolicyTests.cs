using AVPC.Core;

namespace AVPC.Tests;

public sealed class OutputDisplayPolicyTests
{
    [Fact]
    public void AllowsSwitchingBeforePlaybackStarts()
    {
        Assert.True(OutputDisplayPolicy.CanSwitch(isPlaying: false));
    }

    [Fact]
    public void BlocksSwitchingDuringPlayback()
    {
        Assert.False(OutputDisplayPolicy.CanSwitch(isPlaying: true));
    }
}
