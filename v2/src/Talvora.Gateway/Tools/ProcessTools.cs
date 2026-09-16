using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Gateway.Tools;

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "talvora_run_process", Destructive = true, OpenWorld = true), Description("Run any executable available to the Talvora process with explicit arguments. No command deny-list is applied.")]
    public static async Task<ProcessExecutionResult> RunProcess(
        string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments ?? []) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Failed to start process: {executable}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
            return new ProcessExecutionResult(process.ExitCode, await stdoutTask, await stderrTask, false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            return new ProcessExecutionResult(process.ExitCode, await stdoutTask, await stderrTask, true);
        }
    }
}
