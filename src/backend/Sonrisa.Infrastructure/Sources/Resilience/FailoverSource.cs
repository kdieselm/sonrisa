using Microsoft.Extensions.Logging;
using Sonrisa.Application.Abstractions;

namespace Sonrisa.Infrastructure.Sources.Resilience;

/// <summary>
/// One source made of a primary and a fallback feed of the same category (D26). Each poll tries the primary up to
/// <c>attempts</c> times; if it still fails, the same poll reads the fallback, so no poll is lost. A circuit breaker
/// counts failed polls of the primary: while it is open, the primary is skipped and only the fallback is read. Events
/// are stored under this source's key; the category data tells which feed delivered them.
/// </summary>
public sealed class FailoverSource : IEventSource
{
    private readonly IEventSource _primary;
    private readonly IEventSource _fallback;
    private readonly CircuitBreaker _breaker;
    private readonly int _attempts;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public FailoverSource(string key, string displayName, TimeSpan interval, IEventSource primary,
        IEventSource fallback, CircuitBreaker breaker, int attempts, IReadOnlyList<TimeSpan> retryDelays,
        TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        if (primary.CategoryKey != fallback.CategoryKey)
        {
            throw new ArgumentException(
                $"Source '{key}': primary and fallback must have the same category " +
                $"('{primary.CategoryKey}' vs '{fallback.CategoryKey}').");
        }

        if (retryDelays.Any(d => d < TimeSpan.Zero))
        {
            throw new ArgumentException($"Source '{key}': retry delays cannot be negative.");
        }

        Key = key;
        DisplayName = displayName;
        Interval = interval;
        _primary = primary;
        _fallback = fallback;
        _breaker = breaker;
        _attempts = attempts;
        _retryDelays = retryDelays;
        _time = time;
        _logger = logger;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string CategoryKey => _primary.CategoryKey;
    public TimeSpan Interval { get; }

    public CircuitState BreakerState => _breaker.State;

    public async Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        if (_breaker.AllowsCall())
        {
            var halfOpen = _breaker.State == CircuitState.HalfOpen;
            try
            {
                var items = await FetchPrimaryWithRetriesAsync(halfOpen ? 1 : _attempts, ct);
                if (_breaker.RecordSuccess() != CircuitState.Closed)
                {
                    _logger.LogInformation("Source {Source}: {Primary} works again; circuit closed.", Key,
                        _primary.DisplayName);
                }

                return items;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var state = _breaker.RecordFailure();
                _logger.LogWarning(ex, "Source {Source}: {Primary} failed; reading {Fallback} instead.", Key,
                    _primary.DisplayName, _fallback.DisplayName);
                if (state == CircuitState.Open)
                {
                    _logger.LogWarning("Source {Source}: circuit open; {Primary} is skipped until {Until:u}.", Key,
                        _primary.DisplayName, _breaker.OpenUntil);
                }
            }
        }

        // Breaker open, or the primary just failed. A fallback failure propagates to the poller, which logs it.
        return await _fallback.FetchAsync(ct);
    }

    private async Task<IReadOnlyList<IncomingEvent>> FetchPrimaryWithRetriesAsync(int attempts, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _primary.FetchAsync(ct);
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested)
            {
                var delay = _retryDelays.Count == 0
                    ? TimeSpan.Zero
                    : _retryDelays[Math.Min(attempt - 1, _retryDelays.Count - 1)];
                _logger.LogInformation("Source {Source}: {Primary} attempt {Attempt} of {Attempts} failed ({Error}); " +
                    "retrying in {Delay}.", Key, _primary.DisplayName, attempt, attempts, ex.Message, delay);
                await Task.Delay(delay, _time, ct);
            }
        }
    }
}
