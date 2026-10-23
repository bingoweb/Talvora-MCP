using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraClaudeInfoResponse(
    bool Found, string? Executable, string? Version, bool Authenticated,
    string? InteractiveUser, string DefaultPermissionMode, bool SupportsPty,
    string? Error);

public sealed record TalvoraClaudeExecResponse(
    bool Success, int ExitCode, bool TimedOut, int ProcessId,
    string Executable, string WorkingDirectory, long ElapsedMilliseconds,
    string StandardOutput, string StandardError,
    bool StandardOutputTruncated, bool StandardErrorTruncated,
    string? SessionId, int ToolCalls, int FailedTools, int PermissionDenials,
    IReadOnlyList<string> RecentActivities, string? Error)
{
    public DesktopProgressEvidence? CodeEvidence { get; init; }
}

[McpServerToolType]
public static class ClaudeCliTools
{
    private const int OutputLimit = 256 * 1024;

    [McpServerTool(Name = "talvora_claude_info", ReadOnly = true, Idempotent = true,
        OpenWorld = true, UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraClaudeInfoResponse)),
     Description("Discover optional Claude Code native CLI, version and auth status in the logged-on Windows user's profile. Does not return credentials. Codex remains unchanged.")]
    public static async Task<TalvoraClaudeInfoResponse> Info(CancellationToken cancellationToken = default)
    {
        InteractiveUserContext context;
        try { context = WindowsSessionLauncher.GetActiveInteractiveUser(); }
        catch (InvalidOperationException)
        {
            return new(false, null, null, false, null, "auto", false,
                "No logged-on Windows user is available.");
        }
        var executable = Resolve(context);
        if (executable is null)
        {
            return new(false, null, null, false, context.User, "auto", false,
                "Install the official native Claude Code CLI in this Windows user's profile.");
        }
        var cwd = WorkingDirectory(null, context);
        var version = await Run(executable, cwd, ["--version"], null, 30, context, cancellationToken);
        var auth = await Run(executable, cwd, ["auth", "status"], null, 30, context, cancellationToken);
        return new(true, executable, FileLog.RedactSensitiveData(
            TextLines.FirstNonEmpty(version.StandardOutput, version.StandardError)),
            auth.ExitCode == 0 && !auth.TimedOut, context.User, "auto", false,
            auth.ExitCode == 0 && !auth.TimedOut ? null :
                "Run claude auth login in a terminal as the logged-on Windows user.");
    }

    [McpServerTool(Name = "talvora_claude_exec", Destructive = true, Idempotent = false,
        OpenWorld = true, UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraClaudeExecResponse)),
     Description("Edit code with optional Claude Code when explicitly requested (for example: bu kodu Claude Code ile düzenle). Uses the signed-in Windows user's auth, CLAUDE.md, settings, skills, hooks, plugins and MCP servers. Defaults to native auto permissions; bypassPermissions is explicit opt-in. No PTY: unresolved permission requests are reported as failure, never silently completed.")]
    public static Task<TalvoraClaudeExecResponse> Exec(
        string workingDirectory, string prompt, string? model = null, string? effort = null,
        string permissionMode = "auto", int timeoutSeconds = 1800,
        string? resumeSessionId = null, string[]? additionalDirectories = null,
        string[]? extraArguments = null, Dictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default) =>
        ExecuteCore(workingDirectory, prompt, model, effort, permissionMode, timeoutSeconds,
            resumeSessionId, additionalDirectories, extraArguments, environment, cancellationToken, null);

    internal static async Task<TalvoraClaudeExecResponse> ExecuteCore(
        string workingDirectory, string prompt, string? model, string? effort,
        string permissionMode, int timeoutSeconds, string? resumeSessionId,
        string[]? additionalDirectories, string[]? extraArguments,
        Dictionary<string, string?>? environment, CancellationToken token,
        Action<ClaudeCliProgress>? progress,
        Action<int, DateTimeOffset>? onStarted = null)
    {
        var context = WindowsSessionLauncher.GetActiveInteractiveUser();
        var executable = Resolve(context) ?? throw new FileNotFoundException(
            "Claude Code is not installed for the logged-on Windows user.");
        var cwd = WorkingDirectory(workingDirectory, context);
        ValidateTimeout(timeoutSeconds);
        var arguments = ClaudeCliArguments.Create(prompt, model, effort, permissionMode,
            resumeSessionId, additionalDirectories, extraArguments);
        ClaudeCliProgress? latest = null;
        var result = await InteractiveUserProcessRunner.RunAsync(executable, cwd, arguments,
            environment, timeoutSeconds, token, OutputLimit, context,
            onClaudeProgress: value =>
            {
                latest = value;
                progress?.Invoke(value);
            }, closeStandardInput: true,
            claudeWorkspaceRoot: Talvora.SourceEditing.SourceWorkspaceClassifier.Classify(cwd).WorkspaceRoot,
            onStarted: onStarted, standardInputText: prompt).ConfigureAwait(false);
        var success = result.ExitCode == 0 && !result.TimedOut &&
            latest is { HasResult: true, IsError: false, PermissionDenials: 0 };
        var error = result.TimedOut ? "Claude Code reached the time limit." :
            latest?.PermissionDenials > 0
                ? "Claude Code denied actions requiring permission. Review user settings, use an appropriate permissionMode, or run interactively."
                : latest?.Error ?? (!success
                    ? "Claude Code did not complete successfully. Check authentication, model and permission support; interactive commands require a user terminal."
                    : null);
        return new(success, result.ExitCode, result.TimedOut, result.ProcessId, executable, cwd,
            result.ElapsedMilliseconds, Safe(latest?.FinalAnswer ?? "", prompt, environment),
            Safe(result.StandardError, prompt, environment), result.StandardOutputTruncated,
            result.StandardErrorTruncated, latest?.SessionId ?? resumeSessionId,
            latest?.ToolCalls ?? 0, latest?.FailedTools ?? 0, latest?.PermissionDenials ?? 0,
            latest?.RecentActivities ?? [], Safe(error, prompt, environment))
        {
            CodeEvidence = latest?.CodeChange is { } change
                ? TalvoraDesktopProgressNotifier.BuildClaudeCodeEvidence(change, cwd) : null,
        };
    }

    [McpServerTool(Name = "talvora_claude_run", Destructive = true, Idempotent = false,
        OpenWorld = true, UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraClaudeExecResponse)),
     Description("Run any Claude Code CLI argument vector or subcommand as the logged-on Windows user, without a shell (auth status, mcp, plugin, agents, etc.). Preserves native configuration and capabilities. No PTY/stdin interaction: login/setup and other interactive commands must be completed in a user terminal if they require input; timeout is not success. Arguments/environment are never echoed in the response.")]
    public static async Task<TalvoraClaudeExecResponse> RunCli(
        string workingDirectory, string[] arguments, int timeoutSeconds = 120,
        Dictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ValidateTimeout(timeoutSeconds);
        var context = WindowsSessionLauncher.GetActiveInteractiveUser();
        var executable = Resolve(context) ?? throw new FileNotFoundException(
            "Claude Code is not installed for the logged-on Windows user.");
        var cwd = WorkingDirectory(workingDirectory, context);
        var result = await Run(executable, cwd, arguments, environment, timeoutSeconds, context, cancellationToken);
        var success = result.ExitCode == 0 && !result.TimedOut;
        // setup-token intentionally prints credentials: never return that output.
        var credentialOutput = arguments.Contains("setup-token", StringComparer.Ordinal);
        return new(success, result.ExitCode, result.TimedOut, result.ProcessId, executable, cwd,
            result.ElapsedMilliseconds,
            credentialOutput ? "[Credential output withheld; run this command in your own terminal.]" :
                Safe(result.StandardOutput, null, environment),
            Safe(result.StandardError, null, environment), result.StandardOutputTruncated,
            result.StandardErrorTruncated, null, 0, 0, 0, [],
            success ? null : "Claude Code command failed or timed out. Commands requiring terminal input must run in the logged-on user's terminal; this runner has no PTY.");
    }

    private static Task<ProcessExecutionResult> Run(string exe, string cwd, string[] args,
        Dictionary<string, string?>? environment, int timeout, InteractiveUserContext user,
        CancellationToken token) =>
        InteractiveUserProcessRunner.RunAsync(exe, cwd, args, environment, timeout,
            token, OutputLimit, user, closeStandardInput: true);

    internal static string Safe(string? value, string? prompt = null,
        Dictionary<string, string?>? environment = null)
    {
        var text = value ?? "";
        if (!string.IsNullOrEmpty(prompt)) { text = text.Replace(prompt, "[Task text withheld]", StringComparison.Ordinal); }
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (!string.IsNullOrEmpty(pair.Value) &&
                    Regex.IsMatch(pair.Key, "key|token|secret|password|credential|authorization", RegexOptions.IgnoreCase))
                {
                    text = text.Replace(pair.Value, "[REDACTED]", StringComparison.Ordinal);
                }
            }
        }
        return FileLog.RedactSensitiveData(text);
    }

    private static void ValidateTimeout(int value)
    {
        if (value is < 1 or > 43200) { throw new ArgumentOutOfRangeException(nameof(value)); }
    }

    private static string WorkingDirectory(string? path, InteractiveUserContext context)
    {
        var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path)
            ? context.UserProfile ?? throw new InvalidOperationException("User profile unavailable.") : path);
        if (!Directory.Exists(full)) { throw new DirectoryNotFoundException(full); }
        return full;
    }

    private static string? Resolve(InteractiveUserContext context)
    {
        // Resolve only from the interactive user's environment, never the service account's PATH.
        var candidates = new List<string>();
        if (context.UserProfile is { } profile)
        {
            candidates.Add(Path.Combine(profile, ".local", "bin", "claude.exe"));
        }
        if (context.Environment.TryGetValue("PATH", out var userPath))
        {
            candidates.AddRange(userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => Path.Combine(path.Trim('"'), "claude.exe")));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
