namespace ShuBingDesktopPet.Services;

/// <summary>Plays the authored feeding sequence as settle, nibble, then look up.</summary>
public static class FeedingAnimationTimeline
{
    private static readonly int[] ChewingFrames = { 1, 2, 3, 4, 3, 2 };

    public static int GetFrame(double elapsedSeconds, double durationSeconds)
    {
        var duration = double.IsFinite(durationSeconds) ? Math.Clamp(durationSeconds, 1, 20) : 4;
        var elapsed = double.IsNaN(elapsedSeconds) ? 0 : Math.Clamp(elapsedSeconds, 0, duration);
        var opening = Math.Min(0.35, duration * 0.12);
        var ending = Math.Min(0.55, duration * 0.2);
        if (elapsed < opening) return 0;
        if (elapsed >= duration - ending) return 5;
        return ChewingFrames[(int)((elapsed - opening) / 0.16) % ChewingFrames.Length];
    }
}
