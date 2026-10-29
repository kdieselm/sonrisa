using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Tests.Unit.Application;

/// <summary>Ingestor rules without a database: insert vs. revision, first-seen time, the duplicate-insert retry.</summary>
public sealed class EventIngestorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task New_item_is_inserted_with_first_seen_now_and_published_from_the_item()
    {
        var (ingestor, repo, _) = Create();
        var published = T0.AddMinutes(-20);

        var result = await ingestor.IngestAsync("src", "news", Item("1", "Title", published), false, default);

        Assert.Equal(IngestOutcome.Inserted, result.Outcome);
        var stored = Assert.Single(repo.Rows);
        Assert.Equal((T0, published, T0), (stored.FirstSeenAt, stored.PublishedAt, stored.UpdatedAt));
    }

    [Fact]
    public async Task Missing_published_time_becomes_the_first_seen_time()
    {
        var (ingestor, repo, _) = Create();

        await ingestor.IngestAsync("src", "news", Item("1", "Title", null), false, default);

        Assert.Equal(T0, Assert.Single(repo.Rows).PublishedAt);
    }

    [Fact]
    public async Task Same_item_again_is_unchanged_and_a_changed_item_is_a_revision()
    {
        var (ingestor, repo, clock) = Create();
        await ingestor.IngestAsync("src", "news", Item("1", "Title", T0), false, default);

        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await ingestor.IngestAsync("src", "news", Item("1", "Title", T0), false, default);
        Assert.Equal(IngestOutcome.Unchanged, again.Outcome);
        Assert.Equal(T0, again.Event.UpdatedAt);

        clock.Advance(TimeSpan.FromMinutes(5));
        var revised = await ingestor.IngestAsync("src", "news", Item("1", "Title (updated)", T0), false, default);
        Assert.Equal(IngestOutcome.Updated, revised.Outcome);

        var stored = Assert.Single(repo.Rows);
        Assert.Equal("Title (updated)", stored.Title);
        Assert.Equal(T0, stored.FirstSeenAt); // a revision is never treated as new (T9)
        Assert.Equal(T0.AddMinutes(10), stored.UpdatedAt);
    }

    [Fact]
    public async Task A_lost_insert_race_is_retried_as_a_revision()
    {
        var (ingestor, repo, _) = Create();
        repo.RaceOnNextAdd = new Event("news", "src", "1", "Other writer", null, null, T0, T0, false, "{}");

        var result = await ingestor.IngestAsync("src", "news", Item("1", "Title", T0), false, default);

        Assert.Equal(IngestOutcome.Updated, result.Outcome);
        Assert.Equal("Title", Assert.Single(repo.Rows).Title);
    }

    [Fact]
    public async Task Overlong_text_is_shortened_and_an_overlong_link_dropped()
    {
        var (ingestor, repo, _) = Create();
        var item = new IncomingEvent("1", new string('t', 600), new string('s', 3000),
            "https://example.test/" + new string('u', 2000), T0, "{}");

        await ingestor.IngestAsync("src", "news", item, false, default);

        var stored = Assert.Single(repo.Rows);
        Assert.Equal(Event.MaxTitleLength, stored.Title.Length);
        Assert.Equal(Event.MaxSummaryLength, stored.Summary!.Length);
        Assert.Null(stored.Url);
    }

    [Fact]
    public async Task An_item_known_by_an_alternate_id_updates_its_row()
    {
        var (ingestor, repo, _) = Create();
        await ingestor.IngestAsync("usgs", "disaster", Item("us7000abcd", "M 5.0", T0), false, default);

        // The preferred ID changed from us… to pt…; the old one is now listed among the alternates.
        var renamed = Item("pt26123456", "M 5.1", T0) with { AlternateIds = ["us7000abcd"] };
        var result = await ingestor.IngestAsync("usgs", "disaster", renamed, false, default);

        Assert.Equal(IngestOutcome.Updated, result.Outcome);
        var stored = Assert.Single(repo.Rows);
        Assert.Equal(("us7000abcd", "M 5.1"), (stored.ExternalId, stored.Title));
    }

    [Fact]
    public async Task An_overlong_external_id_is_stored_as_a_stable_hash()
    {
        var (ingestor, repo, _) = Create();
        var longId = "https://example.test/" + new string('a', 600);

        await ingestor.IngestAsync("rss", "news", Item(longId, "Title", T0), false, default);
        var again = await ingestor.IngestAsync("rss", "news", Item(longId, "Title", T0), false, default);

        var stored = Assert.Single(repo.Rows);
        Assert.StartsWith("sha256:", stored.ExternalId);
        Assert.True(stored.ExternalId.Length <= Event.MaxExternalIdLength);
        Assert.Equal(IngestOutcome.Unchanged, again.Outcome);
    }

    // --- Matching (spec §2.5, AC7) -----------------------------------------------------------------------------

    private const string FloodRule = """{"keywords":["flood"]}""";

    [Fact]
    public async Task A_matching_new_event_queues_one_pending_delivery_per_destination()
    {
        var (ingestor, repo, _) = Create();
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10, 11]), T0.AddMinutes(-1), "news"));
        repo.Candidates.Add((new MatchCandidate(8, """{"keywords":["fire"]}""", [12]), T0.AddMinutes(-1), "news"));

        var result = await ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0), false, default);

        Assert.Equal(2, result.DeliveriesCreated);
        Assert.Equal([(7L, 10L), (7L, 11L)],
            repo.Deliveries.Select(d => (d.AlertId, d.DestinationId)).Order().ToList());
        Assert.All(repo.Deliveries, d => Assert.Equal(DeliveryStatus.Pending, d.Status));
        Assert.All(repo.Deliveries, d => Assert.Same(result.Event, d.Event));
    }

    [Fact]
    public async Task An_event_published_more_than_two_hours_before_ingestion_is_stored_but_never_matched()
    {
        var (ingestor, repo, _) = Create();
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10]), T0.AddDays(-1), "news"));

        var old = await ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0.AddHours(-2).AddSeconds(-1)),
            false, default);
        var justInside = await ingestor.IngestAsync("src", "news", Item("2", "Flood hits town", T0.AddHours(-2)),
            false, default);

        Assert.Equal(IngestOutcome.Inserted, old.Outcome);
        Assert.Equal((0, 1), (old.DeliveriesCreated, justInside.DeliveriesCreated));
        Assert.Single(repo.Deliveries);
    }

    [Fact]
    public async Task A_revision_never_creates_a_delivery()
    {
        var (ingestor, repo, clock) = Create();
        await ingestor.IngestAsync("src", "news", Item("1", "Rain expected", T0), false, default);
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10]), T0.AddDays(-1), "news"));

        clock.Advance(TimeSpan.FromMinutes(5));
        var revised = await ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0), false, default);

        Assert.Equal((IngestOutcome.Updated, 0), (revised.Outcome, revised.DeliveriesCreated));
        Assert.Empty(repo.Deliveries);
    }

    [Fact]
    public async Task A_lost_insert_race_creates_no_delivery()
    {
        var (ingestor, repo, _) = Create();
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10]), T0.AddDays(-1), "news"));
        repo.RaceOnNextAdd = new Event("news", "src", "1", "Other writer", null, null, T0, T0, false, "{}");

        await ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0), false, default);

        Assert.Empty(repo.Deliveries); // the other writer's insert was the new one; ours became a revision
    }

    [Fact]
    public async Task An_alert_with_an_unreadable_rule_is_skipped_and_reported_without_blocking_the_others()
    {
        var (ingestor, repo, _) = Create();
        repo.Candidates.Add((new MatchCandidate(6, "not json", [9]), T0.AddDays(-1), "news"));
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10]), T0.AddDays(-1), "news"));

        var result = await ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0), false, default);

        Assert.Equal([6L], result.FailedRuleAlertIds);
        Assert.Equal(7, Assert.Single(repo.Deliveries).AlertId);
    }

    [Fact]
    public void A_rule_that_throws_on_unexpected_data_counts_as_no_match()
    {
        // System.Text.Json does not enforce the non-nullable Place, so a stored row could still hold null.
        var item = new Event("disaster", "usgs", "x", "M 6", null, null, T0, T0, false,
            """{"magnitude":6,"place":null}""");

        Assert.Null(EventIngestor.TryMatch(new DisasterCategory(), item,
            """{"minMagnitude":5,"placeContains":"Japan"}"""));
    }

    [Fact]
    public async Task A_unique_error_of_another_index_is_rethrown_not_retried_as_a_revision()
    {
        var (ingestor, repo, _) = Create();
        repo.Candidates.Add((new MatchCandidate(7, FloodRule, [10]), T0.AddDays(-1), "news"));
        repo.FailNextCommitWith = "IX_Deliveries_AlertId_EventId_DestinationId";

        await Assert.ThrowsAsync<DuplicateKeyException>(() =>
            ingestor.IngestAsync("src", "news", Item("1", "Flood hits town", T0), false, default));
        Assert.Empty(repo.Rows);
    }

    private static IncomingEvent Item(string id, string title, DateTimeOffset? published) =>
        new(id, title, null, "https://example.test/" + id, published, """{"feedName":"x"}""");

    private static (EventIngestor, FakeRepository, TestClock) Create()
    {
        var clock = new TestClock(T0);
        var repo = new FakeRepository();
        return (CreateWith(repo, clock), repo, clock);
    }

    internal static EventIngestor CreateWith(FakeRepository repo, TimeProvider clock) =>
        new(repo, repo, repo, new CategoryRegistry([new DisasterCategory(), new NewsCategory()]), repo, clock);

    /// <summary>
    /// In-memory events, alert candidates, deliveries and transaction runner; can simulate another writer winning
    /// the insert. Candidates are returned as the real repository filters them (active before first seen).
    /// </summary>
    internal sealed class FakeRepository : IEventRepository, IAlertRepository, IDeliveryRepository, ITransactionRunner
    {
        private readonly List<Event> _pending = [];
        private readonly List<Delivery> _pendingDeliveries = [];

        public List<Event> Rows { get; } = [];
        public List<Delivery> Deliveries { get; } = [];
        public List<(MatchCandidate Candidate, DateTimeOffset ActiveSince, string Category)> Candidates { get; } = [];
        public Event? RaceOnNextAdd { get; set; }

        /// <summary>The next commit fails with a unique error of this index.</summary>
        public string? FailNextCommitWith { get; set; }

        public Task<Event?> FindAsync(string sourceKey, IReadOnlyCollection<string> externalIds, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(e => e.SourceKey == sourceKey && externalIds.Contains(e.ExternalId)));

        public void Add(Event item) => _pending.Add(item);

        public Task<(IReadOnlyList<Event> Items, int Total)> ListAsync(string? categoryKey, string? sourceKey,
            int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<Event>> ListRecentAsync(string categoryKey, DateTimeOffset since, int limit,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<MatchCandidate>> ListMatchCandidatesAsync(string categoryKey,
            DateTimeOffset firstSeenAt, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MatchCandidate>>(Candidates
                .Where(c => c.Category == categoryKey && c.ActiveSince < firstSeenAt)
                .Select(c => c.Candidate)
                .ToList());

        public void Add(Delivery delivery) => _pendingDeliveries.Add(delivery);

        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            _pending.Clear();
            _pendingDeliveries.Clear();
            var result = await work(ct);
            if (FailNextCommitWith is { } index)
            {
                FailNextCommitWith = null;
                _pending.Clear();
                _pendingDeliveries.Clear();
                throw new DuplicateKeyException("unique", new Exception(), index);
            }

            if (RaceOnNextAdd is { } winner && _pending.Count > 0)
            {
                RaceOnNextAdd = null;
                _pending.Clear();
                _pendingDeliveries.Clear();
                Rows.Add(winner);
                throw new DuplicateKeyException("race", new Exception());
            }

            Rows.AddRange(_pending);
            Deliveries.AddRange(_pendingDeliveries);
            return result;
        }

        // Not used by the ingestor.
        public void Add(Alert alert) => throw new NotSupportedException();

        public Task<Alert?> GetForUserAsync(long id, string userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<Alert> Items, int Total)> ListForUserAsync(string userId, int page, int pageSize,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<int> CountForUserAsync(string userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<Alert>> ListLinkedToAsync(long destinationId, string userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> FailPendingForAlertAsync(long alertId, DeliveryFailure failure, string error,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<int> FailPendingForDestinationAsync(long destinationId, DeliveryFailure failure, string error,
            CancellationToken ct) => throw new NotSupportedException();

        Task<(IReadOnlyList<NotificationRow> Items, int Total)> IDeliveryRepository.ListForUserAsync(string userId,
            int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        Task<NotificationRow?> IDeliveryRepository.GetForUserAsync(long id, string userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
