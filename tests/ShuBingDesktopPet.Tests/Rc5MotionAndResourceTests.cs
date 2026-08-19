using System;
using System.IO;
using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void Atlas_WalkingRightFrames_AreExactHorizontalMirrorsOfWalkingLeftFrames()
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

        for (var column = 0; column < columns; column++)
        {
            for (var y = 0; y < frameHeight; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    var rightOffset = ((frameHeight + y) * stride) + ((column * frameWidth + x) * 4);
                    var mirroredLeftOffset = (((2 * frameHeight) + y) * stride)
                        + ((column * frameWidth + (frameWidth - 1 - x)) * 4);

                    pixels.AsSpan(rightOffset, 4).SequenceEqual(pixels.AsSpan(mirroredLeftOffset, 4))
                        .Should().BeTrue($"walking row 1 frame {column} must mirror row 2 at ({x}, {y})");
                }
            }
        }
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
