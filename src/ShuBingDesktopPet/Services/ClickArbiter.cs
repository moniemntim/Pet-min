using System;

namespace ShuBingDesktopPet.Services;

public enum ClickDecision
{
    None,
    SingleStarted,
    Double
}

public sealed class ClickArbiter
{
    private readonly TimeSpan _doubleClickWindow;
    private bool _pendingSingle;
    private DateTime _pendingSinceUtc;

    public ClickArbiter(TimeSpan doubleClickWindow)
    {
        _doubleClickWindow = doubleClickWindow;
    }

    public bool HasPendingSingle => _pendingSingle;

    public void CancelPending()
    {
        _pendingSingle = false;
    }

    public ClickDecision OnMouseUp(int clickCount, DateTime utcNow)
    {
        if (clickCount >= 2 && _pendingSingle)
        {
            _pendingSingle = false;
            return ClickDecision.Double;
        }

        if (clickCount >= 2)
        {
            return ClickDecision.Double;
        }

        if (!_pendingSingle)
        {
            _pendingSingle = true;
            _pendingSinceUtc = utcNow;
            return ClickDecision.SingleStarted;
        }

        if (clickCount >= 2 || (utcNow - _pendingSinceUtc) <= _doubleClickWindow)
        {
            _pendingSingle = false;
            return ClickDecision.Double;
        }

        _pendingSingle = true;
        _pendingSinceUtc = utcNow;
        return ClickDecision.SingleStarted;
    }

    public bool TryConsumeWave(DateTime utcNow)
    {
        if (!_pendingSingle)
        {
            return false;
        }

        if (utcNow - _pendingSinceUtc >= _doubleClickWindow)
        {
            _pendingSingle = false;
            return true;
        }

        return false;
    }
}
