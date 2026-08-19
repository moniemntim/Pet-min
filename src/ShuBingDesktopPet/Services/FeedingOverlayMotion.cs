namespace ShuBingDesktopPet.Services;

public static class FeedingOverlayMotion
{
    public static double CalculateFoodTop(
        int column,
        int frameCount,
        double startY = 112.0,
        double mouthY = 70.0)
    {
        var lastColumn = Math.Max(1, frameCount - 1);
        var boundedColumn = Math.Clamp(column, 0, lastColumn);
        var progress = boundedColumn / (double)lastColumn;
        return startY + (mouthY - startY) * progress;
    }
}
