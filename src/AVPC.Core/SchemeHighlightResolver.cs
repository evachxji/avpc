namespace AVPC.Core;

public static class SchemeHighlightResolver
{
    public static PlaybackScheme? Resolve(
        PlaybackScheme? outputScheme,
        PlaybackScheme? selectedScheme,
        bool outputVisible) => outputVisible ? outputScheme : selectedScheme;
}
