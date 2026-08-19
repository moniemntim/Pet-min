namespace ShuBingDesktopPet.Services;

public readonly record struct AnimationRowSpec(int FrameCount, int[] Durations, bool Loop);

public readonly record struct AnimationPlan(int Row, bool Loop, bool IsOneShot, int FixedColumn = -1);

public sealed class AnimationSequencer
{
    private int _row;
    private int _column;
    private int _frameCount;
    private double _elapsedMs;
    private bool _isPlanInitialized;
    private AnimationPlan _plan;

    public bool CompletedOneShot { get; private set; }

    public int Row => _row;
    public int Column => _column;
    public int FrameCount => _frameCount;
    public AnimationPlan CurrentPlan => _plan;

    public void Reset()
    {
        _isPlanInitialized = false;
        _row = 0;
        _column = 0;
        _frameCount = 0;
        _elapsedMs = 0;
        _rowLoopCache = default;
        _rowOneShotCache = false;
        _fixedColumnCache = -1;
        CompletedOneShot = false;
    }

    public void ApplyPlan(AnimationPlan plan, AnimationRowSpec rowSpec)
    {
        var rowChanged = !_isPlanInitialized || plan.Row != _row || plan.Loop != _rowLoopCache || plan.IsOneShot != _rowOneShotCache || plan.FixedColumn != _fixedColumnCache;
        _plan = plan;
        _row = plan.Row;
        _frameCount = rowSpec.FrameCount;
        _rowLoopCache = plan.Loop;
        _rowOneShotCache = plan.IsOneShot;
        _fixedColumnCache = plan.FixedColumn;

        if (rowChanged)
        {
            _isPlanInitialized = true;
            _column = 0;
            _elapsedMs = 0;
            CompletedOneShot = false;
        }

        if (plan.FixedColumn >= 0)
        {
            _column = Math.Clamp(plan.FixedColumn, 0, _frameCount - 1);
            _elapsedMs = 0;
            return;
        }

        _column = Math.Clamp(_column, 0, Math.Max(0, _frameCount - 1));
    }

    public void Tick(double deltaMs, AnimationRowSpec rowSpec)
    {
        if (_frameCount <= 0 || !rowSpec.IsValid(_frameCount))
        {
            return;
        }

        CompletedOneShot = false;
        if (_plan.FixedColumn >= 0)
        {
            _column = Math.Clamp(_plan.FixedColumn, 0, _frameCount - 1);
            return;
        }

        _elapsedMs += deltaMs;
        while (_elapsedMs >= rowSpec.Durations[Math.Min(_column, rowSpec.Durations.Length - 1)])
        {
            _elapsedMs -= rowSpec.Durations[Math.Min(_column, rowSpec.Durations.Length - 1)];

            if (_column + 1 < _frameCount)
            {
                _column++;
                continue;
            }

            if (_plan.IsOneShot)
            {
                CompletedOneShot = true;
                return;
            }

            if (_plan.Loop)
            {
                _column = 0;
            }
            else
            {
                _column = _frameCount - 1;
            }
        }
    }

    private bool _rowLoopCache;
    private bool _rowOneShotCache;
    private int _fixedColumnCache;
}

public static class AnimationRowSpecExtensions
{
    public static bool IsValid(this AnimationRowSpec spec, int frameCount)
    {
        if (frameCount <= 0)
        {
            return false;
        }

        if (spec.Durations.Length == 0)
        {
            return false;
        }

        if (spec.FrameCount != frameCount)
        {
            return false;
        }

        return true;
    }
}
