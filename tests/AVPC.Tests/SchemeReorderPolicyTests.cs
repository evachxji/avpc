using AVPC.Core;

namespace AVPC.Tests;

public sealed class SchemeReorderPolicyTests
{
    [Fact]
    public void AllowsReorderingWhenOutputIsHidden()
    {
        Assert.True(SchemeReorderPolicy.CanReorder(outputVisible: false));
    }

    [Fact]
    public void BlocksReorderingWhenOutputIsVisible()
    {
        Assert.False(SchemeReorderPolicy.CanReorder(outputVisible: true));
    }
}
