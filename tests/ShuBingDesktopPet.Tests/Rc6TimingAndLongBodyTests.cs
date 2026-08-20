using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class Rc6TimingAndLongBodyTests
{
    [Fact]
    public void PetSettingsService_NormalizesTimingAndWalkingStyle()
    {
        var service = new PetSettingsService(Path.GetTempFileName());

        var defaults = service.Normalize(new PetSettings
        {
            FeedingDurationSeconds = double.NaN,
            ContinuousWalkDurationSeconds = double.PositiveInfinity,
            WalkingBodyStyle = (WalkingBodyStyle)999
        });
        defaults.FeedingDurationSeconds.Should().Be(4.0);
        defaults.ContinuousWalkDurationSeconds.Should().Be(8.0);
        defaults.WalkingBodyStyle.Should().Be(WalkingBodyStyle.Current);

        var clamped = service.Normalize(new PetSettings
        {
            FeedingDurationSeconds = 99,
            ContinuousWalkDurationSeconds = -1,
            WalkingBodyStyle = WalkingBodyStyle.LongBody
        });
        clamped.FeedingDurationSeconds.Should().Be(20);
        clamped.ContinuousWalkDurationSeconds.Should().Be(2);
        clamped.WalkingBodyStyle.Should().Be(WalkingBodyStyle.LongBody);
    }

    [Fact]
    public void PetSettingsService_RoundTripsLongBodyAndDurations()
    {
        var path = Path.GetTempFileName();
        var service = new PetSettingsService(path);
        service.Save(new PetSettings
        {
            FeedingDurationSeconds = 6.5,
            ContinuousWalkDurationSeconds = 17,
            WalkingBodyStyle = WalkingBodyStyle.LongBody
        });

        var json = File.ReadAllText(path);
        json.Should().Contain("\"WalkingBodyStyle\": \"longBody\"");

        var loaded = service.LoadOrDefault();
        loaded.SchemaVersion.Should().Be(4);
        loaded.FeedingDurationSeconds.Should().Be(6.5);
        loaded.ContinuousWalkDurationSeconds.Should().Be(17);
        loaded.WalkingBodyStyle.Should().Be(WalkingBodyStyle.LongBody);
    }

    [Fact]
    public void PetSettingsService_UnknownWalkingStyleDoesNotDiscardOtherSettings()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"RoamingEnabled\":false,\"Scale\":1.25,\"WalkingBodyStyle\":\"unknown\"}");

        var loaded = new PetSettingsService(path).LoadOrDefault();

        loaded.SchemaVersion.Should().Be(4);
        loaded.RoamingEnabled.Should().BeFalse();
        loaded.Scale.Should().Be(1.25);
        loaded.WalkingBodyStyle.Should().Be(WalkingBodyStyle.Current);
    }

    [Fact]
    public void FeedingController_CompletesAtConfiguredDurationAndThenStartsCooldown()
    {
        var controller = new FeedingController();
        var start = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        controller.TryStart(PetFoodType.Apple, start, isBusy: false, durationSeconds: 6.5).Should().BeTrue();
        controller.TryComplete(start.AddSeconds(6.499), out var earlyFood).Should().BeFalse();
        earlyFood.Should().Be(PetFoodType.None);
        controller.TryComplete(start.AddSeconds(6.5), out var completedFood).Should().BeTrue();
        completedFood.Should().Be(PetFoodType.Apple);
        controller.TryStart(PetFoodType.Carrot, start.AddSeconds(9.499), isBusy: false).Should().BeFalse();
        controller.TryStart(PetFoodType.Carrot, start.AddSeconds(9.5), isBusy: false).Should().BeTrue();
    }

    [Fact]
    public void RoamingWalkTimer_ExpiresAtConfiguredDurationAndHandlesInvalidValues()
    {
        var timer = new RoamingWalkTimer();
        timer.Start(5);

        timer.Advance(4.999).Should().BeFalse();
        timer.Advance(double.NaN).Should().BeFalse();
        timer.Advance(0.001).Should().BeTrue();
        timer.IsActive.Should().BeFalse();

        timer.Start(double.PositiveInfinity);
        timer.RemainingSeconds.Should().Be(8);
        timer.Start(0);
        timer.RemainingSeconds.Should().Be(2);
        timer.Start(100);
        timer.RemainingSeconds.Should().Be(60);
        timer.Reset();
        timer.IsActive.Should().BeFalse();
    }

    [Fact]
    public void WalkingBodyFrameCache_LoadsAllFrozenFallbackFrames()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cache = new WalkingBodyFrameCache(missingRoot, _ => CreateFrameStream());

        cache.IsAvailable.Should().BeTrue();
        for (var column = 0; column < 8; column++)
        {
            var left = cache.GetFrame(movingRight: false, column);
            var right = cache.GetFrame(movingRight: true, column);
            left.Should().NotBeNull();
            right.Should().NotBeNull();
            left!.PixelWidth.Should().Be(256);
            left.PixelHeight.Should().Be(208);
            left.IsFrozen.Should().BeTrue();
            right!.PixelWidth.Should().Be(256);
            right.PixelHeight.Should().Be(208);
            right!.IsFrozen.Should().BeTrue();
        }
    }

    [Fact]
    public void LongBodyAssets_AreCleanRegisteredAndExactMirrors()
    {
        var root = FindRepoRoot();
        var leftFolder = Path.Combine(root, "src", "ShuBingDesktopPet", "Assets", "Walking", "LongBody", "Left");
        var rightFolder = Path.Combine(root, "src", "ShuBingDesktopPet", "Assets", "Walking", "LongBody", "Right");

        for (var column = 0; column < 8; column++)
        {
            var left = LoadBgra32(Path.Combine(leftFolder, $"{column:00}.png"));
            var right = LoadBgra32(Path.Combine(rightFolder, $"{column:00}.png"));
            left.PixelWidth.Should().Be(256);
            left.PixelHeight.Should().Be(208);
            right.PixelWidth.Should().Be(256);
            right.PixelHeight.Should().Be(208);

            var leftPixels = CopyPixels(left);
            var rightPixels = CopyPixels(right);
            var bbox = AlphaBoundingBox(leftPixels, left.PixelWidth, left.PixelHeight);
            bbox.Should().NotBeNull();
            bbox!.Value.Width.Should().Be(246);
            (bbox.Value.Y + bbox.Value.Height - 1).Should().Be(170);
            bbox.Value.Height.Should().BeInRange(126, 131);
            HasHiddenRgb(leftPixels).Should().BeFalse();
            HasHiddenRgb(rightPixels).Should().BeFalse();
            HasOpaqueChromaGreen(leftPixels).Should().BeFalse();
            HasOpaqueChromaGreen(rightPixels).Should().BeFalse();

            for (var y = 0; y < left.PixelHeight; y++)
            {
                for (var x = 0; x < left.PixelWidth; x++)
                {
                    var leftOffset = (y * left.PixelWidth + x) * 4;
                    var rightOffset = (y * right.PixelWidth + (right.PixelWidth - 1 - x)) * 4;
                    leftPixels.AsSpan(leftOffset, 4).SequenceEqual(rightPixels.AsSpan(rightOffset, 4))
                        .Should().BeTrue($"long-body frame {column} must be an exact left/right mirror at ({x}, {y})");
                }
            }
        }
    }

    [Fact]
    public void LongBodyAssets_HaveBrightStableCoatAcrossAllEightFrames()
    {
        var root = FindRepoRoot();
        var leftFolder = Path.Combine(root, "src", "ShuBingDesktopPet", "Assets", "Walking", "LongBody", "Left");
        var headLumas = new List<double>();
        var torsoLumas = new List<double>();
        var whiteLumas = new List<double>();

        for (var column = 0; column < 8; column++)
        {
            var bitmap = LoadBgra32(Path.Combine(leftFolder, $"{column:00}.png"));
            var pixels = CopyPixels(bitmap);
            var bbox = AlphaBoundingBox(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
            bbox.Should().NotBeNull();

            var bounds = bbox!.Value;
            var head = MeasureRegion(pixels, bitmap.PixelWidth, bounds, x => x < bounds.X + bounds.Width * 0.40, y => y < bounds.Y + bounds.Height * 0.74, IsBrownCoat);
            var torso = MeasureRegion(pixels, bitmap.PixelWidth, bounds, x => x >= bounds.X + bounds.Width * 0.37, y => y < bounds.Y + bounds.Height * 0.82, IsBrownCoat);
            var white = MeasureRegion(pixels, bitmap.PixelWidth, bounds, x => true, y => y < bounds.Y + bounds.Height * 0.82, IsWhiteCoat);

            head.Count.Should().BeGreaterThan(30, $"frame {column} must contain enough brown pixels in the head region");
            torso.Count.Should().BeGreaterThan(30, $"frame {column} must contain enough brown pixels in the torso region");
            white.Count.Should().BeGreaterThan(30, $"frame {column} must contain enough light coat pixels");
            headLumas.Add(head.MeanLuma);
            torsoLumas.Add(torso.MeanLuma);
            whiteLumas.Add(white.MeanLuma);

            head.MeanLuma.Should().BeGreaterThanOrEqualTo(150, $"frame {column} head coat must not be too dark");
            head.DarkFraction.Should().BeLessThanOrEqualTo(0.25, $"frame {column} head coat must not contain a dark face mask");
        }

        (headLumas.Max() - headLumas.Min()).Should().BeLessThanOrEqualTo(8, "head coat must not flash between walking frames");
        (torsoLumas.Max() - torsoLumas.Min()).Should().BeLessThanOrEqualTo(8, "torso coat must not flash between walking frames");
        (whiteLumas.Max() - whiteLumas.Min()).Should().BeLessThanOrEqualTo(6, "white coat must not flash between walking frames");
    }

    private static byte[] CopyPixels(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static bool HasHiddenRgb(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0 && (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOpaqueChromaGreen(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 0 && green > 200 && green > red * 1.35 && green > blue * 1.35)
            {
                return true;
            }
        }

        return false;
    }

    private static Int32Rect? AlphaBoundingBox(byte[] pixels, int width, int height)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX ? null : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ShuBingDesktopPet.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static Stream CreateFrameStream()
    {
        const int width = 256;
        const int height = 208;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 3] = 255;
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new MemoryStream(stream.ToArray());
    }

    private static BitmapSource LoadBgra32(string path)
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

    private static CoatStats MeasureRegion(
        byte[] pixels,
        int width,
        Int32Rect bounds,
        Func<double, bool> xPredicate,
        Func<double, bool> yPredicate,
        Func<int, int, int, bool> colorPredicate)
    {
        var lumas = new List<double>();
        var darkCount = 0;
        for (var y = bounds.Y; y < bounds.Y + bounds.Height; y++)
        {
            for (var x = bounds.X; x < bounds.X + bounds.Width; x++)
            {
                if (!xPredicate(x) || !yPredicate(y))
                {
                    continue;
                }

                var offset = (y * width + x) * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var alpha = pixels[offset + 3];
                if (alpha <= 127 || !colorPredicate(red, green, blue))
                {
                    continue;
                }

                var luma = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
                lumas.Add(luma);
                if (luma < 130)
                {
                    darkCount++;
                }
            }
        }

        return lumas.Count == 0
            ? new CoatStats(0, 1, 0)
            : new CoatStats(lumas.Average(), (double)darkCount / lumas.Count, lumas.Count);
    }

    private static bool IsBrownCoat(int red, int green, int blue) =>
        red > green + 7 && green > blue + 1 && ((red - blue) / 255.0) > 0.12;

    private static bool IsWhiteCoat(int red, int green, int blue) =>
        (0.2126 * red) + (0.7152 * green) + (0.0722 * blue) > 182
        && Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) <= 35;

    private readonly record struct CoatStats(double MeanLuma, double DarkFraction, int Count);
}
