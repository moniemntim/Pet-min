using FluentAssertions;
using System;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class FeedingControllerTests
{
    [Theory]
    [InlineData(PetFoodType.Apple)]
    [InlineData(PetFoodType.Carrot)]
    [InlineData(PetFoodType.Pellet)]
    public void FeedingController_EachSupportedFoodCanStart(PetFoodType food)
    {
        var controller = new FeedingController();

        controller.TryStart(food, DateTime.UtcNow, isBusy: false).Should().BeTrue();
        controller.ActiveFood.Should().Be(food);
    }

    [Fact]
    public void FeedingController_AcceptsOnlyThreeFoodTypes()
    {
        var controller = new FeedingController();
        var now = DateTime.UtcNow;

        controller.TryStart(PetFoodType.Apple, now, isBusy: false).Should().BeTrue();
        controller.IsActive.Should().BeTrue();
        controller.TryStart(PetFoodType.Carrot, now, isBusy: false).Should().BeFalse();
        controller.TryComplete(now.AddSeconds(4), out var _).Should().BeTrue();

        controller.Cancel();
        controller.TryStart((PetFoodType)999, now, isBusy: false).Should().BeFalse();
    }

    [Fact]
    public void FeedingController_IsRejectedWhenBusyOrActive()
    {
        var controller = new FeedingController();
        var now = DateTime.UtcNow;

        controller.TryStart(PetFoodType.Carrot, now, isBusy: true).Should().BeFalse();
        controller.IsActive.Should().BeFalse();

        controller.TryStart(PetFoodType.Carrot, now, isBusy: false).Should().BeTrue();
        controller.IsActive.Should().BeTrue();
        controller.TryStart(PetFoodType.Apple, now.AddMilliseconds(100), isBusy: false).Should().BeFalse();
    }

    [Fact]
    public void FeedingController_HasCooldownForThreeSeconds()
    {
        var controller = new FeedingController();
        var start = DateTime.UtcNow;

        controller.TryStart(PetFoodType.Pellet, start, isBusy: false).Should().BeTrue();
        controller.TryComplete(start.AddSeconds(4), out _).Should().BeTrue();

        controller.TryStart(PetFoodType.Apple, start.AddSeconds(6.9), isBusy: false).Should().BeFalse();
        controller.TryStart(PetFoodType.Apple, start.AddSeconds(7), isBusy: false).Should().BeTrue();
    }

    [Fact]
    public void FeedingController_CompleteReturnsFoodAndShowsMessageWindow()
    {
        var controller = new FeedingController();
        var start = DateTime.UtcNow;

        controller.TryStart(PetFoodType.Carrot, start, isBusy: false).Should().BeTrue();
        controller.IsCompletedMessageVisible(start).Should().BeFalse();
        controller.TryComplete(start.AddSeconds(3.9), out var pendingFood).Should().BeFalse();
        pendingFood.Should().Be(PetFoodType.None);
        controller.TryComplete(start.AddSeconds(4), out var completedFood).Should().BeTrue();
        completedFood.Should().Be(PetFoodType.Carrot);
        controller.IsActive.Should().BeFalse();
        controller.IsCompletedMessageVisible(start.AddSeconds(4)).Should().BeTrue();
        controller.IsCompletedMessageVisible(start.AddSeconds(6)).Should().BeFalse();
    }

    [Fact]
    public void FeedingController_CancelClearsActiveFoodWithoutCreatingCooldown()
    {
        var controller = new FeedingController();
        var now = DateTime.UtcNow;

        controller.TryStart(PetFoodType.Apple, now, isBusy: false).Should().BeTrue();
        controller.Cancel();

        controller.IsActive.Should().BeFalse();
        controller.ActiveFood.Should().Be(PetFoodType.None);
        controller.TryStart(PetFoodType.Carrot, now, isBusy: false).Should().BeTrue();
    }
}
