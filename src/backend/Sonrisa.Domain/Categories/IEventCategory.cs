using System.Text.Json;

namespace Sonrisa.Domain.Categories;

/// <summary>
/// An event category (spec §2.11, ADR-2): disaster, news, later market. Each category owns the shape of its event
/// data and validates it. Core code finds a category only through the category registry by <see cref="Key"/>, never
/// by a <c>switch</c> on the key. How to add one: <c>docs/how-to/add-a-category.md</c>.
/// </summary>
/// <remarks>S3a defines data, age limit and simulated data; S3b adds the rule, matching and message content.</remarks>
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
}

/// <summary>Either the normalized data JSON or field errors (field name → messages).</summary>
public sealed record DataValidationResult(string? DataJson, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => DataJson is not null;

    public static DataValidationResult Valid(string dataJson) => new(dataJson, new Dictionary<string, string[]>());

    public static DataValidationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
