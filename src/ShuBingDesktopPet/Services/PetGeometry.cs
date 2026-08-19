using System.Windows;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace ShuBingDesktopPet.Services;

public static class PetGeometry
{
    public const double DragThresholdDip = 6.0;
    public const double HeadZoneHeightRatio = 0.35;
    public const double HeadZoneWidthRatio = 0.45;
    public const double HeadZoneMin = 20.0;

    public static bool IsDragStart(Point down, Point current)
    {
        var dx = current.X - down.X;
        var dy = current.Y - down.Y;
        return Math.Sqrt(dx * dx + dy * dy) >= DragThresholdDip;
    }

    public static int Direction16FromMouse(Point from, Point to)
    {
        var vx = to.X - from.X;
        var vy = to.Y - from.Y;
        if (Math.Abs(vx) < 1 && Math.Abs(vy) < 1)
        {
            return 0;
        }

        var angle = Math.Atan2(vx, -vy) * (180.0 / Math.PI);
        if (angle < 0)
        {
            angle += 360;
        }

        var index = (int)Math.Floor((angle + 11.25) / 22.5) % 16;
        return (index + 16) % 16;
    }

    public static bool IsInHeadZone(Rect bounds, Point windowPoint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var zW = Math.Max(bounds.Width * HeadZoneWidthRatio, HeadZoneMin);
        var zH = Math.Max(bounds.Height * HeadZoneHeightRatio, HeadZoneMin);
        var x0 = bounds.Left + (bounds.Width - zW) / 2;
        var rect = new Rect(x0, bounds.Top, zW, zH);
        return rect.Contains(windowPoint);
    }

    public static Rect ClampRectToWorkArea(Rect candidate, Rect workArea, double frameWidth, double frameHeight, double insetPx = 0)
    {
        var bounds = RoamingBoundary.GetSafeMovementBounds(workArea, frameWidth, frameHeight, insetPx);
        var left = Math.Max(bounds.MinX, Math.Min(candidate.Left, bounds.MaxX));
        var top = Math.Max(bounds.MinY, Math.Min(candidate.Top, bounds.MaxY));
        return new Rect(left, top, frameWidth, frameHeight);
    }
}
