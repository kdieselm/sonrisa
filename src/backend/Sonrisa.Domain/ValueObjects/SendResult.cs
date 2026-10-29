namespace Sonrisa.Domain.ValueObjects;

public enum SendOutcome
{
    Sent,

    /// <summary>Worth another attempt: timeout, connection error, 429, 5xx, SMTP 4xx.</summary>
    Retryable,

    /// <summary>Final: the destination refused the message and will keep refusing it (spec §2.7, D13).</summary>
    Permanent,
}

/// <summary>
/// What a channel reports for one send attempt (architecture §3.2). The channel classifies the outcome; the delivery
/// processor owns the attempt count and the delays. <see cref="Error"/> never contains a secret (spec §2.7).
/// </summary>
public sealed record SendResult(SendOutcome Outcome, string? Error = null, TimeSpan? RetryAfter = null)
{
    public static SendResult Sent() => new(SendOutcome.Sent);

    public static SendResult Retryable(string error, TimeSpan? retryAfter = null) =>
        new(SendOutcome.Retryable, error, retryAfter);

    public static SendResult Permanent(string error) => new(SendOutcome.Permanent, error);
}
