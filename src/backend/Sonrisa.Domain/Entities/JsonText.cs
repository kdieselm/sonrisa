using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sonrisa.Domain.Entities;

/// <summary>JSON helpers shared by the categories.</summary>
public static class JsonText
{
    /// <summary>Serializer settings for every category data object: camelCase, like the API.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Compares two JSON texts ignoring formatting and key order, as jsonb does when it stores a value.</summary>
    public static bool SemanticallyEqual(string a, string b) =>
        a == b || JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
}
