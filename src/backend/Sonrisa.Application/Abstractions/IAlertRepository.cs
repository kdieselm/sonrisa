using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Abstractions;

/// <summary>
/// Alerts table with its destination links (spec §2.2). Every user-facing read takes the caller's
/// <c>userId</c> and filters by it (architecture §2, AC4); deleted alerts are never returned by them.
/// Writes become visible when the surrounding transaction commits.
/// </summary>
public interface IAlertRepository
{
    void Add(Alert alert);

    /// <summary>The caller's alert, tracked, with its links; null if missing, deleted or someone else's.</summary>
    Task<Alert?> GetForUserAsync(long id, string userId, CancellationToken ct);

    /// <summary>The caller's alerts, newest first, with their links. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<Alert> Items, int Total)> ListForUserAsync(string userId, int page, int pageSize,
        CancellationToken ct);

    Task<int> CountForUserAsync(string userId, CancellationToken ct);

    /// <summary>The caller's alerts linked to one destination, tracked (for unlinking on delete).</summary>
    Task<IReadOnlyList<Alert>> ListLinkedToAsync(long destinationId, string userId, CancellationToken ct);

    /// <summary>
    /// Matching read (spec §2.5): enabled, not deleted alerts of one category that were active before the event was
    /// first seen, with their linked, not deleted destinations. Not tracked.
    /// </summary>
    Task<IReadOnlyList<MatchCandidate>> ListMatchCandidatesAsync(string categoryKey, DateTimeOffset firstSeenAt,
        CancellationToken ct);
}

/// <summary>An alert as matching needs it.</summary>
public sealed record MatchCandidate(long AlertId, string Rule, IReadOnlyList<long> DestinationIds);
