using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

public static class InteractiveUserProcessRunner
{
    private static readonly TimeSpan StaleRunRetention =
        TimeSpan.FromHours(24);

    private sealed record Request(
        string Executable,
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment,
        string StandardOutputPath,
        string StandardErrorPath,
        string ResultPath,
        string HelperAssemblyPath,
        int MaximumPersistedCharacters,
        bool DiscardStandardError);

    private sealed record Result(
        int ExitCode,
        string? Error,
        long ElapsedMilliseconds,
        long StandardOutputTotalCharacters = 0,
        long StandardErrorTotalCharacters = 0,
        bool StandardOutputTruncated = false,
        bool StandardErrorTruncated = false);

    public static async Task<ProcessExecutionResult> RunAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default,
        int maxCapturedCharactersPerStream =
            ProcessRunner.DefaultMaximumCapturedCharacters,
        InteractiveUserContext? interactiveUser = null,
        bool discardStandardError = false)
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

        var context = interactiveUser ??
            WindowsSessionLauncher.GetDefaultInteractiveUser();
        if (interactiveUser is not null)
        {
            var current = WindowsSessionLauncher.GetActiveUserForSession(
                context.SessionId);
            if (!string.Equals(current.Sid, context.Sid,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "The Windows user session identity changed before launch.");
            }
        }
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

        var runsRoot = Path.Combine(
            localAppData,
            "Talvora",
            "InteractiveRuns");
        CleanupStaleRunDirectories(runsRoot);

        var runRoot = Path.Combine(
            runsRoot,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);

        var leasePath = Path.Combine(runRoot, "active.lock");
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
                .Location,
            maxCapturedCharactersPerStream,
            discardStandardError);

        FileStream? runLease = null;
        try
        {
            runLease = new FileStream(
                leasePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read);

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
                newConsole: false,
                expectedUserSid: interactiveUser?.Sid);

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
            // A timed-out helper may leave an incomplete result document.
            // Report the timeout using the already bounded partial output.
            if (!timedOut && File.Exists(resultPath))
            {
                result = JsonSerializer.Deserialize<Result>(
                    await TextFileStore.ReadBoundedAsync(
                        resultPath, 64 * 1024,
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
                    stdoutCapture.Truncated || result?.StandardOutputTruncated == true,
                    stderrCapture.Truncated || result?.StandardErrorTruncated == true,
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
                // The helper may return an exception message after the
                // bounded stderr capture. Do not let it bypass the limit.
                var remaining = Math.Max(0,
                    maxCapturedCharactersPerStream - stderr.Length);
                var safeError = result.Error.Length > remaining
                    ? result.Error[..remaining]
                    : result.Error;
                stderr = string.IsNullOrWhiteSpace(stderr)
                    ? safeError
                    : stderr + Environment.NewLine + safeError;
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
                stdoutCapture.Truncated || result.StandardOutputTruncated,
                stderrCapture.Truncated || result.StandardErrorTruncated ||
                    stderr.Length >= maxCapturedCharactersPerStream,
                false,
                Math.Max(stdoutCapture.TotalCharacters, result.StandardOutputTotalCharacters),
                Math.Max(stderrCapture.TotalCharacters, result.StandardErrorTotalCharacters));
        }
        finally
        {
            runLease?.Dispose();

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

    private static void CleanupStaleRunDirectories(string runsRoot)
    {
        try
        {
            if (!Directory.Exists(runsRoot))
            {
                return;
            }

            var cutoffUtc =
                DateTime.UtcNow - StaleRunRetention;

            foreach (var directory in
                     Directory.EnumerateDirectories(runsRoot))
            {
                try
                {
                    var leasePath =
                        Path.Combine(directory, "active.lock");
                    if (Directory.GetLastWriteTimeUtc(directory) > cutoffUtc)
                    {
                        continue;
                    }

                    if (File.Exists(leasePath))
                    {
                        using (new FileStream(
                                   leasePath,
                                   FileMode.Open,
                                   FileAccess.Read,
                                   FileShare.None))
                        {
                        }
                    }

                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    DirectoryNotFoundException or
                    FileNotFoundException or
                    ArgumentException or
                    NotSupportedException)
                {
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            DirectoryNotFoundException or
            ArgumentException or
            NotSupportedException)
        {
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
$resultPath = Join-Path (Split-Path -Parent $RequestPath) 'result.json'
try {
    $request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
    Remove-Item -LiteralPath $RequestPath -Force -ErrorAction SilentlyContinue
    [void][Reflection.Assembly]::LoadFrom(
        [string]$request.HelperAssemblyPath)

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = [string]$request.Executable
    $psi.WorkingDirectory = [string]$request.WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    # Native Codex emits UTF-8, regardless of the Windows OEM code page.
    $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
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

    $pumpCts = [Threading.CancellationTokenSource]::new()
    $stdoutTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardOutput,
        [string]$request.StandardOutputPath,
        [int]$request.MaximumPersistedCharacters,
        $pumpCts.Token)
    $stderrBudget = if($request.DiscardStandardError) {
        0
    } else {
        [int]$request.MaximumPersistedCharacters
    }
    $stderrTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardError,
        [string]$request.StandardErrorPath,
        $stderrBudget,
        $pumpCts.Token)
    $process.WaitForExit()
    $drainTask = [Threading.Tasks.Task]::WhenAll(
        [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))
    if(-not $drainTask.Wait(5000)) {
        $pumpCts.Cancel()
        throw 'Interactive process output pipes did not close after process exit.'
    }
    $stdoutResult = $stdoutTask.GetAwaiter().GetResult()
    $stderrResult = $stderrTask.GetAwaiter().GetResult()

    $sw.Stop()
    @{
        ExitCode = $process.ExitCode
        Error = $null
        ElapsedMilliseconds = $sw.ElapsedMilliseconds
        StandardOutputTotalCharacters = $stdoutResult.TotalCharacters
        StandardErrorTotalCharacters = $stderrResult.TotalCharacters
        StandardOutputTruncated = $stdoutResult.Truncated
        StandardErrorTruncated = $stderrResult.Truncated
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath $resultPath -Encoding utf8
}
catch {
    $sw.Stop()
    @{
        ExitCode = 1
        Error = $_.Exception.Message
        ElapsedMilliseconds = $sw.ElapsedMilliseconds
    } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath $resultPath -Encoding utf8
}
""";
}