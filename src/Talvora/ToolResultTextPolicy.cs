using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Talvora;

internal static class ToolResultTextPolicy
{
    internal const string SummaryText = "Structured result: see structuredContent.";

    internal static CallToolResult Apply(CallToolResult result)
    {
        // Codex prefers structured data; leave empty/null payloads and errors untouched.
        if (result.IsError is true ||
            result.StructuredContent is not { } structured ||
            structured.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return result;
        }

        var updated = new List<ContentBlock>(result.Content.Count);
        var addedSummary = false;
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock)
            {
                if (!addedSummary)
                {
                    updated.Add(new TextContentBlock { Text = SummaryText });
                    addedSummary = true;
                }
                continue;
            }

            // Preserve images, audio and other non-text blocks as they were.
            updated.Add(block);
        }
        if (addedSummary)
        {
            result.Content = updated;
        }
        return result;
    }
}
