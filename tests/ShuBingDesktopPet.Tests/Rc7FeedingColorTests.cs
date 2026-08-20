using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class Rc7FeedingColorTests
{
    [Fact]
    public void FeedingFrames_NormalizeAllEighteenOnceAndPreserveGeometryAlphaAndFeatures()
    {
        var root = FindAssetRoot();
        var cache = new FeedingFrameCache(AppContext.BaseDirectory);
        var foods = new[] { PetFoodType.Apple, PetFoodType.Carrot, PetFoodType.Pellet };

        foreach (var food in foods)
        {
            var folder = food switch
            {
                PetFoodType.Apple => "Apple",
                PetFoodType.Carrot => "Carrot",
                PetFoodType.Pellet => "Pellet",
                _ => throw new ArgumentOutOfRangeException(nameof(food))
            };

            for (var column = 0; column < 6; column++)
            {
                var path = Path.Combine(root, "Assets", "Feeding", folder, $"{column:00}.png");
                var raw = LoadBitmap(path);
                var rawPixels = CopyPixels(raw);
                var frame = cache.GetFrame(food, column);
                var again = cache.GetFrame(food, column);

                frame.Should().NotBeNull();
                frame.Should().BeSameAs(again, "feeding frames should be normalized once and reused by the cache");
                frame!.IsFrozen.Should().BeTrue();
                frame.PixelWidth.Should().Be(raw.PixelWidth);
                frame.PixelHeight.Should().Be(raw.PixelHeight);
                frame.PixelWidth.Should().Be(192);
                frame.PixelHeight.Should().Be(208);

                var pixels = CopyPixels(frame);
                AlphaBytes(pixels).Should().Equal(AlphaBytes(rawPixels));
                CountPixels(rawPixels, IsBlackFeature).Should().BeGreaterThan(10);
                CountPixels(pixels, IsBlackFeature).Should().BeGreaterThan(10);
                CountPixels(rawPixels, IsPinkFeature).Should().BeGreaterThan(100);
                CountPixels(pixels, IsPinkFeature).Should().BeGreaterThan(100);

                var brown = MeasureBrown(pixels);
                brown.Count.Should().BeGreaterThan(100, $"{food} frame {column} must retain a measurable brown coat");
                brown.MeanLuma.Should().BeGreaterThanOrEqualTo(150, $"{food} frame {column} brown coat must not be too dark");
                brown.DarkFraction.Should().BeLessThanOrEqualTo(0.25, $"{food} frame {column} must not contain a darkened coat");
            }
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if (frame.Format == PixelFormats.Bgra32)
        {
            frame.Freeze();
            return frame;
        }

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] CopyPixels(BitmapSource source)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    private static byte[] AlphaBytes(byte[] pixels)
    {
        var alpha = new byte[pixels.Length / 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[i * 4 + 3];
        }

        return alpha;
    }

    private static int CountPixels(byte[] pixels, Func<int, int, int, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 127 && predicate(pixels[i + 2], pixels[i + 1], pixels[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsBlackFeature(int red, int green, int blue) =>
        Math.Max(red, Math.Max(green, blue)) < 40;

    private static bool IsPinkFeature(int red, int green, int blue) =>
        red > green + 20 && red > blue + 10;

    private static BrownStats MeasureBrown(byte[] pixels)
    {
        var lumas = new List<double>();
        var dark = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            if (pixels[i + 3] <= 127 || red <= green + 7 || green <= blue + 1 || red - blue <= 30)
            {
                continue;
            }

            var luma = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
            if (luma <= 58)
            {
                continue;
            }

            lumas.Add(luma);
            if (luma < 130)
            {
                dark++;
            }
        }

        return lumas.Count == 0
            ? new BrownStats(0, 1, 0)
            : new BrownStats(lumas.Average(), dark / (double)lumas.Count, lumas.Count);
    }

    private static string FindAssetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourceRoot = Path.Combine(directory.FullName, "src", "ShuBingDesktopPet");
            if (File.Exists(Path.Combine(sourceRoot, "Assets", "Feeding", "Apple", "00.png")))
            {
                return sourceRoot;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private readonly record struct BrownStats(double MeanLuma, double DarkFraction, int Count);
}
