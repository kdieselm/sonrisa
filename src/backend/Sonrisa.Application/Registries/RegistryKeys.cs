namespace Sonrisa.Application.Registries;

internal static class RegistryKeys
{
    /// <summary>Builds the key → implementation map of a registry; an empty or duplicate key fails at startup.</summary>
    public static Dictionary<string, T> ToDictionary<T>(IEnumerable<T> items, Func<T, string> key, string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var k = key(item);
            if (string.IsNullOrWhiteSpace(k))
            {
                throw new InvalidOperationException($"A {kind} of type {item?.GetType().Name} has no key.");
            }

            if (!result.TryAdd(k, item))
            {
                throw new InvalidOperationException(
                    $"The {kind} key '{k}' is registered twice ({result[k]?.GetType().Name}, {item?.GetType().Name}).");
            }
        }

        return result;
    }
}
