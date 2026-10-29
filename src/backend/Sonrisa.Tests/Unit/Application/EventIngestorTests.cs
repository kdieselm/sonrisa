using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Services;
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

    private static IncomingEvent Item(string id, string title, DateTimeOffset? published) =>
        new(id, title, null, "https://example.test/" + id, published, """{"feedName":"x"}""");

    private static (EventIngestor, FakeRepository, TestClock) Create()
    {
        var clock = new TestClock(T0);
        var repo = new FakeRepository();
        return (new EventIngestor(repo, repo, clock), repo, clock);
    }

    /// <summary>In-memory repository and transaction runner; can simulate another writer winning the insert.</summary>
    private sealed class FakeRepository : IEventRepository, ITransactionRunner
    {
        private readonly List<Event> _pending = [];

        public List<Event> Rows { get; } = [];
        public Event? RaceOnNextAdd { get; set; }

        public Task<Event?> FindAsync(string sourceKey, IReadOnlyCollection<string> externalIds, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(e => e.SourceKey == sourceKey && externalIds.Contains(e.ExternalId)));

        public void Add(Event item) => _pending.Add(item);

        public Task<(IReadOnlyList<Event> Items, int Total)> ListAsync(string? categoryKey, string? sourceKey,
            int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();

        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            _pending.Clear();
            var result = await work(ct);
            if (RaceOnNextAdd is { } winner && _pending.Count > 0)
            {
                RaceOnNextAdd = null;
                _pending.Clear();
                Rows.Add(winner);
                throw new DuplicateKeyException("race", new Exception());
            }

            Rows.AddRange(_pending);
            return result;
        }
    }
}
