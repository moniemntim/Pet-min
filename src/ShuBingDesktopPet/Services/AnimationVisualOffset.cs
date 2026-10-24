namespace ShuBingDesktopPet.Services;

public readonly record struct AnimationFrameOffset(double TranslateX, double TranslateY);

public static class AnimationVisualOffset
{
    public static AnimationFrameOffset GetFrameOffset(int row, int column)
    {
        return new AnimationFrameOffset(0, 0);
    }
}
