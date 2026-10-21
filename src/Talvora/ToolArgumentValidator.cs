using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Talvora;

internal static class ToolArgumentValidator
{
    private const int MaximumListedProperties = 12;

    internal static CallToolResult? TryCreateError(
        RequestContext<CallToolRequestParams> request)
    {
        var name = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(name) ||
            request.Server?.ServerOptions?.ToolCollection is not { } tools ||
            !tools.TryGetPrimitive(name, out var tool))
        {
            return null; // Unknown tool: keep the SDK's own answer.
        }

        var message = Validate(tool.ProtocolTool.InputSchema, request.Params!.Arguments);
        return message is null
            ? null
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = message }],
            };
    }

    internal static string? Validate(
        JsonElement schema,
        IDictionary<string, JsonElement>? arguments)
    {
        var args = arguments ?? new Dictionary<string, JsonElement>();
        var properties = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var p) &&
            p.ValueKind == JsonValueKind.Object
                ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var required = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("required", out var r) &&
            r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToArray()
                : [];
        var allowsAdditional = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("additionalProperties", out var ap) &&
            ap.ValueKind is not JsonValueKind.False;

        var missing = required.Where(n => !args.ContainsKey(n)).ToArray();
        var unknown = allowsAdditional
            ? []
            : args.Keys.Where(k => !properties.ContainsKey(k)).ToArray();
        var wrongType = args
            .Where(a => properties.TryGetValue(a.Key, out var ps) && !TypeMatches(ps, a.Value))
            .Select(a => $"'{a.Key}' (expected {TypeLabel(properties[a.Key])})")
            .ToArray();

        if (missing.Length == 0 && unknown.Length == 0 && wrongType.Length == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (missing.Length > 0) parts.Add("missing " + string.Join(", ", missing.Select(m => $"'{m}'")));
        if (unknown.Length > 0) parts.Add("unknown " + string.Join(", ", unknown.Select(u => $"'{u}'")));
        if (wrongType.Length > 0) parts.Add("wrong type " + string.Join(", ", wrongType));

        var expected = properties.Keys
            .OrderBy(k => Array.IndexOf(required, k) is var i && i >= 0 ? i : int.MaxValue)
            .Take(MaximumListedProperties)
            .Select(k => $"{k} ({TypeLabel(properties[k])}{(required.Contains(k) ? ", required" : string.Empty)})");
        var more = properties.Count > MaximumListedProperties ? ", …" : string.Empty;

        return $"InvalidArguments: {string.Join("; ", parts)}. Expected: {string.Join(", ", expected)}{more}";
    }

    private static IReadOnlyList<string> Types(JsonElement propertySchema) =>
        !propertySchema.TryGetProperty("type", out var type)
            ? []
            : type.ValueKind switch
            {
                JsonValueKind.String => [type.GetString()!],
                JsonValueKind.Array => type.EnumerateArray()
                    .Where(t => t.ValueKind == JsonValueKind.String)
                    .Select(t => t.GetString()!)
                    .ToArray(),
                _ => [],
            };

    private static string TypeLabel(JsonElement propertySchema)
    {
        var types = Types(propertySchema).Where(t => t != "null").ToArray();
        return types.Length == 0 ? "any" : string.Join("|", types);
    }

    private static bool TypeMatches(JsonElement propertySchema, JsonElement value)
    {
        var types = Types(propertySchema);
        if (types.Count == 0)
        {
            return true;
        }

        return types.Any(t => t switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        });
    }
}
