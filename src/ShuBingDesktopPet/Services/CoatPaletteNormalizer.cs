using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuBingDesktopPet.Services;

/// <summary>One-time per-cell colour harmonisation for the canonical atlas.</summary>
public static class CoatPaletteNormalizer
{
    private const int CellWidth = 192;
    private const int CellHeight = 208;
    private const byte MinimumLuma = 58;

    public static BitmapSource Normalize(BitmapSource source, Int32Rect? excludedRegion = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bgra = ToBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        var isAtlasGrid = width >= CellWidth && height >= CellHeight
            && width % CellWidth == 0 && height % CellHeight == 0;
        var columns = isAtlasGrid ? width / CellWidth : 1;
        var rows = isAtlasGrid ? height / CellHeight : 1;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x = isAtlasGrid ? column * CellWidth : 0;
            var y = isAtlasGrid ? row * CellHeight : 0;
            var cellWidth = isAtlasGrid ? CellWidth : width;
            var cellHeight = isAtlasGrid ? CellHeight : height;
            NormalizeCell(pixels, stride, x, y, cellWidth, cellHeight, excludedRegion);
        }

        var result = new WriteableBitmap(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    private static void NormalizeCell(byte[] pixels, int stride, int x0, int y0, int width, int height, Int32Rect? excludedRegion)
    {
        double brownWeight = 0, brownR = 0, brownG = 0, brownB = 0;
        double whiteWeight = 0, whiteR = 0, whiteG = 0, whiteB = 0;
        for (var y = y0; y < y0 + height; y++)
        for (var x = x0; x < x0 + width; x++)
        {
            if (IsExcluded(excludedRegion, x, y)) continue;
            var i = y * stride + x * 4;
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            if (pixels[i + 3] == 0) continue;
            var luma = Luma(r, g, b);
            if (luma < MinimumLuma) continue;
            var brown = BrownWeight(r, g, b);
            brownWeight += brown; brownR += r * brown; brownG += g * brown; brownB += b * brown;
            var white = WhiteWeight(r, g, b, luma);
            whiteWeight += white; whiteR += r * white; whiteG += g * white; whiteB += b * white;
        }

        // Shared neutral taupe/ivory targets visually calibrated against the
        // photo references. Keeping these targets identical across atlas, feeding,
        // and long-body caches prevents a visible orange cast on transitions.
        var dr = brownWeight > 0.01 ? Math.Clamp(181 - brownR / brownWeight, -60, 48) : 0;
        var dg = brownWeight > 0.01 ? Math.Clamp(156 - brownG / brownWeight, -60, 48) : 0;
        var db = brownWeight > 0.01 ? Math.Clamp(133 - brownB / brownWeight, -60, 48) : 0;
        var wr = whiteWeight > 0.01 ? Math.Clamp(237 - whiteR / whiteWeight, -24, 30) : 0;
        var wg = whiteWeight > 0.01 ? Math.Clamp(234 - whiteG / whiteWeight, -24, 30) : 0;
        var wb = whiteWeight > 0.01 ? Math.Clamp(228 - whiteB / whiteWeight, -24, 30) : 0;

        for (var y = y0; y < y0 + height; y++)
        for (var x = x0; x < x0 + width; x++)
        {
            if (IsExcluded(excludedRegion, x, y)) continue;
            var i = y * stride + x * 4;
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            if (pixels[i + 3] == 0) continue;
            var luma = Luma(r, g, b);
            if (luma < MinimumLuma) continue;
            var brown = BrownWeight(r, g, b);
            if (brown > 0)
            {
                var f = 0.90 + 0.10 * brown;
                var darkLift = 20 * (1 - SmoothStep(88, 130, luma));
                pixels[i] = Add(b, db * f + darkLift); pixels[i + 1] = Add(g, dg * f + darkLift); pixels[i + 2] = Add(r, dr * f + darkLift);
                continue;
            }
            var white = WhiteWeight(r, g, b, luma);
            if (white > 0)
            {
                var f = 0.75 + 0.25 * white;
                pixels[i] = Add(b, wb * f); pixels[i + 1] = Add(g, wg * f); pixels[i + 2] = Add(r, wr * f);
            }
        }
    }

    private static bool IsExcluded(Int32Rect? region, int x, int y) =>
        region is { } rect
        && x >= rect.X && x < rect.X + rect.Width
        && y >= rect.Y && y < rect.Y + rect.Height;

    private static double BrownWeight(byte r, byte g, byte b)
    {
        var luma = Luma(r, g, b);
        if (luma < MinimumLuma) return 0;
        if (r > 190 && r - g > 18 && g - b < 6) return 0;
        var vivid = SmoothStep(4, 26, r - g) * SmoothStep(2, 24, g - b) * SmoothStep(12, 70, r - b);
        // The real coat also contains low-saturation taupe/grey-brown fur.
        // Keep this branch below the white range and reject the high-red pink
        // skin colours, while retaining a continuous edge weight.
        var range = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        var muted = SmoothStep(-6, 14, r - g) * SmoothStep(-4, 14, g - b)
            * SmoothStep(6, 55, r - b) * (1 - SmoothStep(180, 200, luma));
        var pinkGuard = 1 - SmoothStep(190, 230, r) * SmoothStep(12, 36, r - g) * (1 - SmoothStep(2, 14, g - b));
        var neutralGuard = 1 - SmoothStep(8, 22, range);
        return Math.Max(vivid, muted * pinkGuard * (1 - 0.35 * neutralGuard));
    }

    private static double WhiteWeight(byte r, byte g, byte b, double luma)
    {
        if (luma < 155 || r - b > 34 || r - g > 30 || g - b > 30) return 0;
        var range = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        return SmoothStep(155, 190, luma) * (1 - SmoothStep(18, 42, range));
    }

    private static double Luma(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
    private static double SmoothStep(double a, double b, double value)
    {
        var t = Math.Clamp((value - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
    private static byte Add(byte value, double amount) => (byte)Math.Clamp(Math.Round(value + amount), 0, 255);

    private static BitmapSource ToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32) return source;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}
