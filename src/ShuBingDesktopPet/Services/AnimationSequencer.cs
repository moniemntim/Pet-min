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
        _finished = false;
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
            _finished = false;
        }

        // A malformed asset must never make the render loop throw (in particular,
        // Math.Clamp rejects a range whose maximum is -1).
        if (!rowSpec.IsValid(_frameCount))
        {
            _column = 0;
            _elapsedMs = 0;
            return;
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
        // Completion is an event for the current tick; _finished is the state
        // that keeps a one-shot parked on its final frame afterwards.
        CompletedOneShot = false;
        if (_frameCount <= 0 || !rowSpec.IsValid(_frameCount) || !double.IsFinite(deltaMs) || deltaMs <= 0)
        {
            return;
        }

        if (_finished)
        {
            return;
        }

        if (_plan.FixedColumn >= 0)
        {
            _column = Math.Clamp(_plan.FixedColumn, 0, _frameCount - 1);
            return;
        }

        if (_plan.Loop && !_plan.IsOneShot)
        {
            AdvanceLoop(deltaMs, rowSpec);
            return;
        }

        // Non-looping plans cross at most one boundary per frame, so even a very
        // large (but finite) timer delta remains bounded and deterministic.
        var remaining = rowSpec.Durations[_column] - _elapsedMs;
        if (deltaMs < remaining)
        {
            _elapsedMs += deltaMs;
            return;
        }

        var budget = deltaMs - remaining;
        while (true)
        {
            if (_column + 1 < _frameCount)
            {
                _column++;
                var duration = rowSpec.Durations[_column];
                if (budget < duration)
                {
                    _elapsedMs = budget;
                    return;
                }

                budget -= duration;
                continue;
            }

            _elapsedMs = 0;
            if (_plan.IsOneShot)
            {
                CompletedOneShot = true;
                _finished = true;
                return;
            }

            // A non-looping, non-one-shot plan simply remains on its last frame.
            return;
        }
    }

    private void AdvanceLoop(double deltaMs, AnimationRowSpec rowSpec)
    {
        long cycleMs = 0;
        for (var i = 0; i < _frameCount; i++)
        {
            cycleMs += rowSpec.Durations[i];
        }

        var cycle = (double)cycleMs;
        var position = _elapsedMs;
        for (var i = 0; i < _column; i++)
        {
            position += rowSpec.Durations[i];
        }

        position = (position + (deltaMs % cycle)) % cycle;
        for (var i = 0; i < _frameCount; i++)
        {
            if (position < rowSpec.Durations[i])
            {
                _column = i;
                _elapsedMs = position;
                return;
            }

            position -= rowSpec.Durations[i];
        }

        // Modulo can land on the cycle endpoint due to floating point rounding.
        _column = 0;
        _elapsedMs = 0;
    }

    private bool _rowLoopCache;
    private bool _rowOneShotCache;
    private int _fixedColumnCache;
    private bool _finished;
}

public static class AnimationRowSpecExtensions
{
    public static bool IsValid(this AnimationRowSpec spec, int frameCount)
    {
        if (frameCount <= 0)
        {
            return false;
        }

        if (spec.Durations is null || spec.Durations.Length < frameCount)
        {
            return false;
        }

        if (spec.FrameCount != frameCount)
        {
            return false;
        }

        return spec.Durations.All(duration => duration > 0);
    }
}
