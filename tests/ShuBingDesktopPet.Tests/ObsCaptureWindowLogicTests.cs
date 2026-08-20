using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class ObsCaptureWindowLogicTests
{
    [Theory]
    [InlineData(0, 0, 1920, -1984, 0)]
    [InlineData(-1920, -240, 1280, -3264, -240)]
    [InlineData(-3840, 100, 1, -3905, 100)]
    public void OffscreenPosition_IsFullyLeftOfVirtualDesktop(
        int virtualLeft,
        int virtualTop,
        int windowWidth,
        int expectedX,
        int expectedY)
    {
        var position = ObsCaptureGeometry.GetOffscreenWindowPosition(
            virtualLeft,
            virtualTop,
            windowWidth);

        position.Should().Be((expectedX, expectedY));
        ((long)position.x + Math.Max(1, windowWidth)).Should().BeLessThan(virtualLeft);
    }

    [Theory]
    [InlineData(WalkingBodyStyle.Current, true, 192)]
    [InlineData(WalkingBodyStyle.LongBody, false, 192)]
    [InlineData(WalkingBodyStyle.LongBody, true, WalkingBodyFrameCache.ExpectedFrameWidth)]
    public void PetFrameWidth_UsesLongBodyCanvasOnlyWhenAssetsAreAvailable(
        WalkingBodyStyle style,
        bool longBodyAvailable,
        int expectedWidth)
    {
        ObsCaptureGeometry.ResolvePetFrameWidth(192, style, longBodyAvailable)
            .Should().Be(expectedWidth);
    }
}
