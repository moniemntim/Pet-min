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
            TwitchMinimumBits = 2_000_000
        });

        normalized.SchemaVersion.Should().Be(6);
        normalized.TwitchClientId.Should().HaveLength(128).And.StartWith("abc");
        normalized.TwitchMinimumBits.Should().Be(1_000_000);
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
            TwitchMinimumBits = 100
        });

        var loaded = service.LoadOrDefault();
        loaded.TwitchEnabled.Should().BeTrue();
        loaded.TwitchClientId.Should().Be("client-id");
        loaded.TwitchMinimumBits.Should().Be(100);
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
