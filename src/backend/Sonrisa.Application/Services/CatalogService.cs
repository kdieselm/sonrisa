using Sonrisa.Application.Mapping;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos.Catalog;

namespace Sonrisa.Application.Services;

/// <summary>
/// The categories and channels a user can choose from, read from the registries (spec §2.11), so a new category or
/// channel appears in the forms without core changes.
/// </summary>
public sealed class CatalogService(CategoryRegistry categories, ChannelRegistry channels,
    DestinationService destinations)
{
    public async Task<CatalogDto> GetAsync(string userId, CancellationToken ct)
    {
        var context = await destinations.ContextAsync(userId, ct);
        return new CatalogDto(
            categories.All
                .Select(c => new CatalogCategoryDto(c.Key, c.DisplayName, c.SupportsBackfill))
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList(),
            channels.All
                .Select(c => new CatalogChannelDto(c.Key, c.DisplayName, DtoMapping.Json(c.DefaultConfig(context))))
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList());
    }
}
