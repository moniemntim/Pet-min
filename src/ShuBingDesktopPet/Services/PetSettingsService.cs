using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public sealed class PetSettingsService
{
    private const int CurrentSchemaVersion = 7;
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new PetFoodTypeJsonConverter(), new WalkingBodyStyleJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
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
        lock (_lock)
        {
            if (TryLoad(SettingsPath, out var settings) ||
                TryLoad(GetBackupPath(), out settings))
            {
                return settings;
            }

            return new PetSettings();
        }
    }

    public static IReadOnlyList<(int Width, int Height)> GetAllowedResolutions()
    {
        return ObsCaptureGeometry.GetAllowedResolutions();
    }

    public void Save(PetSettings settings)
    {
        var normalized = Normalize(settings);
        var text = JsonSerializer.Serialize(normalized, SerializerOptions);
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(
                directory ?? string.Empty,
                $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom, bufferSize: 4096, leaveOpen: true))
                {
                    writer.Write(text);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(SettingsPath))
                {
                    File.Replace(tempPath, SettingsPath, GetBackupPath(), ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, SettingsPath);
                }
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
    }

    private bool TryLoad(string path, out PetSettings settings)
    {
        settings = new PetSettings();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<PetSettings>(text, SerializerOptions);
            if (loaded is null)
            {
                return false;
            }

            if (loaded.SchemaVersion == 0)
            {
                loaded = loaded with { SchemaVersion = CurrentSchemaVersion };
            }

            settings = Normalize(loaded);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private string GetBackupPath() => SettingsPath + ".bak";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed save must not mask its original exception during best-effort cleanup.
        }
    }

    public PetSettings Normalize(PetSettings settings)
    {
        var scale = double.IsFinite(settings.Scale) ? settings.Scale : 1.0;
        scale = Math.Clamp(scale, 0.75, 1.5);
        var (obsCanvasWidth, obsCanvasHeight) = ObsCaptureGeometry.NormalizeResolution(settings.ObsCanvasWidth, settings.ObsCanvasHeight);

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
        var feedingDurationSeconds = double.IsFinite(settings.FeedingDurationSeconds) ? settings.FeedingDurationSeconds : 4.0;
        feedingDurationSeconds = Math.Clamp(feedingDurationSeconds, 1.0, 20.0);
        var continuousWalkDurationSeconds = double.IsFinite(settings.ContinuousWalkDurationSeconds) ? settings.ContinuousWalkDurationSeconds : 8.0;
        continuousWalkDurationSeconds = Math.Clamp(continuousWalkDurationSeconds, 2.0, 60.0);
        var walkingBodyStyle = settings.WalkingBodyStyle is WalkingBodyStyle.Current or WalkingBodyStyle.LongBody
            ? settings.WalkingBodyStyle
            : WalkingBodyStyle.Current;
        var twitchClientId = settings.TwitchClientId?.Trim() ?? string.Empty;
        if (twitchClientId.Length > 128) twitchClientId = twitchClientId[..128];
        var twitchMinimumBits = Math.Clamp(settings.TwitchMinimumBits, 1, 1_000_000);
        var twitchRewardTitle = settings.TwitchChannelPointsRewardTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(twitchRewardTitle))
        {
            twitchRewardTitle = PetSettings.DefaultTwitchRewardTitle;
        }
        if (twitchRewardTitle.Length > 45) twitchRewardTitle = twitchRewardTitle[..45];

        return new PetSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            RoamingEnabled = settings.RoamingEnabled,
            RoamingScope = roamingScope,
            Speed = speed,
            Scale = scale,
            ObsCaptureEnabled = settings.ObsCaptureEnabled,
            ObsCanvasWidth = obsCanvasWidth,
            ObsCanvasHeight = obsCanvasHeight,
            ClickThrough = settings.ClickThrough,
            StartWithWindows = settings.StartWithWindows,
            LastMonitorId = monitor,
            LastX = x,
            LastY = y,
            TotalFeedCount = totalFeedCount,
            LastFedUtc = lastFedUtc,
            LastFoodType = lastFoodType,
            FeedingDurationSeconds = feedingDurationSeconds,
            ContinuousWalkDurationSeconds = continuousWalkDurationSeconds,
            WalkingBodyStyle = walkingBodyStyle,
            TwitchEnabled = settings.TwitchEnabled,
            TwitchClientId = twitchClientId,
            TwitchMinimumBits = twitchMinimumBits,
            TwitchChannelPointsEnabled = settings.TwitchChannelPointsEnabled,
            TwitchChannelPointsRewardTitle = twitchRewardTitle
        };
    }
}

/// <summary>Reads the public names current/longBody and falls back safely for unknown values.</summary>
internal sealed class WalkingBodyStyleJsonConverter : JsonConverter<WalkingBodyStyle>
{
    public override WalkingBodyStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (string.Equals(raw, "longBody", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "long_body", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "long-body", StringComparison.OrdinalIgnoreCase))
            {
                return WalkingBodyStyle.LongBody;
            }

            if (string.Equals(raw, "current", StringComparison.OrdinalIgnoreCase))
            {
                return WalkingBodyStyle.Current;
            }

            return WalkingBodyStyle.Current;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value))
        {
            return value == (int)WalkingBodyStyle.LongBody ? WalkingBodyStyle.LongBody : WalkingBodyStyle.Current;
        }

        return WalkingBodyStyle.Current;
    }

    public override void Write(Utf8JsonWriter writer, WalkingBodyStyle value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value == WalkingBodyStyle.LongBody ? "longBody" : "current");
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
