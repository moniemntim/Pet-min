using System.Windows;
using System.Windows.Media.Imaging;

namespace ShuBingDesktopPet.Services;

public sealed class SpriteFrameCache
{
    private readonly CroppedBitmap[,] _frames;
    public int CachedFrames { get; }

    public int Rows { get; }
    public int Cols { get; }

    public SpriteFrameCache(BitmapSource source, int rows, int cols, int frameWidth, int frameHeight)
    {
        Rows = Math.Max(0, rows);
        Cols = Math.Max(0, cols);

        if (Rows <= 0 || Cols <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            _frames = new CroppedBitmap[0, 0];
            CachedFrames = 0;
            return;
        }

        if (source.PixelWidth < cols * frameWidth || source.PixelHeight < rows * frameHeight)
        {
            _frames = new CroppedBitmap[0, 0];
            CachedFrames = 0;
            return;
        }

        _frames = new CroppedBitmap[Rows, Cols];

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                var x = col * frameWidth;
                var y = row * frameHeight;
                if (x + frameWidth <= source.PixelWidth && y + frameHeight <= source.PixelHeight)
                {
                    var crop = new CroppedBitmap(source, new Int32Rect(x, y, frameWidth, frameHeight));
                    crop.Freeze();
                    _frames[row, col] = crop;
                    CachedFrames++;
                }
            }
        }
    }

    public CroppedBitmap? GetFrame(int row, int col)
    {
        if (CachedFrames == 0)
        {
            return null;
        }

        if (Rows <= 0 || Cols <= 0)
        {
            return null;
        }

        var safeRow = Math.Clamp(row, 0, Rows - 1);
        var safeCol = Math.Clamp(col, 0, Cols - 1);
        return _frames[safeRow, safeCol];
    }
}
