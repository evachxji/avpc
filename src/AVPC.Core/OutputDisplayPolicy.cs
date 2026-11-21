namespace AVPC.Core;

public static class OutputDisplayPolicy
{
    public static bool CanSwitch(bool isPlaying) => !isPlaying;
}
