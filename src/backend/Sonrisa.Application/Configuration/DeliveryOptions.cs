namespace Sonrisa.Application.Configuration;

/// <summary>Section <c>Delivery</c> (spec §2.7, architecture §3.2).</summary>
public sealed class DeliveryOptions
{
    public const string Section = "Delivery";

    /// <summary>Off in the integration tests that drive the processor themselves.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>How often the worker looks for due rows.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Rows taken per round; the rest wait for the next round.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>One send attempt may take at most this long; a timeout is a retryable failure (T36).</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay after attempt 1, after attempt 2, … (spec §2.7: 10 s, 60 s). The last entry repeats.</summary>
    public List<TimeSpan> RetryDelays { get; set; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)];

    /// <summary>A <c>Retry-After</c> from the destination replaces the delay, up to this cap (spec §2.7).</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Public address of the web app, for the alert link in messages.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
}
