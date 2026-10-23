using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraCodexCliInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    bool Authenticated,
    string? InteractiveUser,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    string? Error,
    string DefaultApprovalMode = "automatic",
    bool DefaultNetworkAccess = true,
    bool SupportsSessionResume = true);

public sealed record TalvoraCodexCliExecResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    long ElapsedMilliseconds,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? SessionId = null,
    int CompletedCommands = 0,
    int FailedCommands = 0,
    int McpToolCalls = 0,
    IReadOnlyList<string>? RecentActivities = null,
    string? ErrorDetail = null);

/// <summary>
/// Uses the Codex CLI bundled with the signed-in user's desktop app.
/// No Codex copy, API key, or LocalSystem Codex profile is needed.
/// </summary>
[McpServerToolType]
public static class CodexCliTools
{
    private const string DefaultModel = "gpt-6.1-sol";
    private const string DefaultReasoningEffort = "medium";
    private const string ProtectedSandbox = "workspace-write";
    private const int MaximumPromptCharacters = 100_000;
    private const int MaximumOutputCharacters = 256 * 1024;
    private static readonly Regex ModelNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [McpServerTool(
        Name = "talvora_codex_info",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexCliInfoResponse)),
     Description("Discover desktop Codex CLI, signed-in user, model and preferred developer profile. Balanced is writable workspace; trusted gives Codex full normal-user development access with functional NuGet/network support.")]
    public static async Task<TalvoraCodexCliInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        InteractiveUserContext context;
        try
        {
            context = WindowsSessionLauncher.GetActiveInteractiveUser();
        }
        catch (InvalidOperationException)
        {
            return new TalvoraCodexCliInfoResponse(
                false, null, null, false, null,
                DefaultModel, DefaultReasoningEffort, ProtectedSandbox,
                "No logged-on interactive Windows user session is available.");
        }

        var executable = ResolveCodex(context);
        if (executable is null)
        {
            return new TalvoraCodexCliInfoResponse(
                false, null, null, false, context.User,
                DefaultModel, DefaultReasoningEffort, ProtectedSandbox,
                "Codex CLI was not found in the desktop application.");
        }

        var cwd = GetWorkingDirectory(null, context);
        var userProfile = CodexPreferencesStore.Read(
            context.LocalAppData ?? throw new InvalidOperationException(
                "Interactive LOCALAPPDATA is unavailable."));
        var version = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: 4096,
            interactiveUser: context);
        var login = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, ["login", "status"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: 4096,
            interactiveUser: context);
        return new TalvoraCodexCliInfoResponse(
            true,
            executable,
            FileLog.RedactSensitiveData(TextLines.FirstNonEmpty(
                version.StandardOutput, version.StandardError)),
            login.ExitCode == 0 && !login.TimedOut,
            context.User,
            DefaultModel,
            DefaultReasoningEffort,
            userProfile == CodexPreferencesStore.Trusted
                ? "danger-full-access"
                : ProtectedSandbox,
            login.ExitCode == 0 && !login.TimedOut
                ? null
                : "Codex is installed but the Windows user's ChatGPT login is unavailable.");
    }

    [McpServerTool(
        Name = "talvora_codex_exec",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCodexCliExecResponse)),
     Description("Build software with desktop Codex and the logged-on user's development profile. Balanced: writable project/automatic review. Trusted: full normal-user development access and NuGet/network support. Supports saved session resume, extra roots and structured work summaries.")]
    public static Task<TalvoraCodexCliExecResponse> Exec(
        string workingDirectory,
        string prompt,
        string model = DefaultModel,
        string reasoningEffort = DefaultReasoningEffort,
        string sandbox = "auto",
        int timeoutSeconds = 1800,
        string? resumeSessionId = null,
        bool preserveSession = true,
        bool networkAccess = true,
        string approvalMode = "automatic",
        string[]? additionalWritableDirectories = null,
        bool allowNonGitWorkspace = true,
        bool allowFullAccess = false,
        string windowsSandboxMode = "default",
        CancellationToken cancellationToken = default)
        => ExecuteCore(
            workingDirectory, prompt, model, reasoningEffort, sandbox,
            timeoutSeconds, resumeSessionId, preserveSession, networkAccess,
            approvalMode, additionalWritableDirectories, allowNonGitWorkspace,
            allowFullAccess, windowsSandboxMode, cancellationToken, null);

    internal static async Task<TalvoraCodexCliExecResponse> ExecuteCore(
        string workingDirectory,
        string prompt,
        string model,
        string reasoningEffort,
        string sandbox,
        int timeoutSeconds,
        string? resumeSessionId,
        bool preserveSession,
        bool networkAccess,
        string approvalMode,
        string[]? additionalWritableDirectories,
        bool allowNonGitWorkspace,
        bool allowFullAccess,
        string windowsSandboxMode,
        CancellationToken cancellationToken,
        Action<CodexCliEventResult>? onProgress,
        Action<int, DateTimeOffset>? onStarted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (prompt.Length > MaximumPromptCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(prompt));
        }
        if (!ModelNamePattern.IsMatch(model))
        {
            throw new ArgumentException(
                "Model must be a simple Codex model identifier.", nameof(model));
        }
        if (reasoningEffort is not ("low" or "medium" or "high" or "xhigh" or "max"))
        {
            throw new ArgumentException(
                "Reasoning effort must be low, medium, high, xhigh or max.",
                nameof(reasoningEffort));
        }
        if (sandbox is not ("auto" or "read-only" or ProtectedSandbox or "danger-full-access"))
        {
            throw new ArgumentException(
                "Codex sandbox must be auto, read-only, workspace-write or danger-full-access.",
                nameof(sandbox));
        }
        if (approvalMode is not ("automatic" or "never"))
        {
            throw new ArgumentException(
                "Approval mode must be automatic or never.", nameof(approvalMode));
        }
        if (resumeSessionId is not null &&
            (!Guid.TryParse(resumeSessionId, out _) || !preserveSession))
        {
            throw new ArgumentException(
                "Resume requires a saved Codex session UUID and preserveSession=true.",
                nameof(resumeSessionId));
        }
        if (additionalWritableDirectories is { Length: > 12 })
        {
            throw new ArgumentOutOfRangeException(nameof(additionalWritableDirectories),
                "Specify at most twelve additional writable directories.");
        }
        // Background developer tasks may run for several hours.
        if (timeoutSeconds is < 1 or > 43200)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var context = WindowsSessionLauncher.GetActiveInteractiveUser();
        var configuredProfile = CodexPreferencesStore.Read(
            context.LocalAppData ?? throw new InvalidOperationException(
                "Interactive LOCALAPPDATA is unavailable."));
        var effectiveSandbox = sandbox == "auto"
            ? configuredProfile == CodexPreferencesStore.Trusted
                ? "danger-full-access"
                : ProtectedSandbox
            : sandbox;
        if (effectiveSandbox == "danger-full-access" &&
            !allowFullAccess &&
            !(sandbox == "auto" &&
              configuredProfile == CodexPreferencesStore.Trusted))
        {
            throw new ArgumentException(
                "Select the trusted developer profile or set allowFullAccess=true for this task.",
                nameof(allowFullAccess));
        }
        var executable = ResolveCodex(context)
            ?? throw new FileNotFoundException(
                "Codex CLI was not found in the interactive Windows user's desktop app.");
        var cwd = GetWorkingDirectory(workingDirectory, context);
        var writableDirectories = new List<string>();
        if (additionalWritableDirectories is not null)
        {
            foreach (var directory in additionalWritableDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory) ||
                    !Path.IsPathFullyQualified(directory))
                {
                    throw new ArgumentException(
                        "Additional writable directories must be absolute paths.",
                        nameof(additionalWritableDirectories));
                }
                writableDirectories.Add(GetWorkingDirectory(directory, context));
            }
        }
        // The positional dash is an stdin marker; task text is never in argv.
        var arguments = CodexCliInvocationPolicy.CreateArguments(
            cwd, prompt, model, reasoningEffort, effectiveSandbox,
            resumeSessionId, preserveSession, networkAccess, approvalMode,
            writableDirectories, allowNonGitWorkspace, windowsSandboxMode);
        var result = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: MaximumOutputCharacters,
            interactiveUser: context,
            discardStandardError: true,
            onCodexProgress: onProgress,
            onStarted: onStarted,
            closeStandardInput: true,
            standardInputText: prompt);
        // Extract final answer and task activity from Codex's JSONL output.
        var events = CodexCliEventSummary.Parse(result.StandardOutput);
        var finalAnswer = events.HasEvents
            ? events.FinalAnswer
            : result.StandardOutput;
        var safeError = events.LastError is null
            ? null
            : CodexCliInvocationPolicy.SanitizeFinalAnswer(
                events.LastError, prompt);
        return new TalvoraCodexCliExecResponse(
            result.ExitCode,
            CodexCliInvocationPolicy.SanitizeFinalAnswer(finalAnswer, prompt),
            CodexCliInvocationPolicy.SafeErrorSummary(result.ExitCode, result.TimedOut),
            result.TimedOut,
            result.ProcessId,
            executable,
            cwd,
            model,
            reasoningEffort,
            effectiveSandbox,
            result.ElapsedMilliseconds,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            preserveSession
                ? events.SessionId ?? resumeSessionId
                : null,
            events.CompletedCommands,
            events.FailedCommands,
            events.McpToolCalls,
            events.RecentActivities
                .Select(activity => CodexCliInvocationPolicy.SanitizeFinalAnswer(
                    activity, prompt))
                .ToArray(),
            safeError);
    }

    private static string GetWorkingDirectory(
        string? workingDirectory,
        InteractiveUserContext context)
    {
        var full = string.IsNullOrWhiteSpace(workingDirectory)
            ? context.UserProfile
                ?? throw new InvalidOperationException(
                    "Interactive user profile is unavailable.")
            : Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(
                $"Codex workspace was not found: {full}");
        }
        return full;
    }

    private static string? ResolveCodex(InteractiveUserContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.LocalAppData))
        {
            var binRoot = Path.Combine(
                context.LocalAppData, "OpenAI", "Codex", "bin");
            var found = EnumerateCodexExecutables(binRoot, false).FirstOrDefault();
            if (found is not null) { return found; }
        }
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        return string.IsNullOrWhiteSpace(programFiles)
            ? null
            : EnumerateCodexExecutables(
                Path.Combine(programFiles, "WindowsApps"), true).FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateCodexExecutables(
        string root, bool isAppPackage)
    {
        if (!Directory.Exists(root)) { yield break; }
        string[] directories;
        try
        {
            directories = Directory.EnumerateDirectories(
                    root, isAppPackage ? "OpenAI.Codex_*" : "*",
                    SearchOption.TopDirectoryOnly)
                .Where(directory => isAppPackage ||
                    (new DirectoryInfo(directory).Name is { Length: 16 } name &&
                     name.All(Uri.IsHexDigit)))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Take(128)
                .ToArray();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            yield break;
        }
        foreach (var directory in directories)
        {
            if (!isAppPackage &&
                !(new DirectoryInfo(directory).Name is { Length: 16 } name &&
                  name.All(Uri.IsHexDigit)))
            {
                continue;
            }
            var executable = isAppPackage
                ? Path.Combine(directory, "app", "resources", "codex.exe")
                : Path.Combine(directory, "codex.exe");
            if (File.Exists(executable)) { yield return executable; }
        }
    }
}
