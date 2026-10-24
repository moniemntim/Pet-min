namespace ShuBingDesktopPet.Services;

public readonly record struct AnimationFrameScale(double ScaleX, double ScaleY);

public static class AnimationVisualScale
{
    public static AnimationFrameScale GetFrameScale(int row)
    {
        // Every atlas row is authored on the same 192x208 canvas and already
        // uses the shared character scale.  Keep this transform neutral so a
        // pose cannot become larger or smaller merely by changing rows.
        return new AnimationFrameScale(1.0, 1.0);
    }
}
