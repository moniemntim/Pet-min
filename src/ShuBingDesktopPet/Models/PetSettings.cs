namespace ShuBingDesktopPet.Models;

public sealed record PetSettings
{
    public const string DefaultTwitchRewardTitle = "餵薯餅吃東西";

    public int SchemaVersion { get; init; } = 7;
    public bool TwitchEnabled { get; init; } = false;
    public string TwitchClientId { get; init; } = string.Empty;
    public int TwitchMinimumBits { get; init; } = 1;
    public bool TwitchChannelPointsEnabled { get; init; } = false;
    public string TwitchChannelPointsRewardTitle { get; init; } = DefaultTwitchRewardTitle;
    public bool RoamingEnabled { get; init; } = true;
    public RoamingScope RoamingScope { get; init; } = RoamingScope.CurrentScreen;
    public RoamingSpeed Speed { get; init; } = RoamingSpeed.Normal;
    public double Scale { get; init; } = 1.0;
    public bool ClickThrough { get; init; } = false;
    public bool StartWithWindows { get; init; } = false;
    public bool ObsCaptureEnabled { get; init; } = false;
    public int ObsCanvasWidth { get; init; } = 1920;
    public int ObsCanvasHeight { get; init; } = 1080;
    public string LastMonitorId { get; init; } = string.Empty;
    public double LastX { get; init; } = double.NaN;
    public double LastY { get; init; } = double.NaN;
    public int TotalFeedCount { get; init; } = 0;
    public DateTime? LastFedUtc { get; init; }
    public PetFoodType LastFoodType { get; init; } = PetFoodType.None;
    public double FeedingDurationSeconds { get; init; } = 4.0;
    public double ContinuousWalkDurationSeconds { get; init; } = 8.0;
    public WalkingBodyStyle WalkingBodyStyle { get; init; } = WalkingBodyStyle.Current;
}

public enum RoamingScope
{
    CurrentScreen = 0,
    AllScreens = 1
}

public enum RoamingSpeed
{
    Slow = 0,
    Normal = 1,
    Fast = 2
}

public enum WalkingBodyStyle
{
    Current = 0,
    LongBody = 1
}
