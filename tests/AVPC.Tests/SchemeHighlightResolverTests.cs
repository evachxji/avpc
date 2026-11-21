using AVPC.Core;

namespace AVPC.Tests;

public sealed class SchemeHighlightResolverTests
{
    [Fact]
    public void UsesSelectedSchemeWhenOutputIsHidden()
    {
        var outputScheme = new PlaybackScheme { Name = "输出方案" };
        var selectedScheme = new PlaybackScheme { Name = "选中方案" };

        var highlighted = SchemeHighlightResolver.Resolve(outputScheme, selectedScheme, outputVisible: false);

        Assert.Same(selectedScheme, highlighted);
    }

    [Fact]
    public void UsesOutputSchemeWhenOutputIsVisible()
    {
        var outputScheme = new PlaybackScheme { Name = "输出方案" };
        var selectedScheme = new PlaybackScheme { Name = "选中方案" };

        var highlighted = SchemeHighlightResolver.Resolve(outputScheme, selectedScheme, outputVisible: true);

        Assert.Same(outputScheme, highlighted);
    }
}
