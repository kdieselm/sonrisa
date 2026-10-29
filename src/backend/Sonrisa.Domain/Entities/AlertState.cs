namespace Sonrisa.Domain.Entities;

/// <summary>
/// The state a stateful category keeps per alert (spec §2.4, ADR-5), as JSON in the category's own shape. Written in
/// the transaction of the event that changed it. Kept apart from <see cref="Alert"/> so the ingestor and the owner
/// editing the alert never write the same row.
/// </summary>
public sealed class AlertState
{
    public long AlertId { get; private set; }
    public string State { get; private set; } = "{}";
    public DateTimeOffset UpdatedAt { get; private set; }

    private AlertState()
    {
    }

    public AlertState(string state, DateTimeOffset now)
    {
        Set(state, now);
    }

    public AlertState(long alertId, string state, DateTimeOffset now) : this(state, now)
    {
        AlertId = alertId;
    }

    public void Set(string state, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        State = state;
        UpdatedAt = Event.ToStoredPrecision(now);
    }
}
