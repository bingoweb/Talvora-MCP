using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public sealed record TalvoraPowerShellResult(
    string Engine,
    string Executable,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    long StandardOutputTotalCharacters,
    long StandardErrorTotalCharacters,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    bool OutputDrainTimedOut);

[McpServerToolType]
public static class PowerShellTools
{
    [McpServerTool(
        Name = "talvora_run_powershell",
        Destructive = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPowerShellResult)),
     Description("Run an arbitrary multiline PowerShell script as the Talvora LocalSystem service. " + SourceEditRoutingContract.EscapeHatchRouting + " The script is passed with UTF-16LE Base64 -EncodedCommand to avoid quoting loss. No command or script deny-list is applied.")]
    public static async Task<TalvoraPowerShellResult> RunPowerShell(
        string script,
        string engine = "auto",
        string? workingDirectory = null,
        int timeoutSeconds = 300,
        int maxCapturedCharactersPerStream =
            ProcessRunner.DefaultMaximumCapturedCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var resolved = ResolveEngine(engine);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var arguments = new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-EncodedCommand",
            encoded,
        };

        var result = await ProcessRunner.RunAsync(
            resolved.Executable,
            workingDirectory,
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream:
                maxCapturedCharactersPerStream);

        return new TalvoraPowerShellResult(
            resolved.Name,
            resolved.Executable,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.StandardOutputTotalCharacters,
            result.StandardErrorTotalCharacters,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            result.OutputDrainTimedOut);
    }
    private static (string Name, string Executable) ResolveEngine(string engine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engine);
        var normalized = engine.Trim().ToLowerInvariant();

        if (normalized is "auto")
        {
            var pwsh = FindOnPath("pwsh.exe") ?? FindCommonPwsh();
            return pwsh is not null
                ? ("pwsh", pwsh)
                : ("windows-powershell", GetWindowsPowerShell());
        }

        if (normalized is "pwsh" or "powershell7" or "powershell-core")
        {
            var pwsh = FindOnPath("pwsh.exe") ?? FindCommonPwsh();
            if (pwsh is null)
            {
                throw new FileNotFoundException("PowerShell 7 (pwsh.exe) was not found.");
            }

            return ("pwsh", pwsh);
        }

        if (normalized is "powershell" or "windows-powershell" or "windowspowershell")
        {
            return ("windows-powershell", GetWindowsPowerShell());
        }

        throw new ArgumentException(
            $"Unsupported PowerShell engine: {engine}. Use auto, pwsh, or windows-powershell.",
            nameof(engine));
    }

    private static string GetWindowsPowerShell()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Windows PowerShell was not found.", path);
        }

        return path;
    }

    private static string? FindCommonPwsh()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        var powerShellRoot = Path.Combine(programFiles, "PowerShell");
        if (!Directory.Exists(powerShellRoot))
        {
            return null;
        }

        try
        {
            return Directory.GetDirectories(powerShellRoot)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "pwsh.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? FindOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string directory;
            try
            {
                directory = Environment.ExpandEnvironmentVariables(rawDirectory.Trim('"'));
            }
            catch (ArgumentException)
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(directory, executableName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
