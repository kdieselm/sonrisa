namespace Sonrisa.Infrastructure.Sources.Resilience;

public enum CircuitState
{
    /// <summary>Normal: calls go through; failures are counted.</summary>
    Closed,

    /// <summary>Too many failures: calls are skipped until the open period ends.</summary>
    Open,

    /// <summary>The open period ended: one trial call decides between Closed and Open.</summary>
    HalfOpen,
}

/// <summary>
/// A small circuit breaker (D26). It opens after <c>failuresToOpen</c> failed calls in a row, stays open for
/// <c>openDuration</c>, then lets one trial call through: success closes it, failure opens it again. Used by one poll
/// loop at a time; the lock only keeps the state consistent if that ever changes.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly int _failuresToOpen;
    private readonly TimeSpan _openDuration;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private int _failures;
    private DateTimeOffset _openUntil;

    public CircuitBreaker(int failuresToOpen, TimeSpan openDuration, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failuresToOpen, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(openDuration, TimeSpan.Zero);
        _failuresToOpen = failuresToOpen;
        _openDuration = openDuration;
        _time = time;
    }

    public CircuitState State { get; private set; } = CircuitState.Closed;

    /// <summary>Whether a call may go through now. An expired open period turns into a half-open trial.</summary>
    public bool AllowsCall()
    {
        lock (_lock)
        {
            if (State == CircuitState.Open && _time.GetUtcNow() >= _openUntil)
            {
                State = CircuitState.HalfOpen;
            }

            return State != CircuitState.Open;
        }
    }

    /// <summary>Records a successful call; returns the state before it (to log a recovery).</summary>
    public CircuitState RecordSuccess()
    {
        lock (_lock)
        {
            var before = State;
            State = CircuitState.Closed;
            _failures = 0;
            return before;
        }
    }

    /// <summary>Records a failed call; returns the state after it (to log an opening).</summary>
    public CircuitState RecordFailure()
    {
        lock (_lock)
        {
            _failures++;
            if (State == CircuitState.HalfOpen || _failures >= _failuresToOpen)
            {
                State = CircuitState.Open;
                _openUntil = _time.GetUtcNow() + _openDuration;
            }

            return State;
        }
    }

    public DateTimeOffset OpenUntil
    {
        get
        {
            lock (_lock)
            {
                return _openUntil;
            }
        }
    }
}
