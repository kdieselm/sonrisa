namespace Sonrisa.Domain.Entities;

/// <summary>
/// Where a user's notifications go (spec §2.6): a channel key, a display name and the channel's config as opaque JSON
/// (ADR-2). The config can hold a secret (a Slack webhook URL); it leaves the API only through the channel's mask.
/// Soft-deleted, so its deliveries keep their history.
/// </summary>
public sealed class Destination
{
    public const int MaxDisplayNameLength = 100;

    public long Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ChannelKey { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The channel's config as JSON (stored as jsonb).</summary>
    public string Config { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    private Destination()
    {
    }

    public Destination(string userId, string channelKey, string displayName, string config, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelKey);
        UserId = userId;
        ChannelKey = channelKey;
        DisplayName = ValidName(displayName);
        Config = ValidConfig(config);
        CreatedAt = Event.ToStoredPrecision(now);
    }

    public void Update(string displayName, string config)
    {
        DisplayName = ValidName(displayName);
        Config = ValidConfig(config);
    }

    public void SoftDelete(DateTimeOffset now)
    {
        if (DeletedAt is null)
        {
            DeletedAt = Event.ToStoredPrecision(now);
        }
    }

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException($"A name of 1 to {MaxDisplayNameLength} characters is required.",
                nameof(name));
        }

        return trimmed;
    }

    private static string ValidConfig(string config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config);
        return config;
    }
}
