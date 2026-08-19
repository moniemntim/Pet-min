using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class FeedingFrameCacheTests
{
    [Fact]
    public void FeedingFrameCache_BundledAssetsContainSixFrozenFramesPerFood()
    {
        var cache = new FeedingFrameCache(AppContext.BaseDirectory);

        foreach (var food in new[] { PetFoodType.Apple, PetFoodType.Carrot, PetFoodType.Pellet })
        {
            for (var column = 0; column < 6; column++)
            {
                var frame = cache.GetFrame(food, column);
                frame.Should().NotBeNull();
                frame!.PixelWidth.Should().Be(192);
                frame.PixelHeight.Should().Be(208);
                frame.IsFrozen.Should().BeTrue();
            }
        }
    }

    [Fact]
    public void FeedingFrameCache_LoadsFramesAndFallsBackToNullForMissingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new FeedingFrameCache(root);

            cache.GetFrame(PetFoodType.Apple, 0).Should().BeNull();
            cache.GetFrame(PetFoodType.Carrot, 5).Should().BeNull();
            cache.GetFrame(PetFoodType.Pellet, -10).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void FeedingFrameCache_ClampColumn_UsesCachedFrames_WhenLoaded()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetRoot = Path.Combine(root, "Assets", "Feeding", "Apple");
        Directory.CreateDirectory(assetRoot);

        try
        {
            for (var i = 0; i < 6; i++)
            {
                CreatePng(Path.Combine(assetRoot, $"{i:00}.png"), Colors.CornflowerBlue, alpha: 255);
            }

            var cache = new FeedingFrameCache(root);
            var firstFrame = cache.GetFrame(PetFoodType.Apple, 0);
            var lastFrame = cache.GetFrame(PetFoodType.Apple, 5);
            firstFrame.Should().NotBeNull();
            lastFrame.Should().NotBeNull();
            firstFrame!.IsFrozen.Should().BeTrue();
            lastFrame!.IsFrozen.Should().BeTrue();
            cache.GetFrame(PetFoodType.Apple, -1).Should().BeSameAs(firstFrame);
            cache.GetFrame(PetFoodType.Apple, 99).Should().BeSameAs(lastFrame);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void FeedingFrameCache_ReturnsNullForInvalidImageFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetRoot = Path.Combine(root, "Assets", "Feeding", "Pellet");
        Directory.CreateDirectory(assetRoot);
        File.WriteAllText(Path.Combine(assetRoot, "00.png"), "not-an-image");
        CreatePng(Path.Combine(assetRoot, "01.png"), Colors.Gainsboro, alpha: 255);

        try
        {
            var cache = new FeedingFrameCache(root);
            cache.GetFrame(PetFoodType.Pellet, 0).Should().BeNull();
            cache.GetFrame(PetFoodType.Pellet, 1).Should().NotBeNull();
            cache.GetFrame(PetFoodType.Pellet, 99).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void CreatePng(string path, Color color, byte alpha)
    {
        var width = 192;
        var height = 208;
        var pixels = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var start = i * 4;
            pixels[start] = color.B;
            pixels[start + 1] = color.G;
            pixels[start + 2] = color.R;
            pixels[start + 3] = alpha;
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
