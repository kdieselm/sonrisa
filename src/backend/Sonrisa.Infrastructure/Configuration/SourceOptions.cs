namespace Sonrisa.Infrastructure.Configuration;

/// <summary>Section <c>Sources</c>: settings shared by every polled source (spec §2.5).</summary>
public sealed class SourcesOptions
{
    public const string Section = "Sources";

    /// <summary>Off in the integration tests, so they never call real feeds.</summary>
    public bool PollingEnabled { get; set; } = true;

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Larger responses fail the fetch instead of filling memory.</summary>
    public long MaxResponseBytes { get; set; } = 5 * 1024 * 1024;

    public UsgsOptions Usgs { get; set; } = new();

    /// <summary>Plain RSS feeds; each one is its own source. The list is configuration, not code (spec §2.5).</summary>
    public List<RssFeedOptions> Rss { get; set; } = [];

    /// <summary>RSS sources made of a primary and a fallback feed behind a circuit breaker (D26).</summary>
    public List<RssFailoverOptions> RssFailover { get; set; } = [];
}

/// <summary>Section <c>Sources:Usgs</c>.</summary>
public sealed class UsgsOptions
{
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/4.5_day.geojson";
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// One entry of <c>Sources:RssFailover</c> (D26): the primary feed is tried up to <see cref="Attempts"/> times per
/// poll, then the fallback is read; after <see cref="FailuresToOpen"/> failed polls in a row the primary is skipped
/// for <see cref="OpenDuration"/>.
/// </summary>
public sealed class RssFailoverOptions
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
    public RssFeed Primary { get; set; } = new();
    public RssFeed Fallback { get; set; } = new();
    public int Attempts { get; set; } = 3;

    /// <summary>Wait before retry 1, 2, …; the last value repeats. Null = 2 s, then 5 s.</summary>
    public TimeSpan[]? RetryDelays { get; set; }

    public int FailuresToOpen { get; set; } = 2;
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays =>
        RetryDelays ?? [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    /// <summary>The settings of one of the two feeds as a plain RSS source (its key is not used).</summary>
    public RssFeedOptions FeedOptions(RssFeed feed) =>
        new() { Key = Key, DisplayName = feed.Name, Url = feed.Url, Interval = Interval };
}

/// <summary>A feed inside <see cref="RssFailoverOptions"/>; <see cref="Name"/> is stored as the event's feed name.</summary>
public sealed class RssFeed
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

/// <summary>One entry of <c>Sources:Rss</c>.</summary>
public sealed class RssFeedOptions
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}
