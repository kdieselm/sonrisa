namespace Sonrisa.Domain.Entities;

/// <summary>
/// A user's alert (spec §2.2, §2.5): one category, one rule in that category's shape, one or more destinations. The
/// rule is opaque JSON written and read only by the category (ADR-2). Soft-deleted, so its deliveries keep their
/// history (D20).
/// </summary>
public sealed class Alert
{
    public const int MaxNameLength = 100;
    public const int MaxDestinations = 10;

    private readonly List<AlertDestination> _destinations = [];

    public long Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string CategoryKey { get; private set; } = string.Empty;

    /// <summary>The category's rule as JSON (stored as jsonb).</summary>
    public string Rule { get; private set; } = "{}";

    public bool Enabled { get; private set; }

    /// <summary>
    /// Creation, last re-enable or last rule change (spec §2.5, T34). Only events first seen after it can match, so an
    /// alert never notifies about something that happened before it was active.
    /// </summary>
    public DateTimeOffset ActiveSince { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<AlertDestination> Destinations => _destinations;

    public bool IsDeleted => DeletedAt is not null;

    private Alert()
    {
    }

    public Alert(string userId, string name, string categoryKey, string rule, IEnumerable<long> destinationIds,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);
        UserId = userId;
        CategoryKey = categoryKey;
        Name = ValidName(name);
        Rule = ValidRule(rule);
        Enabled = true;
        CreatedAt = ActiveSince = Event.ToStoredPrecision(now);
        SetDestinations(destinationIds);
    }

    public void Rename(string name) => Name = ValidName(name);

    /// <summary>A changed rule makes the alert active again from now; an unchanged one changes nothing.</summary>
    public void ChangeRule(string rule, DateTimeOffset now)
    {
        rule = ValidRule(rule);
        if (JsonText.SemanticallyEqual(Rule, rule))
        {
            return;
        }

        Rule = rule;
        ActiveSince = Event.ToStoredPrecision(now);
    }

    /// <summary>
    /// Replaces the linked destinations. Does not change <see cref="ActiveSince"/>: a new destination gets matches
    /// from now on, never earlier ones (spec §2.5).
    /// </summary>
    public void SetDestinations(IEnumerable<long> destinationIds)
    {
        var ids = destinationIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxDestinations)
        {
            throw new ArgumentException($"An alert needs 1 to {MaxDestinations} destinations.", nameof(destinationIds));
        }

        _destinations.RemoveAll(d => !ids.Contains(d.DestinationId));
        foreach (var id in ids.Where(id => _destinations.All(d => d.DestinationId != id)))
        {
            _destinations.Add(new AlertDestination(Id, id));
        }
    }

    /// <summary>Removes one destination link (the destination was deleted). May leave the alert without any.</summary>
    public void Unlink(long destinationId) => _destinations.RemoveAll(d => d.DestinationId == destinationId);

    /// <summary>Re-enabling resets <see cref="ActiveSince"/> (AC5); enabling an enabled alert changes nothing.</summary>
    public void Enable(DateTimeOffset now)
    {
        if (Enabled)
        {
            return;
        }

        Enabled = true;
        ActiveSince = Event.ToStoredPrecision(now);
    }

    public void Disable() => Enabled = false;

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
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A name of 1 to {MaxNameLength} characters is required.", nameof(name));
        }

        return trimmed;
    }

    private static string ValidRule(string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        return rule;
    }
}

/// <summary>Link between an alert and a destination (T67: many-to-many).</summary>
public sealed class AlertDestination
{
    public long AlertId { get; private set; }
    public long DestinationId { get; private set; }

    private AlertDestination()
    {
    }

    public AlertDestination(long alertId, long destinationId)
    {
        AlertId = alertId;
        DestinationId = destinationId;
    }
}
