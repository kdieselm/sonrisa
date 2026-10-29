using Microsoft.EntityFrameworkCore;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

public sealed class EventRepository(AppDbContext db) : IEventRepository
{
    public Task<Event?> FindAsync(string sourceKey, IReadOnlyCollection<string> externalIds, CancellationToken ct) =>
        db.Set<Event>()
            .Where(e => e.SourceKey == sourceKey && externalIds.Contains(e.ExternalId))
            .OrderBy(e => e.Id)
            .FirstOrDefaultAsync(ct);

    public void Add(Event item) => db.Set<Event>().Add(item);

    public async Task<(IReadOnlyList<Event> Items, int Total)> ListAsync(string? categoryKey, string? sourceKey,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.Set<Event>().AsNoTracking();
        if (categoryKey is not null)
        {
            query = query.Where(e => e.CategoryKey == categoryKey);
        }

        if (sourceKey is not null)
        {
            query = query.Where(e => e.SourceKey == sourceKey);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.FirstSeenAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }
}
