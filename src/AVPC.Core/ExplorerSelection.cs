namespace AVPC.Core;

public static class ExplorerSelection
{
    public static string BuildArguments(string filePath) => $"/select,\"{filePath}\"";
}
