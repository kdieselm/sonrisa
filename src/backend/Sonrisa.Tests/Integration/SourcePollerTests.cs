using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;
using static Sonrisa.Tests.Integration.TestApi;

namespace Sonrisa.Tests.Integration;

/// <summary>AC17 (source part, T26): a source that throws does not stop the polling of the other sources.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SourcePollerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_throwing_source_does_not_stop_the_other_sources()
    {
        var throwing = new ThrowingSource();
        var healthy = new CountingSource($"healthy-{Guid.NewGuid():N}");

        await using var factory = new ApiFactory(postgres.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sources:PollingEnabled", "true");
            builder.ConfigureTestServices(services =>
            {
                // Only fake sources: the real feeds are never called from tests.
                services.RemoveAll<IEventSource>();
                services.AddSingleton<IEventSource>(throwing);
                services.AddSingleton<IEventSource>(healthy);
            });
        });
        _ = factory.Server; // start the host and its poller

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && (throwing.Calls < 3 || await CountAsync(factory, healthy.Key) < 3))
        {
            await Task.Delay(100, Ct);
        }

        Assert.True(throwing.Calls >= 3, $"the throwing source was polled {throwing.Calls} times");
        Assert.True(await CountAsync(factory, healthy.Key) >= 3, "the healthy source kept delivering events");
    }

    [Fact]
    public async Task An_item_the_database_rejects_does_not_block_the_other_items_or_later_polls()
    {
        var mixed = new MixedSource($"mixed-{Guid.NewGuid():N}");

        await using var factory = new ApiFactory(postgres.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sources:PollingEnabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEventSource>();
                services.AddSingleton<IEventSource>(mixed);
            });
        });
        _ = factory.Server;

        // Each poll: a good item, an item whose data is not valid JSON (PostgreSQL rejects it), another good item.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && mixed.Polls < 4)
        {
            await Task.Delay(100, Ct);
        }

        Assert.True(mixed.Polls >= 4, $"polled {mixed.Polls} times");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Set<Event>().Where(e => e.SourceKey == mixed.Key).Select(e => e.ExternalId).ToListAsync(Ct);

        // Polls 1–3 are complete: the item after the rejected one was stored in each, and no bad item was.
        for (var n = 1; n <= 3; n++)
        {
            Assert.Contains($"good-a-{n}", ids);
            Assert.Contains($"good-b-{n}", ids);
        }

        Assert.DoesNotContain(ids, id => id.StartsWith("bad-", StringComparison.Ordinal));
    }

    private static async Task<int> CountAsync(WebApplicationFactory<Program> factory, string sourceKey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Event>().CountAsync(e => e.SourceKey == sourceKey, Ct);
    }

    private sealed class ThrowingSource : IEventSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public string Key => "throwing";
        public string DisplayName => "Throwing";
        public string CategoryKey => NewsCategory.CategoryKey;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(100);

        public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            throw new HttpRequestException("feed is down");
        }
    }

    private sealed class MixedSource(string key) : IEventSource
    {
        private int _polls;

        public int Polls => Volatile.Read(ref _polls);
        public string Key => key;
        public string DisplayName => "Mixed";
        public string CategoryKey => NewsCategory.CategoryKey;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(100);

        public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _polls);
            var data = NewsCategory.Serialize(new NewsData("Test"));
            IReadOnlyList<IncomingEvent> items =
            [
                new($"good-a-{n}", "Good A", null, null, null, data),
                new($"bad-{n}", "Bad", null, null, null, "{not json"),
                new($"good-b-{n}", "Good B", null, null, null, data),
            ];
            return Task.FromResult(items);
        }
    }

    /// <summary>Returns one new item per poll.</summary>
    private sealed class CountingSource(string key) : IEventSource
    {
        private int _calls;

        public string Key => key;
        public string DisplayName => "Healthy";
        public string CategoryKey => NewsCategory.CategoryKey;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(100);

        public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _calls);
            IReadOnlyList<IncomingEvent> items =
                [new IncomingEvent($"item-{n}", $"Headline {n}", null, null, null, NewsCategory.Serialize(new NewsData("Test")))];
            return Task.FromResult(items);
        }
    }
}
