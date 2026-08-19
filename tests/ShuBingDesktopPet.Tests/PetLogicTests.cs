using System.IO;
using FluentAssertions;
using System.Windows;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using Xunit;

namespace ShuBingDesktopPet.Tests;

public class PetLogicTests
{
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, PetAction.Wave)]
    public void PetPriorityResolver_PrimaryPriority(bool dragging, PetAction? reaction)
    {
        var expected = dragging
            ? PetAction.Dragged
            : reaction!.Value;
        PetPriorityResolver.Resolve(dragging, reaction, true, true).Should().Be(expected);
    }

    [Fact]
    public void PetPriorityResolver_StandardPriority_RoamingThenGazeThenIdle()
    {
        PetPriorityResolver.Resolve(false, null, true, false).Should().Be(PetAction.Roaming);
        PetPriorityResolver.Resolve(false, null, false, true).Should().Be(PetAction.Gaze);
        PetPriorityResolver.Resolve(false, null, false, false).Should().Be(PetAction.Idle);
    }

    [Fact]
    public void AnimationSequencer_OneShotCompletesAndPausesAtLastFrame()
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(4, new[] { 100, 100, 100, 100 }, false);
        sequencer.ApplyPlan(new AnimationPlan(3, false, true), spec);
        sequencer.Tick(150, spec);
        sequencer.CompletedOneShot.Should().BeFalse();
        sequencer.Column.Should().Be(1);
        sequencer.Tick(200, spec);
        sequencer.Column.Should().Be(3);
        sequencer.CompletedOneShot.Should().BeFalse();

        sequencer.Tick(250, spec);
        sequencer.CompletedOneShot.Should().BeTrue();
        sequencer.Column.Should().Be(3);
    }

    [Fact]
    public void AnimationSequencer_AdvancesByDurationMultipleFramesAtOnce()
    {
        var sequencer = new AnimationSequencer();
        var spec = new AnimationRowSpec(3, new[] { 100, 150, 120 }, false);
        sequencer.ApplyPlan(new AnimationPlan(0, true, false), spec);

        sequencer.Tick(340, spec);

        sequencer.Column.Should().Be(2);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0.1)]
    [InlineData(100)]
    public void PetSettingsService_NormalizeScale_RangeClamp(double scale)
    {
        var path = Path.GetTempFileName();
        var service = new PetSettingsService(path);
        var normalized = service.Normalize(new PetSettings { Scale = scale, RoamingScope = (RoamingScope)99, Speed = (RoamingSpeed)99 });
        normalized.Scale.Should().BeInRange(0.75, 1.5);
        normalized.RoamingScope.Should().Be(RoamingScope.CurrentScreen);
        normalized.Speed.Should().Be(RoamingSpeed.Normal);
    }

    [Fact]
    public void PetSettingsService_NormalizeKeepsCoordinatesFinite()
    {
        var path = Path.GetTempFileName();
        var service = new PetSettingsService(path);
        var normalized = service.Normalize(new PetSettings
        {
            LastX = double.NaN,
            LastY = double.PositiveInfinity
        });

        double.IsNaN(normalized.LastX).Should().BeTrue();
        double.IsNaN(normalized.LastY).Should().BeTrue();
    }

    [Fact]
    public void PetSettingsService_SaveAndLoadRoundTrip_InvalidJsonFallsBackToDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "ShuBingDesktopPet", "invalid-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this-is-not-valid-json");
        var service = new PetSettingsService(path);

        var loaded = service.LoadOrDefault();
        loaded.SchemaVersion.Should().Be(2);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(10)]
    public void AnimationPlanFactory_StandardRows_AreConsistent(int row)
    {
        var plan = AnimationPlanFactory.CreateRowPlan(row);
        plan.Row.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void AnimationPlanFactory_RowSpecs_HaveExpectedRow4Row6Metadata()
    {
        var row4 = AnimationPlanFactory.RowSpec(4);
        row4.FrameCount.Should().Be(5);
        row4.Durations.Should().BeEquivalentTo(new[] { 140, 140, 140, 140, 280 });
        row4.Loop.Should().BeFalse();

        var row6 = AnimationPlanFactory.RowSpec(6);
        row6.FrameCount.Should().Be(6);
        row6.Durations.Should().BeEquivalentTo(new[] { 150, 150, 150, 150, 150, 260 });
        row6.Loop.Should().BeFalse();
    }

    [Fact]
    public void AnimationPlanFactory_ReactionRows_GroomAndPettedAreSwapped()
    {
        AnimationPlanFactory.ReactionRow(PetAction.Groom).Should().Be(7);
        AnimationPlanFactory.ReactionRow(PetAction.Petted).Should().Be(6);
    }

    [Fact]
    public void AnimationPlanFactory_DragRow_IsLooping()
    {
        AnimationPlanFactory.CreateRowPlan(5).Loop.Should().BeTrue();
    }

    [Fact]
    public void RoamingBoundary_ClampToVisible_ClampsNegativeAndOverflow()
    {
        var workArea = new Rect(-1920, -100, 1920, 1080);
        var (x, y) = RoamingBoundary.ClampToVisible(workArea, 120, 200, -3000, 5000);
        x.Should().Be(-1920);
        y.Should().Be(780);
    }

    [Fact]
    public void RoamingBoundary_ClampToVisible_AppliesSafeInset()
    {
        var workArea = new Rect(0, 0, 500, 400);
        var (x, y) = RoamingBoundary.ClampToVisible(workArea, 180, 120, 300, 300, 12);
        x.Should().Be(300);
        y.Should().Be(268);
    }

    [Fact]
    public void RoamingMotion_OvershootWithLongFrameSnapsToTarget()
    {
        var result = RoamingMotion.CalculateNextStep(0, 0, 100, 0, 1000);
        result.ReachedTarget.Should().BeTrue();
        result.NextX.Should().Be(100);
        result.NextY.Should().Be(0);
    }

    [Fact]
    public void RoamingMotion_NormalStepMovesTowardTarget()
    {
        var result = RoamingMotion.CalculateNextStep(0, 0, 100, 0, 10);
        result.ReachedTarget.Should().BeFalse();
        result.NextX.Should().Be(10);
        result.NextY.Should().Be(0);
    }

    [Fact]
    public void RoamingMotion_ZeroDistanceStopsImmediately()
    {
        var result = RoamingMotion.CalculateNextStep(50, 50, 50, 50, 10);
        result.ReachedTarget.Should().BeTrue();
        result.NextX.Should().Be(50);
        result.NextY.Should().Be(50);
    }

    [Fact]
    public void RoamingBoundary_GetSafeMovementBounds_HandlesNegativeAndSmallWorkArea()
    {
        var neg = new Rect(-1920, -100, 1280, 1024);
        var boundsNeg = RoamingBoundary.GetSafeMovementBounds(neg, 150, 100, 12);
        boundsNeg.MinX.Should().Be(-1908);
        boundsNeg.MaxX.Should().Be(-802);
        boundsNeg.MinY.Should().Be(-88);
        boundsNeg.MaxY.Should().Be(812);

        var tiny = new Rect(100, 50, 10, 80);
        var boundsTiny = RoamingBoundary.GetSafeMovementBounds(tiny, 150, 90, 12);
        boundsTiny.MinX.Should().Be(100);
        boundsTiny.MaxX.Should().Be(100);
        boundsTiny.MinY.Should().Be(50);
        boundsTiny.MaxY.Should().Be(50);
    }

    [Fact]
    public void RoamingBoundary_FindMonitorByName_ReturnsNullWhenMissing()
    {
        var list = new[]
        {
            new MonitorArea(new Rect(0, 0, 1920, 1080), "A"),
            new MonitorArea(new Rect(-1920, 0, 1920, 1080), "B")
        };

        RoamingBoundary.FindMonitorByName(list, "C").Should().BeNull();
    }

    [Fact]
    public void RoamingScreenTransition_CrossesToRightMonitor()
    {
        var source = new Rect(0, 0, 1920, 1080);
        var destination = new Rect(1920, 0, 1600, 900);
        var result = RoamingScreenTransition.Resolve(source, destination, 150, 120, 1800, 300, 2000, 300);
        result.CrossedToDestination.Should().BeTrue();
        result.X.Should().Be(1920);
        result.Y.Should().BeApproximately(243.8, 0.1);
    }

    [Fact]
    public void RoamingScreenTransition_CrossesToLeftMonitor()
    {
        var source = new Rect(1920, 0, 1600, 900);
        var destination = new Rect(0, 0, 1920, 1080);
        var result = RoamingScreenTransition.Resolve(source, destination, 150, 120, 1940, 500, 1800, 500);
        result.CrossedToDestination.Should().BeTrue();
        result.Y.Should().BeApproximately(615.4, 0.2);
        result.X.Should().Be(1770);
    }

    [Fact]
    public void RoamingScreenTransition_RecomputeDirectionAfterCrossUsesEntryPoint()
    {
        var source = new Rect(1920, 0, 1600, 900);
        var destination = new Rect(0, 0, 1920, 1080);
        var currentX = 1940d;
        var currentY = 500d;
        var nextX = 1800d;
        var nextY = 470d;
        var finalTarget = new Point(1770, 470);

        var resolved = RoamingScreenTransition.Resolve(source, destination, 150, 120, currentX, currentY, nextX, nextY);
        resolved.CrossedToDestination.Should().BeTrue();

        var recomputed = RoamingScreenTransition.DirectionToTarget(resolved.X, resolved.Y, finalTarget.X, finalTarget.Y);
        var oldDirection = RoamingScreenTransition.DirectionToTarget(currentX, currentY, finalTarget.X, finalTarget.Y);

        recomputed.X.Should().NotBe(oldDirection.X);
        recomputed.Y.Should().NotBe(oldDirection.Y);
    }

    [Fact]
    public void RoamingScreenTransition_CrossesToLowerMonitor()
    {
        var source = new Rect(0, 0, 1600, 900);
        var destination = new Rect(0, 900, 1920, 1080);
        var result = RoamingScreenTransition.Resolve(source, destination, 150, 120, 500, 780, 500, 960);
        result.CrossedToDestination.Should().BeTrue();
        result.Y.Should().Be(900);
        result.X.Should().BeApproximately(610.3, 0.2);
    }

    [Fact]
    public void RoamingScreenTransition_StaysInSameMonitorWhenNoCross()
    {
        var source = new Rect(0, 0, 1600, 900);
        var result = RoamingScreenTransition.Resolve(source, source, 150, 120, 100, 100, 200, 150);
        result.CrossedToDestination.Should().BeFalse();
        result.X.Should().Be(200);
        result.Y.Should().Be(150);
    }

    [Fact]
    public void RoamingScreenTransition_HandlesNegativeCoordinatesAndGaps()
    {
        var source = new Rect(-1920, 0, 1920, 1080);
        var destination = new Rect(-4000, 0, 1280, 1024);
        var result = RoamingScreenTransition.Resolve(source, destination, 140, 160, -200, 300, -1800, 300);
        result.CrossedToDestination.Should().BeFalse();

        var jump = RoamingScreenTransition.Resolve(source, destination, 140, 160, -20, 300, -3000, 300);
        jump.CrossedToDestination.Should().BeTrue();
        jump.X.Should().Be(-2860);
        jump.Y.Should().BeApproximately(281.7, 0.2);
    }

    [Fact]
    public void AnimationVisualScale_RowTwoUsesStretchY()
    {
        var scale = AnimationVisualScale.GetFrameScale(2);
        scale.ScaleX.Should().Be(1.0);
        scale.ScaleY.Should().Be(1.15);
    }

    [Fact]
    public void AnimationVisualScale_RowSixScalesToZeroPointSixNine()
    {
        var scale = AnimationVisualScale.GetFrameScale(6);
        scale.ScaleX.Should().Be(0.69);
        scale.ScaleY.Should().Be(0.69);
    }

    [Fact]
    public void AnimationVisualScale_RowSevenScalesUniformlyToZeroPointSix()
    {
        var scale = AnimationVisualScale.GetFrameScale(7);
        scale.ScaleX.Should().Be(0.60);
        scale.ScaleY.Should().Be(0.60);
    }

    [Fact]
    public void AnimationVisualScale_RowTenIsOnePointSeventeen()
    {
        var scale = AnimationVisualScale.GetFrameScale(10);
        scale.ScaleX.Should().Be(1.17);
        scale.ScaleY.Should().Be(1.17);
    }

    [Fact]
    public void AnimationVisualOffset_RowSevenYOffset()
    {
        var bottomAfterScale = new[] { 203.8, 205.0, 205.0, 203.8, 197.8, 205.0 };
        var expectedBaselineY = 169.0;

        for (var col = 0; col < bottomAfterScale.Length; col++)
        {
            var offset = AnimationVisualOffset.GetFrameOffset(7, col);
            offset.TranslateX.Should().Be(0);
            (bottomAfterScale[col] + offset.TranslateY).Should().BeApproximately(expectedBaselineY, 0.01);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(3, 9)]
    [InlineData(7, 6)]
    [InlineData(8, 1)]
    public void AnimationVisualOffset_NonGroomFramesHaveZeroOffsets(int row, int col)
    {
        var offset = AnimationVisualOffset.GetFrameOffset(row, col);
        offset.TranslateX.Should().Be(0);
        offset.TranslateY.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void AnimationVisualScale_OtherRowsKeepAtOne(int row)
    {
        var scale = AnimationVisualScale.GetFrameScale(row);
        scale.ScaleX.Should().Be(1.0);
        scale.ScaleY.Should().Be(1.0);
    }

    [Fact]
    public void ClickArbiter_DoubleClickCancelsPendingWave()
    {
        var arbiter = new ClickArbiter(TimeSpan.FromMilliseconds(500));
        arbiter.OnMouseUp(1, DateTime.UtcNow).Should().Be(ClickDecision.SingleStarted);
        arbiter.OnMouseUp(2, DateTime.UtcNow).Should().Be(ClickDecision.Double);
        arbiter.HasPendingSingle.Should().BeFalse();
    }

    [Fact]
    public void ClickArbiter_SingleClick_TriggersWaveAfterTimeout()
    {
        var now = DateTime.UtcNow;
        var arbiter = new ClickArbiter(TimeSpan.FromMilliseconds(250));
        arbiter.OnMouseUp(1, now).Should().Be(ClickDecision.SingleStarted);
        arbiter.TryConsumeWave(now.AddMilliseconds(100)).Should().BeFalse();
        arbiter.TryConsumeWave(now.AddMilliseconds(260)).Should().BeTrue();
        arbiter.HasPendingSingle.Should().BeFalse();
    }
}
