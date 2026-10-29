using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.News;

namespace Sonrisa.Tests.Unit.Registries;

/// <summary>ADR-2: registries map keys to implementations and fail at startup on a broken registration.</summary>
public sealed class RegistryTests
{
    private static readonly CategoryRegistry Categories = new([new DisasterCategory(), new NewsCategory()]);

    [Fact]
    public void Finds_categories_and_sources_by_key()
    {
        var sources = new SourceRegistry([new StubSource("a", "news")], Categories);

        Assert.IsType<DisasterCategory>(Categories.Find("disaster"));
        Assert.Null(Categories.Find("weather"));
        Assert.Equal("a", sources.Find("a")?.Key);
        Assert.Null(sources.Find("b"));
    }

    [Fact]
    public void A_duplicate_category_key_fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new CategoryRegistry([new DisasterCategory(), new StubCategory("disaster")]));
        Assert.Contains("'disaster'", ex.Message);
    }

    [Fact]
    public void A_duplicate_source_key_fails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SourceRegistry([new StubSource("a", "news"), new StubSource("a", "disaster")], Categories));
    }

    [Fact]
    public void A_source_of_an_unregistered_category_fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SourceRegistry([new StubSource("a", "market")], Categories));
        Assert.Contains("'market'", ex.Message);
    }

    [Fact]
    public void The_simulated_source_key_is_reserved()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SourceRegistry([new StubSource(SourceRegistry.SimulatedKey, "news")], Categories));
    }

    [Fact]
    public void A_source_needs_a_positive_interval()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SourceRegistry([new StubSource("a", "news", TimeSpan.Zero)], Categories));
    }

    private sealed class StubCategory(string key) : IEventCategory
    {
        public string Key => key;
        public string DisplayName => key;
        public TimeSpan? MaxAge => null;
        public DataValidationResult ValidateSimulatedData(JsonElement data) => DataValidationResult.Valid("{}");
        public DataValidationResult ValidateRule(JsonElement rule) => DataValidationResult.Valid("{}");
        public bool Matches(Event item, string ruleJson) => false;
        public bool SupportsBackfill => false;
        public string? BackfillUnavailableReason => null;

        public NotificationContent Describe(Event item, Alert alert) =>
            new(item.Title, key, "Key", "value", item.SourceKey, null, false, "test", "/alerts/1", null);
    }

    private sealed class StubSource(string key, string category, TimeSpan? interval = null) : IEventSource
    {
        public string Key => key;
        public string DisplayName => key;
        public string CategoryKey => category;
        public TimeSpan Interval => interval ?? TimeSpan.FromMinutes(1);

        public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IncomingEvent>>([]);
    }
}
