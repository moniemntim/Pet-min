using System;
using System.IO;
using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Resources;
using System.Security.Cryptography;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class Rc5MotionAndResourceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnimationPlanFactory_Row1AndRow2_HaveUniform120msCadenceAndLoop(int row)
    {
        var spec = AnimationPlanFactory.RowSpec(row);
        spec.FrameCount.Should().Be(8);
        spec.Durations.Should().AllSatisfy(duration => duration.Should().Be(120));
        spec.Loop.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnimationSequencer_Row1And2_WrapsWithoutLastFrameHitch(int row)
    {
        var sequencer = new AnimationSequencer();
        var spec = AnimationPlanFactory.RowSpec(row);
        sequencer.ApplyPlan(AnimationPlanFactory.CreateRowPlan(row), spec);

        sequencer.Tick(1080, spec);

        sequencer.Column.Should().Be(1);
        sequencer.CompletedOneShot.Should().BeFalse();
    }

    [Theory]
    [InlineData(200, 100, 650, 110, 1)]
    [InlineData(650, 100, 200, 110, 2)]
    [InlineData(400, 100, 400, 10, 1)]
    public void RoamingDirectionFromTargetPosition_MapsToExpectedWalkingRow(double currentX, double currentY, double targetX, double targetY, int expectedRow)
    {
        var direction = RoamingScreenTransition.DirectionToTarget(currentX, currentY, targetX, targetY);

        (direction.X >= 0 ? 1 : 2).Should().Be(expectedRow);
    }

    [Fact]
    public void Atlas_WalkingDirections_HaveStableCanvasBaselineAndArea()
    {
        const int frameWidth = 192;
        const int frameHeight = 208;
        const int columns = 8;
        var repoRoot = FindRepoRoot();
        var atlasPath = Path.Combine(repoRoot, "src", "ShuBingDesktopPet", "Assets", "spritesheet.png");
        var bitmap = LoadBgra32(atlasPath);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var leftAreas = new List<int>();
        var rightAreas = new List<int>();
        for (var column = 0; column < columns; column++)
        {
            leftAreas.Add(MeasureOpaqueArea(pixels, stride, column * frameWidth, frameHeight, frameWidth, frameHeight));
            rightAreas.Add(MeasureOpaqueArea(pixels, stride, column * frameWidth, 2 * frameHeight, frameWidth, frameHeight));
            MeasureBottom(pixels, stride, column * frameWidth, frameHeight, frameWidth, frameHeight)
                .Should().BeInRange(168, 170);
            MeasureBottom(pixels, stride, column * frameWidth, 2 * frameHeight, frameWidth, frameHeight)
                .Should().BeInRange(168, 170);
            TouchesFrameEdge(pixels, stride, column * frameWidth, frameHeight, frameWidth, frameHeight)
                .Should().BeFalse();
            TouchesFrameEdge(pixels, stride, column * frameWidth, 2 * frameHeight, frameWidth, frameHeight)
                .Should().BeFalse();
        }

        ((leftAreas.Max() - leftAreas.Min()) * 100).Should().BeLessThan(leftAreas.OrderBy(x => x).ElementAt(4) * 12);
        ((rightAreas.Max() - rightAreas.Min()) * 100).Should().BeLessThan(rightAreas.OrderBy(x => x).ElementAt(4) * 12);
        leftAreas.Zip(rightAreas).Any(pair => pair.First != pair.Second).Should().BeTrue(
            "directional resources should retain genuine differences rather than being exact mirrors");
    }

    private static int MeasureOpaqueArea(byte[] pixels, int stride, int x0, int y0, int width, int height)
    {
        var area = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (pixels[(y0 + y) * stride + (x0 + x) * 4 + 3] > 127) area++;
        return area;
    }

    private static int MeasureBottom(byte[] pixels, int stride, int x0, int y0, int width, int height)
    {
        for (var y = height - 1; y >= 0; y--)
        for (var x = 0; x < width; x++)
            if (pixels[(y0 + y) * stride + (x0 + x) * 4 + 3] > 127) return y;
        return -1;
    }

    private static bool TouchesFrameEdge(byte[] pixels, int stride, int x0, int y0, int width, int height)
    {
        for (var x = 0; x < width; x++)
        {
            if (pixels[y0 * stride + (x0 + x) * 4 + 3] > 127
                || pixels[(y0 + height - 1) * stride + (x0 + x) * 4 + 3] > 127)
                return true;
        }
        for (var y = 1; y < height - 1; y++)
        {
            if (pixels[(y0 + y) * stride + x0 * 4 + 3] > 127
                || pixels[(y0 + y) * stride + (x0 + width - 1) * 4 + 3] > 127)
                return true;
        }
        return false;
    }

    [Fact]
    public void FeedingFrameCache_FallsBackToResourceStream_WhenBaseDirectoryMissing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new FeedingFrameCache(missingRoot, _ => CreateFeedingFrameStream());

            var frame = cache.GetFrame(PetFoodType.Apple, 0);

            frame.Should().NotBeNull();
            frame!.PixelWidth.Should().Be(192);
            frame.PixelHeight.Should().Be(208);
            frame.IsFrozen.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(missingRoot))
            {
                Directory.Delete(missingRoot, true);
            }
        }
    }

    [Fact]
    public void EmbeddedFeedingAndLongBodyResources_MatchSourcePngBytes()
    {
        var root = FindRepoRoot();
        using var resourceStream = typeof(FeedingFrameCache).Assembly
            .GetManifestResourceStream("ShuBingDesktopPet.g.resources");
        resourceStream.Should().NotBeNull("the compiled resource bundle must be present");
        using var resources = new ResourceSet(resourceStream!);

        var paths = new List<string>();
        foreach (var food in new[] { "apple", "carrot", "pellet", "broccoli", "bokchoy" })
        for (var column = 0; column < 6; column++)
            paths.Add($"assets/feeding/{food}/{column:00}.png");
        foreach (var side in new[] { "left", "right" })
        for (var column = 0; column < 8; column++)
            paths.Add($"assets/walking/longbody/{side}/{column:00}.png");

        paths.Should().HaveCount(46);
        foreach (var relative in paths)
        {
            var resource = resources.GetObject(relative).Should().BeAssignableTo<Stream>().Subject;
            using (resource)
            {
                using var actual = new MemoryStream();
                resource.CopyTo(actual);
                var assetRelative = relative["assets/".Length..];
                var sourcePath = Path.Combine(root, "src", "ShuBingDesktopPet", "Assets",
                    assetRelative.Replace('/', Path.DirectorySeparatorChar));
                File.Exists(sourcePath).Should().BeTrue(sourcePath);
                Convert.ToHexString(SHA256.HashData(actual.ToArray()))
                    .Should().Be(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))), relative);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ShuBingDesktopPet.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Could not resolve repository root from test working directory.");
        }

        return current.FullName;
    }

    private static Stream CreateFeedingFrameStream()
    {
        const int width = 192;
        const int height = 208;
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
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
}
