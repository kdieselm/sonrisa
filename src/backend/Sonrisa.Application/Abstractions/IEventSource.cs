namespace Sonrisa.Application.Abstractions;

/// <summary>
/// A source adapter (spec §2.5, §2.11): fetches one external feed and returns normalized events of one category.
/// Polled by <c>SourcePoller</c> in its own loop on <see cref="Interval"/>; an exception is logged and the loop goes
/// on (T26). Sources are registered once, in the Api's source registration; how to add one:
/// <c>docs/how-to/add-a-source.md</c>.
/// </summary>
public interface IEventSource
{
    /// <summary>Stable key stored in <c>Events.SourceKey</c>, for example <c>usgs</c>.</summary>
    string Key { get; }

    /// <summary>Label for the admin view.</summary>
    string DisplayName { get; }

    /// <summary>Key of the category every event of this source belongs to; checked against the registry at startup.</summary>
    string CategoryKey { get; }

    TimeSpan Interval { get; }

    Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct);
}

/// <summary>
/// One item as a source delivers it. <paramref name="PublishedAt"/> null = unknown (the ingestor uses the first-seen
/// time). <paramref name="DataJson"/> is written with the category's own serializer. <paramref name="AlternateIds"/>
/// are other IDs of the same item (USGS lists every network's ID in <c>ids</c>, and the preferred one can differ
/// later); an item known by any of them updates its row instead of creating a second one.
/// </summary>
public sealed record IncomingEvent(
    string ExternalId,
    string Title,
    string? Summary,
    string? Url,
    DateTimeOffset? PublishedAt,
    string DataJson,
    IReadOnlyList<string>? AlternateIds = null);
