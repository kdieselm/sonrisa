using System.Text.Json;

namespace Sonrisa.Contracts.Dtos.Alerts;

/// <summary>
/// An alert (spec §2.2). <c>Rule</c> is the category's own rule object; the frontend category registry renders it.
/// </summary>
public sealed record AlertDto(
    long Id,
    string Name,
    string CategoryKey,
    string CategoryName,
    JsonElement Rule,
    bool Enabled,
    DateTimeOffset ActiveSince,
    DateTimeOffset CreatedAt,
    IReadOnlyList<long> DestinationIds);

/// <summary>Create an alert. <c>Rule</c> is validated by the category.</summary>
public sealed record CreateAlertRequest(string? Name, string? CategoryKey, JsonElement Rule,
    IReadOnlyList<long>? DestinationIds);

/// <summary>Replace name, rule and destinations; the category of an alert does not change.</summary>
public sealed record UpdateAlertRequest(string? Name, JsonElement Rule, IReadOnlyList<long>? DestinationIds);

/// <summary>
/// Up to 3 already-ingested events of the last 24 h that match the alert (spec §2.5, D11). Nothing is sent. When the
/// category does not support it (market), <c>Supported</c> is false and <c>Reason</c> says why.
/// </summary>
public sealed record RecentMatchesDto(bool Supported, string? Reason, IReadOnlyList<RecentMatchDto> Items);

public sealed record RecentMatchDto(
    long EventId,
    string Title,
    string? Url,
    string SourceName,
    DateTimeOffset PublishedAt,
    DateTimeOffset FirstSeenAt,
    bool IsSimulated,
    JsonElement Data);
