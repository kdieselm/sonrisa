using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Configuration;
using Sonrisa.Infrastructure.Persistence;
using Sonrisa.Infrastructure.Persistence.Repositories;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>AC16 on PostgreSQL: re-ingesting an item updates its row; the unique index; jsonb round trip (spec §8).</summary>
[Collection(PostgresCollection.Name)]
public sealed class EventIngestionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _ = _factory.Server; // start the host: migrations run
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Reingesting_the_same_item_updates_the_row_and_creates_no_new_one()
    {
        var source = $"test-{Guid.NewGuid():N}";
        var first = Disaster("q1", "M 4.6 - Somewhere", 4.6);

        Assert.Equal(IngestOutcome.Inserted, (await IngestAsync(source, first)).Outcome);
        Assert.Equal(IngestOutcome.Unchanged, (await IngestAsync(source, first)).Outcome);
        var revised = await IngestAsync(source, Disaster("q1", "M 6.0 - Somewhere", 6.0));
        Assert.Equal(IngestOutcome.Updated, revised.Outcome);

        var rows = await RowsAsync(source);
        var row = Assert.Single(rows);
        Assert.Equal("M 6.0 - Somewhere", row.Title);
        Assert.Equal(6.0, DisasterCategory.Deserialize(row.Data).Magnitude);
        Assert.True(row.UpdatedAt > row.FirstSeenAt);
        Assert.Equal(revised.Event.FirstSeenAt, row.FirstSeenAt);
    }

    [Fact]
    public async Task The_unique_index_rejects_a_second_row_for_the_same_item()
    {
        var source = $"test-{Guid.NewGuid():N}";
        await IngestAsync(source, Disaster("dup", "M 5", 5));

        // Bypass the ingestor's read: insert the same (source, external ID) directly.
        await using var scope = _factory.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var now = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<DuplicateKeyException>(() => runner.RunAsync(_ =>
        {
            repository.Add(new Event("disaster", source, "dup", "Copy", null, null, now, now, false, "{}"));
            return Task.FromResult(0);
        }, Ct));

        Assert.Single(await RowsAsync(source));
    }

    [Fact]
    public async Task Data_is_stored_as_jsonb_and_read_back_unchanged()
    {
        var source = $"test-{Guid.NewGuid():N}";
        await IngestAsync(source, Disaster("j1", "M 5.3 - Here", 5.3));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.Database
            .SqlQuery<string>($"""SELECT pg_typeof("Data")::text AS "Value" FROM "Events" WHERE "SourceKey" = {source}""")
            .SingleAsync(Ct);
        var magnitude = await db.Database
            .SqlQuery<string>($"""SELECT "Data"->>'magnitude' AS "Value" FROM "Events" WHERE "SourceKey" = {source}""")
            .SingleAsync(Ct);

        Assert.Equal("jsonb", type);
        Assert.Equal("5.3", magnitude);
        Assert.Equal(new DisasterData(5.3, "Here", 1.5, 2.5, 10),
            DisasterCategory.Deserialize(Assert.Single(await RowsAsync(source)).Data));
    }

    [Fact]
    public async Task A_lost_insert_race_on_postgres_ends_as_a_revision_of_the_winning_row()
    {
        var source = $"test-{Guid.NewGuid():N}";
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventRepository>();
            services.AddScoped<IEventRepository>(sp => new RacingRepository(
                new EventRepository(sp.GetRequiredService<AppDbContext>()), sp.GetRequiredService<IServiceScopeFactory>(),
                source));
        }));

        await using var scope = factory.Services.CreateAsyncScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<EventIngestor>();
        var result = await ingestor.IngestAsync(source, DisasterCategory.CategoryKey, Disaster("race", "M 5.5 - Ours", 5.5),
            false, Ct);

        // The other writer's insert won; ours hit the unique index, was rolled back and retried as a revision.
        Assert.Equal(IngestOutcome.Updated, result.Outcome);
        var row = Assert.Single(await RowsAsync(source));
        Assert.Equal("M 5.5 - Ours", row.Title);
    }

    [Fact]
    public void The_test_host_never_polls_real_sources()
    {
        Assert.False(_factory.Services.GetRequiredService<IOptions<SourcesOptions>>().Value.PollingEnabled);
    }

    /// <summary>On the first lookup, another connection commits the same item first; then it behaves normally.</summary>
    private sealed class RacingRepository(IEventRepository inner, IServiceScopeFactory scopes, string source)
        : IEventRepository
    {
        private bool _raced;

        public async Task<Event?> FindAsync(string sourceKey, IReadOnlyCollection<string> externalIds,
            CancellationToken ct)
        {
            if (!_raced && sourceKey == source)
            {
                _raced = true;
                await using var other = scopes.CreateAsyncScope();
                var db = other.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                db.Set<Event>().Add(new Event(DisasterCategory.CategoryKey, source, externalIds.First(),
                    "M 5.4 - Theirs", null, null, now, now, false,
                    DisasterCategory.Serialize(new DisasterData(5.4, "Here", null, null, null))));
                await db.SaveChangesAsync(ct);
                return null; // as if our read happened just before their insert
            }

            return await inner.FindAsync(sourceKey, externalIds, ct);
        }

        public void Add(Event item) => inner.Add(item);

        public Task<(IReadOnlyList<Event> Items, int Total)> ListAsync(string? categoryKey, string? sourceKey,
            int page, int pageSize, CancellationToken ct) => inner.ListAsync(categoryKey, sourceKey, page, pageSize, ct);
    }

    private static IncomingEvent Disaster(string id, string title, double magnitude) =>
        new(id, title, null, null, DateTimeOffset.UtcNow.AddMinutes(-5),
            DisasterCategory.Serialize(new DisasterData(magnitude, "Here", 1.5, 2.5, 10)));

    private async Task<IngestResult> IngestAsync(string source, IncomingEvent item)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<EventIngestor>();
        return await ingestor.IngestAsync(source, DisasterCategory.CategoryKey, item, false, Ct);
    }

    private async Task<List<Event>> RowsAsync(string source)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Event>().AsNoTracking().Where(e => e.SourceKey == source).ToListAsync(Ct);
    }
}
