using Microsoft.EntityFrameworkCore;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository(AppDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditEntry entry, CancellationToken ct)
    {
        db.Set<AuditEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, int Total)> ListAsync(int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.Set<AuditEntry>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.At)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }
}
