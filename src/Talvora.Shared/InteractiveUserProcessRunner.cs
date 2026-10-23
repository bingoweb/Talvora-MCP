using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
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
        bool DiscardStandardError,
string? CodexProgressPath,
        string? ClaudeProgressPath,
        bool CloseStandardInput,
        string? ClaudeWorkspaceRoot);

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
        bool discardStandardError = false,
        Action<string>? onStandardOutputSnapshot = null,
Action<CodexCliEventResult>? onCodexProgress = null,
        Action<ClaudeCliProgress>? onClaudeProgress = null,
        bool closeStandardInput = false,
        string? claudeWorkspaceRoot = null)
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
        var runsRoot = GetSecureRunsRoot();
        Directory.CreateDirectory(runsRoot);
        if (!TalvoraOwnedTempCleanup.IsDirectoryPathReparseSafe(
                Path.GetDirectoryName(runsRoot)!, runsRoot))
        {
            throw new InvalidOperationException(
                "Interactive run storage contains a reparse point.");
        }
        CleanupStaleRunDirectories(runsRoot);
        var runRoot = CreateProtectedRunDirectory(runsRoot, context.Sid);

        var leasePath = Path.Combine(runRoot, "active.lock");
        var requestPath = Path.Combine(runRoot, "request.json");
        var stdoutPath = Path.Combine(runRoot, "stdout.txt");
        var stderrPath = Path.Combine(runRoot, "stderr.txt");
        var resultPath = Path.Combine(runRoot, "result.json");
        var scriptPath = Path.Combine(runRoot, "runner.ps1");
        var progressPath = onCodexProgress is null
            ? null
            : Path.Combine(runRoot, "codex-progress.json");

        var claudeProgressPath = onClaudeProgress is null
            ? null : Path.Combine(runRoot, "claude-progress.json");
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
            discardStandardError,
            progressPath,
            claudeProgressPath,
            closeStandardInput,
            claudeWorkspaceRoot);

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
            using var progressCts = new CancellationTokenSource();
            var progressMonitor = onStandardOutputSnapshot is null
                ? Task.CompletedTask
                : MonitorStandardOutputAsync(
                    stdoutPath, onStandardOutputSnapshot, progressCts.Token);
            var codexMonitor = onCodexProgress is null
                ? Task.CompletedTask
                : MonitorCodexProgressAsync(
                    progressPath!, onCodexProgress, progressCts.Token);

            var claudeMonitor = onClaudeProgress is null
                ? Task.CompletedTask
                : MonitorClaudeProgressAsync(claudeProgressPath!, onClaudeProgress, progressCts.Token);

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
            finally
            {
                progressCts.Cancel();
                try
                {
                    await progressMonitor.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the interactive process exits.
                }
                try
                {
                    await codexMonitor.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the interactive process exits.
                }
                try { await claudeMonitor.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                if (onClaudeProgress is not null)
                {
                    TryReadClaudeProgress(claudeProgressPath!, onClaudeProgress);
                }
                if (onCodexProgress is not null)
                {
                    TryReadCodexProgress(progressPath!, onCodexProgress);
                }
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
            onStandardOutputSnapshot?.Invoke(stdout);

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
                TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                    runsRoot, runRoot, DateTimeOffset.MaxValue, out _);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task MonitorClaudeProgressAsync(
        string path, Action<ClaudeCliProgress> observer, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TryReadClaudeProgress(path, observer);
            await Task.Delay(600, token).ConfigureAwait(false);
        }
    }

    private static void TryReadClaudeProgress(string path, Action<ClaudeCliProgress> observer)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) { return; }
            var value = JsonSerializer.Deserialize<ClaudeCliProgress>(File.ReadAllText(path, Encoding.UTF8));
            if (value is not null) { observer(value); }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private static async Task MonitorCodexProgressAsync(
        string path,
        Action<CodexCliEventResult> observer,
        CancellationToken cancellationToken)
    {
        string? previous = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var latest = TryReadCodexProgress(path, observer, previous);
            if (latest is not null) { previous = latest; }
            await Task.Delay(600, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? TryReadCodexProgress(
        string path,
        Action<CodexCliEventResult> observer,
        string? previous = null)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024)
            {
                return null;
            }
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (json == previous) { return json; }
            var progress = JsonSerializer.Deserialize<CodexCliEventResult>(json);
            if (progress is not null && progress.HasEvents)
            {
                observer(progress);
            }
            return json;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or DirectoryNotFoundException or
            IOException or JsonException)
        {
            return null;
        }
    }

    private static async Task MonitorStandardOutputAsync(
        string path,
        Action<string> observer,
        CancellationToken cancellationToken)
    {
        long previousLength = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, useAsync: true);
                var length = Math.Min(stream.Length, 256 * 1024L);
                if (length > 0 && length != previousLength)
                {
                    var bytes = new byte[(int)length];
                    await stream.ReadExactlyAsync(
                        bytes, cancellationToken).ConfigureAwait(false);
                    observer(Encoding.UTF8.GetString(bytes));
                    previousLength = length;
                }
            }
            catch (Exception ex) when (
                ex is FileNotFoundException or DirectoryNotFoundException or
                IOException)
            {
                // The helper may not have opened its pipes yet.
            }
            await Task.Delay(600, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetSecureRunsRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "InteractiveRuns");

    private static string CreateProtectedRunDirectory(string runsRoot, string userSid)
    {
        if (!TalvoraOwnedTempCleanup.IsDirectoryPathReparseSafe(
                runsRoot, runsRoot))
        {
            throw new InvalidOperationException(
                "Interactive run root contains a reparse point.");
        }
        var runRoot = Path.Combine(runsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None,
            AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(userSid),
            FileSystemRights.Modify, inherit, PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(runRoot).SetAccessControl(sec);
        return runRoot;
    }

    private static void CleanupStaleRunDirectories(string runsRoot)
    {
        try
        {
            TalvoraOwnedTempCleanup.CleanupGuidDirectories(
                runsRoot, DateTimeOffset.UtcNow - StaleRunRetention,
                CancellationToken.None, candidateFilter: directory =>
                {
                    var lease = Path.Combine(directory, "active.lock");
                    if (!File.Exists(lease)) { return true; }
                    try
                    {
                        using var stream = new FileStream(
                            lease, FileMode.Open, FileAccess.Read, FileShare.None);
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException or
                        UnauthorizedAccessException)
                    {
                        return false;
                    }
                });
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
    $psi.RedirectStandardInput = [bool]$request.CloseStandardInput
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

    if ($request.CloseStandardInput) { $process.StandardInput.Close() }
    $pumpCts = [Threading.CancellationTokenSource]::new()
    $stdoutTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardOutput,
        [string]$request.StandardOutputPath,
        [int]$request.MaximumPersistedCharacters,
        $pumpCts.Token,
        $request.CodexProgressPath,
        $request.ClaudeProgressPath,
        $request.ClaudeWorkspaceRoot)
    $stderrBudget = if($request.DiscardStandardError) {
        0
    } else {
        [int]$request.MaximumPersistedCharacters
    }
    $stderrTask = [Talvora.Shared.ProcessOutputPump]::PumpToUtf8FileAsync(
        $process.StandardError,
        [string]$request.StandardErrorPath,
        $stderrBudget,
        $pumpCts.Token,
        $null,
        $null)
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
