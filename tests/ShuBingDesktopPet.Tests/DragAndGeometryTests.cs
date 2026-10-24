using System.Windows;
using FluentAssertions;
using ShuBingDesktopPet;
using ShuBingDesktopPet.Services;
using ShuBingDesktopPet.Models;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class DragAndGeometryTests
{
    [Fact]
    public void IsDragStart_WhenMovedMoreThanThreshold_ShouldTrue()
    {
        var start = new Point(10, 10);
        var moved = new Point(16, 10);

        PetGeometry.IsDragStart(start, moved).Should().BeTrue();
    }

    [Fact]
    public void IsDragStart_WhenMovedLessThanThreshold_ShouldFalse()
    {
        var start = new Point(10, 10);
        var moved = new Point(13, 12);

        PetGeometry.IsDragStart(start, moved).Should().BeFalse();
    }

    [Theory]
    [InlineData( 0.0, -100.0, 0)]
    [InlineData( 38.27, -92.39, 1)]
    [InlineData( 71.0, -71.0, 2)]
    [InlineData( 92.39, -38.27, 3)]
    [InlineData(100.0,   0.0, 4)]
    [InlineData( 92.39,  38.27, 5)]
    [InlineData( 71.0,  71.0, 6)]
    [InlineData( 38.27,  92.39, 7)]
    [InlineData(  0.0, 100.0, 8)]
    [InlineData(-38.27,  92.39, 9)]
    [InlineData(-71.0,  71.0, 10)]
    [InlineData(-92.39,  38.27, 11)]
    [InlineData(-100.0,   0.0, 12)]
    [InlineData(-92.39, -38.27, 13)]
    [InlineData(-71.0, -71.0, 14)]
    [InlineData(-38.27, -92.39, 15)]
    [InlineData(  0.0,  0.0, 0)]
    public void Direction16FromMouse_CoversCardinalAndIntermediates(double x, double y, int expected)
    {
        var dir = PetGeometry.Direction16FromMouse(new Point(0, 0), new Point(x, y));
        dir.Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 10, false)]
    [InlineData(80, 20, true)]
    [InlineData(110, 20, true)]
    public void HeadZoneDetection_ShouldDetectWithinBounds(double x, double y, bool expected)
    {
        var bounds = new Rect(0, 0, 192, 208);
        var point = new Point(x, y);
        PetGeometry.IsInHeadZone(bounds, point).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void AuthoredHeadZone_ScalesWithFrameAndUsesDirectionalCoordinates(double scale)
    {
        var bounds = new Rect(0, 0, 192 * scale, 208 * scale);
        var right = PetGeometry.GetAuthoredHeadZone(bounds, 1, longBody: false);
        var left = PetGeometry.GetAuthoredHeadZone(bounds, 2, longBody: false);

        right.Contains(new Point((100 + 40) * scale, 100 * scale)).Should().BeTrue();
        left.Contains(new Point((15 + 40) * scale, 100 * scale)).Should().BeTrue();
        right.Contains(new Point(40 * scale, 100 * scale)).Should().BeFalse();
        right.Top.Should().BeApproximately(65 * scale, 0.001);
    }

    [Fact]
    public void AuthoredHeadZone_LongBodyAndGazeStayWithinCanvasAndExcludeTransparentTop()
    {
        var longRight = PetGeometry.GetAuthoredHeadZone(new Rect(0, 0, 256, 208), 1, longBody: true);
        longRight.Contains(new Point(180, 100)).Should().BeTrue();
        longRight.Contains(new Point(80, 20)).Should().BeFalse();

        var gaze = PetGeometry.GetAuthoredHeadZone(new Rect(12, 0, 288, 312), 9, longBody: false);
        gaze.Left.Should().BeGreaterThan(12);
        gaze.Right.Should().BeLessThan(300);
        gaze.Top.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Direction16FromMouse_CoversAll16Directions()
    {
        for (int i = 0; i < 16; i++)
        {
            var angle = (i * 22.5) * Math.PI / 180.0;
            var x = Math.Sin(angle);
            var y = -Math.Cos(angle);
            var dir = PetGeometry.Direction16FromMouse(new Point(0, 0), new Point(x * 100, y * 100));
            dir.Should().Be(i);
        }

        // Dead-zone: near-zero cursor displacement should keep default facing upward.
        PetGeometry.Direction16FromMouse(new Point(100, 100), new Point(100.02, 99.98)).Should().Be(0);
    }
}
