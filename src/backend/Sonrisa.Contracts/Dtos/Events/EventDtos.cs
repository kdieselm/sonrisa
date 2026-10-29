using System.Text.Json;

namespace Sonrisa.Contracts.Dtos.Events;

/// <summary>
/// An event as the admin view lists it (spec §2.9). <c>Data</c> is the category's own data object; the frontend
/// category registry renders it.
/// </summary>
public sealed record AdminEventDto(
    long Id,
    string CategoryKey,
    string CategoryName,
    string SourceKey,
    string SourceName,
    string ExternalId,
    string Title,
    string? Summary,
    string? Url,
    DateTimeOffset PublishedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset UpdatedAt,
    bool IsSimulated,
    JsonElement Data);

/// <summary>Choices for the admin event filters and the simulate form.</summary>
public sealed record EventFiltersDto(IReadOnlyList<CategoryOptionDto> Categories, IReadOnlyList<SourceOptionDto> Sources);

public sealed record CategoryOptionDto(string Key, string Name);

public sealed record SourceOptionDto(string Key, string Name, string? CategoryKey);

/// <summary>
/// Admin "simulate event" form (spec §2.9). <c>Data</c> holds the category-specific fields, validated by the category.
/// </summary>
public sealed record SimulateEventRequest(string? CategoryKey, string? Title, string? Summary, string? Url,
    JsonElement Data);
