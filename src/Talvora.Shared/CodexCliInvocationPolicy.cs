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
        string reasoningEffort, string sandbox,
        string? resumeSessionId = null,
        bool preserveSession = true,
        bool networkAccess = true,
        string approvalMode = "automatic",
        IReadOnlyList<string>? additionalWritableDirectories = null,
        bool allowNonGitWorkspace = true)
    {
        if (approvalMode is not ("automatic" or "never"))
        {
            throw new ArgumentException(
                "Approval mode must be automatic or never.", nameof(approvalMode));
        }
        if (resumeSessionId is not null && !preserveSession)
        {
            throw new ArgumentException(
                "A resumed session cannot be ephemeral.", nameof(preserveSession));
        }
        if (resumeSessionId is not null &&
            additionalWritableDirectories is { Count: > 0 })
        {
            throw new ArgumentException(
                "Codex exec resume does not accept --add-dir. Start a new session with additional directories.",
                nameof(additionalWritableDirectories));
        }

        var args = new List<string>();
        if (approvalMode == "automatic" && sandbox == "workspace-write")
        {
            // Current Codex supports automatic approval review for ordinary
            // workspace tasks. No human approval round trip is required.
            args.Add("--approve-for-me");
        }
        else
        {
            args.AddRange(["-a", "never"]);
        }
        args.Add("exec");
        if (resumeSessionId is not null)
        {
            args.Add("resume");
        }
        args.Add("--json");
        if (!preserveSession)
        {
            args.Add("--ephemeral");
        }
        args.AddRange(["--model", model]);
        if (resumeSessionId is null)
        {
            // Codex --approve-for-me conflicts with an explicit --sandbox
            // argument and itself selects workspace-write.
            if (approvalMode != "automatic" || sandbox != "workspace-write")
            {
                args.AddRange(["--sandbox", sandbox]);
            }
            args.AddRange(["--cd", directory]);
        }
        else
        {
            // Resume exposes -c but not the initial exec --sandbox/--cd flags.
            args.AddRange(["--config", $"sandbox_mode=\"{sandbox}\""]);
        }
        args.AddRange(["--config", $"model_reasoning_effort=\"{reasoningEffort}\""]);
        if (sandbox == "workspace-write")
        {
            args.AddRange([
                "--config",
                $"sandbox_workspace_write.network_access={(networkAccess ? "true" : "false")}",
            ]);
        }
        if (allowNonGitWorkspace)
        {
            args.Add("--skip-git-repo-check");
        }
        if (additionalWritableDirectories is not null)
        {
            foreach (var writableDirectory in additionalWritableDirectories)
            {
                args.AddRange(["--add-dir", writableDirectory]);
            }
        }
        if (resumeSessionId is not null)
        {
            args.Add(resumeSessionId);
        }
        // A task beginning with --help must not be parsed as a CLI flag.
        args.AddRange(["--", prompt]);
        return args.ToArray();
    }

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
