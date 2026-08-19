using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuBingDesktopPet.Services;

public sealed class FeedingFrameCache
{
    private const int FrameCount = 6;
    private readonly BitmapSource?[] _appleFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _carrotFrames = new BitmapSource?[FrameCount];
    private readonly BitmapSource?[] _pelletFrames = new BitmapSource?[FrameCount];

    public FeedingFrameCache(string? baseDirectory = null)
    {
        var baseFolder = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        var feedingRoot = Path.Combine(baseFolder, "Assets", "Feeding");
        _appleFrames = LoadFrames(Path.Combine(feedingRoot, "Apple"));
        _carrotFrames = LoadFrames(Path.Combine(feedingRoot, "Carrot"));
        _pelletFrames = LoadFrames(Path.Combine(feedingRoot, "Pellet"));
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

    private static BitmapSource?[] LoadFrames(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return new BitmapSource?[FrameCount];
        }

        var frames = new BitmapSource?[FrameCount];
        for (var i = 0; i < FrameCount; i++)
        {
            frames[i] = LoadFrame(Path.Combine(folderPath, $"{i:00}.png"));
        }

        return frames;
    }

    private static BitmapSource? LoadFrame(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
