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
    }

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
