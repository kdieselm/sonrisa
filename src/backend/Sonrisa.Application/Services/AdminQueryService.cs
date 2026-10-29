using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Mapping;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>Read-only queries of the admin view (spec §2.9). Every list is paged (<see cref="Paging"/>).</summary>
public sealed class AdminQueryService(
    IUserDirectory users,
    IAuditRepository audit,
    IAlertRepository alerts,
    IDestinationRepository destinations,
    IDeliveryRepository deliveries,
    ISlackMockStore slackMock,
    CategoryRegistry categories,
    ChannelRegistry channels)
{
    /// <summary>
    /// One user with their alerts and destinations (configs masked). One page holds all of them, because both are
    /// capped per user below <see cref="Paging.MaxPageSize"/>. Null if the user is unknown.
    /// </summary>
    public async Task<AdminUserDetailDto?> GetUserAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        var (userAlerts, _) = await alerts.ListForUserAsync(userId, 1, Paging.MaxPageSize, ct);
        var (userDestinations, _) = await destinations.ListForUserAsync(userId, 1, Paging.MaxPageSize, ct);
        return new AdminUserDetailDto(
            new AdminUserDto(user.Id, user.Email, user.Roles),
            userAlerts.Select(a => a.ToDto(categories)).ToList(),
            userDestinations.Select(d => d.ToDto(channels)).ToList());
    }

    public async Task<PagedResult<AdminUserDto>> ListUsersAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var (items, total) = await users.ListAsync(page, pageSize, ct);
        var dtos = items.Select(u => new AdminUserDto(u.Id, u.Email, u.Roles)).ToList();
        return new PagedResult<AdminUserDto>(dtos, page, pageSize, total);
    }

    /// <summary>
    /// The delivery log (spec §2.9): all deliveries, newest first, with the last error text the user never sees.
    /// <paramref name="status"/> is a <see cref="DeliveryStatus"/> name or empty; any other text yields null.
    /// </summary>
    public async Task<PagedResult<AdminDeliveryDto>?> ListDeliveriesAsync(string? status, int page, int pageSize,
        CancellationToken ct)
    {
        DeliveryStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DeliveryStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return null;
            }

            filter = parsed;
        }

        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await deliveries.ListForAdminAsync(filter, page, pageSize, ct);

        var emails = await users.GetEmailsAsync(items.Select(i => i.OwnerUserId).Distinct().ToList(), ct);
        var dtos = items.Select(r => new AdminDeliveryDto(
            r.Id,
            r.CreatedAt,
            r.OwnerUserId,
            emails.GetValueOrDefault(r.OwnerUserId),
            r.AlertId,
            r.AlertName,
            r.EventId,
            r.EventTitle,
            r.EventIsSimulated,
            r.ChannelKey,
            channels.DisplayNameOf(r.ChannelKey),
            r.DestinationName,
            r.Status.ToString(),
            r.Attempts,
            r.NextAttemptAt,
            r.SentAt,
            r.Failure?.ToString(),
            r.LastError)).ToList();
        return new PagedResult<AdminDeliveryDto>(dtos, page, pageSize, total);
    }

    /// <summary>Messages the Slack mock receiver accepted, newest first (spec §2.9).</summary>
    public async Task<PagedResult<SlackMockMessageDto>> ListSlackMockAsync(int page, int pageSize,
        CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await slackMock.ListAsync(page, pageSize, ct);
        return new PagedResult<SlackMockMessageDto>(
            items.Select(m => new SlackMockMessageDto(m.Id, m.HookName, m.Body, m.ReceivedAt)).ToList(), page,
            pageSize, total);
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
