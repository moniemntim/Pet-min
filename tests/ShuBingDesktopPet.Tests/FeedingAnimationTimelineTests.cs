using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class FeedingAnimationTimelineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void FeedingHasOpeningChewingAndHeldEnding(double duration)
    {
        Assert.Equal(0, FeedingAnimationTimeline.GetFrame(0, duration));
        Assert.InRange(FeedingAnimationTimeline.GetFrame(duration * 0.5, duration), 1, 4);
        Assert.Equal(5, FeedingAnimationTimeline.GetFrame(duration - 0.01, duration));
        Assert.Equal(5, FeedingAnimationTimeline.GetFrame(duration + 100, duration));
    }

    [Fact]
    public void ChewingReversesThroughFramesInsteadOfJumpingToOpening()
    {
        var frames = Enumerable.Range(0, 6)
            .Select(i => FeedingAnimationTimeline.GetFrame(0.35 + i * 0.16 + 0.001, 4));
        Assert.Equal(new[] { 1, 2, 3, 4, 3, 2 }, frames);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 5)]
    [InlineData(-3, 0)]
    public void InvalidElapsedIsBounded(double elapsed, int expected)
        => Assert.Equal(expected, FeedingAnimationTimeline.GetFrame(elapsed, 4));

    [Fact]
    public void ControllerRestartAndCancelResetThePlayback()
    {
        var controller = new FeedingController();
        var start = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(controller.TryStart(PetFoodType.Pellet, start, false, 4));
        Assert.Equal(5, controller.GetAnimationFrame(start.AddSeconds(3.9)));
        Assert.True(controller.TryComplete(start.AddSeconds(4), out var food));
        Assert.Equal(PetFoodType.Pellet, food);
        Assert.False(controller.TryComplete(start.AddSeconds(5), out _));
        Assert.False(controller.TryStart(food, start.AddSeconds(6), false));
        Assert.True(controller.TryStart(food, start.AddSeconds(7), false));
        Assert.Equal(0, controller.GetAnimationFrame(start.AddSeconds(7)));
        controller.Cancel();
        Assert.Equal(0, controller.GetAnimationFrame(start.AddSeconds(8)));
    }
}
