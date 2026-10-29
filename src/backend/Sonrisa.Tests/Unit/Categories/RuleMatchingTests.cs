using System.Text.Json;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Tests.Unit.Categories;

/// <summary>AC6: rule validation and matching per category, with the boundaries of spec §2.2 and D27.</summary>
public sealed class RuleMatchingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DisasterCategory Disaster = new();
    private static readonly NewsCategory News = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string DisasterRule(string json)
    {
        var result = Disaster.ValidateRule(Json(json));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.SelectMany(e => e.Value)));
        return result.DataJson!;
    }

    private static string NewsRule(params string[] keywords)
    {
        var result = News.ValidateRule(JsonSerializer.SerializeToElement(new { keywords }));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.SelectMany(e => e.Value)));
        return result.DataJson!;
    }

    private static Event Quake(double magnitude, string place = "10 km SW of Somewhere, Chile",
        double? latitude = null, double? longitude = null) =>
        new("disaster", "usgs", Guid.NewGuid().ToString("N"), $"M {magnitude}", null, null, T0, T0, false,
            DisasterCategory.Serialize(new DisasterData(magnitude, place, latitude, longitude, 10)));

    private static Event Headline(string title, string? summary = null) =>
        new("news", "world-news", Guid.NewGuid().ToString("N"), title, summary, null, T0, T0, false,
            NewsCategory.Serialize(new NewsData("Test feed")));

    // --- Disaster: magnitude and place -------------------------------------------------------------------------

    [Fact]
    public void Magnitude_equal_to_the_minimum_matches_and_one_tenth_below_does_not()
    {
        var rule = DisasterRule("""{"minMagnitude":6.0}""");

        Assert.True(Disaster.Matches(Quake(6.0), rule));
        Assert.True(Disaster.Matches(Quake(7.2), rule));
        Assert.False(Disaster.Matches(Quake(5.9), rule));
    }

    [Fact]
    public void Place_text_is_matched_case_insensitively_as_a_substring()
    {
        var rule = DisasterRule("""{"minMagnitude":5,"placeContains":"  CHILE "}""");

        Assert.True(Disaster.Matches(Quake(5.5, "45 km W of Valparaíso, Chile"), rule));
        Assert.False(Disaster.Matches(Quake(5.5, "Off the coast of Peru"), rule));
    }

    // --- Disaster: point and radius (D27) ----------------------------------------------------------------------

    [Fact]
    public void An_epicentre_exactly_at_the_radius_matches_and_just_beyond_does_not()
    {
        // Budapest → Vienna, computed with the same formula; the rule radius is exactly that distance.
        var distance = GeoCircle.DistanceKm(47.4979, 19.0402, 48.2082, 16.3738);
        string Rule(double radiusKm) => DisasterRule(JsonSerializer.Serialize(new
        {
            minMagnitude = 5,
            near = new { latitude = 47.4979, longitude = 19.0402, radiusKm },
        }));
        var at = Rule(distance);
        var short_ = Rule(distance - 0.5);

        Assert.InRange(distance, 210, 220); // sanity check of the formula: about 214 km
        Assert.True(Disaster.Matches(Quake(5.5, "Vienna", 48.2082, 16.3738), at));
        Assert.False(Disaster.Matches(Quake(5.5, "Vienna", 48.2082, 16.3738), short_));
    }

    [Fact]
    public void A_radius_rule_never_matches_an_event_without_coordinates()
    {
        var rule = DisasterRule("""{"minMagnitude":5,"near":{"latitude":0,"longitude":0,"radiusKm":20000}}""");

        Assert.False(Disaster.Matches(Quake(6.0), rule));
        Assert.True(Disaster.Matches(Quake(6.0, latitude: -45, longitude: 170), rule)); // 20 000 km covers the Earth
    }

    [Fact]
    public void Distance_works_across_the_antimeridian()
    {
        // Two points 0.2° apart in longitude, on both sides of ±180°: about 22 km, not 40 000.
        Assert.InRange(GeoCircle.DistanceKm(0, 179.9, 0, -179.9), 21, 23);
    }

    [Fact]
    public void Place_and_radius_must_both_hold_when_both_are_set()
    {
        var rule = DisasterRule(
            """{"minMagnitude":5,"placeContains":"Japan","near":{"latitude":35.68,"longitude":139.69,"radiusKm":300}}""");

        Assert.True(Disaster.Matches(Quake(6, "Near the east coast of Honshu, Japan", 36.5, 140.5), rule));
        Assert.False(Disaster.Matches(Quake(6, "Near the east coast of Honshu, Japan", 43.0, 145.0), rule));
        Assert.False(Disaster.Matches(Quake(6, "Somewhere else", 36.5, 140.5), rule));
    }

    [Theory]
    [InlineData("""{}""", "minMagnitude")]
    [InlineData("""{"minMagnitude":4.4}""", "minMagnitude")]
    [InlineData("""{"minMagnitude":10.1}""", "minMagnitude")]
    [InlineData("""{"minMagnitude":"big"}""", "rule")]
    [InlineData("""[1]""", "rule")]
    [InlineData("""{"minMagnitude":5,"near":{"latitude":91,"longitude":0,"radiusKm":10}}""", "near.latitude")]
    [InlineData("""{"minMagnitude":5,"near":{"latitude":0,"longitude":-181,"radiusKm":10}}""", "near.longitude")]
    [InlineData("""{"minMagnitude":5,"near":{"latitude":0,"longitude":0,"radiusKm":0.5}}""", "near.radiusKm")]
    [InlineData("""{"minMagnitude":5,"near":{"latitude":0,"longitude":0}}""", "near.radiusKm")]
    public void Invalid_disaster_rules_are_rejected(string json, string field)
    {
        var result = Disaster.ValidateRule(Json(json));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void The_feed_floor_is_an_allowed_minimum_and_a_blank_place_is_dropped()
    {
        var rule = DisasterCategory.DeserializeRule(DisasterRule("""{"minMagnitude":4.5,"placeContains":"   "}"""));

        Assert.Equal(new DisasterRule(4.5, null, null), rule);
    }

    // --- News: headline keywords (D15) -------------------------------------------------------------------------

    [Fact]
    public void A_keyword_matches_a_whole_word_in_the_headline_case_insensitively()
    {
        var rule = NewsRule("earthquake");

        Assert.True(News.Matches(Headline("Strong EARTHQUAKE hits coast"), rule));
        Assert.True(News.Matches(Headline("Earthquake: what we know"), rule));
    }

    [Fact]
    public void A_keyword_inside_another_word_does_not_match()
    {
        var rule = NewsRule("war");

        Assert.False(News.Matches(Headline("Software warning for drivers"), rule));
        Assert.False(News.Matches(Headline("Award season begins"), rule));
        Assert.True(News.Matches(Headline("War in the region enters day 3"), rule));
    }

    [Fact]
    public void The_summary_alone_is_not_searched()
    {
        Assert.False(News.Matches(Headline("Markets close higher", "An earthquake struck overnight"),
            NewsRule("earthquake")));
    }

    [Fact]
    public void A_phrase_matches_across_any_whitespace_and_any_keyword_is_enough()
    {
        var rule = NewsRule("  central   bank ", "flood");

        Assert.True(News.Matches(Headline("Central  bank raises rates"), rule));
        Assert.True(News.Matches(Headline("Flood warnings issued"), rule));
        Assert.False(News.Matches(Headline("Floods hit town"), rule)); // plural is another word
        Assert.False(News.Matches(Headline("Central banking news"), rule));
    }

    [Fact]
    public void Keywords_with_symbols_and_regex_characters_are_literal()
    {
        Assert.True(News.Matches(Headline("Why C++ still matters"), NewsRule("C++")));
        Assert.False(News.Matches(Headline("C is old"), NewsRule("C++")));
        Assert.False(News.Matches(Headline("abc"), NewsRule("a.c")));
        Assert.True(News.Matches(Headline("The a.c unit failed"), NewsRule("a.c")));
    }

    [Fact]
    public void Keywords_are_trimmed_collapsed_and_deduplicated()
    {
        var rule = NewsCategory.DeserializeRule(NewsRule(" Flood ", "flood", "central   bank"));

        Assert.Equal(["Flood", "central bank"], rule.Keywords);
    }

    [Theory]
    [InlineData("""{"keywords":[]}""")]
    [InlineData("""{"keywords":["   "]}""")]
    [InlineData("""{"keywords":["a","b","c","d","e","f","g","h","i","j","k"]}""")]
    [InlineData("""{"keywords":["!!!"]}""")]
    [InlineData("""{}""")]
    public void Invalid_news_rules_are_rejected(string json)
    {
        var result = News.ValidateRule(Json(json));

        Assert.False(result.IsValid);
        Assert.Contains("keywords", result.Errors.Keys);
    }

    [Fact]
    public void A_too_long_keyword_is_rejected()
    {
        var result = News.ValidateRule(JsonSerializer.SerializeToElement(new { keywords = new[] { new string('a', 101) } }));

        Assert.False(result.IsValid);
    }
}
