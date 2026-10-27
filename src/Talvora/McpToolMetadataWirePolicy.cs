using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

internal static class McpToolMetadataWirePolicy
{
    public static void Apply(ListToolsResult result, TalvoraMcpToolSurface surface)
    {
        foreach (var tool in result.Tools)
        {
            var declared = tool.Annotations;
            var title = TalvoraToolTitles.Get(tool.Name);
            var readOnly =
                declared?.ReadOnlyHint == true;

            tool.Title = title ?? tool.Title;
            tool.Description =
                TalvoraMcpToolMetadataPolicy.NormalizeDescription(
                    tool.Name,
                    tool.Description);

            tool.Annotations = new ToolAnnotations
            {
                Title = title ?? declared?.Title,
                ReadOnlyHint = readOnly,
                DestructiveHint =
                    TalvoraMcpToolMetadataPolicy.ResolveDestructive(
                        tool.Name,
                        readOnly,
                        declared?.DestructiveHint),
                IdempotentHint =
                    TalvoraMcpToolMetadataPolicy.ResolveIdempotent(
                        readOnly,
                        declared?.IdempotentHint),
                OpenWorldHint =
                    TalvoraMcpToolMetadataPolicy.IsOpenWorld(
                        tool.Name),
            };
        }

        // Q7: output schemas are 155 KB of the 304 KB /mcp/dev list. Focused surfaces omit them (MCP makes
        // outputSchema optional; results still carry structuredContent); the full /mcp surface keeps them.
        if (surface != TalvoraMcpToolSurface.Full)
        {
            // Copy, never mutate: in stateless mode the Tool objects are shared by every surface's request.
            result.Tools = result.Tools.Select(WithoutOutputSchema).ToList();
        }
        else
        {
            // AP1: the SDK omits null members from structuredContent, so a member whose schema allows null must
            // not be required; strict clients otherwise reject the whole result, including its error message.
            result.Tools = result.Tools.Select(WithOptionalNullableOutput).ToList();
        }
    }

    private static readonly ConcurrentDictionary<string, JsonElement> RelaxedOutputSchemas =
        new(StringComparer.Ordinal);

    private static Tool WithOptionalNullableOutput(Tool tool)
    {
        if (tool.OutputSchema is not JsonElement schema)
        {
            return tool;
        }

        var relaxed = RelaxedOutputSchemas.GetOrAdd(tool.Name, _ =>
        {
            var node = JsonNode.Parse(schema.GetRawText());
            RelaxNullableRequired(node);
            return JsonSerializer.SerializeToElement(node);
        });
        return new Tool
        {
            Name = tool.Name,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = tool.InputSchema,
            OutputSchema = relaxed,
            Annotations = tool.Annotations,
            Icons = tool.Icons,
            Meta = tool.Meta,
        };
    }

    internal static void RelaxNullableRequired(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties && obj["required"] is JsonArray required)
                {
                    for (var index = required.Count - 1; index >= 0; index--)
                    {
                        if (required[index] is JsonValue name &&
                            name.TryGetValue<string>(out var propertyName) &&
                            properties[propertyName] is JsonObject property &&
                            AllowsNull(property))
                        {
                            required.RemoveAt(index);
                        }
                    }
                }

                foreach (var child in obj.Select(pair => pair.Value).ToList())
                {
                    RelaxNullableRequired(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array.ToList())
                {
                    RelaxNullableRequired(child);
                }

                break;
        }
    }

    private static bool AllowsNull(JsonObject property) => property["type"] switch
    {
        JsonValue value => value.TryGetValue<string>(out var type) && type == "null",
        JsonArray types => types.Any(type =>
            type is JsonValue value && value.TryGetValue<string>(out var name) && name == "null"),
        _ => false,
    };

    // Copies every settable property of ModelContextProtocol.Protocol.Tool (SDK 2.2.0) except OutputSchema; the
    // payload smoke fails if the SDK adds a property, so a new field can never be dropped silently.
    private static Tool WithoutOutputSchema(Tool tool) => new()
    {
        Name = tool.Name,
        Title = tool.Title,
        Description = tool.Description,
        InputSchema = tool.InputSchema,
        Annotations = tool.Annotations,
        Icons = tool.Icons,
        Meta = tool.Meta,
    };
}
