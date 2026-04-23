namespace CpuGuard.Service.Policy;

public enum PolicyAction
{
    None,
    ApplyCap,
    RemoveCap
}

public sealed class CpuPolicyController
{
    private readonly TimeSpan _zeroPlayerDelay;
    private readonly TimeSpan _transitionCooldown;
    private readonly TimeSpan _processAddPlayerGrace;
    private readonly object _sync = new();

    private int _playerCount;
    private DateTimeOffset? _zeroPlayersSinceUtc;
    private DateTimeOffset? _lastTransitionUtc;
    private DateTimeOffset? _processAddPlayerGraceUntilUtc;
    private bool _capActive;

    public CpuPolicyController(TimeSpan zeroPlayerDelay, TimeSpan transitionCooldown, TimeSpan processAddPlayerGrace)
    {
        _zeroPlayerDelay = zeroPlayerDelay;
        _transitionCooldown = transitionCooldown;
        _processAddPlayerGrace = processAddPlayerGrace;
    }

    public bool IsCapActive
    {
        get
        {
            lock (_sync)
            {
                return _capActive;
            }
        }
    }

    public PolicyAction OnPlayerCountChanged(int playerCount, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            _playerCount = Math.Max(0, playerCount);

            if (_playerCount > 0)
            {
                _zeroPlayersSinceUtc = null;
                _processAddPlayerGraceUntilUtc = null;

                return _capActive ? PolicyAction.RemoveCap : PolicyAction.None;
            }

            _zeroPlayersSinceUtc ??= nowUtc;
            return EvaluateLocked(nowUtc);
        }
    }

    public PolicyAction OnProcessAddPlayerSignal(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            if (_playerCount > 0)
            {
                return PolicyAction.None;
            }

            _zeroPlayersSinceUtc = nowUtc;
            _processAddPlayerGraceUntilUtc = nowUtc + _processAddPlayerGrace;

            return _capActive ? PolicyAction.RemoveCap : PolicyAction.None;
        }
    }

    public PolicyAction Evaluate(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            return EvaluateLocked(nowUtc);
        }
    }

    public void MarkCapApplied(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            _capActive = true;
            _lastTransitionUtc = nowUtc;
        }
    }

    public void MarkCapRemoved(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            _capActive = false;
            _lastTransitionUtc = nowUtc;
        }
    }

    public void MarkCapApplyFailed(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            _capActive = false;
            _lastTransitionUtc = nowUtc;
        }
    }

    private PolicyAction EvaluateLocked(DateTimeOffset nowUtc)
    {
        if (_playerCount > 0)
        {
            return _capActive ? PolicyAction.RemoveCap : PolicyAction.None;
        }

        if (_processAddPlayerGraceUntilUtc.HasValue)
        {
            if (nowUtc < _processAddPlayerGraceUntilUtc.Value)
            {
                return PolicyAction.None;
            }

            _processAddPlayerGraceUntilUtc = null;
        }

        _zeroPlayersSinceUtc ??= nowUtc;
        if (_capActive)
        {
            return PolicyAction.None;
        }

        var zeroElapsed = nowUtc - _zeroPlayersSinceUtc.Value;
        if (zeroElapsed < _zeroPlayerDelay)
        {
            return PolicyAction.None;
        }

        if (_lastTransitionUtc.HasValue)
        {
            var cooldownElapsed = nowUtc - _lastTransitionUtc.Value;
            if (cooldownElapsed < _transitionCooldown)
            {
                return PolicyAction.None;
            }
        }

        return PolicyAction.ApplyCap;
    }
}
