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
    long ElapsedMilliseconds,
    bool StandardOutputTruncated = false,
    bool StandardErrorTruncated = false,
    bool OutputDrainTimedOut = false,
    long StandardOutputTotalCharacters = 0,
    long StandardErrorTotalCharacters = 0);

public static class ProcessRunner
{
    public const int DefaultMaximumCapturedCharacters =
        BoundedTextCapture.DefaultMaximumCharacters;
    public const int AbsoluteMaximumCapturedCharacters =
        BoundedTextCapture.AbsoluteMaximumCharacters;

    public static async Task<ProcessExecutionResult> RunAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default,
        int maxCapturedCharactersPerStream =
            DefaultMaximumCapturedCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }
        ValidateMaximumCapturedCharacters(
            maxCapturedCharactersPerStream);

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {cwd}");
        }

        // W7: Process.Start ignores PATHEXT, so "npm" (npm.cmd) failed and a bare "npm.cmd" broke the shim's %~dp0.
        // Only PATH directories are searched; a same-named file in the working directory is never chosen.
        if (OperatingSystem.IsWindows() &&
            !Path.IsPathRooted(executable) &&
            executable.IndexOfAny(['\\', '/']) < 0 &&
            CommandResolver.ResolveAll([executable], workingDirectory: cwd)
                .FirstOrDefault(path =>
                    !string.Equals(Path.GetDirectoryName(path), cwd, StringComparison.OrdinalIgnoreCase) &&
                    CommandResolver.GetExecutableExtensions().Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            is { } resolvedExecutable)
        {
            executable = resolvedExecutable;
        }

        var requestedArguments = arguments?.ToArray() ?? [];
        var startInfo = LiteralProcessStart.Create(
            executable,
            cwd,
            requestedArguments,
            environment,
            clearEnvironment: false,
            out var temporaryCommandScript);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stopwatch = Stopwatch.StartNew();

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start executable: {executable}");
            }

            var processId = process.Id;

            using var timeoutCts = new CancellationTokenSource();
            if (timeoutSeconds > 0)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            }
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            var stdoutTask = BoundedTextCapture.ReadAsync(
                process.StandardOutput,
                maxCapturedCharactersPerStream,
                linkedCts.Token);
            var stderrTask = BoundedTextCapture.ReadAsync(
                process.StandardError,
                maxCapturedCharactersPerStream,
                linkedCts.Token);

            var timedOut = false;
            var processExitedBeforeDeadline = false;
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                processExitedBeforeDeadline = true;
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
            cancellationToken.ThrowIfCancellationRequested();

            var outputDrainTimedOut =
                processExitedBeforeDeadline &&
                timeoutSeconds > 0 &&
                timeoutCts.IsCancellationRequested &&
                (stdout.Interrupted || stderr.Interrupted);
            if (outputDrainTimedOut)
            {
                timedOut = true;
            }

            stopwatch.Stop();

            return new ProcessExecutionResult(
                process.ExitCode,
                stdout.Text,
                stderr.Text,
                timedOut,
                processId,
                executable,
                cwd,
                requestedArguments,
                stopwatch.ElapsedMilliseconds,
                stdout.Truncated,
                stderr.Truncated,
                outputDrainTimedOut,
                stdout.TotalCharacters,
                stderr.TotalCharacters);
        }
        finally
        {
            LiteralProcessStart.DeleteTemporaryCommandScript(temporaryCommandScript);
        }
    }

    public static async Task<ProcessExecutionResult> RunCheckedAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default,
        int maxCapturedCharactersPerStream =
            DefaultMaximumCapturedCharacters)
    {
        var result = await RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken,
            maxCapturedCharactersPerStream).ConfigureAwait(false);

        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed. Executable={executable}, ExitCode={result.ExitCode}, " +
                $"TimedOut={result.TimedOut}, OutputDrainTimedOut={result.OutputDrainTimedOut}, " +
                $"StderrTruncated={result.StandardErrorTruncated}, stderr={result.StandardError}");
        }

        return result;
    }

    private static void ValidateMaximumCapturedCharacters(
        int maximumCharacters)
    {
        if (maximumCharacters <= 0 ||
            maximumCharacters > AbsoluteMaximumCapturedCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                maximumCharacters,
                $"Capture must be between 1 and {AbsoluteMaximumCapturedCharacters} characters per stream.");
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
