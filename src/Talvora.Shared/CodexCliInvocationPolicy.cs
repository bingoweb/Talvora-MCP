using System.Text.RegularExpressions;

namespace Talvora.Shared;

/// <summary>
/// Pure Codex argument and response policy, testable without a live account.
/// </summary>
public static class CodexCliInvocationPolicy
{
    public const string PromptRedactionMarker = "[TASK_PROMPT_REDACTED]";

    public static string[] CreateArguments(
        string directory, string prompt, string model,
        string reasoningEffort, string sandbox) =>
    [
        "-a", "never",
        "exec",
        "--ephemeral",
        "--model", model,
        "--sandbox", sandbox,
        "--config", $"model_reasoning_effort=\"{reasoningEffort}\"",
        "--color", "never",
        "--cd", directory,
        // Do not interpret a task starting with --help as a CLI option.
        "--", prompt,
    ];

    public static string SanitizeFinalAnswer(string output, string prompt)
    {
        // Replace the exact original task before masking individual secrets.
        var safe = output;
        var normalized = prompt.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var variant in new[]
                 {
                     prompt,
                     normalized,
                     normalized.Replace("\n", "\r\n", StringComparison.Ordinal),
                 }.Distinct(StringComparer.Ordinal))
        {
            if (variant.Length > 0)
            {
                if (variant.Length <= 24 && !variant.Contains('\n'))
                {
                    // Short tasks such as "--help" may also be legitimate
                    // tokens inside an otherwise useful final answer.
                    // Conceal a complete echoed task line, not every
                    // occurrence of a common argument or word.
                    safe = Regex.Replace(
                        safe,
                        @"(?m)^(?<indent>[ \t]*)" + Regex.Escape(variant) +
                        @"(?=\r?$)",
                        match => match.Groups["indent"].Value +
                            PromptRedactionMarker,
                        RegexOptions.CultureInvariant);
                }
                else
                {
                    safe = safe.Replace(variant, PromptRedactionMarker,
                        StringComparison.Ordinal);
                }
            }
        }
        return FileLog.RedactSensitiveData(safe);
    }

    /// <summary>
    /// Codex diagnostic stderr includes raw prompts and command transcripts.
    /// Never return it via MCP: substring redaction cannot protect partial
    /// echoes or text corrupted by a console encoding mismatch.
    /// </summary>
    public static string SafeErrorSummary(int exitCode, bool timedOut) =>
        timedOut
            ? "Codex task timed out. Raw diagnostics were withheld to protect task contents."
            : exitCode == 0
                ? string.Empty
                : $"Codex exited with code {exitCode}. Raw diagnostics were withheld to protect task contents.";
}
