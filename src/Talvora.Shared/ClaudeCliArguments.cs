namespace Talvora.Shared;

/// <summary>Native CLI semantics; only the print/stream framing is owned by the bridge.</summary>
public static class ClaudeCliArguments
{
    public static string[] Create(string prompt, string? model = null, string? effort = null,
        string permissionMode = "auto", string? resumeSessionId = null,
        string[]? additionalDirectories = null, string[]? extraArguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (prompt.Length > 100_000) { throw new ArgumentOutOfRangeException(nameof(prompt)); }
        if (permissionMode is not ("default" or "acceptEdits" or "auto" or "plan" or "dontAsk" or "bypassPermissions"))
        {
            throw new ArgumentException("Use a native Claude Code permission mode.", nameof(permissionMode));
        }
        var arguments = new List<string>();
        if (extraArguments is not null)
        {
            foreach (var arg in extraArguments)
            {
                // Stream framing must remain parseable; generic run exposes the complete CLI instead.
                if (arg is "-p" or "--print" or "--" or "--output-format" or "--input-format" ||
                    arg.StartsWith("--output-format=", StringComparison.Ordinal) ||
                    arg.StartsWith("--input-format=", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Use talvora_claude_run to change CLI input/output framing.", nameof(extraArguments));
                }
                arguments.Add(arg);
            }
        }
        arguments.AddRange(["--print", "--output-format", "stream-json", "--verbose",
            "--permission-mode", permissionMode]);
        if (!string.IsNullOrWhiteSpace(model)) { arguments.AddRange(["--model", model]); }
        if (!string.IsNullOrWhiteSpace(effort)) { arguments.AddRange(["--effort", effort]); }
        if (!string.IsNullOrWhiteSpace(resumeSessionId)) { arguments.AddRange(["--resume", resumeSessionId]); }
        foreach (var path in additionalDirectories ?? [])
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) { throw new DirectoryNotFoundException(full); }
            arguments.AddRange(["--add-dir", full]);
        }
        arguments.AddRange(["--", prompt]);
        return arguments.ToArray();
    }
}
