using System.Text.Json;

namespace Sonrisa.Contracts.Dtos.Catalog;

/// <summary>
/// What a user can choose from (spec §2.11): the registered categories for alerts and channels for destinations.
/// The frontend renders each through its own registry entry.
/// </summary>
public sealed record CatalogDto(IReadOnlyList<CatalogCategoryDto> Categories, IReadOnlyList<CatalogChannelDto> Channels);

public sealed record CatalogCategoryDto(string Key, string Name, bool SupportsBackfill);

/// <summary><c>DefaultConfig</c> is the config the create form starts with (already safe to show).</summary>
public sealed record CatalogChannelDto(string Key, string Name, JsonElement DefaultConfig);
