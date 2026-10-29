using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Abstractions;

/// <summary>
/// Destinations table (spec §2.6). Every user-facing read takes the caller's <c>userId</c> and filters by it
/// (architecture §2, AC4); deleted destinations are never returned by them.
/// </summary>
public interface IDestinationRepository
{
    void Add(Destination destination);

    /// <summary>The caller's destination, tracked; null if missing, deleted or someone else's.</summary>
    Task<Destination?> GetForUserAsync(long id, string userId, CancellationToken ct);

    /// <summary>The caller's destinations, oldest first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<Destination> Items, int Total)> ListForUserAsync(string userId, int page, int pageSize,
        CancellationToken ct);

    Task<int> CountForUserAsync(string userId, CancellationToken ct);

    /// <summary>Which of <paramref name="ids"/> are the caller's, not deleted destinations (linking check, AC4).</summary>
    Task<IReadOnlyList<long>> FindOwnedAsync(string userId, IReadOnlyCollection<long> ids, CancellationToken ct);
}
