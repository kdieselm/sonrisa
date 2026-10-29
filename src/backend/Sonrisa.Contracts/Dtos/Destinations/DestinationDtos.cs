using System.Text.Json;

namespace Sonrisa.Contracts.Dtos.Destinations;

/// <summary>
/// A destination (spec §2.6). <c>Config</c> is always the channel's masked config: a Slack webhook URL shows only its
/// last 4 characters (AC18).
/// </summary>
public sealed record DestinationDto(
    long Id,
    string ChannelKey,
    string ChannelName,
    string DisplayName,
    JsonElement Config,
    DateTimeOffset CreatedAt);

/// <summary>Create a destination. A missing <c>Config</c> takes the channel's defaults.</summary>
public sealed record CreateDestinationRequest(string? ChannelKey, string? DisplayName, JsonElement? Config);

/// <summary>
/// Change a destination. A missing <c>Config</c> keeps the stored one, so a masked secret never has to be sent back.
/// </summary>
public sealed record UpdateDestinationRequest(string? DisplayName, JsonElement? Config);
