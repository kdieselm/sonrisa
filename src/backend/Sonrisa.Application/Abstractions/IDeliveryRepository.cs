using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Abstractions;

/// <summary>
/// Deliveries table (spec §2.7). The unique index on (alert, event, destination) rejects a second row for the same
/// triple (AC13). The user-facing reads take the caller's <c>userId</c> and return only rows of the caller's alerts
/// (spec §2.12, AC21).
/// </summary>
public interface IDeliveryRepository
{
    void Add(Delivery delivery);

    /// <summary>
    /// Turns the pending deliveries of an alert into failed ones (spec §2.6: deleting an alert), inside the
    /// surrounding transaction. Returns how many rows changed.
    /// </summary>
    Task<int> FailPendingForAlertAsync(long alertId, DeliveryFailure failure, string error, CancellationToken ct);

    /// <summary>The same for a deleted destination.</summary>
    Task<int> FailPendingForDestinationAsync(long destinationId, DeliveryFailure failure, string error,
        CancellationToken ct);

    /// <summary>The caller's notifications, newest first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<NotificationRow> Items, int Total)> ListForUserAsync(string userId, int page, int pageSize,
        CancellationToken ct);

    /// <summary>One of the caller's notifications; null if missing or someone else's.</summary>
    Task<NotificationRow?> GetForUserAsync(long id, string userId, CancellationToken ct);
}

/// <summary>
/// A delivery joined with what the Notifications page shows. Carries no config and no error text (spec §2.12).
/// </summary>
public sealed record NotificationRow(
    long Id,
    DateTimeOffset CreatedAt,
    long AlertId,
    string AlertName,
    bool AlertDeleted,
    long EventId,
    string EventTitle,
    string? EventUrl,
    bool EventIsSimulated,
    string ChannelKey,
    string DestinationName,
    bool DestinationDeleted,
    DeliveryStatus Status,
    DeliveryFailure? Failure,
    int Attempts,
    DateTimeOffset? SentAt);
