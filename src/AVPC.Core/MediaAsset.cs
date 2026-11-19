using System.Text.RegularExpressions;

namespace AVPC.Core;

public enum MediaKind
{
    Video,
    Audio,
    Image
}

public sealed partial record MediaAsset(string Path, MediaKind Kind)
{
    public string DisplayName => PrefixRegex().Replace(System.IO.Path.GetFileNameWithoutExtension(Path), string.Empty);

    [GeneratedRegex(@"^\d+-[0-9a-f]+-", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixRegex();
}
