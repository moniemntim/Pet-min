using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public sealed class PetSettingsService
{
    private const int CurrentSchemaVersion = 3;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new PetFoodTypeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string SettingsPath { get; }
    private readonly object _lock = new();

    public PetSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var basePath = Path.Combine(appData, "ShuBingDesktopPet");
        Directory.CreateDirectory(basePath);
        SettingsPath = Path.Combine(basePath, "settings.json");
    }

    public PetSettingsService(string pathForTest)
    {
        SettingsPath = pathForTest;
        var directory = Path.GetDirectoryName(pathForTest);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public PetSettings LoadOrDefault()
    {
        if (!File.Exists(SettingsPath))
        {
            return new PetSettings();
        }

        try
        {
            var text = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<PetSettings>(text, SerializerOptions) ?? new PetSettings();
            if (loaded.SchemaVersion == 0)
            {
                loaded = loaded with { SchemaVersion = CurrentSchemaVersion };
            }

            return Normalize(loaded);
        }
        catch
        {
            return new PetSettings();
        }
    }

    public void Save(PetSettings settings)
    {
        var normalized = Normalize(settings);
        var text = JsonSerializer.Serialize(normalized, SerializerOptions);
        lock (_lock)
        {
            File.WriteAllText(SettingsPath, text, Encoding.UTF8);
        }
    }

    public PetSettings Normalize(PetSettings settings)
    {
        var scale = double.IsFinite(settings.Scale) ? settings.Scale : 1.0;
        scale = Math.Clamp(scale, 0.75, 1.5);

        var roamingScope = settings.RoamingScope is RoamingScope.CurrentScreen or RoamingScope.AllScreens
            ? settings.RoamingScope
            : RoamingScope.CurrentScreen;

        var speed = settings.Speed is RoamingSpeed.Slow or RoamingSpeed.Normal or RoamingSpeed.Fast
            ? settings.Speed
            : RoamingSpeed.Normal;

        var x = double.IsFinite(settings.LastX) ? settings.LastX : double.NaN;
        var y = double.IsFinite(settings.LastY) ? settings.LastY : double.NaN;

        var monitor = settings.LastMonitorId?.Trim() ?? string.Empty;
        var totalFeedCount = Math.Max(0, settings.TotalFeedCount);
        var lastFoodType = settings.LastFoodType switch
        {
            PetFoodType.None or PetFoodType.Apple or PetFoodType.Carrot or PetFoodType.Pellet => settings.LastFoodType,
            _ => PetFoodType.None
        };
        var lastFedUtc = settings.LastFedUtc?.ToUniversalTime();

        return new PetSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            RoamingEnabled = settings.RoamingEnabled,
            RoamingScope = roamingScope,
            Speed = speed,
            Scale = scale,
            ClickThrough = settings.ClickThrough,
            StartWithWindows = settings.StartWithWindows,
            LastMonitorId = monitor,
            LastX = x,
            LastY = y,
            TotalFeedCount = totalFeedCount,
            LastFedUtc = lastFedUtc,
            LastFoodType = lastFoodType
        };
    }
}

internal sealed class PetFoodTypeJsonConverter : JsonConverter<PetFoodType>
{
    public override PetFoodType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return PetFoodType.None;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (raw is null)
            {
                return PetFoodType.None;
            }

            if (Enum.TryParse(raw, ignoreCase: true, out PetFoodType parsed)
                && Enum.IsDefined(typeof(PetFoodType), parsed))
            {
                return parsed;
            }
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
        {
            if (Enum.IsDefined(typeof(PetFoodType), value))
            {
                return (PetFoodType)value;
            }
        }

        return PetFoodType.None;
    }

    public override void Write(Utf8JsonWriter writer, PetFoodType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            PetFoodType.Apple => "apple",
            PetFoodType.Carrot => "carrot",
            PetFoodType.Pellet => "pellet",
            _ => "none"
        });
    }
}
