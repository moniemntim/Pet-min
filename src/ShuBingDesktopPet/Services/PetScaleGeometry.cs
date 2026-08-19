using System;

namespace ShuBingDesktopPet.Services;

public static class PetScaleGeometry
{
    public const double MinScale = 0.75;
    public const double MaxScale = 1.5;

    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    public static (double WidthDip, double HeightDip) GetLogicalWindowSize(double frameWidth, double frameHeight, double scale)
    {
        var clamped = ClampScale(scale);
        return (frameWidth * clamped, frameHeight * clamped);
    }

    public static (int WidthPx, int HeightPx) GetPhysicalWindowSize(double frameWidth, double frameHeight, double scale, double dpiScaleX, double dpiScaleY)
    {
        var clamped = ClampScale(scale);
        var widthPx = (int)Math.Round(frameWidth * clamped * dpiScaleX, 0, MidpointRounding.AwayFromZero);
        var heightPx = (int)Math.Round(frameHeight * clamped * dpiScaleY, 0, MidpointRounding.AwayFromZero);
        return (Math.Max(1, widthPx), Math.Max(1, heightPx));
    }
}
