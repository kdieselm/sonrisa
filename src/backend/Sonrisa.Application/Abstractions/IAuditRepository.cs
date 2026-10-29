using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Abstractions;

/// <summary>Append-only store of audit entries (spec §2.10).</summary>
public interface IAuditRepository
{
    Task AddAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>Newest first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<AuditEntry> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct);
}
