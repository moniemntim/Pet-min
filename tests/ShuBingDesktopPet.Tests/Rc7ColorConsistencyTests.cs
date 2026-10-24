using FluentAssertions;
using ShuBingDesktopPet.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class Rc7ColorConsistencyTests
{
    [Fact]
    public void Normalize_PreservesAlphaTransparentRgbAndFeatureColours()
    {
        var source = CreatePixels(new byte[]
        {
            60, 90, 130, 255,   // visible brown
            150, 70, 220, 255,  // pink skin (B > G)
            0, 0, 0, 255,       // eye/near-black
            17, 23, 29, 0       // transparent RGB must survive byte-for-byte
        });

        var result = CoatPaletteNormalizer.Normalize(source);
        var actual = CopyPixels(result);

        result.IsFrozen.Should().BeTrue();
        actual[3].Should().Be(255);
        actual[7].Should().Be(255);
        actual[11].Should().Be(255);
        actual[15].Should().Be(0);
        actual[12].Should().Be(17);
        actual[13].Should().Be(23);
        actual[14].Should().Be(29);
        actual[0].Should().BeGreaterThan(60);
        actual[1].Should().BeGreaterThan(90);
        actual[2].Should().BeGreaterThan(130);
        actual[4].Should().Be(150);
        actual[5].Should().Be(70);
        actual[6].Should().Be(220);
        actual[8].Should().Be(0);
        actual[9].Should().Be(0);
        actual[10].Should().Be(0);
    }

    [Fact]
    public void Normalize_LeavesDimensionsAndGeometryUntouched()
    {
        var source = CreateBitmap(23, 17);
        var result = CoatPaletteNormalizer.Normalize(source);

        result.PixelWidth.Should().Be(source.PixelWidth);
        result.PixelHeight.Should().Be(source.PixelHeight);
        result.Format.Should().Be(PixelFormats.Bgra32);
        CopyAlpha(source).Should().Equal(CopyAlpha(result));
    }

    [Fact]
    public void Normalize_AlsoCorrectsLowSaturationTaupeBrownWithoutTouchingPink()
    {
        var source = CreatePixels(new byte[]
        {
            137, 150, 165, 255, // muted taupe-brown, BGR
            150, 150, 220, 255, // pink skin, BGR
            0, 0, 0, 255,
            0, 0, 0, 0
        });
        var actual = CopyPixels(CoatPaletteNormalizer.Normalize(source));
        var correctedLuma = Luma(actual[2], actual[1], actual[0]);
        correctedLuma.Should().BeGreaterThan(Luma(165, 150, 137));
        actual[2].Should().BeGreaterThan(165);
        actual[4].Should().Be(150);
        actual[5].Should().Be(150);
        actual[6].Should().Be(220);
    }

    [Fact]
    public void Normalize_ExcludedRegionRemainsByteStable()
    {
        var source = CreatePixels(new byte[]
        {
            30, 80, 130, 255, // excluded brown swatch, BGR
            30, 80, 130, 255, // normalized brown swatch, BGR
            0, 0, 0, 255,
            0, 0, 0, 0
        });

        var actual = CopyPixels(CoatPaletteNormalizer.Normalize(source, new Int32Rect(0, 0, 1, 1)));
        actual[0].Should().Be(30);
        actual[1].Should().Be(80);
        actual[2].Should().Be(130);
        actual[4].Should().NotBe(30);
        actual[5].Should().NotBe(80);
        actual[6].Should().NotBe(130);
    }

    [Fact]
    public void SpriteSheet_LoadNormalizesOnceAndFrameCacheReusesFrozenFrames()
    {
        var root = FindAssetRoot();
        var sheet = SpriteSheet.Load(root);
        sheet.IsValid.Should().BeTrue();
        sheet.Source.Should().NotBeNull();
        sheet.Source!.IsFrozen.Should().BeTrue();
        sheet.Source.Format.Should().Be(PixelFormats.Bgra32);

        var cache = new SpriteFrameCache(sheet.Source, sheet.Rows, sheet.Columns,
            sheet.FrameWidth, sheet.FrameHeight);
        var first = cache.GetFrame(0, 0);
        var again = cache.GetFrame(0, 0);
        first.Should().NotBeNull();
        first!.IsFrozen.Should().BeTrue();
        again.Should().BeSameAs(first);
    }

    [Fact]
    public void SpriteSheet_NormalizedAtlasHasStablePerCellCoatLuminance()
    {
        var root = FindAssetRoot();
        var sheet = SpriteSheet.Load(root);
        sheet.Source.Should().NotBeNull();
        var source = sheet.Source!;
        var raw = LoadBitmap(Path.Combine(root, "Assets", "spritesheet.png"));
        var rawPixels = CopyPixels(raw);
        var normalizedPixels = CopyPixels(source);
        for (var i = 3; i < rawPixels.Length; i += 4)
            normalizedPixels[i].Should().Be(rawPixels[i]);
        var means = new List<double>();
        var darkFractions = new List<double>();
        var whiteMeans = new List<double>();
        for (var row = 0; row < 11; row++)
        for (var column = 0; column < 8; column++)
        {
            var frame = new CroppedBitmap(source, new Int32Rect(column * 192, row * 208, 192, 208));
            var pixels = CopyPixels(frame);
            var brown = new List<double>();
            var white = new List<double>();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                if (BrownWeight(r, g, b) > 0) brown.Add(Luma(r, g, b));
                if (WhiteWeight(r, g, b) > 0) white.Add(Luma(r, g, b));
            }
            if (brown.Count < 80) continue;
            if (white.Count >= 80) whiteMeans.Add(white.Average());
            var mean = brown.Average();
            var dark = brown.Count(x => x < 130) / (double)brown.Count;
            means.Add(mean);
            darkFractions.Add(dark);
            Console.WriteLine($"cell {row},{column}: luma={mean:F1}, dark={dark:F3}");
        }

        means.Should().NotBeEmpty();
        Console.WriteLine($"RC7 atlas brown luma min={means.Min():F1}, max={means.Max():F1}, dark max={darkFractions.Max():F3}");
        means.Min().Should().BeGreaterThanOrEqualTo(155);
        // The neutral taupe target is luma 159.7; bright fur highlights may
        // remain above the target without indicating an orange cast.
        means.Max().Should().BeLessThanOrEqualTo(180);
        darkFractions.Max().Should().BeLessThan(0.25);
        (means.Max() - means.Min()).Should().BeLessThanOrEqualTo(15);
        whiteMeans.Should().NotBeEmpty();
        Console.WriteLine($"RC7 atlas white luma min={whiteMeans.Min():F1}, max={whiteMeans.Max():F1}");
        // The shared ivory target is 237/234/228; authored shadow pixels can
        // remain around luma 211 while retaining the intended white coat.
        whiteMeans.Min().Should().BeGreaterThanOrEqualTo(210);
        whiteMeans.Max().Should().BeLessThanOrEqualTo(235);
    }

    [Fact]
    public void SharedPalette_NormalizesAtlasAndLongBodyHeadBrightness()
    {
        var root = FindAssetRoot();
        var sheet = SpriteSheet.Load(root);
        sheet.Source.Should().NotBeNull();

        var idleLumas = new List<double>();
        for (var column = 0; column < 6; column++)
        {
            var frame = new CroppedBitmap(sheet.Source,
                new Int32Rect(column * sheet.FrameWidth, 0, sheet.FrameWidth, sheet.FrameHeight));
            idleLumas.Add(MeasureHeadBrownLuma(frame, frontFacing: true));
        }

        var longLumas = new List<double>();
        var longBodyCache = new WalkingBodyFrameCache(root);
        for (var column = 0; column < 8; column++)
        {
            longLumas.Add(MeasureHeadBrownLuma(longBodyCache.GetFrame(false, column)!, frontFacing: false));
        }

        idleLumas.Min().Should().BeGreaterThanOrEqualTo(155,
            "every standard idle frame must avoid the dark face seen before rc.7");
        Math.Abs(idleLumas.Average() - longLumas.Average()).Should().BeLessThanOrEqualTo(6,
            "stopping long-body walking must not look like switching to a darker hamster");
    }

    private static WriteableBitmap CreatePixels(byte[] pixels)
    {
        var bitmap = new WriteableBitmap(4, 1, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, 4, 1), pixels, 16, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static WriteableBitmap CreateBitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 60;
            pixels[i + 1] = 90;
            pixels[i + 2] = 130;
            pixels[i + 3] = (byte)(i % 8 == 0 ? 0 : 255);
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] CopyPixels(BitmapSource source)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    private static double MeasureHeadBrownLuma(BitmapSource source, bool frontFacing)
    {
        var bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = CopyPixels(bgra);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] <= 16)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        maxX.Should().BeGreaterThanOrEqualTo(minX);
        var bodyWidth = maxX - minX + 1;
        var bodyHeight = maxY - minY + 1;
        var lumas = new List<double>();
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var insideHead = frontFacing
                    ? x >= minX + (bodyWidth * 0.12) && x < maxX + 1 - (bodyWidth * 0.12)
                        && y < minY + (bodyHeight * 0.64)
                    : x < minX + (bodyWidth * 0.40) && y < minY + (bodyHeight * 0.74);
                if (!insideHead)
                {
                    continue;
                }

                var offset = (y * width + x) * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var alpha = pixels[offset + 3];
                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                var saturation = max == 0 ? 0 : (max - min) / (double)max;
                var luma = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
                if (alpha > 127 && red > green + 7 && green > blue + 1
                    && saturation > 0.09 && luma > 58 && red < 248)
                {
                    lumas.Add(luma);
                }
            }
        }

        lumas.Count.Should().BeGreaterThan(100);
        return lumas.Average();
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static byte[] CopyAlpha(BitmapSource source)
    {
        var pixels = CopyPixels(CoatPaletteNormalizer.Normalize(source));
        var alpha = new byte[pixels.Length / 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[i * 4 + 3];
        }
        return alpha;
    }

    private static double BrownWeight(byte r, byte g, byte b)
    {
        var luma = Luma(r, g, b);
        if (luma < 58) return 0;
        if (r > 190 && r - g > 18 && g - b < 6) return 0;
        var vivid = SmoothStep(4, 26, r - g) * SmoothStep(2, 24, g - b) * SmoothStep(12, 70, r - b);
        var range = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        var muted = SmoothStep(-6, 14, r - g) * SmoothStep(-4, 14, g - b)
            * SmoothStep(6, 55, r - b) * (1 - SmoothStep(180, 200, luma));
        var pinkGuard = 1 - SmoothStep(190, 230, r) * SmoothStep(12, 36, r - g) * (1 - SmoothStep(2, 14, g - b));
        var neutralGuard = 1 - SmoothStep(8, 22, range);
        return Math.Max(vivid, muted * pinkGuard * (1 - 0.35 * neutralGuard));
    }

    private static double WhiteWeight(byte r, byte g, byte b)
    {
        var luma = Luma(r, g, b);
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

    private static string FindAssetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var sourceRoot = Path.Combine(directory.FullName, "src", "ShuBingDesktopPet");
            if (File.Exists(Path.Combine(sourceRoot, "Assets", "spritesheet.png")))
            {
                return sourceRoot;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
