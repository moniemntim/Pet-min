using FluentAssertions;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class PetScaleGeometryTests
{
    [Theory]
    [InlineData(0.75, 144.0, 156.0)]
    [InlineData(1.0, 192.0, 208.0)]
    [InlineData(1.5, 288.0, 312.0)]
    public void GetLogicalWindowSize_ShouldReturnScaledDipSize(double scale, double expectedWidth, double expectedHeight)
    {
        var size = PetScaleGeometry.GetLogicalWindowSize(192, 208, scale);
        size.WidthDip.Should().Be(expectedWidth);
        size.HeightDip.Should().Be(expectedHeight);
    }

    [Fact]
    public void GetPhysicalWindowSize_ShouldUseCurrentDpiAndScaleForPixels()
    {
        var size = PetScaleGeometry.GetPhysicalWindowSize(192, 208, 1.5, 1.25, 1.25);

        size.WidthPx.Should().Be(360);
        size.HeightPx.Should().Be(390);
    }

    [Theory]
    [InlineData(0.2, 0.75)]
    [InlineData(1.2, 1.2)]
    [InlineData(2.0, 1.5)]
    public void ClampScale_ShouldClampToSupportedRange(double input, double expected)
    {
        PetScaleGeometry.ClampScale(input).Should().Be(expected);
    }
}
