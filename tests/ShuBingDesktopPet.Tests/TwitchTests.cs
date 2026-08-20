using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class TwitchTests
{
    [Fact]
    public void Settings_NormalizesTwitchFields()
    {
        var service = new PetSettingsService(Path.GetTempFileName());

        var normalized = service.Normalize(new PetSettings
        {
            TwitchEnabled = true,
            TwitchClientId = "  abc " + new string('x', 140),
            TwitchMinimumBits = 2_000_000,
            TwitchChannelPointsEnabled = true,
            TwitchChannelPointsRewardTitle = "  " + new string('獎', 50) + "  "
        });

        normalized.SchemaVersion.Should().Be(7);
        normalized.TwitchClientId.Should().HaveLength(128).And.StartWith("abc");
        normalized.TwitchMinimumBits.Should().Be(1_000_000);
        normalized.TwitchChannelPointsEnabled.Should().BeTrue();
        normalized.TwitchChannelPointsRewardTitle.Should().HaveLength(45);
    }

    [Fact]
    public void Settings_TwitchFieldsRoundTripWithoutTokens()
    {
        var path = Path.GetTempFileName();
        var service = new PetSettingsService(path);

        service.Save(new PetSettings
        {
            TwitchEnabled = true,
            TwitchClientId = "client-id",
            TwitchMinimumBits = 100,
            TwitchChannelPointsEnabled = true,
            TwitchChannelPointsRewardTitle = "餵薯餅吃胡蘿蔔"
        });

        var loaded = service.LoadOrDefault();
        loaded.TwitchEnabled.Should().BeTrue();
        loaded.TwitchClientId.Should().Be("client-id");
        loaded.TwitchMinimumBits.Should().Be(100);
        loaded.TwitchChannelPointsEnabled.Should().BeTrue();
        loaded.TwitchChannelPointsRewardTitle.Should().Be("餵薯餅吃胡蘿蔔");
        File.ReadAllText(path).Should().NotContain("AccessToken").And.NotContain("RefreshToken");
    }

    [Fact]
    public void Protocol_ParsesWelcomeCheerAndReconnect()
    {
        const string welcomeJson = """
            {"metadata":{"message_type":"session_welcome"},"payload":{"session":{"id":"s1","keepalive_timeout_seconds":30}}}
            """;
        const string cheerJson = """
            {"metadata":{"message_type":"notification","message_id":"m1"},"payload":{"subscription":{"type":"channel.cheer"},"event":{"bits":5,"user_name":"u","message":"hi"}}}
            """;
        const string reconnectJson = """
            {"metadata":{"message_type":"session_reconnect"},"payload":{"session":{"reconnect_url":"wss://x"}}}
            """;

        TwitchEventSubProtocol.TryParseWelcome(welcomeJson, out var welcome).Should().BeTrue();
        welcome.SessionId.Should().Be("s1");
        welcome.KeepaliveTimeoutSeconds.Should().Be(30);
        TwitchEventSubProtocol.TryParseCheer(cheerJson, out var cheer).Should().BeTrue();
        cheer.Should().Be(new TwitchCheer("m1", 5, "u", "hi"));
        TwitchEventSubProtocol.TryParseReconnect(reconnectJson, out var reconnect).Should().BeTrue();
        reconnect.ReconnectUrl.Should().Be("wss://x");
    }

    [Fact]
    public void Protocol_ParsesChannelPointsCustomRewardRedemption()
    {
        const string json = """
            {
              "metadata":{"message_type":"notification","message_id":"redeem-message-1"},
              "payload":{
                "subscription":{"type":"channel.channel_points_custom_reward_redemption.add"},
                "event":{
                  "user_name":"viewer",
                  "user_input":"加菜",
                  "reward":{"id":"reward-1","title":"餵薯餅吃東西","cost":500}
                }
              }
            }
            """;

        TwitchEventSubProtocol.TryParseChannelPointsRedemption(json, out var redemption).Should().BeTrue();
        redemption.Should().Be(new TwitchChannelPointsRedemption(
            "redeem-message-1",
            "reward-1",
            "餵薯餅吃東西",
            500,
            "viewer",
            "加菜"));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("{}")]
    [InlineData("{\"metadata\":{\"message_type\":\"notification\",\"message_id\":\"m1\"},\"payload\":{\"subscription\":{\"type\":\"channel.cheer\"},\"event\":{}}}")]
    [InlineData("{\"metadata\":{\"message_type\":\"notification\",\"message_id\":\"m1\"},\"payload\":{\"subscription\":{\"type\":\"channel.channel_points_custom_reward_redemption.add\"},\"event\":{\"reward\":{\"id\":\"r1\",\"title\":\"\",\"cost\":1}}}}")]
    public void Protocol_RejectsMalformedChannelPointsRedemption(string json)
    {
        TwitchEventSubProtocol.TryParseChannelPointsRedemption(json, out _).Should().BeFalse();
    }

    [Fact]
    public void Settings_EmptyRewardTitleFallsBackToDefault()
    {
        var service = new PetSettingsService(Path.GetTempFileName());

        var normalized = service.Normalize(new PetSettings
        {
            TwitchChannelPointsEnabled = true,
            TwitchChannelPointsRewardTitle = "   "
        });

        normalized.TwitchChannelPointsRewardTitle.Should().Be(PetSettings.DefaultTwitchRewardTitle);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("{}")]
    [InlineData("{\"metadata\":{\"message_type\":\"notification\"}}")]
    public void Protocol_RejectsMalformedCheer(string json)
    {
        TwitchEventSubProtocol.TryParseCheer(json, out _).Should().BeFalse();
    }

    [Fact]
    public void FeedQueue_IsBoundedOnlyConsumesOnSuccessAndCanClear()
    {
        var queue = new TwitchFeedQueue();
        for (var i = 0; i < TwitchFeedQueue.Capacity; i++)
        {
            queue.EnqueuePellet().Should().BeTrue();
        }

        queue.EnqueuePellet().Should().BeFalse();
        queue.TryStartFeeding(_ => false).Should().BeFalse();
        queue.Count.Should().Be(TwitchFeedQueue.Capacity);
        queue.TryStartFeeding(food => food == PetFoodType.Pellet).Should().BeTrue();
        queue.Count.Should().Be(TwitchFeedQueue.Capacity - 1);

        queue.Clear();
        queue.Count.Should().Be(0);
    }
}
