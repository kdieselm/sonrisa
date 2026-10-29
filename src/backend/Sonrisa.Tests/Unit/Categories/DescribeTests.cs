using System.Text.Json;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Tests.Unit.Categories;

/// <summary>Spec §2.6, AC9, AC19: the channel-neutral content a category produces.</summary>
public sealed class DescribeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DisasterCategory Disaster = new();
    private static readonly NewsCategory News = new();

    private static Alert AlertOf(string name, string category, string rule) => new("user-1", name, category, rule, [1], T0);

    [Fact]
    public void A_disaster_message_has_magnitude_place_why_the_alert_link_and_the_disclaimer()
    {
        var rule = Disaster.ValidateRule(JsonDocument.Parse("""{"minMagnitude":5,"placeContains":"japan"}""").RootElement)
            .DataJson!;
        var data = DisasterCategory.Serialize(new DisasterData(6.1, "Near the coast of Japan", 35.0, 139.0, 10));
        var item = new Event("disaster", "usgs", "x1", "M 6.1 - Japan", null, "https://example.test/q", T0, T0, false,
            data);

        var content = Disaster.Describe(item, AlertOf("Japan quakes", "disaster", rule));

        Assert.Equal("M 6.1 - Japan", content.Title);
        Assert.Equal("Magnitude", content.KeyLabel);
        Assert.Contains("6.1", content.KeyValue);
        Assert.Contains("Near the coast of Japan", content.KeyValue);
        Assert.Equal("usgs", content.SourceName);
        Assert.Equal("https://example.test/q", content.Link);
        Assert.False(content.IsSimulated);
        Assert.Contains("Japan quakes", content.WhyMatched);
        Assert.Contains("magnitude at least 5.0", content.WhyMatched);
        Assert.Contains("japan", content.WhyMatched);
        Assert.StartsWith("/alerts/", content.AlertPath);
        Assert.Contains("Not an official warning", content.Disclaimer);
    }

    [Fact]
    public void A_simulated_event_is_marked_in_the_content()
    {
        var rule = Disaster.ValidateRule(JsonDocument.Parse("""{"minMagnitude":5}""").RootElement).DataJson!;
        var data = DisasterCategory.Serialize(new DisasterData(6.0, "Nowhere", null, null, null));
        var item = new Event("disaster", "simulated", "s1", "Simulated quake", null, null, T0, T0, true, data);

        var content = Disaster.Describe(item, AlertOf("a", "disaster", rule));

        Assert.True(content.IsSimulated);
        Assert.Null(content.Link);
    }

    [Fact]
    public void A_radius_rule_is_described_in_words()
    {
        var rule = Disaster.ValidateRule(JsonDocument
            .Parse("""{"minMagnitude":5,"near":{"latitude":35.68,"longitude":139.69,"radiusKm":500}}""").RootElement)
            .DataJson!;
        var data = DisasterCategory.Serialize(new DisasterData(6.1, "Tokyo", 35.7, 139.7, 10));
        var item = new Event("disaster", "usgs", "x2", "M 6.1", null, null, T0, T0, false, data);

        var content = Disaster.Describe(item, AlertOf("Tokyo", "disaster", rule));

        Assert.Contains("within 500 km of 35.68, 139.69", content.WhyMatched);
    }

    [Fact]
    public void A_news_message_names_the_matched_keywords_and_has_no_disclaimer()
    {
        var rule = News.ValidateRule(JsonSerializer.SerializeToElement(new { keywords = new[] { "flood", "storm", "volcano" } }))
            .DataJson!;
        var data = NewsCategory.Serialize(new NewsData("Guardian"));
        var item = new Event("news", "guardian-world", "n1", "Storm and flood hit the coast", "ignored summary",
            "https://example.test/n", T0, T0, false, data);

        var content = News.Describe(item, AlertOf("Weather", "news", rule));

        Assert.Equal("Keywords", content.KeyLabel);
        Assert.Equal("\"flood\", \"storm\"", content.KeyValue);
        Assert.Contains("Weather", content.WhyMatched);
        Assert.Null(content.Disclaimer);
    }

    [Fact]
    public void A_single_matched_keyword_uses_the_singular_label()
    {
        var rule = News.ValidateRule(JsonSerializer.SerializeToElement(new { keywords = new[] { "flood" } })).DataJson!;
        var item = new Event("news", "guardian-world", "n2", "Flood warning", null, null, T0, T0, false,
            NewsCategory.Serialize(new NewsData("Guardian")));

        Assert.Equal("Keyword", News.Describe(item, AlertOf("a", "news", rule)).KeyLabel);
    }
}
