using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>The one place that writes audit entries (spec §2.10). Callers pass IDs only, never secrets.</summary>
public sealed class AuditService(IAuditRepository repository, TimeProvider time)
{
    public Task RecordAsync(string? actorUserId, string action, string? targetType, string? targetId,
        AuditOutcome outcome, CancellationToken ct) =>
        repository.AddAsync(
            new AuditEntry(time.GetUtcNow(), actorUserId, action, targetType, targetId, outcome), ct);
}
