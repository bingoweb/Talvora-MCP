using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;

internal static partial class SmokeScenarios
{
    internal static void RunToolResultTextPolicy()
    {
        var structured = JsonSerializer.SerializeToElement(new
        {
            status = "ready",
            total = 19,
            nested = new { valid = true },
        });
        var originalJson = structured.GetRawText();
        var input = new CallToolResult
        {
            StructuredContent = structured,
            IsError = false,
            Content =
            [
                new TextContentBlock { Text = originalJson },
            ],
        };
        var output = ToolResultTextPolicy.Apply(input);
        if (!ReferenceEquals(output, input) ||
            output.Content.Count != 1 ||
            output.Content[0] is not TextContentBlock text ||
            text.Text != "Structured result: see structuredContent." ||
            output.StructuredContent?.GetRawText() != originalJson ||
            output.IsError != false)
        {
            throw new InvalidOperationException(
                "Structured tool responses must retain JSON and replace duplicate text.");
        }

        var noStructure = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "original only-text output" }],
        };
        var unchanged = ToolResultTextPolicy.Apply(noStructure);
        if (!ReferenceEquals(unchanged, noStructure) ||
            unchanged.Content[0] is not TextContentBlock plain ||
            plain.Text != "original only-text output")
        {
            throw new InvalidOperationException(
                "Unstructured tool results must remain unchanged.");
        }

        var error = new CallToolResult
        {
            IsError = true,
            StructuredContent = structured,
            Content = [new TextContentBlock { Text = "important original error" }],
        };
        var errorOutput = ToolResultTextPolicy.Apply(error);
        if (!ReferenceEquals(errorOutput, error) ||
            errorOutput.Content[0] is not TextContentBlock failure ||
            failure.Text != "important original error" ||
            errorOutput.StructuredContent?.GetRawText() != originalJson)
        {
            throw new InvalidOperationException(
                "Error tool result text and structured data must survive untouched.");
        }

        var nullValue = JsonSerializer.SerializeToElement<object?>(null);
        var nullResult = new CallToolResult
        {
            StructuredContent = nullValue,
            Content = [new TextContentBlock { Text = "keep text for null JSON" }],
        };
        ToolResultTextPolicy.Apply(nullResult);
        if (nullResult.Content[0] is not TextContentBlock nullText ||
            nullText.Text != "keep text for null JSON")
        {
            throw new InvalidOperationException(
                "Null JSON must preserve original text.");
        }

        foreach (var failureJson in new object[]
        {
            new { success = false, error = "rejected" },
            new { timedOut = true },
            new { exitCode = 1, standardError = "boom" },
        })
        {
            var failureStructured = JsonSerializer.SerializeToElement(failureJson);
            var failureText = failureStructured.GetRawText();
            var failureInput = new CallToolResult
            {
                StructuredContent = failureStructured,
                Content = [new TextContentBlock { Text = failureText }],
            };
            var failureOutput = ToolResultTextPolicy.Apply(failureInput);
            if (failureOutput.Content.Count != 1 ||
                failureOutput.Content[0] is not TextContentBlock kept ||
                kept.Text != failureText)
            {
                throw new InvalidOperationException(
                    "Structured failures must keep their original text: " + failureText);
            }
        }
    }

    internal static async Task RunLiveToolResultTextPolicyAsync(
        string endpoint, string repositoryPath)
    {
        await using var client = await CreateClientAsync(endpoint);
        var result = await client.CallToolAsync(
            "talvora_git_status",
            new Dictionary<string, object?>
            {
                ["repositoryPath"] = repositoryPath,
                ["includeUntracked"] = false,
            });
        if (result.IsError is true ||
            result.StructuredContent is not { } structured ||
            !structured.TryGetProperty("root", out _) ||
            result.Content.Count != 1 ||
            result.Content[0] is not TextContentBlock text ||
            text.Text != ToolResultTextPolicy.SummaryText)
        {
            throw new InvalidOperationException(
                "Installed Dev MCP tool result lost structured data or retained duplicate text.");
        }
    }
}
