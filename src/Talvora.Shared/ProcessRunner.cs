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
        var startInfo = CreateStartInfo(executable, cwd, requestedArguments);

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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = timeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
            : null;
        using var linkedCts = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutCts?.IsCancellationRequested is true &&
            !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
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
        IReadOnlyList<string> arguments)
    {
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

        var script = new ProcessStartInfo
        {
            FileName = commandProcessor,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        script.Arguments = BuildCommandScriptArguments(
            executable,
            arguments);
        return script;
    }

    private static string BuildCommandScriptArguments(
        string executable,
        IReadOnlyList<string> arguments)
    {
        static string Quote(string value) =>
            "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        var command = string.Join(
            " ",
            new[] { Quote(executable) }.Concat(arguments.Select(Quote)));

        // cmd.exe /s /c applies special quote stripping to its command string.
        // Supplying the complete raw command line preserves the required outer
        // quote pair while each batch argument remains independently quoted.
        return "/d /v:off /s /c \"" + command + "\"";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}