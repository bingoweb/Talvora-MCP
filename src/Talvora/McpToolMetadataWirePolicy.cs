using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

internal static class McpToolMetadataWirePolicy
{
    public static void Apply(ListToolsResult result)
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
    }
}
