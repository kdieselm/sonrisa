using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Notifications;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>
/// The user's Notifications page (spec §2.12): their own delivery rows only, newest first, paged. The detailed error
/// text stays in the admin delivery log; the user sees a short generic reason.
/// </summary>
public sealed class NotificationService(IDeliveryRepository deliveries, ChannelRegistry channels)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(string userId, int page, int pageSize,
        CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await deliveries.ListForUserAsync(userId, page, pageSize, ct);
        return new PagedResult<NotificationDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<NotificationDto?> GetAsync(long id, string userId, CancellationToken ct) =>
        await deliveries.GetForUserAsync(id, userId, ct) is { } row ? ToDto(row) : null;

    /// <summary>Owner-safe text for each failure kind (not a channel or category key).</summary>
    public static string? FailureText(DeliveryFailure? failure) => failure switch
    {
        null => null,
        DeliveryFailure.AlertDeleted => "The alert was deleted before it was sent.",
        DeliveryFailure.DestinationDeleted => "The destination was deleted before it was sent.",
        _ => "It could not be delivered.",
    };

    private NotificationDto ToDto(NotificationRow r) =>
        new(r.Id,
            r.CreatedAt,
            r.AlertId,
            r.AlertName,
            r.AlertDeleted,
            r.EventTitle,
            r.EventUrl,
            r.EventIsSimulated,
            r.ChannelKey,
            channels.DisplayNameOf(r.ChannelKey),
            r.DestinationName,
            r.DestinationDeleted,
            r.Status.ToString(),
            r.Status == DeliveryStatus.Failed ? FailureText(r.Failure) : null,
            r.Attempts,
            r.SentAt);
}
