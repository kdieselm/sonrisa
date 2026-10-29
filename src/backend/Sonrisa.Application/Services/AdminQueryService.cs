using Sonrisa.Application.Abstractions;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Application.Services;

/// <summary>Read-only queries of the admin view (spec §2.9). Every list is paged (<see cref="Paging"/>).</summary>
public sealed class AdminQueryService(IUserDirectory users, IAuditRepository audit)
{
    public async Task<PagedResult<AdminUserDto>> ListUsersAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var (items, total) = await users.ListAsync(page, pageSize, ct);
        var dtos = items.Select(u => new AdminUserDto(u.Id, u.Email, u.Roles)).ToList();
        return new PagedResult<AdminUserDto>(dtos, page, pageSize, total);
    }

    public async Task<PagedResult<AuditEntryDto>> ListAuditAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var (items, total) = await audit.ListAsync(page, pageSize, ct);

        // At most one page of actors, so the lookup stays within IUserDirectory's batch limit.
        var actorIds = items.Select(e => e.ActorUserId).OfType<string>().Distinct().ToList();
        var emails = await users.GetEmailsAsync(actorIds, ct);

        var dtos = items
            .Select(e => new AuditEntryDto(
                e.Id,
                e.At,
                e.ActorUserId,
                e.ActorUserId is not null && emails.TryGetValue(e.ActorUserId, out var email) ? email : null,
                e.Action,
                e.TargetType,
                e.TargetId,
                e.Outcome.ToString()))
            .ToList();

        return new PagedResult<AuditEntryDto>(dtos, page, pageSize, total);
    }
}
