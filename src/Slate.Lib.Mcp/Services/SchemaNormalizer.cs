using System.Text.Json;
using System.Text.Json.Nodes;

namespace Slate.Lib.Mcp.Services;

/// <summary>
/// Rewrites boolean subschemas (<c>true</c>, <c>false</c>) as their object equivalents (<c>{}</c>, <c>{"not":{}}</c>).
/// JSON Schema allows booleans, and the SDK emits <c>true</c> for free-form JSON results, but strict clients such as the
/// official TypeScript SDK reject a whole <c>tools/list</c> when any subschema is not an object.
/// </summary>
public static class SchemaNormalizer
{
    private static readonly string[] Maps = ["properties", "patternProperties", "$defs", "definitions", "dependentSchemas"];
    private static readonly string[] Singles = ["items", "additionalProperties", "additionalItems", "unevaluatedProperties", "unevaluatedItems", "contains", "propertyNames", "not", "if", "then", "else"];
    private static readonly string[] Lists = ["anyOf", "oneOf", "allOf", "prefixItems", "items"];

    public static JsonElement Normalize(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText());
        return JsonSerializer.SerializeToElement(Normalize(node));
    }

    private static JsonNode? Normalize(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out var flag))
            return flag ? new JsonObject() : new JsonObject { ["not"] = new JsonObject() };
        if (node is not JsonObject schema) return node;
        foreach (var map in Maps)
            if (schema[map] is JsonObject children)
                foreach (var name in children.Select(pair => pair.Key).ToArray()) children[name] = Normalize(children[name]?.DeepClone());
        foreach (var single in Singles)
            if (schema[single] is JsonNode child and not JsonArray) schema[single] = Normalize(child.DeepClone());
        foreach (var list in Lists)
            if (schema[list] is JsonArray items)
                for (var index = 0; index < items.Count; index++) items[index] = Normalize(items[index]?.DeepClone());
        return schema;
    }
}
