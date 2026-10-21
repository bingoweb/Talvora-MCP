using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraUserRunResponse(
    bool Success,
    int ExitCode,
    bool TimedOut,
    string User,
    string WorkingDirectory,
    long ElapsedMilliseconds,
    string StandardOutput,
    string StandardError,
    long StandardOutputTotalCharacters,
    long StandardErrorTotalCharacters);

[McpServerToolType]
public static class UserRunTools
{
    [McpServerTool(
        Name = "talvora_user_run",
        Title = "Kullanıcı olarak çalıştır",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraUserRunResponse)),
     Description("Run an executable as the signed-in Windows user (not LocalSystem) and return exit code and bounded output. Use for builds, tests and scripts inside user workspaces.")]
    public static async Task<TalvoraUserRunResponse> Run(
        string executable,
        string[] arguments,
        string workingDirectory,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 600,
        int maxOutputCharacters = ToolOutputCompactor.DefaultMaximumOutputCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeoutSeconds is < 1 or > 43200)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var cwd = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException(cwd);
        }

        var user = WindowsSessionLauncher.GetActiveInteractiveUser();
        var result = await InteractiveUserProcessRunner.RunAsync(
            executable,
            cwd,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken,
            ProcessRunner.DefaultMaximumCapturedCharacters,
            user,
            closeStandardInput: true).ConfigureAwait(false);

        return new TalvoraUserRunResponse(
            result.ExitCode == 0 && !result.TimedOut,
            result.ExitCode,
            result.TimedOut,
            user.User,
            cwd,
            result.ElapsedMilliseconds,
            ToolOutputCompactor.Compact(
                FileLog.RedactSensitiveData(result.StandardOutput), maxOutputCharacters),
            ToolOutputCompactor.Compact(
                FileLog.RedactSensitiveData(result.StandardError), maxOutputCharacters),
            result.StandardOutputTotalCharacters,
            result.StandardErrorTotalCharacters);
    }
}
