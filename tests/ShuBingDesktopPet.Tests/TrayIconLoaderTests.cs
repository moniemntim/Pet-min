using FluentAssertions;
using ShuBingDesktopPet.Services;
using Xunit;
using System.Drawing;

namespace ShuBingDesktopPet.Tests;

public class TrayIconLoaderTests
{
    [Fact]
    public void ResolveTrayIcon_UsesAssociatedIconFirst()
    {
        using var associated = (Icon)SystemIcons.Application.Clone();
        using var fallback = (Icon)SystemIcons.Application.Clone();
        var associatedCalls = 0;
        var resourceCalls = 0;

        var result = TrayIconLoader.ResolveTrayIcon(
            "dummy.exe",
            _ =>
            {
                associatedCalls++;
                return associated;
            },
            () =>
            {
                resourceCalls++;
                return fallback;
            });

        result.Should().BeSameAs(associated);
        associatedCalls.Should().Be(1);
        resourceCalls.Should().Be(0);
    }

    [Fact]
    public void ResolveTrayIcon_FallsBackToResourceWhenAssociatedIconMissing()
    {
        using var fallback = (Icon)SystemIcons.Application.Clone();
        var associatedCalls = 0;
        var resourceCalls = 0;

        var result = TrayIconLoader.ResolveTrayIcon(
            null,
            _ =>
            {
                associatedCalls++;
                return null;
            },
            () =>
            {
                resourceCalls++;
                return fallback;
            });

        result.Should().BeSameAs(fallback);
        associatedCalls.Should().Be(1);
        resourceCalls.Should().Be(1);
    }

    [Fact]
    public void ResolveTrayIcon_ReturnsNullWhenNoIconCanBeFound()
    {
        var associatedCalls = 0;
        var resourceCalls = 0;

        var result = TrayIconLoader.ResolveTrayIcon(
            null,
            _ =>
            {
                associatedCalls++;
                return null;
            },
            () =>
            {
                resourceCalls++;
                return null;
            });

        result.Should().BeNull();
        associatedCalls.Should().Be(1);
        resourceCalls.Should().Be(1);
    }
}
