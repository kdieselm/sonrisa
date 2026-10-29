namespace Sonrisa.Contracts.Dtos.Notifications;

/// <summary>
/// One row of the user's Notifications page (spec §2.12): one delivery of one event to one destination. No config,
/// no error text: a failure shows only a short generic <c>FailureReason</c>.
/// </summary>
public sealed record NotificationDto(
    long Id,
    DateTimeOffset CreatedAt,
    long AlertId,
    string AlertName,
    bool AlertDeleted,
    string EventTitle,
    string? EventUrl,
    bool EventIsSimulated,
    string ChannelKey,
    string ChannelName,
    string DestinationName,
    bool DestinationDeleted,
    string Status,
    string? FailureReason,
    int Attempts,
    DateTimeOffset? SentAt);
