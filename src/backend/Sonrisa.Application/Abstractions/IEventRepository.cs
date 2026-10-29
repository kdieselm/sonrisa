using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Abstractions;

/// <summary>Events table (spec §2.3). Writes become visible when the surrounding transaction commits.</summary>
public interface IEventRepository
{
    /// <summary>The row of this source whose external ID is one of <paramref name="externalIds"/>; the oldest if several.</summary>
    Task<Event?> FindAsync(string sourceKey, IReadOnlyCollection<string> externalIds, CancellationToken ct);

    void Add(Event item);

    /// <summary>Newest first by first-seen time. Filters are optional. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<Event> Items, int Total)> ListAsync(string? categoryKey, string? sourceKey, int page,
        int pageSize, CancellationToken ct);

    /// <summary>
    /// Back-fill read (spec §2.5, AC14): events of one category first seen at or after <paramref name="since"/>,
    /// newest first, at most <paramref name="limit"/>. Not tracked.
    /// </summary>
    Task<IReadOnlyList<Event>> ListRecentAsync(string categoryKey, DateTimeOffset since, int limit,
        CancellationToken ct);
}
