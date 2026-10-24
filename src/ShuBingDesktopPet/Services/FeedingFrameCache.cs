using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Resources;

namespace ShuBingDesktopPet.Services;

public sealed class FeedingFrameCache
{
    private const int FrameCount = 6;
    // The authored seed-stick frames (including legacy enum aliases) occupy
    // these approximate regions. A small margin protects antialiased food and
    // nearby authored paw/white pixels from coat statistics.
    private static readonly Int32Rect[] SeedFoodRegions =
    {
        new(58, 106, 28, 46),
        new(53, 96, 28, 44),
        new(52, 100, 31, 47),
        new(58, 119, 30, 35),
        new(54, 105, 29, 39),
        new(57, 109, 30, 40)
    };
    private static readonly Int32Rect[] BokChoyFoodRegions =
    {
        new(54, 101, 30, 39),
        new(51, 97, 25, 22),
        new(51, 101, 23, 21),
        new(54, 118, 24, 22),
        new(50, 103, 25, 24),
        new(54, 106, 24, 25)
    };
    private readonly BitmapSource?[] _appleFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _carrotFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _pelletFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _broccoliFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _bokChoyFrames = new BitmapSource?[FrameCount];
    private readonly Func<string, Stream?> _resourceStreamFactory;
    private readonly bool _allowEmbeddedFallback;

    public FeedingFrameCache(string? baseDirectory = null, Func<string, Stream?>? resourceStreamFactory = null)
    {
        var baseFolder = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        var feedingRoot = Path.Combine(baseFolder, "Assets", "Feeding");
        _allowEmbeddedFallback = resourceStreamFactory is not null
            || baseDirectory is null
            || string.Equals(baseFolder, AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase);
        _resourceStreamFactory = resourceStreamFactory ?? PackResourceStream;
        _appleFrames = LoadFrames(Path.Combine(feedingRoot, "Apple"), "assets/feeding/apple", Models.PetFoodType.Apple);
        _carrotFrames = LoadFrames(Path.Combine(feedingRoot, "Carrot"), "assets/feeding/carrot", Models.PetFoodType.Carrot);
        _pelletFrames = LoadFrames(Path.Combine(feedingRoot, "Pellet"), "assets/feeding/pellet", Models.PetFoodType.Pellet);
        _broccoliFrames = LoadFrames(Path.Combine(feedingRoot, "Broccoli"), "assets/feeding/broccoli", Models.PetFoodType.Broccoli);
        _bokChoyFrames = LoadFrames(Path.Combine(feedingRoot, "BokChoy"), "assets/feeding/bokchoy", Models.PetFoodType.BokChoy);
    }

    public BitmapSource? GetFrame(Models.PetFoodType food, int column)
    {
        var clampedColumn = Math.Clamp(column, 0, FrameCount - 1);
        return food switch
        {
            Models.PetFoodType.Apple => _appleFrames[clampedColumn],
            Models.PetFoodType.Carrot => _carrotFrames[clampedColumn],
            Models.PetFoodType.Pellet => _pelletFrames[clampedColumn],
            Models.PetFoodType.Broccoli => _broccoliFrames[clampedColumn],
            Models.PetFoodType.BokChoy => _bokChoyFrames[clampedColumn],
            _ => null
        };
    }

    private BitmapSource?[] LoadFrames(string folderPath, string resourceFolder, Models.PetFoodType food)
    {
        var frames = new BitmapSource?[FrameCount];
        for (var i = 0; i < FrameCount; i++)
        {
            var filePath = Path.Combine(folderPath, $"{i:00}.png");
            var resourcePath = $"{resourceFolder}/{i:00}.png";
            frames[i] = LoadFrame(filePath, resourcePath, GetFoodExclusion(food, i));
        }

        return frames;
    }

    private BitmapSource? LoadFrame(string filePath, string resourcePath, Int32Rect? foodExclusion)
    {
        if (File.Exists(filePath))
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;
                return NormalizeLoadedFrame(memory, foodExclusion);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        try
        {
            if (!_allowEmbeddedFallback)
            {
                return null;
            }

            var stream = _resourceStreamFactory(resourcePath);
            if (stream is null)
            {
                return null;
            }

            using (stream)
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                memory.Position = 0;
                return NormalizeLoadedFrame(memory, foodExclusion);
            }
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? CreateBitmap(Stream source)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = source;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource? NormalizeLoadedFrame(Stream source, Int32Rect? foodExclusion)
    {
        var image = CreateBitmap(source);
        if (image is null)
        {
            return null;
        }

        try
        {
            // Feeding frames are independent files (not part of SpriteSheet), so
            // apply the same one-time coat correction while loading them. The
            // normalized result is frozen and retained by the per-food cache.
            return CoatPaletteNormalizer.Normalize(image, foodExclusion);
        }
        catch
        {
            // A malformed/unsupported colour format must not make feeding fail
            // when the decoded BitmapImage itself is usable.
            return image;
        }
    }

    private static Int32Rect? GetFoodExclusion(Models.PetFoodType food, int column) =>
        food is Models.PetFoodType.Apple or Models.PetFoodType.Carrot or Models.PetFoodType.Pellet
            ? SeedFoodRegions[Math.Clamp(column, 0, FrameCount - 1)]
            : food == Models.PetFoodType.BokChoy
                ? BokChoyFoodRegions[Math.Clamp(column, 0, FrameCount - 1)]
            : null;

    private static Stream? PackResourceStream(string relativePath)
    {
        try
        {
            var normalized = relativePath.Replace('\\', '/');
            var assemblyName = typeof(FeedingFrameCache).Assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return null;
            }

            var uri = new Uri($"pack://application:,,/{assemblyName};component/{normalized}".Replace(" ", "%20"), UriKind.Absolute);
            var streamInfo = global::System.Windows.Application.GetResourceStream(uri);
            return streamInfo?.Stream;
        }
        catch
        {
            // fallback handled below
        }

        try
        {
            using var gResourceStream = typeof(FeedingFrameCache).Assembly.GetManifestResourceStream("ShuBingDesktopPet.g.resources");
            if (gResourceStream is null)
            {
                return null;
            }

            var resourcePath = relativePath.Replace('\\', '/').ToLowerInvariant();
            using var resourceSet = new ResourceSet(gResourceStream);
            if (resourceSet.GetObject(resourcePath) is not Stream resourceStream)
            {
                return null;
            }

            using (resourceStream)
            using (var memory = new MemoryStream())
            {
                resourceStream.CopyTo(memory);
                memory.Position = 0;
                return new MemoryStream(memory.ToArray());
            }
        }
        catch
        {
            return null;
        }
    }
}
