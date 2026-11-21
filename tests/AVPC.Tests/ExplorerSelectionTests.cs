using AVPC.Core;

namespace AVPC.Tests;

public sealed class ExplorerSelectionTests
{
    [Fact]
    public void BuildArguments_SelectsFileInExplorer()
    {
        var path = @"C:\素材库\晨光 01.mp4";

        var arguments = ExplorerSelection.BuildArguments(path);

        Assert.Equal("/select,\"C:\\素材库\\晨光 01.mp4\"", arguments);
    }
}
