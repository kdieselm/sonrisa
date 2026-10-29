using System.Text.Json;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Domain.Categories.News;

/// <summary>Breaking news headlines from RSS feeds (spec §2.2, §2.5).</summary>
public sealed class NewsCategory : IEventCategory
{
    public const string CategoryKey = "news";
    public const string SimulatedFeedName = "Simulated feed";

    public string Key => CategoryKey;
    public string DisplayName => "Breaking news";
    public TimeSpan? MaxAge => TimeSpan.FromHours(2);
    public bool SupportsBackfill => true;
    public string? BackfillUnavailableReason => null;

    /// <summary>The one way to write news data; sources use it so the shape has a single owner.</summary>
    public static string Serialize(NewsData data) => JsonSerializer.Serialize(data, JsonText.Options);

    public static NewsData Deserialize(string json) =>
        JsonSerializer.Deserialize<NewsData>(json, JsonText.Options) ?? throw new JsonException("News data is null.");

    public DataValidationResult ValidateSimulatedData(JsonElement data)
    {
        var errors = new FieldErrors();
        var input = errors.Read<SimulatedNewsInput>(data);
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        var feedName = string.IsNullOrWhiteSpace(input.FeedName) ? SimulatedFeedName : input.FeedName.Trim();
        if (feedName.Length > NewsData.MaxFeedNameLength)
        {
            errors.Add("feedName", $"Feed name is at most {NewsData.MaxFeedNameLength} characters.");
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        return DataValidationResult.Valid(Serialize(new NewsData(feedName)));
    }

    public static NewsRule DeserializeRule(string json) =>
        JsonSerializer.Deserialize<NewsRule>(json, JsonText.Options) ?? throw new JsonException("News rule is null.");

    public DataValidationResult ValidateRule(JsonElement rule)
    {
        var errors = new FieldErrors();
        var input = errors.Read<RuleInput>(rule, "rule");
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        // Whitespace inside a phrase is collapsed; duplicates (ignoring case) are dropped.
        var keywords = (input.Keywords ?? [])
            .Select(k => string.Join(' ', (k ?? string.Empty).Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .ToList();

        if (keywords.Count is 0 or > NewsRule.MaxKeywords)
        {
            errors.Add("keywords", $"Give 1 to {NewsRule.MaxKeywords} keywords or phrases.");
        }

        if (keywords.Any(k => k.Length > NewsRule.MaxKeywordLength))
        {
            errors.Add("keywords", $"A keyword is at most {NewsRule.MaxKeywordLength} characters.");
        }

        if (keywords.Any(k => !k.Any(char.IsLetterOrDigit)))
        {
            errors.Add("keywords", "A keyword needs at least one letter or digit.");
        }

        return errors.Any
            ? DataValidationResult.Invalid(errors.ToDictionary())
            : DataValidationResult.Valid(JsonSerializer.Serialize(new NewsRule(keywords), JsonText.Options));
    }

    /// <summary>Any keyword in the headline (the event title); the summary is not searched (D15).</summary>
    public bool Matches(Event item, string ruleJson) =>
        DeserializeRule(ruleJson).MatchedKeywords(item.Title).Count > 0;

    public NotificationContent Describe(Event item, Alert alert)
    {
        var rule = DeserializeRule(alert.Rule);
        var matched = rule.MatchedKeywords(item.Title);
        var keys = string.Join(", ", matched.Select(k => $"\"{k}\""));
        return new NotificationContent(item.Title, DisplayName, matched.Count == 1 ? "Keyword" : "Keywords", keys,
            item.SourceKey, item.Url, item.IsSimulated,
            $"Alert \"{alert.Name}\": the headline contains {keys}.", $"/alerts/{alert.Id}", null);
    }

    private sealed record RuleInput(IReadOnlyList<string?>? Keywords);

    private sealed record SimulatedNewsInput(string? FeedName);
}
