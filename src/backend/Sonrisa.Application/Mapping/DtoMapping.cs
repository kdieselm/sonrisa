using System.Text.Json;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Mapping;

/// <summary>Entity → DTO mapping (hand-written, architecture §2).</summary>
public static class DtoMapping
{
    public static AlertDto ToDto(this Alert alert, CategoryRegistry categories) =>
        new(alert.Id,
            alert.Name,
            alert.CategoryKey,
            categories.Find(alert.CategoryKey)?.DisplayName ?? alert.CategoryKey,
            Json(alert.Rule),
            alert.Enabled,
            alert.ActiveSince,
            alert.CreatedAt,
            alert.Destinations.Select(d => d.DestinationId).Order().ToList());

    /// <summary>The config goes out only through the channel's mask (AC18).</summary>
    public static DestinationDto ToDto(this Destination destination, ChannelRegistry channels) =>
        new(destination.Id,
            destination.ChannelKey,
            channels.DisplayNameOf(destination.ChannelKey),
            destination.DisplayName,
            Json(channels.MaskedConfig(destination.ChannelKey, destination.Config)),
            destination.CreatedAt);

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
