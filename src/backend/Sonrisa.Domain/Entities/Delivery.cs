namespace Sonrisa.Domain.Entities;

/// <summary>
/// One notification of one event, for one alert, to one destination (spec §2.7). Unique by
/// (<see cref="AlertId"/>, <see cref="EventId"/>, <see cref="DestinationId"/>), so the same event is never queued
/// twice for the same alert and destination (D16, AC13). Created <see cref="DeliveryStatus.Pending"/> at matching
/// time; the delivery worker (S4) sends it.
/// </summary>
public sealed class Delivery
{
    public const int MaxErrorLength = 1000;

    public long Id { get; private set; }
    public long AlertId { get; private set; }
    public long EventId { get; private set; }
    public long DestinationId { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }

    /// <summary>When the worker may try next; set while <see cref="DeliveryStatus.Pending"/>.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>
    /// Why it failed, in a form that is safe to show the owner (<see cref="DeliveryFailure"/>). The detailed text is
    /// in <see cref="LastError"/>, which only the admin delivery log shows (spec §2.12).
    /// </summary>
    public DeliveryFailure? Failure { get; private set; }

    /// <summary>Last error text for the admin view; never holds a secret.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    private Delivery()
    {
    }

    public Delivery(long alertId, long eventId, long destinationId, DateTimeOffset now)
    {
        AlertId = alertId;
        EventId = eventId;
        DestinationId = destinationId;
        Status = DeliveryStatus.Pending;
        CreatedAt = Event.ToStoredPrecision(now);
        NextAttemptAt = CreatedAt;
    }

    /// <summary>
    /// For an event inserted in the same transaction (its ID is not known yet): EF Core fills in
    /// <see cref="EventId"/> from the navigation when it saves.
    /// </summary>
    public Delivery(long alertId, Event item, long destinationId, DateTimeOffset now)
        : this(alertId, item.Id, destinationId, now)
    {
        Event = item;
    }

    public Event? Event { get; private set; }

    /// <summary>
    /// Counts the attempt <b>before</b> it is made and parks <see cref="NextAttemptAt"/> at <paramref name="leaseUntil"/>,
    /// in a saved write of its own. If the process dies or the outcome cannot be saved, the row is due again only after
    /// the lease and the attempt is already counted, so a crash costs at most <c>MaxAttempts</c> sends, not an endless
    /// loop (S4 review 2). Returns the number of this attempt.
    /// </summary>
    public int BeginAttempt(DateTimeOffset leaseUntil)
    {
        Attempts++;
        NextAttemptAt = Event.ToStoredPrecision(leaseUntil);
        return Attempts;
    }

    /// <summary>A successful attempt (spec §2.7): the delivery is done.</summary>
    public void RecordSent(DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Pending)
        {
            return;
        }

        Status = DeliveryStatus.Sent;
        SentAt = Event.ToStoredPrecision(now);
        NextAttemptAt = null;
        Failure = null;
    }

    /// <summary>A failed attempt (already counted by <see cref="BeginAttempt"/>) that will be tried again at <paramref name="nextAttemptAt"/>. Still pending.</summary>
    public void RecordRetry(string error, DateTimeOffset nextAttemptAt)
    {
        if (Status != DeliveryStatus.Pending)
        {
            return;
        }

        LastError = Truncate(error);
        NextAttemptAt = Event.ToStoredPrecision(nextAttemptAt);
    }

    /// <summary>The last failed attempt, or a permanent error: the channel gave up (<see cref="DeliveryFailure.SendFailed"/>).</summary>
    public void RecordFinalFailure(string error)
    {
        if (Status != DeliveryStatus.Pending)
        {
            return;
        }

        Fail(DeliveryFailure.SendFailed, error);
    }

    private static string Truncate(string error) =>
        error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];

    /// <summary>Ends a pending delivery as failed. Sent and already failed rows are not changed.</summary>
    public void Fail(DeliveryFailure failure, string error)
    {
        if (Status != DeliveryStatus.Pending)
        {
            return;
        }

        Status = DeliveryStatus.Failed;
        Failure = failure;
        LastError = Truncate(error);
        NextAttemptAt = null;
    }
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>Owner-safe reason of a failed delivery (spec §2.12: the user sees a short generic reason).</summary>
public enum DeliveryFailure
{
    /// <summary>The channel gave up (S4): attempts used up, or a permanent error.</summary>
    SendFailed,

    /// <summary>The alert was deleted while the delivery was pending.</summary>
    AlertDeleted,

    /// <summary>The destination was deleted while the delivery was pending.</summary>
    DestinationDeleted,
}
