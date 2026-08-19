namespace ShuBingDesktopPet.Services;

public readonly record struct AnimationFrameOffset(double TranslateX, double TranslateY);

public static class AnimationVisualOffset
{
    private static readonly double[] GroomRowColumnOffsets =
    {
        -34.8,
        -36.0,
        -36.0,
        -34.8,
        -28.8,
        -36.0
    };

    public static AnimationFrameOffset GetFrameOffset(int row, int column)
    {
        if (row == 7 && (uint)column < (uint)GroomRowColumnOffsets.Length)
        {
            return new AnimationFrameOffset(0, GroomRowColumnOffsets[column]);
        }

        return new AnimationFrameOffset(0, 0);
    }
}
