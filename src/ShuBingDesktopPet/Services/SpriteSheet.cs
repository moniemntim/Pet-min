using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ShuBingDesktopPet.Services;

public sealed class SpriteSheet
{
    public BitmapImage? Source { get; init; }
    public int FrameWidth { get; init; } = 192;
    public int FrameHeight { get; init; } = 208;
    public int Columns { get; init; } = 8;
    public int Rows { get; init; } = 11;
    public bool LoadedFromFile { get; init; }

    public bool IsValid => Source != null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class SpriteManifest
    {
        [JsonPropertyName("spriteVersionNumber")]
        public int? SpriteVersionNumber { get; set; }

        [JsonPropertyName("spritesheetPath")]
        public string? SpritesheetPath { get; set; }
    }

    public static SpriteSheet Load(string rootFolder)
    {
        var assetsFolder = Path.Combine(rootFolder, "Assets");
        var manifestPath = Path.Combine(assetsFolder, "pet.json");
        var pngPath = Path.Combine(assetsFolder, "spritesheet.png");
        var fallbackPetPng = Path.Combine(assetsFolder, "pet.png");
        var fallbackPetWebP = Path.Combine(assetsFolder, "pet.webp");

        if (File.Exists(manifestPath))
        {
            try
            {
                var json = JsonSerializer.Deserialize<SpriteManifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (json?.SpriteVersionNumber == 2 && !string.IsNullOrWhiteSpace(json.SpritesheetPath))
                {
                    var configured = Path.Combine(assetsFolder, json.SpritesheetPath.Trim());
                    if (LoadValidatedSprite(configured) is { IsValid: true } configuredAtlas)
                    {
                        return configuredAtlas;
                    }
                }
            }
            catch
            {
                // fallback below
            }
        }

        if (LoadValidatedSprite(pngPath) is { IsValid: true } directPng)
        {
            return directPng;
        }

        if (LoadValidatedSprite(fallbackPetPng) is { IsValid: true } fallbackPng)
        {
            return fallbackPng;
        }

        if (LoadValidatedSprite(fallbackPetWebP) is { IsValid: true } fallbackWebp)
        {
            return fallbackWebp;
        }

        return new SpriteSheet();
    }

    private static SpriteSheet? LoadValidatedSprite(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var image = LoadImage(path);
        if (image is null)
        {
            return null;
        }

        if (!IsExpectedAtlasSize(image.PixelWidth, image.PixelHeight))
        {
            return null;
        }

        return new SpriteSheet
        {
            Source = image,
            FrameWidth = 192,
            FrameHeight = 208,
            Columns = 8,
            Rows = 11,
            LoadedFromFile = true
        };
    }

    private static bool IsExpectedAtlasSize(int pixelWidth, int pixelHeight)
    {
        return pixelWidth == 1536 && pixelHeight == 2288;
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = fs;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
