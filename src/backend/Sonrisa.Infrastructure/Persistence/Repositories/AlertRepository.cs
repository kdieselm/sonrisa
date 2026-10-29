using Microsoft.EntityFrameworkCore;
using Sonrisa.Application;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

public sealed class AlertRepository(AppDbContext db) : IAlertRepository
{
    public void Add(Alert alert) => db.Set<Alert>().Add(alert);

    public Task<Alert?> GetForUserAsync(long id, string userId, CancellationToken ct) =>
        Owned(userId).Include(a => a.Destinations).FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<(IReadOnlyList<Alert> Items, int Total)> ListForUserAsync(string userId, int page,
        int pageSize, CancellationToken ct)
    {
        var query = Owned(userId).AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .Include(a => a.Destinations)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountForUserAsync(string userId, CancellationToken ct) => Owned(userId).CountAsync(ct);

    public async Task<IReadOnlyList<Alert>> ListLinkedToAsync(long destinationId, string userId,
        CancellationToken ct) =>
        await Owned(userId)
            .Include(a => a.Destinations)
            .Where(a => a.Destinations.Any(d => d.DestinationId == destinationId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MatchCandidate>> ListMatchCandidatesAsync(string categoryKey,
        DateTimeOffset firstSeenAt, CancellationToken ct)
    {
        var rows = await db.Set<Alert>().AsNoTracking()
            .Where(a => a.CategoryKey == categoryKey && a.Enabled && a.DeletedAt == null
                        && a.ActiveSince < firstSeenAt)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.Rule,
                DestinationIds = a.Destinations
                    .Where(link => db.Set<Destination>()
                        .Any(d => d.Id == link.DestinationId && d.DeletedAt == null))
                    .Select(link => link.DestinationId)
                    .ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new MatchCandidate(r.Id, r.Rule, r.DestinationIds.Order().ToList())).ToList();
    }

    /// <summary>The one starting point of every user-facing query: the caller's not deleted alerts.</summary>
    private IQueryable<Alert> Owned(string userId) =>
        db.Set<Alert>().Where(a => a.UserId == userId && a.DeletedAt == null);
}
