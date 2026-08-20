using System.IO;
using System.Windows.Media.Imaging;

namespace ShuBingDesktopPet.Services;

/// <summary>Preloads the optional elongated-body walking frames once at startup.</summary>
public sealed class WalkingBodyFrameCache
{
    private const int FrameCount = 8;
    public const int ExpectedFrameWidth = 256;
    public const int ExpectedFrameHeight = 208;
    private readonly BitmapSource?[] _left = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _right = new BitmapSource?[FrameCount];

    public WalkingBodyFrameCache(string? baseDirectory = null, Func<string, Stream?>? resourceStreamFactory = null)
    {
        var root = Path.Combine(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "Assets", "Walking", "LongBody");
        var allowFallback = resourceStreamFactory is not null || baseDirectory is null ||
            string.Equals(baseDirectory, AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase);
        resourceStreamFactory ??= PackResourceStream;
        Load(_left, Path.Combine(root, "Left"), "assets/walking/longbody/left", allowFallback, resourceStreamFactory);
        Load(_right, Path.Combine(root, "Right"), "assets/walking/longbody/right", allowFallback, resourceStreamFactory);
    }

    public bool IsAvailable => _left.All(IsExpectedFrame) && _right.All(IsExpectedFrame);

    public BitmapSource? GetFrame(bool movingRight, int column)
    {
        var frames = movingRight ? _right : _left;
        return frames[Math.Clamp(column, 0, FrameCount - 1)];
    }

    private static bool IsExpectedFrame(BitmapSource? frame) =>
        frame is { PixelWidth: ExpectedFrameWidth, PixelHeight: ExpectedFrameHeight };

    private static void Load(BitmapSource?[] destination, string folder, string resourceFolder, bool allowFallback, Func<string, Stream?> resourceFactory)
    {
        for (var i = 0; i < FrameCount; i++)
        {
            var path = Path.Combine(folder, $"{i:00}.png");
            destination[i] = LoadOne(path, $"{resourceFolder}/{i:00}.png", allowFallback, resourceFactory);
        }
    }

    private static BitmapSource? LoadOne(string filePath, string resourcePath, bool allowFallback, Func<string, Stream?> resourceFactory)
    {
        try
        {
            Stream? stream = File.Exists(filePath) ? File.OpenRead(filePath) : (allowFallback ? resourceFactory(resourcePath) : null);
            if (stream is null) return null;
            using (stream)
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                memory.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memory;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
        catch { return null; }
    }

    private static Stream? PackResourceStream(string relativePath)
    {
        try
        {
            var assemblyName = typeof(WalkingBodyFrameCache).Assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName)) return null;
            var uri = new Uri($"pack://application:,,/{assemblyName};component/{relativePath}", UriKind.Absolute);
            return global::System.Windows.Application.GetResourceStream(uri)?.Stream;
        }
        catch { return null; }
    }
}
