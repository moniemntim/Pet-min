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
        loaded.SchemaVersion.Should().Be(7);
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

        loaded.SchemaVersion.Should().Be(7);
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
            left.Should().BeSameAs(cache.GetFrame(movingRight: false, column));
            right!.PixelWidth.Should().Be(256);
            right.PixelHeight.Should().Be(208);
            right!.IsFrozen.Should().BeTrue();
            right.Should().BeSameAs(cache.GetFrame(movingRight: true, column));

            // The cache keeps the one-time normalized bitmap and preserves
            // transparent pixels while loading long-body assets.
            var leftPixels = CopyPixels(left);
            leftPixels[(10 * left.PixelWidth + 10) * 4 + 3].Should().Be(0);
            leftPixels[(100 * left.PixelWidth + 100) * 4 + 2].Should().BeGreaterThan(100);
        }
    }

    [Fact]
    public void LongBodyAssets_HaveStableCanvasAlphaAndBaseline()
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
            bbox!.Value.Width.Should().BeInRange(1, 256);
            bbox.Value.X.Should().BeGreaterThan(0);
            (bbox.Value.X + bbox.Value.Width - 1).Should().BeLessThan(left.PixelWidth - 1);
            bbox.Value.Y.Should().BeGreaterThan(0);
            (bbox.Value.Y + bbox.Value.Height - 1).Should().BeInRange(168, 172);
            bbox.Value.Height.Should().BeInRange(2, 208);
            var rightBox = AlphaBoundingBox(rightPixels, right.PixelWidth, right.PixelHeight);
            rightBox.Should().NotBeNull();
            rightBox!.Value.X.Should().BeGreaterThan(0);
            (rightBox.Value.X + rightBox.Value.Width - 1).Should().BeLessThan(right.PixelWidth - 1);
            rightBox.Value.Y.Should().BeGreaterThan(0);
            (rightBox.Value.Y + rightBox.Value.Height - 1).Should().BeInRange(168, 172);
            var opaqueLeft = AlphaBoundingBox(leftPixels, left.PixelWidth, left.PixelHeight, minimumAlpha: 127);
            var opaqueRight = AlphaBoundingBox(rightPixels, right.PixelWidth, right.PixelHeight, minimumAlpha: 127);
            opaqueLeft.Should().NotBeNull();
            opaqueRight.Should().NotBeNull();
            (opaqueLeft!.Value.Y + opaqueLeft.Value.Height).Should().Be(170);
            (opaqueRight!.Value.Y + opaqueRight.Value.Height).Should().Be(170);
            HasHiddenRgb(leftPixels).Should().BeFalse();
            HasHiddenRgb(rightPixels).Should().BeFalse();
            HasOpaqueChromaGreen(leftPixels).Should().BeFalse();
            HasOpaqueChromaGreen(rightPixels).Should().BeFalse();

            // Directional assets may share a pose, but must not be required to
            // be byte-for-byte mirrors: real coat markings and head turns are
            // allowed to differ between left and right.
            leftPixels.SequenceEqual(rightPixels).Should().BeFalse(
                $"long-body frame {column} should retain genuine directional differences");
        }
    }

    [Fact]
    public void VisualQaExport_IsOptInAndUsesDecodedNormalizedFrames()
    {
        var output = Environment.GetEnvironmentVariable("SHUBING_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        var root = FindRepoRoot();
        var assetRoot = Path.Combine(root, "src", "ShuBingDesktopPet");
        var sprite = SpriteSheet.Load(assetRoot);
        sprite.IsValid.Should().BeTrue();
        var atlas = new SpriteFrameCache(sprite.Source!, sprite.Rows, sprite.Columns,
            sprite.FrameWidth, sprite.FrameHeight);
        for (var row = 0; row < sprite.Rows; row++)
        for (var column = 0; column < sprite.Columns; column++)
        {
            SavePng(atlas.GetFrame(row, column), Path.Combine(output, $"atlas-{row:00}-{column:00}.png"));
        }

        var longBody = new WalkingBodyFrameCache(assetRoot);
        for (var column = 0; column < 8; column++)
        {
            SavePng(longBody.GetFrame(false, column), Path.Combine(output, $"long-left-{column:00}.png"));
            SavePng(longBody.GetFrame(true, column), Path.Combine(output, $"long-right-{column:00}.png"));
        }

        var feeding = new FeedingFrameCache(assetRoot);
        foreach (var food in new[] { PetFoodType.Pellet, PetFoodType.Broccoli, PetFoodType.BokChoy })
        for (var column = 0; column < 6; column++)
        {
            SavePng(feeding.GetFrame(food, column), Path.Combine(output, $"feeding-{food}-{column:00}.png"));
        }

        // Export a small scale comparison from the decoded atlas frame. This
        // exercises the same WPF bitmap scaling used by visual QA without
        // changing runtime assets.
        var sample = atlas.GetFrame(0, 0)!;
        foreach (var scale in new[] { 0.75, 1.0, 1.5 })
        {
            var transformed = new TransformedBitmap(sample,
                new System.Windows.Media.ScaleTransform(scale, scale));
            transformed.Freeze();
            SavePng(transformed, Path.Combine(output, $"atlas-00-00-{scale:0.00}x.png"));
        }
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

    private static Int32Rect? AlphaBoundingBox(byte[] pixels, int width, int height, byte minimumAlpha = 0)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] <= minimumAlpha)
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

        // A visible brown swatch plus transparent edge gives this fallback
        // stream enough signal to verify palette normalization and alpha.
        for (var y = 80; y < 120; y++)
        for (var x = 80; x < 160; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = 30;       // B
            pixels[i + 1] = 80;   // G
            pixels[i + 2] = 130;  // R
        }
        pixels[(10 * width + 10) * 4 + 3] = 0;

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

    private static void SavePng(BitmapSource? source, string path)
    {
        source.Should().NotBeNull();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source!));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
