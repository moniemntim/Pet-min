namespace ShuBingDesktopPet.Services;

public readonly record struct AnimationFrameScale(double ScaleX, double ScaleY);

public static class AnimationVisualScale
{
    public static AnimationFrameScale GetFrameScale(int row)
    {
        return row switch
        {
            6 => new AnimationFrameScale(0.69, 0.69),
            7 => new AnimationFrameScale(0.60, 0.60),
            10 => new AnimationFrameScale(1.17, 1.17),
            _ => new AnimationFrameScale(1.0, 1.0)
        };
    }
}
