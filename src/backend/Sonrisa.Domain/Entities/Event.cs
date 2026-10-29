namespace Sonrisa.Domain.Entities;

/// <summary>
/// One ingested world event (spec §2.3). Unique by (<see cref="SourceKey"/>, <see cref="ExternalId"/>). The shape of
/// <see cref="Data"/> belongs to the category (ADR-2): core code stores and passes it without reading it.
/// </summary>
public sealed class Event
{
    public const int MaxTitleLength = 500;
    public const int MaxSummaryLength = 2000;
    public const int MaxUrlLength = 2000;
    public const int MaxExternalIdLength = 500;

    public long Id { get; private set; }
    public string CategoryKey { get; private set; } = string.Empty;
    public string SourceKey { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    public string? Url { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsSimulated { get; private set; }

    /// <summary>Category-specific data as JSON (stored as jsonb).</summary>
    public string Data { get; private set; } = "{}";

    private Event()
    {
    }

    public Event(string categoryKey, string sourceKey, string externalId, string title, string? summary, string? url,
        DateTimeOffset publishedAt, DateTimeOffset firstSeenAt, bool isSimulated, string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(data);
        CategoryKey = categoryKey;
        SourceKey = sourceKey;
        ExternalId = externalId;
        Title = title;
        Summary = summary;
        Url = url;
        PublishedAt = ToStoredPrecision(publishedAt);
        FirstSeenAt = ToStoredPrecision(firstSeenAt);
        UpdatedAt = FirstSeenAt;
        IsSimulated = isSimulated;
        Data = data;
    }

    /// <summary>
    /// Applies a revision of the same item (spec §2.3, T9). <see cref="FirstSeenAt"/> never changes, so a revision is
    /// never treated as a new event. Returns false and changes nothing when the content is the same.
    /// </summary>
    public bool Revise(string title, string? summary, string? url, DateTimeOffset publishedAt, string data,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(data);
        publishedAt = ToStoredPrecision(publishedAt);
        if (Title == title && Summary == summary && Url == url && PublishedAt == publishedAt
            && JsonText.SemanticallyEqual(Data, data))
        {
            return false;
        }

        Title = title;
        Summary = summary;
        Url = url;
        PublishedAt = publishedAt;
        Data = data;
        UpdatedAt = ToStoredPrecision(now);
        return true;
    }

    /// <summary>
    /// PostgreSQL <c>timestamptz</c> keeps microseconds, .NET keeps 100 ns ticks. Rounding down here keeps an entity
    /// equal to its stored row, so re-reading an unchanged item is not mistaken for a revision.
    /// </summary>
    public static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
