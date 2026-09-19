using System.Diagnostics;

namespace Talvora.Shared;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    long ElapsedMilliseconds);

public static class ProcessRunner
{
    public static async Task<ProcessExecutionResult> RunAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {cwd}");
        }

        var requestedArguments = arguments?.ToArray() ?? [];
        var startInfo = CreateStartInfo(
            executable,
            cwd,
            requestedArguments,
            out var temporaryCommandScript);

        try
        {
            if (environment is not null)
            {
                foreach (var pair in environment)
                {
                    if (pair.Value is null)
                    {
                        startInfo.Environment.Remove(pair.Key);
                    }
                    else
                    {
                        startInfo.Environment[pair.Key] = pair.Value;
                    }
                }
            }

            using var process = new Process { StartInfo = startInfo };
            var stopwatch = Stopwatch.StartNew();

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start executable: {executable}");
            }

            var processId = process.Id;
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeoutCts = new CancellationTokenSource();
            if (timeoutSeconds > 0)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeoutSeconds > 0 &&
                timeoutCts.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                var killIssued = TryKill(process);
                if (!await WaitForTerminationAsync(process).ConfigureAwait(false))
                {
                    throw new TimeoutException(
                        $"Process {processId} exceeded the timeout and could not be terminated. " +
                        $"KillIssued={killIssued}, Executable={executable}");
                }
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                _ = await WaitForTerminationAsync(process).ConfigureAwait(false);
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            stopwatch.Stop();

            return new ProcessExecutionResult(
                process.ExitCode,
                stdout,
                stderr,
                timedOut,
                processId,
                executable,
                cwd,
                requestedArguments,
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            TryDeleteTemporaryCommandScript(temporaryCommandScript);
        }
    }

    public static async Task<ProcessExecutionResult> RunCheckedAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed. Executable={executable}, ExitCode={result.ExitCode}, " +
                $"TimedOut={result.TimedOut}, stderr={result.StandardError}");
        }

        return result;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        out string? temporaryCommandScript)
    {
        temporaryCommandScript = null;
        var extension = Path.GetExtension(executable);
        var isCommandScript = OperatingSystem.IsWindows() &&
            (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

        if (!isCommandScript)
        {
            var direct = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                direct.ArgumentList.Add(argument);
            }
            return direct;
        }

        var commandProcessor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        var variablePrefix = "TALVORA_" + Guid.NewGuid().ToString("N");
        temporaryCommandScript = Path.Combine(
            Path.GetTempPath(),
            $"talvora-run-{Guid.NewGuid():N}.cmd");

        var wrapper = new System.Text.StringBuilder();
        wrapper.AppendLine("@echo off");
        wrapper.AppendLine("setlocal EnableDelayedExpansion");
        wrapper.Append($"\"!{variablePrefix}_SCRIPT!\"");
        for (var index = 0; index < arguments.Count; index++)
        {
            wrapper.Append($" \"!{variablePrefix}_ARG_{index}!\"");
        }
        wrapper.AppendLine();

        File.WriteAllText(
            temporaryCommandScript,
            wrapper.ToString(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var script = new ProcessStartInfo
        {
            FileName = commandProcessor,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = "/d /v:on /s /c \"\"" + temporaryCommandScript + "\"\"",
        };

        script.Environment[variablePrefix + "_SCRIPT"] = executable;
        for (var index = 0; index < arguments.Count; index++)
        {
            script.Environment[$"{variablePrefix}_ARG_{index}"] =
                EncodeBatchArgument(arguments[index]);
        }

        return script;
    }

    private static string EncodeBatchArgument(string value) =>
        value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal);

    private static void TryDeleteTemporaryCommandScript(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryKill(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return process.HasExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return process.HasExited;
        }
    }

    private static async Task<bool> WaitForTerminationAsync(Process process)
    {
        if (process.HasExited)
        {
            return true;
        }

        using var terminationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(terminationTimeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return process.HasExited;
        }
    }
}
