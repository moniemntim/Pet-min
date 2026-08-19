using System.Windows;
using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public static class DragPlacement
{
    public static (double x, double y) ResolveDragMove(
        double leftPx,
        double topPx,
        int frameWidthPx,
        int frameHeightPx,
        double safeMarginPx,
        RoamingScope scope,
        MonitorArea? startMonitor)
    {
        if (scope != RoamingScope.CurrentScreen || startMonitor is null)
        {
            return (leftPx, topPx);
        }

        return RoamingBoundary.ClampToVisible(startMonitor.WorkingArea, frameWidthPx, frameHeightPx, leftPx, topPx, safeMarginPx);
    }

    public static (double x, double y, MonitorArea destination) ResolveDragDrop(
        double leftPx,
        double topPx,
        int frameWidthPx,
        int frameHeightPx,
        double safeMarginPx,
        RoamingScope scope,
        IReadOnlyList<MonitorArea> monitors,
        int cursorX,
        int cursorY,
        MonitorArea? startMonitor = null)
    {
        var destination = GetDestinationMonitor(monitors, scope, cursorX, cursorY, startMonitor);
        var clamped = RoamingBoundary.ClampToVisible(destination.WorkingArea, frameWidthPx, frameHeightPx, leftPx, topPx, safeMarginPx);
        return (clamped.x, clamped.y, destination);
    }

    private static MonitorArea GetDestinationMonitor(
        IReadOnlyList<MonitorArea> monitors,
        RoamingScope scope,
        int cursorX,
        int cursorY,
        MonitorArea? startMonitor)
    {
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitor area available.");
        }

        if (scope == RoamingScope.CurrentScreen && startMonitor is not null)
        {
            return startMonitor;
        }

        foreach (var monitor in monitors)
        {
            if (IsInsideVisible(monitor.WorkingArea, cursorX, cursorY))
            {
                return monitor;
            }
        }

        var nearest = FindNearestMonitor(monitors, cursorX, cursorY);
        if (nearest is not null)
        {
            return nearest;
        }

        if (scope == RoamingScope.CurrentScreen && startMonitor is not null)
        {
            return startMonitor;
        }

        return monitors[0];
    }

    private static bool IsInsideVisible(Rect area, double x, double y)
    {
        var maxX = Math.Max(area.Left, area.Right);
        var maxY = Math.Max(area.Top, area.Bottom);
        return x >= area.Left && x <= maxX && y >= area.Top && y <= maxY;
    }

    private static MonitorArea? FindNearestMonitor(IReadOnlyList<MonitorArea> monitors, double x, double y)
    {
        MonitorArea? nearest = null;
        var nearestDistance = double.MaxValue;

        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var distance = DistanceToAreaSq(monitor.WorkingArea, x, y);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monitor;
            }
        }

        return nearest;
    }

    private static double DistanceToAreaSq(Rect area, double x, double y)
    {
        var clampedX = Math.Clamp(x, area.Left, area.Right);
        var clampedY = Math.Clamp(y, area.Top, area.Bottom);
        var dx = x - clampedX;
        var dy = y - clampedY;
        return dx * dx + dy * dy;
    }
}
