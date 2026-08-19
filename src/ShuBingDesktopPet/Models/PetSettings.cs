namespace ShuBingDesktopPet.Models;

public sealed record PetSettings
{
    public int SchemaVersion { get; init; } = 2;
    public bool RoamingEnabled { get; init; } = true;
    public RoamingScope RoamingScope { get; init; } = RoamingScope.CurrentScreen;
    public RoamingSpeed Speed { get; init; } = RoamingSpeed.Normal;
    public double Scale { get; init; } = 1.0;
    public bool ClickThrough { get; init; } = false;
    public bool StartWithWindows { get; init; } = false;
    public string LastMonitorId { get; init; } = string.Empty;
    public double LastX { get; init; } = double.NaN;
    public double LastY { get; init; } = double.NaN;
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
