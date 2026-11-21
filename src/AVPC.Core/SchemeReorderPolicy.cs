namespace AVPC.Core;

public static class SchemeReorderPolicy
{
    public static bool CanReorder(bool outputVisible) => !outputVisible;
}
