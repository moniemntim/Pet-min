using System;

namespace ShuBingDesktopPet.Services;

public static class ObsCanvasMotion
{
    public static (double x, double y) ChooseRandomTarget(
        int canvasWidthPx,
        int canvasHeightPx,
        int petWidthPx,
        int petHeightPx,
        double safeMarginPx,
        Random random)
    {
        var safeBounds = ObsCaptureGeometry.GetSafeBounds(canvasWidthPx, canvasHeightPx, petWidthPx, petHeightPx, safeMarginPx);

        var spanX = Math.Max(0.0, safeBounds.MaxX - safeBounds.MinX);
        var spanY = Math.Max(0.0, safeBounds.MaxY - safeBounds.MinY);

        return (
            safeBounds.MinX + random.NextDouble() * spanX,
            safeBounds.MinY + random.NextDouble() * spanY);
    }

    public static RoamingMotion.StepResult MoveTowardTarget(
        double currentX,
        double currentY,
        double targetX,
        double targetY,
        double stepPxPerFrame)
    {
        return RoamingMotion.CalculateNextStep(currentX, currentY, targetX, targetY, stepPxPerFrame);
    }

    public static (double x, double y) ClampToCanvas(
        int canvasWidthPx,
        int canvasHeightPx,
        int petWidthPx,
        int petHeightPx,
        double x,
        double y,
        double safeMarginPx)
    {
        return ObsCaptureGeometry.ClampPetPosition(canvasWidthPx, canvasHeightPx, petWidthPx, petHeightPx, x, y, safeMarginPx);
    }
}
