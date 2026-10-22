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

        foreach (var food in new[] { PetFoodType.Apple, PetFoodType.Carrot, PetFoodType.Pellet, PetFoodType.Broccoli, PetFoodType.BokChoy })
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
    public void FeedingFrameCache_BroccoliFramesHaveGreenFoodPixelsAndDistinctFrames()
    {
        var cache = new FeedingFrameCache(AppContext.BaseDirectory);
        var frames = Enumerable.Range(0, 6).Select(i => cache.GetFrame(PetFoodType.Broccoli, i)).ToArray();
        frames.Should().OnlyContain(frame => frame != null);
        frames.Should().OnlyContain(frame => frame!.IsFrozen);
        frames.Distinct(ReferenceEqualityComparer.Instance).Should().HaveCount(6);

        foreach (var frame in frames)
        {
            var greenPixels = 0;
            var bitmap = new FormatConvertedBitmap(frame!, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                if (pixels[i + 3] > 200 && g > r + 15 && g > b + 10)
                {
                    greenPixels++;
                }
            }
            greenPixels.Should().BeGreaterThan(20, "each frame should retain visible green broccoli after coat normalization");
        }
    }

    [Fact]
    public void FeedingFrameCache_BokChoyFramesHaveNoLargeDetachedAlphaArtifacts()
    {
        var root = FindAssetRoot();
        var cache = new FeedingFrameCache(root);
        for (var column = 0; column < 6; column++)
        {
            var frame = cache.GetFrame(PetFoodType.BokChoy, column);
            frame.Should().NotBeNull();
            var pixels = CopyPixels(frame!);
            var components = AlphaComponents(pixels, frame!.PixelWidth, frame.PixelHeight, 127);
            components.Should().NotBeEmpty();
            if (components.Count > 1)
            {
                components.OrderByDescending(x => x).Skip(1).Should().OnlyContain(
                    size => size <= 8,
                    $"BokChoy frame {column} should not contain a large detached assembly artifact");
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

    private static byte[] CopyPixels(BitmapSource source)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    private static List<int> AlphaComponents(byte[] pixels, int width, int height, byte threshold)
    {
        var seen = new bool[width * height];
        var components = new List<int>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var start = y * width + x;
            if (seen[start] || pixels[start * 4 + 3] <= threshold) continue;
            var pending = new Stack<int>();
            pending.Push(start); seen[start] = true;
            var count = 0;
            while (pending.Count > 0)
            {
                var index = pending.Pop(); count++;
                var px = index % width; var py = index / width;
                foreach (var neighbor in new[] { (px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1) })
                {
                    if (neighbor.Item1 < 0 || neighbor.Item1 >= width || neighbor.Item2 < 0 || neighbor.Item2 >= height) continue;
                    var next = neighbor.Item2 * width + neighbor.Item1;
                    if (!seen[next] && pixels[next * 4 + 3] > threshold) { seen[next] = true; pending.Push(next); }
                }
            }
            components.Add(count);
        }
        return components;
    }

    private static string FindAssetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "src", "ShuBingDesktopPet");
            if (File.Exists(Path.Combine(root, "Assets", "Feeding", "BokChoy", "00.png"))) return root;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate feeding asset root.");
    }
}
