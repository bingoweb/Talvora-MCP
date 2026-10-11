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
    string? Error);

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
    bool StandardErrorTruncated);

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
    private const int MaximumPromptCharacters = 20_000;
    private const int MaximumOutputCharacters = 128 * 1024;
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
     Description("Discover the existing Codex Windows desktop CLI, its version and the logged-on Windows user's ChatGPT login. Never expose credentials. The default Talvora Codex model is GPT-6.1 Sol with medium reasoning.")]
    public static async Task<TalvoraCodexCliInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        InteractiveUserContext context;
        try
        {
            context = WindowsSessionLauncher.GetDefaultInteractiveUser();
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
        var version = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: 4096);
        var login = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, ["login", "status"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: 4096);
        return new TalvoraCodexCliInfoResponse(
            true,
            executable,
            FileLog.RedactSensitiveData(TextLines.FirstNonEmpty(
                version.StandardOutput, version.StandardError)),
            login.ExitCode == 0 && !login.TimedOut,
            context.User,
            DefaultModel,
            DefaultReasoningEffort,
            ProtectedSandbox,
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
     Description("Delegate a coding task to the existing Codex CLI in the signed-in Windows user's session, not LocalSystem. Default GPT-6.1 Sol, medium reasoning. Use read-only or workspace-write sandbox; noninteractive approvals fail closed. Not a means to bypass security restrictions.")]
    public static async Task<TalvoraCodexCliExecResponse> Exec(
        string workingDirectory,
        string prompt,
        string model = DefaultModel,
        string reasoningEffort = DefaultReasoningEffort,
        string sandbox = ProtectedSandbox,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
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
        if (sandbox is not ("read-only" or ProtectedSandbox))
        {
            throw new ArgumentException(
                "Codex can run only in read-only or workspace-write mode.",
                nameof(sandbox));
        }
        if (timeoutSeconds is < 1 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var context = WindowsSessionLauncher.GetDefaultInteractiveUser();
        var executable = ResolveCodex(context)
            ?? throw new FileNotFoundException(
                "Codex CLI was not found in the interactive Windows user's desktop app.");
        var cwd = GetWorkingDirectory(workingDirectory, context);
        // Explicit sandbox/approval flags override any less-restrictive user
        // settings. Separate argv elements avoid shell interpolation.
        string[] arguments =
        [
            "-a", "never",
            "exec",
            "--ephemeral",
            "--model", model,
            "--sandbox", sandbox,
            "--config", $"model_reasoning_effort=\"{reasoningEffort}\"",
            "--color", "never",
            "--cd", cwd,
            prompt,
        ];
        var result = await InteractiveUserProcessRunner.RunAsync(
            executable, cwd, arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: MaximumOutputCharacters);
        // Do not include raw prompts, command arguments or auth data in
        // returned metadata.
        return new TalvoraCodexCliExecResponse(
            result.ExitCode,
            FileLog.RedactSensitiveData(result.StandardOutput),
            FileLog.RedactSensitiveData(result.StandardError),
            result.TimedOut,
            result.ProcessId,
            executable,
            cwd,
            model,
            reasoningEffort,
            sandbox,
            result.ElapsedMilliseconds,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated);
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
                .Take(128)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
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
