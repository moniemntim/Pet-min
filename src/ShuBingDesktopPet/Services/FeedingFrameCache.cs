using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Resources;

namespace ShuBingDesktopPet.Services;

public sealed class FeedingFrameCache
{
    private const int FrameCount = 6;
    private readonly BitmapSource?[] _appleFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _carrotFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _pelletFrames = new BitmapSource?[FrameCount];
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
        _appleFrames = LoadFrames(Path.Combine(feedingRoot, "Apple"), "assets/feeding/apple");
        _carrotFrames = LoadFrames(Path.Combine(feedingRoot, "Carrot"), "assets/feeding/carrot");
        _pelletFrames = LoadFrames(Path.Combine(feedingRoot, "Pellet"), "assets/feeding/pellet");
    }

    public BitmapSource? GetFrame(Models.PetFoodType food, int column)
    {
        var clampedColumn = Math.Clamp(column, 0, FrameCount - 1);
        return food switch
        {
            Models.PetFoodType.Apple => _appleFrames[clampedColumn],
            Models.PetFoodType.Carrot => _carrotFrames[clampedColumn],
            Models.PetFoodType.Pellet => _pelletFrames[clampedColumn],
            _ => null
        };
    }

    private BitmapSource?[] LoadFrames(string folderPath, string resourceFolder)
    {
        var frames = new BitmapSource?[FrameCount];
        for (var i = 0; i < FrameCount; i++)
        {
            var filePath = Path.Combine(folderPath, $"{i:00}.png");
            var resourcePath = $"{resourceFolder}/{i:00}.png";
            frames[i] = LoadFrame(filePath, resourcePath);
        }

        return frames;
    }

    private BitmapSource? LoadFrame(string filePath, string resourcePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;
                return CreateBitmap(memory);
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
                return CreateBitmap(memory);
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
