using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora;

internal static class McpToolSurfaceWirePolicy
{
    public static TalvoraMcpToolSurface Apply(
        McpServerOptions options,
        string? requestPath)
    {
        var surface =
            TalvoraMcpToolSurfacePolicy.ResolvePath(
                requestPath);
        if (surface == TalvoraMcpToolSurface.Full ||
            options.ToolCollection is null)
        {
            return surface;
        }

        var toolCollection =
            options.ToolCollection;
        var namesToRemove =
            toolCollection.PrimitiveNames
                .Where(name =>
                    !TalvoraMcpToolSurfacePolicy.Includes(
                        surface,
                        name))
                .ToArray();

        foreach (var name in namesToRemove)
        {
            if (toolCollection.TryGetPrimitive(
                    name,
                    out var tool))
            {
                toolCollection.Remove(tool);
            }
        }

        return surface;
    }
}
