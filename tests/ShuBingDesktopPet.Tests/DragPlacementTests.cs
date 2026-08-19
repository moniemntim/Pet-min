using System.Windows;
using FluentAssertions;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class DragPlacementTests
{
    [Fact]
    public void ResolveDragMove_CurrentScreenClampsInsideStartMonitor()
    {
        var startMonitor = new MonitorArea(new Rect(0, 0, 1280, 720), "Current");
        var result = DragPlacement.ResolveDragMove(-300, -40, 220, 120, 12, RoamingScope.CurrentScreen, startMonitor);

        result.x.Should().Be(12);
        result.y.Should().Be(12);
    }

    [Fact]
    public void ResolveDragMove_AllScreensDoesNotClampDuringDrag()
    {
        var result = DragPlacement.ResolveDragMove(-300, -40, 220, 120, 12, RoamingScope.AllScreens, null);

        result.x.Should().Be(-300);
        result.y.Should().Be(-40);
    }

    [Fact]
    public void ResolveDragDrop_CurrentScreenKeepsStartMonitorEvenIfCursorLeavesIt()
    {
        var startMonitor = new MonitorArea(new Rect(0, 0, 1280, 720), "Start");
        var destinationMonitor = new MonitorArea(new Rect(1280, 0, 1280, 720), "Right");
        var monitors = new[] { startMonitor, destinationMonitor };

        var result = DragPlacement.ResolveDragDrop(
            10,
            15,
            220,
            120,
            12,
            RoamingScope.CurrentScreen,
            monitors,
            2000,
            20,
            startMonitor);

        result.destination.Should().Be(startMonitor);
        result.x.Should().Be(12);
        result.y.Should().Be(15);
    }

    [Fact]
    public void ResolveDragDrop_AllScreensUsesCursorMonitorAndClampToSafeArea()
    {
        var leftMonitor = new MonitorArea(new Rect(0, 0, 1920, 1080), "Left");
        var rightMonitor = new MonitorArea(new Rect(2020, 0, 1600, 900), "Right");
        var monitors = new[] { leftMonitor, rightMonitor };

        var result = DragPlacement.ResolveDragDrop(
            2010,
            2000,
            200,
            140,
            12,
            RoamingScope.AllScreens,
            monitors,
            2030,
            80,
            null);

        result.destination.Should().Be(rightMonitor);
        result.x.Should().Be(2032);
        result.y.Should().Be(748);
    }

    [Fact]
    public void ResolveDragDrop_AllScreensFallsBackToNearestMonitorAcrossGap()
    {
        var leftMonitor = new MonitorArea(new Rect(0, 0, 1920, 1080), "Left");
        var rightMonitor = new MonitorArea(new Rect(2060, 0, 1600, 900), "Right");
        var monitors = new[] { leftMonitor, rightMonitor };

        var result = DragPlacement.ResolveDragDrop(
            2020,
            200,
            180,
            160,
            12,
            RoamingScope.AllScreens,
            monitors,
            1980,
            50,
            null);

        result.destination.Should().Be(leftMonitor);
        result.x.Should().Be(1728);
        result.y.Should().Be(200);
    }

    [Fact]
    public void ResolveDragDrop_AllScreensWorksWithNegativeMonitors()
    {
        var leftMonitor = new MonitorArea(new Rect(-1920, 0, 1280, 1024), "Left");
        var rightMonitor = new MonitorArea(new Rect(0, 0, 1920, 1080), "Right");
        var monitors = new[] { leftMonitor, rightMonitor };

        var result = DragPlacement.ResolveDragDrop(
            -4000,
            1200,
            150,
            120,
            12,
            RoamingScope.AllScreens,
            monitors,
            -1700,
            100,
            null);

        result.destination.Should().Be(leftMonitor);
        result.x.Should().Be(-1908);
        result.y.Should().Be(892);
    }
}
