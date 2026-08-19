using System.Windows;

namespace ShuBingDesktopPet.Services;

public static class RoamingScreenTransition
{
    public readonly record struct MoveResult(double X, double Y, bool CrossedToDestination);

    public static MoveResult Resolve(
        Rect sourceWorkArea,
        Rect destinationWorkArea,
        double frameWidth,
        double frameHeight,
        double currentX,
        double currentY,
        double desiredX,
        double desiredY)
    {
        if (frameWidth <= 1)
        {
            frameWidth = 1;
        }

        if (frameHeight <= 1)
        {
            frameHeight = 1;
        }

        var sameMonitor = sourceWorkArea.Equals(destinationWorkArea);
        if (sameMonitor)
        {
            var clampedInSource = RoamingBoundary.ClampToVisible(sourceWorkArea, frameWidth, frameHeight, desiredX, desiredY);
            return new MoveResult(clampedInSource.x, clampedInSource.y, false);
        }

        var desiredInsideSource = IsInsideVisible(sourceWorkArea, frameWidth, frameHeight, desiredX, desiredY);
        if (desiredInsideSource)
        {
            var clampedInSource = RoamingBoundary.ClampToVisible(sourceWorkArea, frameWidth, frameHeight, desiredX, desiredY);
            return new MoveResult(clampedInSource.x, clampedInSource.y, false);
        }

        var dx = desiredX - currentX;
        var dy = desiredY - currentY;
        var destinationX = destinationWorkArea.Left;
        var destinationY = destinationWorkArea.Top;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx >= 0)
            {
                destinationX = destinationWorkArea.Left;
                destinationY = MapAxis(
                    sourceWorkArea.Top,
                    sourceWorkArea.Height,
                    destinationWorkArea.Top,
                    destinationWorkArea.Height,
                    currentY,
                    frameHeight);
            }
            else
            {
                destinationX = destinationWorkArea.Right - frameWidth;
                destinationY = MapAxis(
                    sourceWorkArea.Top,
                    sourceWorkArea.Height,
                    destinationWorkArea.Top,
                    destinationWorkArea.Height,
                    currentY,
                    frameHeight);
            }
        }
        else
        {
            if (dy >= 0)
            {
                destinationY = destinationWorkArea.Top;
                destinationX = MapAxis(
                    sourceWorkArea.Left,
                    sourceWorkArea.Width,
                    destinationWorkArea.Left,
                    destinationWorkArea.Width,
                    currentX,
                    frameWidth);
            }
            else
            {
                destinationY = destinationWorkArea.Bottom - frameHeight;
                destinationX = MapAxis(
                    sourceWorkArea.Left,
                    sourceWorkArea.Width,
                    destinationWorkArea.Left,
                    destinationWorkArea.Width,
                    currentX,
                    frameWidth);
            }
        }

        var clampedDestination = RoamingBoundary.ClampToVisible(
            destinationWorkArea,
            frameWidth,
            frameHeight,
            destinationX,
            destinationY);

        return new MoveResult(clampedDestination.x, clampedDestination.y, true);
    }

    public static Vector DirectionToTarget(double fromX, double fromY, double targetX, double targetY)
    {
        return new Vector(targetX - fromX, targetY - fromY);
    }

    private static bool IsInsideVisible(Rect workArea, double frameWidth, double frameHeight, double x, double y)
    {
        var minX = workArea.Left;
        var minY = workArea.Top;
        var maxX = Math.Max(workArea.Left, workArea.Right - frameWidth);
        var maxY = Math.Max(workArea.Top, workArea.Bottom - frameHeight);
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static double MapAxis(
        double sourceStart,
        double sourceLength,
        double destinationStart,
        double destinationLength,
        double current,
        double frameLength)
    {
        var sourceTravel = Math.Max(1.0, sourceLength - frameLength);
        var t = Math.Clamp((current - sourceStart) / sourceTravel, 0.0, 1.0);
        return destinationStart + t * Math.Max(0.0, destinationLength - frameLength);
    }
}
