using System.Text.Json;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Domain.Categories;

/// <summary>
/// An event category (spec §2.11, ADR-2): disaster, news, later market. Each category owns the shape of its event
/// data and validates it. Core code finds a category only through the category registry by <see cref="Key"/>, never
/// by a <c>switch</c> on the key. How to add one: <c>docs/how-to/add-a-category.md</c>.
/// </summary>
/// <remarks>
/// S3a: data, age limit, simulated data. S3b: the alert rule and matching. S4: the message content
/// (<c>Describe</c>). S5 adds the per-alert state that a stateful category (market) needs.
/// </remarks>
public interface IEventCategory
{
    /// <summary>Stable key stored in <c>Events.CategoryKey</c>, for example <c>disaster</c>.</summary>
    string Key { get; }

    /// <summary>Label for the admin view.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Age limit (spec §2.5, T34): an event published more than this before it was first seen is stored and listed,
    /// but it never notifies. Feeds repeat older items (the USGS day feed holds 24 h), so without this the first poll
    /// after a start or an outage would notify about old events. Null = no limit (market observations are always
    /// fresh). Used by matching from S3b on.
    /// </summary>
    TimeSpan? MaxAge { get; }

    /// <summary>
    /// Validates the category-specific fields of the admin "simulate event" form and returns the event data JSON in
    /// the category's own shape.
    /// </summary>
    DataValidationResult ValidateSimulatedData(JsonElement data);

    /// <summary>
    /// Validates the rule of an alert of this category (spec §2.2) and returns it as JSON in the category's own
    /// shape, normalized (trimmed, defaults filled in). Stored in <c>Alerts.Rule</c> and read only by this category.
    /// </summary>
    DataValidationResult ValidateRule(JsonElement rule);

    /// <summary>
    /// Whether <paramref name="item"/> (an event of this category) matches <paramref name="ruleJson"/> (a rule this
    /// category wrote). Pure: no clock, no I/O. Timing rules (active since, age limit) are applied by the caller.
    /// </summary>
    bool Matches(Event item, string ruleJson);

    /// <summary>
    /// Matching with state (spec §2.4, ADR-5). The ingestor calls this, not <see cref="Matches"/>. A category without
    /// state keeps the default, which is <see cref="Matches"/> and no state. A stateful category (market) reads the
    /// alert's stored state (null = none) and returns the new one; the ingestor saves it in the event's transaction.
    /// Pure: no clock, no I/O.
    /// </summary>
    MatchOutcome Evaluate(Event item, string ruleJson, string? stateJson) => new(Matches(item, ruleJson));

    /// <summary>
    /// The state a new alert starts with, when it becomes active (create, re-enable, rule change; spec §2.5). Null =
    /// no state, which is the default.
    /// </summary>
    string? InitialState(string ruleJson, IObservationStore observations) => null;

    /// <summary>
    /// The channel-neutral content of the notification for <paramref name="item"/> matched by <paramref name="alert"/>
    /// (spec §2.6, ADR-2). Pure. <see cref="NotificationContent.SourceName"/> holds the source key; the delivery
    /// processor replaces it with the display name and fills in the absolute alert link.
    /// </summary>
    NotificationContent Describe(Event item, Alert alert);

    /// <summary>
    /// Whether the alert page may list recent matching events (spec §2.5, D11). False for a category whose events are
    /// observations relative to a previous state (market), where a past event says nothing about the alert.
    /// </summary>
    bool SupportsBackfill { get; }

    /// <summary>Shown instead of the list when <see cref="SupportsBackfill"/> is false.</summary>
    string? BackfillUnavailableReason { get; }
}

/// <summary>Either the normalized JSON (event data or a rule) or field errors (field name → messages).</summary>
public sealed record DataValidationResult(string? DataJson, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => DataJson is not null;

    public static DataValidationResult Valid(string dataJson) => new(dataJson, new Dictionary<string, string[]>());

    public static DataValidationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
