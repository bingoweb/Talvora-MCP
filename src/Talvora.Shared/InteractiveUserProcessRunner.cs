using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

public static class InteractiveUserProcessRunner
{
    private sealed record Request(
        string Executable,
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment,
        string StandardOutputPath,
        string StandardErrorPath,
        string ResultPath,
        string HelperAssemblyPath);

    private sealed record Result(
        int ExitCode,
        string? Error,
        long ElapsedMilliseconds);

    public static async Task<ProcessExecutionResult> RunAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default,
        int maxCapturedCharactersPerStream =
            ProcessRunner.DefaultMaximumCapturedCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (maxCapturedCharactersPerStream <= 0 ||
            maxCapturedCharactersPerStream >
            ProcessRunner.AbsoluteMaximumCapturedCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCapturedCharactersPerStream));
        }

        var context = WindowsSessionLauncher.GetDefaultInteractiveUser();
        var localAppData = context.Environment.TryGetValue(
            "LOCALAPPDATA",
            out var localValue)
            ? localValue
            : null;

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "Interactive user's LOCALAPPDATA could not be resolved.");
        }

        var runRoot = Path.Combine(
            localAppData,
            "Talvora",
            "InteractiveRuns",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);

        var requestPath = Path.Combine(runRoot, "request.json");
        var stdoutPath = Path.Combine(runRoot, "stdout.txt");
        var stderrPath = Path.Combine(runRoot, "stderr.txt");
        var resultPath = Path.Combine(runRoot, "result.json");
        var scriptPath = Path.Combine(runRoot, "runner.ps1");

        var requestedArguments = arguments?.ToArray() ?? [];
        var request = new Request(
            executable,
            Path.GetFullPath(workingDirectory),
            requestedArguments,
            environment is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(environment),
            stdoutPath,
            stderrPath,
            resultPath,
            typeof(InteractiveUserProcessRunner)
                .Assembly
                .Location);

        try
        {
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                scriptPath,
                RunnerScript,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            var pwsh = CommandResolver.Resolve(
                ["pwsh.exe", "pwsh"],
                [@"C:\Program Files\PowerShell\7\pwsh.exe"])
                ?? throw new FileNotFoundException(
                    "PowerShell 7 is required for interactive user execution.");

            var launch = WindowsSessionLauncher.StartProcess(
                pwsh,
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                    "-RequestPath",
                    requestPath,
                ],
                context.SessionId,
                runRoot,
                environment: null,
                visible: false,
                newConsole: false);

            using var process = Process.GetProcessById(launch.ProcessId);
            using var timeoutCts = timeoutSeconds > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                : new CancellationTokenSource();
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeoutSeconds > 0 &&
                timeoutCts.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
                await WaitForExitBestEffortAsync(process).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await WaitForExitBestEffortAsync(process).ConfigureAwait(false);
                throw;
            }

            var stdoutCapture =
                await BoundedTextCapture.ReadFileAsync(
                    stdoutPath,
                    maxCapturedCharactersPerStream,
                    cancellationToken).ConfigureAwait(false);
            var stderrCapture =
                await BoundedTextCapture.ReadFileAsync(
                    stderrPath,
                    maxCapturedCharactersPerStream,
                    cancellationToken).ConfigureAwait(false);
            var stdout = stdoutCapture.Text;
            var stderr = stderrCapture.Text;

            Result? result = null;
            if (File.Exists(resultPath))
            {
                result = JsonSerializer.Deserialize<Result>(
                    await File.ReadAllTextAsync(
                        resultPath,
                        cancellationToken).ConfigureAwait(false));
            }

            if (timedOut)
            {
                return new ProcessExecutionResult(
                    -1,
                    stdout,
                    stderr,
                    true,
                    launch.ProcessId,
                    executable,
                    Path.GetFullPath(workingDirectory),
                    requestedArguments,
                    timeoutSeconds * 1000L,
                    stdoutCapture.Truncated,
                    stderrCapture.Truncated,
                    false,
                    stdoutCapture.TotalCharacters,
                    stderrCapture.TotalCharacters);
            }

            if (result is null)
            {
                throw new InvalidOperationException(
                    "Interactive user command finished without a result document.");
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                stderr = string.IsNullOrWhiteSpace(stderr)
                    ? result.Error
                    : stderr + Environment.NewLine + result.Error;
            }

            return new ProcessExecutionResult(
                result.ExitCode,
                stdout,
                stderr,
                false,
                launch.ProcessId,
                executable,
                Path.GetFullPath(workingDirectory),
                requestedArguments,
                result.ElapsedMilliseconds,
                stdoutCapture.Truncated,
                stderrCapture.Truncated,
                false,
                stdoutCapture.TotalCharacters,
                stderrCapture.TotalCharacters);
        }
        finally
        {
            try
            {
                Directory.Delete(runRoot, recursive: true);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
            }
        }
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
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task WaitForExitBestEffortAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private const string RunnerScript = """
param(
    [Parameter(Mandatory=$true)]
    [string]$RequestPath
)

$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
[void][Reflection.Assembly]::LoadFrom(
    [string]$request.HelperAssemblyPath)

try {
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = [string]$request.Executable
    $psi.WorkingDirectory = [string]$request.WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    foreach($arg in @($request.Arguments)) {
        [void]$psi.ArgumentList.Add([string]$arg)
    }

    foreach($property in $request.Environment.PSObject.Properties) {
        if($null -eq $property.Value) {
            [void]$psi.Environment.Remove($property.Name)
        } else {
            $psi.Environment[$property.Name] = [string]$property.Value
        }
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if(-not $process.Start()) {
        throw "Failed to start interactive child process."
    }

    $stdoutTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardOutput,
        [string]$request.StandardOutputPath)
    $stderrTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardError,
        [string]$request.StandardErrorPath)
    $process.WaitForExit()
    $stdoutTask.GetAwaiter().GetResult()
    $stderrTask.GetAwaiter().GetResult()

    $sw.Stop()
    @{
        ExitCode = $process.ExitCode
        Error = $null
        ElapsedMilliseconds = $sw.ElapsedMilliseconds
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath ([string]$request.ResultPath) -Encoding utf8
}
catch {
    $sw.Stop()
    @{
        ExitCode = 1
        Error = $_.Exception.Message
        ElapsedMilliseconds = $sw.ElapsedMilliseconds
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath ([string]$request.ResultPath) -Encoding utf8
}
""";
}