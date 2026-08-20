namespace ShuBingDesktopPet.Services;

/// <summary>Tracks one continuous walking episode. It is advanced only while the pet is free to walk.</summary>
public sealed class RoamingWalkTimer
{
    public double RemainingSeconds { get; private set; }
    public bool IsActive => RemainingSeconds > 0;

    public void Start(double durationSeconds)
    {
        var normalized = double.IsFinite(durationSeconds) ? durationSeconds : 8.0;
        RemainingSeconds = Math.Clamp(normalized, 2.0, 60.0);
    }

    public bool Advance(double elapsedSeconds)
    {
        if (!IsActive) return true;
        var normalizedElapsed = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0
            ? elapsedSeconds
            : 0;
        var remaining = RemainingSeconds - normalizedElapsed;
        RemainingSeconds = remaining <= 1e-9 ? 0 : remaining;
        return RemainingSeconds <= 0;
    }

    public void Reset() => RemainingSeconds = 0;
}
