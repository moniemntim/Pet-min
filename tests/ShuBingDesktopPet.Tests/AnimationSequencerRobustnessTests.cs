using FluentAssertions;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public sealed class AnimationSequencerRobustnessTests
{
    [Fact]
    public void ZeroFramePlanWithFixedColumnIsSafe()
    {
        var sequencer = new AnimationSequencer();
        var empty = new AnimationRowSpec(0, Array.Empty<int>(), false);

        var action = () =>
        {
            sequencer.ApplyPlan(new AnimationPlan(4, false, true, FixedColumn: 2), empty);
            sequencer.Tick(1000, empty);
        };

        action.Should().NotThrow();
        sequencer.Column.Should().Be(0);
        sequencer.CompletedOneShot.Should().BeFalse();
    }

    [Fact]
    public void InvalidDurationsAreRejectedWithoutAdvancing()
    {
        var sequencer = new AnimationSequencer();
        var invalid = new AnimationRowSpec(2, new[] { 100, 0 }, false);

        invalid.IsValid(2).Should().BeFalse();
        sequencer.ApplyPlan(new AnimationPlan(2, false, true), invalid);
        sequencer.Tick(1000, invalid);

        sequencer.Column.Should().Be(0);
        sequencer.CompletedOneShot.Should().BeFalse();

        var nullDurations = new AnimationRowSpec(1, null!, false);
        nullDurations.IsValid(1).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NegativeOrNonFiniteDeltaIsIgnored(double deltaMs)
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(2, new[] { 100, 100 }, false);
        sequencer.ApplyPlan(new AnimationPlan(1, false, true), spec);

        sequencer.Tick(deltaMs, spec);

        sequencer.Column.Should().Be(0);
        sequencer.CompletedOneShot.Should().BeFalse();
    }

    [Fact]
    public void OneShotCompletionPulsesOnceAndThenHoldsUntilPlanChanges()
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(2, new[] { 100, 100 }, false);
        var plan = new AnimationPlan(1, false, true);
        sequencer.ApplyPlan(plan, spec);
        sequencer.Tick(250, spec);
        sequencer.CompletedOneShot.Should().BeTrue();

        sequencer.Tick(1, spec);
        sequencer.CompletedOneShot.Should().BeFalse();
        sequencer.Column.Should().Be(1);
        sequencer.ApplyPlan(plan, spec);
        sequencer.CompletedOneShot.Should().BeFalse();

        sequencer.ApplyPlan(new AnimationPlan(2, false, true), spec);
        sequencer.CompletedOneShot.Should().BeFalse();
        sequencer.Column.Should().Be(0);

        sequencer.ApplyPlan(plan, spec);
        sequencer.Tick(200, spec);
        sequencer.CompletedOneShot.Should().BeTrue();
        sequencer.Reset();
        sequencer.ApplyPlan(plan, spec);
        sequencer.Tick(200, spec);
        sequencer.CompletedOneShot.Should().BeTrue();
    }

    [Fact]
    public void HugeLoopDeltaUsesCycleModulo()
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(3, new[] { 100, 200, 300 }, true);
        sequencer.ApplyPlan(new AnimationPlan(0, true, false), spec);

        sequencer.Tick(1_000_000_000_000d, spec);

        // 1e12 % 600 == 400: 100 ms into frame 2.
        sequencer.Column.Should().Be(2);
        sequencer.CompletedOneShot.Should().BeFalse();

        sequencer.Reset();
        sequencer.ApplyPlan(new AnimationPlan(0, true, false), spec);
        sequencer.Tick(50, spec);
        sequencer.Tick(1_000_000_000_000d, spec);
        sequencer.Column.Should().Be(2);
        sequencer.Tick(149, spec);
        sequencer.Column.Should().Be(2);
        sequencer.Tick(1, spec);
        sequencer.Column.Should().Be(0);
    }

    [Fact]
    public void HugeNonLoopDeltaStopsAtLastFrame()
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(3, new[] { 100, 200, 300 }, false);
        sequencer.ApplyPlan(new AnimationPlan(0, false, false), spec);

        sequencer.Tick(double.MaxValue, spec);

        sequencer.Column.Should().Be(2);
        sequencer.CompletedOneShot.Should().BeFalse();
    }
}
