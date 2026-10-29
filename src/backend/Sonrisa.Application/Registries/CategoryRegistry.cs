using Sonrisa.Domain.Categories;

namespace Sonrisa.Application.Registries;

/// <summary>
/// The only place that maps a category key to its implementation (ADR-2, AC11). Every category registered in DI is
/// here; a duplicate key fails at startup.
/// </summary>
public sealed class CategoryRegistry
{
    private readonly Dictionary<string, IEventCategory> _byKey;

    public CategoryRegistry(IEnumerable<IEventCategory> categories)
    {
        _byKey = RegistryKeys.ToDictionary(categories, c => c.Key, "category");
    }

    public IReadOnlyCollection<IEventCategory> All => _byKey.Values;

    public IEventCategory? Find(string key) => _byKey.GetValueOrDefault(key);
}
