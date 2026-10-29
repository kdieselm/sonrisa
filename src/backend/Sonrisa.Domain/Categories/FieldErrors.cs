using System.Text.Json;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Domain.Categories;

/// <summary>Collects field errors while a category validates its input.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    /// <summary>
    /// Reads <paramref name="data"/> as <typeparamref name="T"/>; a JSON object is required. On a wrong shape or type
    /// it records one error on <c>data</c> and returns null.
    /// </summary>
    public T? Read<T>(JsonElement data) where T : class
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            Add("data", "An object with the category fields is required.");
            return null;
        }

        try
        {
            return data.Deserialize<T>(JsonText.Options);
        }
        catch (JsonException)
        {
            Add("data", "One or more fields have the wrong type.");
            return null;
        }
    }
}
