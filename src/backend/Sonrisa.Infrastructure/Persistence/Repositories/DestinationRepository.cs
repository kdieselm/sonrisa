using Microsoft.EntityFrameworkCore;
using Sonrisa.Application;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

public sealed class DestinationRepository(AppDbContext db) : IDestinationRepository
{
    public void Add(Destination destination) => db.Set<Destination>().Add(destination);

    public Task<Destination?> GetForUserAsync(long id, string userId, CancellationToken ct) =>
        Owned(userId).FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<(IReadOnlyList<Destination> Items, int Total)> ListForUserAsync(string userId, int page,
        int pageSize, CancellationToken ct)
    {
        var query = Owned(userId).AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountForUserAsync(string userId, CancellationToken ct) => Owned(userId).CountAsync(ct);

    public async Task<IReadOnlyList<long>> FindOwnedAsync(string userId, IReadOnlyCollection<long> ids,
        CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await Owned(userId).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct);

    /// <summary>The one starting point of every user-facing query: the caller's not deleted destinations.</summary>
    private IQueryable<Destination> Owned(string userId) =>
        db.Set<Destination>().Where(d => d.UserId == userId && d.DeletedAt == null);
}
