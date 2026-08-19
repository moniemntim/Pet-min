using System;

namespace ShuBingDesktopPet.Services;

public static class RoamingMotion
{
    public readonly record struct StepResult(double NextX, double NextY, bool ReachedTarget);

    public static StepResult CalculateNextStep(
        double currentX,
        double currentY,
        double targetX,
        double targetY,
        double stepPx)
    {
        var dx = targetX - currentX;
        var dy = targetY - currentY;
        var distanceToTarget = Math.Sqrt(dx * dx + dy * dy);

        if (distanceToTarget <= 0)
        {
            return new StepResult(targetX, targetY, true);
        }

        if (stepPx >= distanceToTarget)
        {
            return new StepResult(targetX, targetY, true);
        }

        if (stepPx <= 0)
        {
            return new StepResult(currentX, currentY, false);
        }

        var ratio = stepPx / distanceToTarget;
        return new StepResult(currentX + dx * ratio, currentY + dy * ratio, false);
    }

    public static bool HasNoMovement(double previousX, double previousY, double nextX, double nextY, double epsilon = 0.0)
    {
        return Math.Abs(previousX - nextX) <= epsilon && Math.Abs(previousY - nextY) <= epsilon;
    }
}
