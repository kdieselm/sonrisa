namespace Sonrisa.Domain.Entities;

/// <summary>
/// One row of the append-only audit log (spec §2.10). Never holds secrets: no passwords, tokens or webhook URLs.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; private set; }
    public DateTimeOffset At { get; private set; }

    /// <summary>The acting user; null when unknown (for example a failed login with an unknown email).</summary>
    public string? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public AuditOutcome Outcome { get; private set; }

    private AuditEntry()
    {
    }

    public AuditEntry(DateTimeOffset at, string? actorUserId, string action, string? targetType, string? targetId,
        AuditOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        At = at;
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
    }
}

public enum AuditOutcome
{
    Success,
    Failure,
}
