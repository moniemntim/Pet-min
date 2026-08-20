using System.Collections.Generic;
using System.Windows;
using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public static class ObsCaptureGeometry
{
    private static readonly IReadOnlyList<(int Width, int Height)> AllowedResolutions = new[]
    {
        (1280, 720),
        (1920, 1080),
        (2560, 1440),
        (3840, 2160)
    };

    public static IReadOnlyList<(int Width, int Height)> GetAllowedResolutions() => AllowedResolutions;

    public static (int Width, int Height) NormalizeResolution(int width, int height)
    {
        foreach (var (allowedWidth, allowedHeight) in AllowedResolutions)
        {
            if (allowedWidth == width && allowedHeight == height)
            {
                return (width, height);
            }
        }

        return AllowedResolutions[1];
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) GetSafeBounds(
        int canvasWidthPx,
        int canvasHeightPx,
        int petWidthPx,
        int petHeightPx,
        double safeMarginPx)
    {
        return RoamingBoundary.GetSafeMovementBounds(new Rect(0, 0, canvasWidthPx, canvasHeightPx), petWidthPx, petHeightPx, safeMarginPx);
    }

    public static (double x, double y) ClampPetPosition(
        int canvasWidthPx,
        int canvasHeightPx,
        int petWidthPx,
        int petHeightPx,
        double x,
        double y,
        double safeMarginPx)
    {
        return RoamingBoundary.ClampToVisible(new Rect(0, 0, canvasWidthPx, canvasHeightPx), petWidthPx, petHeightPx, x, y, safeMarginPx);
    }

    public static int ResolvePetFrameWidth(int spriteFrameWidth, WalkingBodyStyle bodyStyle, bool longBodyFramesAvailable)
    {
        var safeSpriteWidth = Math.Max(1, spriteFrameWidth);
        return bodyStyle == WalkingBodyStyle.LongBody && longBodyFramesAvailable
            ? WalkingBodyFrameCache.ExpectedFrameWidth
            : safeSpriteWidth;
    }

    public static (int x, int y) GetOffscreenWindowPosition(
        int virtualDesktopLeftPx,
        int virtualDesktopTopPx,
        int windowWidthPx,
        int gapPx = 64)
    {
        var safeWidth = Math.Max(1, windowWidthPx);
        var safeGap = Math.Max(1, gapPx);
        var offscreenX = (long)virtualDesktopLeftPx - safeWidth - safeGap;
        return ((int)Math.Clamp(offscreenX, int.MinValue, int.MaxValue), virtualDesktopTopPx);
    }
}
