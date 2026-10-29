namespace Sonrisa.Domain.Categories.News;

/// <summary>Data of a news event (spec §2.3): the feed it came from. The headline is the event title.</summary>
public sealed record NewsData(string FeedName)
{
    public const int MaxFeedNameLength = 100;
}
