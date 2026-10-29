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

    private sealed record SimulatedNewsInput(string? FeedName);
}
