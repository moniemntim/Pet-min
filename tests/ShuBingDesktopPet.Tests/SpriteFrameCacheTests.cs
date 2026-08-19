using FluentAssertions;
using ShuBingDesktopPet.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class SpriteFrameCacheTests
{
    [Fact]
    public void SpriteFrameCache_CreatesExpectedFrameCount_AndFramesAreFrozen()
    {
        var source = CreateTestBitmap(1536, 2288);
        var cache = new SpriteFrameCache(source, 11, 8, 192, 208);

        var cacheSet = new HashSet<CroppedBitmap>();
        for (var row = 0; row < 11; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                var frame = cache.GetFrame(row, col);
                frame.Should().NotBeNull();
                frame!.IsFrozen.Should().BeTrue();
                cacheSet.Add(frame);

                var sameFrame = cache.GetFrame(row, col);
                sameFrame.Should().NotBeNull();
                sameFrame.Should().BeSameAs(frame);
            }
        }

        cacheSet.Count.Should().Be(88);
        cache.CachedFrames.Should().Be(88);
    }

    [Fact]
    public void SpriteFrameCache_BoundsClampToValidRowsAndCols()
    {
        var source = CreateTestBitmap(1536, 2288);
        var cache = new SpriteFrameCache(source, 11, 8, 192, 208);
        var topLeft = cache.GetFrame(0, 0);
        var bottomRight = cache.GetFrame(10, 7);
        var belowBounds = cache.GetFrame(99, 99);
        var aboveBounds = cache.GetFrame(-1, -1);

        belowBounds.Should().BeSameAs(bottomRight);
        aboveBounds.Should().BeSameAs(topLeft);
    }

    [Fact]
    public void SpriteFrameCache_InvalidAtlasDoesNotBuildFrames()
    {
        var source = CreateTestBitmap(100, 100);
        var cache = new SpriteFrameCache(source, 11, 8, 192, 208);

        cache.CachedFrames.Should().Be(0);
        cache.GetFrame(0, 0).Should().BeNull();
    }

    [Fact]
    public void SpriteFrameCache_PartialAtlasDoesNotBuildFrames()
    {
        var source = CreateTestBitmap(192, 208);
        var cache = new SpriteFrameCache(source, 11, 8, 192, 208);

        cache.CachedFrames.Should().Be(0);
        cache.GetFrame(0, 0).Should().BeNull();
    }

    private static WriteableBitmap CreateTestBitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
