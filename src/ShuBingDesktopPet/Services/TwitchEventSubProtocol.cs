using System.Text.Json;

namespace ShuBingDesktopPet.Services;

public sealed record TwitchWelcome(string SessionId, string? ReconnectUrl, int KeepaliveTimeoutSeconds = 10);
public sealed record TwitchCheer(string MessageId, int Bits, string UserName, string Message);
public sealed record TwitchChannelPointsRedemption(
    string MessageId,
    string RewardId,
    string RewardTitle,
    int RewardCost,
    string UserName,
    string UserInput);
public sealed record TwitchReconnect(string ReconnectUrl);

public static class TwitchEventSubProtocol
{
    public static bool TryParseWelcome(string json, out TwitchWelcome welcome)
    {
        welcome = new TwitchWelcome(string.Empty, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetMessageType(root, out var messageType) || messageType != "session_welcome")
            {
                return false;
            }

            var session = root.GetProperty("payload").GetProperty("session");
            var sessionId = session.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            var reconnectUrl = session.TryGetProperty("reconnect_url", out var reconnectElement)
                ? reconnectElement.GetString()
                : null;
            var keepaliveSeconds = session.TryGetProperty("keepalive_timeout_seconds", out var keepaliveElement) &&
                keepaliveElement.ValueKind == JsonValueKind.Number &&
                keepaliveElement.TryGetInt32(out var parsedKeepalive)
                    ? Math.Clamp(parsedKeepalive, 10, 600)
                    : 10;

            welcome = new TwitchWelcome(sessionId, reconnectUrl, keepaliveSeconds);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    public static bool TryParseCheer(string json, out TwitchCheer cheer)
    {
        cheer = new TwitchCheer(string.Empty, 0, string.Empty, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetMessageType(root, out var messageType) || messageType != "notification")
            {
                return false;
            }

            var metadata = root.GetProperty("metadata");
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("subscription").GetProperty("type").GetString() != "channel.cheer")
            {
                return false;
            }

            var eventData = payload.GetProperty("event");
            var messageId = metadata.GetProperty("message_id").GetString();
            if (string.IsNullOrWhiteSpace(messageId) ||
                !eventData.GetProperty("bits").TryGetInt32(out var bits) ||
                bits < 0)
            {
                return false;
            }

            cheer = new TwitchCheer(
                messageId,
                bits,
                eventData.TryGetProperty("user_name", out var userName) ? userName.GetString() ?? string.Empty : string.Empty,
                eventData.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    public static bool TryParseChannelPointsRedemption(string json, out TwitchChannelPointsRedemption redemption)
    {
        redemption = new TwitchChannelPointsRedemption(string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetMessageType(root, out var messageType) || messageType != "notification")
            {
                return false;
            }

            var metadata = root.GetProperty("metadata");
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("subscription").GetProperty("type").GetString() !=
                "channel.channel_points_custom_reward_redemption.add")
            {
                return false;
            }

            var eventData = payload.GetProperty("event");
            var reward = eventData.GetProperty("reward");
            var messageId = metadata.GetProperty("message_id").GetString();
            var rewardId = reward.GetProperty("id").GetString();
            var rewardTitle = reward.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(messageId) ||
                string.IsNullOrWhiteSpace(rewardId) ||
                string.IsNullOrWhiteSpace(rewardTitle) ||
                !reward.GetProperty("cost").TryGetInt32(out var rewardCost) ||
                rewardCost < 0)
            {
                return false;
            }

            redemption = new TwitchChannelPointsRedemption(
                messageId,
                rewardId,
                rewardTitle,
                rewardCost,
                eventData.TryGetProperty("user_name", out var userName) ? userName.GetString() ?? string.Empty : string.Empty,
                eventData.TryGetProperty("user_input", out var userInput) ? userInput.GetString() ?? string.Empty : string.Empty);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    public static bool TryParseReconnect(string json, out TwitchReconnect reconnect)
    {
        reconnect = new TwitchReconnect(string.Empty);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetMessageType(root, out var messageType) || messageType != "session_reconnect")
            {
                return false;
            }

            var reconnectUrl = root.GetProperty("payload").GetProperty("session").GetProperty("reconnect_url").GetString();
            if (string.IsNullOrWhiteSpace(reconnectUrl))
            {
                return false;
            }

            reconnect = new TwitchReconnect(reconnectUrl);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetMessageType(JsonElement root, out string? messageType)
    {
        messageType = null;
        return root.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("message_type", out var typeElement) &&
            (messageType = typeElement.GetString()) is not null;
    }
}
