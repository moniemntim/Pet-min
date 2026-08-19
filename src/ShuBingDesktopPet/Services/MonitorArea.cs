using System.Windows;

namespace ShuBingDesktopPet.Services;

public sealed record MonitorArea(Rect WorkingArea, string DeviceName);

public static class RoamingBoundary
{
    public static (double x, double y) ClampToVisible(Rect workArea, double width, double height, double x, double y)
    {
        return ClampToVisible(workArea, width, height, x, y, 0);
    }

    public static (double x, double y) ClampToVisible(
        Rect workArea,
        double width,
        double height,
        double x,
        double y,
        double insetPx)
    {
        var clamped = PetGeometry.ClampRectToWorkArea(
            new Rect(x, y, width, height),
            workArea,
            width,
            height,
            insetPx);
        return (clamped.Left, clamped.Top);
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) GetSafeMovementBounds(
        Rect workArea,
        double width,
        double height,
        double insetPx)
    {
        var safeXInset = GetAxisInset(workArea.Width, width, insetPx);
        var safeYInset = GetAxisInset(workArea.Height, height, insetPx);

        var minX = workArea.Left + safeXInset;
        var minY = workArea.Top + safeYInset;
        var maxX = workArea.Right - width - safeXInset;
        var maxY = workArea.Bottom - height - safeYInset;

        if (maxX < minX)
        {
            maxX = minX;
        }

        if (maxY < minY)
        {
            maxY = minY;
        }

        return (minX, minY, maxX, maxY);
    }

    private static double GetAxisInset(double workAreaAxisLength, double frameAxisLength, double requestedInset)
    {
        var availableMarginSpace = Math.Max(0.0, workAreaAxisLength - frameAxisLength);
        if (availableMarginSpace <= 0)
        {
            return 0.0;
        }

        return Math.Min(Math.Max(0.0, requestedInset), availableMarginSpace / 2.0);
    }

    public static MonitorArea? FindMonitorByName(IEnumerable<MonitorArea> monitors, string name)
    {
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.DeviceName, name, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    public static MonitorArea GetPreferredMonitor(IEnumerable<MonitorArea> monitors, string desiredName)
    {
        var byName = FindMonitorByName(monitors, desiredName);
        if (byName != null)
        {
            return byName;
        }

        var first = monitors.FirstOrDefault();
        return first ?? new MonitorArea(SystemParameters.WorkArea, string.Empty);
    }
}
