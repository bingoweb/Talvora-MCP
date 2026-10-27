using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

// H1: a failed call ends with the exact usage of its tool, so the model can correct the next call itself.
internal static class ToolFailureGuidance
{
    internal static CallToolResult Apply(string? toolName, CallToolResult result)
    {
        if (toolName is null || TalvoraToolGuides.FailureHint(toolName) is not { } hint)
        {
            return result;
        }

        var failed = result.IsError is true ||
            result.StructuredContent is { } structured && StructuredResultFailure.IsFailure(structured);
        if (!failed)
        {
            return result;
        }

        result.Content = [.. result.Content, new TextContentBlock { Text = hint }];
        return result;
    }
}
