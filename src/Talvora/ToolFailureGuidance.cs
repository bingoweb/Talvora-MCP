using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora.Shared;

namespace Talvora;

// H1: a failed call ends with the exact usage of its tool, so the model can correct the next call itself.
internal static class ToolFailureGuidance
{
    internal static CallToolResult Apply(string? toolName, CallToolResult result)
    {
        var failed = result.IsError is true ||
            result.StructuredContent is { } structured && StructuredResultFailure.IsFailure(structured);
        if (!failed)
        {
            return result;
        }

        // H3: a structured failure is an error result too, so no client mistakes it for success.
        result.IsError = true;
        if (toolName is not null && !RanWithOwnExitCode(result) && TalvoraToolGuides.FailureHint(toolName) is { } hint)
        {
            result.Content = [.. result.Content, new TextContentBlock { Text = hint }];
        }

        return result;
    }

    // H3: a thrown exception becomes a readable error result; the message passes the canonical redaction first.
    internal static CallToolResult FromException(string? toolName, Exception exception)
    {
        var reason = exception is AggregateException { InnerException: { } inner } ? inner : exception;
        return new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock
                {
                    Text = $"{toolName ?? "The tool"} failed ({reason.GetType().Name}): {FileLog.RedactSensitiveData(reason.Message)}",
                },
            ],
        };
    }

    // H4: the command ran and returned its own non-zero exit code, so the call was right and its usage would mislead.
    private static bool RanWithOwnExitCode(CallToolResult result) =>
        result.StructuredContent is { ValueKind: JsonValueKind.Object } structured &&
        StructuredResultFailure.Describe(structured) != StructuredResultFailure.TimedOut &&
        structured.TryGetProperty("exitCode", out var exitCode) &&
        exitCode.ValueKind == JsonValueKind.Number &&
        exitCode.TryGetInt32(out var code) &&
        code != 0;
}
