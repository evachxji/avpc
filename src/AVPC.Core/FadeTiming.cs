namespace AVPC.Core;

public static class FadeTiming
{
    public static TimeSpan? GetFadeDuration(TimeSpan position, TimeSpan duration, bool isLooping, VideoEndBehavior behavior)
    {
        if (isLooping || behavior != VideoEndBehavior.FadeToBlack || duration <= TimeSpan.Zero)
            return null;
        var remaining = duration - position;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromSeconds(2))
            return null;
        return remaining;
    }
}
