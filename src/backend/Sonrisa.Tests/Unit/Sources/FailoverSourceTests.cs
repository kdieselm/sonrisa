using Microsoft.Extensions.Logging.Abstractions;
using Sonrisa.Application.Abstractions;
using Sonrisa.Infrastructure.Sources.Resilience;

namespace Sonrisa.Tests.Unit.Sources;

/// <summary>D26: the Guardian first with retries, the BBC as fallback, and a circuit breaker in between.</summary>
public sealed class FailoverSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OpenFor = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task A_working_primary_is_used_and_the_fallback_is_never_called()
    {
        var (source, primary, fallback, _) = Create();

        var items = await source.FetchAsync(default);

        Assert.Equal("primary", Assert.Single(items).ExternalId);
        Assert.Equal((1, 0), (primary.Calls, fallback.Calls));
    }

    [Fact]
    public async Task A_primary_that_recovers_within_the_retries_needs_no_fallback()
    {
        var (source, primary, fallback, _) = Create();
        primary.FailNext = 2;

        var items = await source.FetchAsync(default);

        Assert.Equal("primary", Assert.Single(items).ExternalId);
        Assert.Equal((3, 0), (primary.Calls, fallback.Calls));
        Assert.Equal(CircuitState.Closed, source.BreakerState);
    }

    [Fact]
    public async Task After_all_retries_fail_the_same_poll_reads_the_fallback()
    {
        var (source, primary, fallback, _) = Create();
        primary.FailNext = int.MaxValue;

        var items = await source.FetchAsync(default);

        Assert.Equal("fallback", Assert.Single(items).ExternalId);
        Assert.Equal((3, 1), (primary.Calls, fallback.Calls)); // 3 attempts, then the fallback
        Assert.Equal(CircuitState.Closed, source.BreakerState); // one failed poll is not enough to open
    }

    [Fact]
    public async Task The_circuit_opens_after_two_failed_polls_and_the_primary_is_skipped_while_open()
    {
        var (source, primary, fallback, clock) = Create();
        primary.FailNext = int.MaxValue;

        await source.FetchAsync(default);
        await source.FetchAsync(default);
        Assert.Equal(CircuitState.Open, source.BreakerState);
        Assert.Equal(6, primary.Calls);

        clock.Advance(OpenFor - TimeSpan.FromSeconds(1));
        var items = await source.FetchAsync(default);

        Assert.Equal("fallback", Assert.Single(items).ExternalId);
        Assert.Equal((6, 3), (primary.Calls, fallback.Calls)); // the primary was not called
    }

    [Fact]
    public async Task After_the_open_period_one_trial_call_closes_the_circuit_when_the_primary_works()
    {
        var (source, primary, fallback, clock) = Create();
        primary.FailNext = 6;
        await source.FetchAsync(default);
        await source.FetchAsync(default);

        clock.Advance(OpenFor);
        var items = await source.FetchAsync(default);

        Assert.Equal("primary", Assert.Single(items).ExternalId);
        Assert.Equal(CircuitState.Closed, source.BreakerState);
        Assert.Equal((7, 2), (primary.Calls, fallback.Calls));
    }

    [Fact]
    public async Task A_failed_trial_call_opens_the_circuit_again_without_retries()
    {
        var (source, primary, fallback, clock) = Create();
        primary.FailNext = int.MaxValue;
        await source.FetchAsync(default);
        await source.FetchAsync(default);

        clock.Advance(OpenFor);
        var items = await source.FetchAsync(default);

        Assert.Equal("fallback", Assert.Single(items).ExternalId);
        Assert.Equal(CircuitState.Open, source.BreakerState);
        Assert.Equal((7, 3), (primary.Calls, fallback.Calls)); // one trial attempt, not three
    }

    [Fact]
    public async Task A_success_resets_the_count_of_failed_polls()
    {
        var (source, primary, _, _) = Create();
        primary.FailNext = 3;
        await source.FetchAsync(default); // failed poll 1
        await source.FetchAsync(default); // success
        primary.FailNext = 3;
        await source.FetchAsync(default); // failed poll 1 again

        Assert.Equal(CircuitState.Closed, source.BreakerState);
    }

    [Fact]
    public async Task When_both_feeds_fail_the_poll_fails()
    {
        var (source, primary, fallback, _) = Create();
        primary.FailNext = int.MaxValue;
        fallback.FailNext = int.MaxValue;

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(default));
    }

    [Fact]
    public void Primary_and_fallback_must_share_a_category()
    {
        var clock = new TestClock(T0);
        Assert.Throws<ArgumentException>(() => new FailoverSource("x", "X", TimeSpan.FromMinutes(5),
            new FakeFeed("primary", "news"), new FakeFeed("fallback", "disaster"),
            new CircuitBreaker(2, OpenFor, clock), 3, [], clock, NullLogger.Instance));
    }

    private static (FailoverSource, FakeFeed, FakeFeed, TestClock) Create()
    {
        var clock = new TestClock(T0);
        var primary = new FakeFeed("primary", "news");
        var fallback = new FakeFeed("fallback", "news");
        var source = new FailoverSource("world-news", "World news", TimeSpan.FromMinutes(5), primary, fallback,
            new CircuitBreaker(2, OpenFor, clock), attempts: 3, retryDelays: [TimeSpan.Zero], clock,
            NullLogger.Instance);
        return (source, primary, fallback, clock);
    }

    private sealed class FakeFeed(string id, string category) : IEventSource
    {
        public int Calls { get; private set; }
        public int FailNext { get; set; }
        public string Key => id;
        public string DisplayName => id;
        public string CategoryKey => category;
        public TimeSpan Interval => TimeSpan.FromMinutes(5);

        public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
        {
            Calls++;
            if (FailNext > 0)
            {
                FailNext--;
                throw new HttpRequestException($"{id} is down");
            }

            IReadOnlyList<IncomingEvent> items = [new IncomingEvent(id, id, null, null, null, "{}")];
            return Task.FromResult(items);
        }
    }
}
