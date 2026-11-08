using System.Collections.Concurrent;
using System.ComponentModel;
using Talvora.Shared;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class JobTools
{
[McpServerTool(
        Name = "talvora_job_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobStartResponse)),
     Description("Start a long-running development job as the signed-in user (runAs=user, default) or under the Talvora Windows service (runAs=service). stdout/stderr are persisted under ProgramData so they can be read incrementally.")]
    public static async Task<TalvoraJobStartResponse> Start(
        [Description("Executable path or command resolvable by Windows.")] string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool clearEnvironment = false,
        string runAs = "user",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException("Executable is required.", nameof(executable));
        }
        if (runAs is not ("user" or "service"))
            throw new ArgumentOutOfRangeException(nameof(runAs), runAs, "runAs is user or service.");

        cancellationToken.ThrowIfCancellationRequested();

        var jobsRoot = GetJobsRoot();
        await CleanupCompletedJobsAsync(
            jobsRoot,
            MaximumCompletedJobs,
            MaximumCompletedJobBytes,
            CompletedJobRetention,
            cancellationToken).ConfigureAwait(false);

        var jobId = Guid.NewGuid().ToString("N");
        var jobDirectory = Path.Combine(jobsRoot, jobId);
        Directory.CreateDirectory(jobDirectory);

        var stdoutPath = Path.Combine(jobDirectory, "stdout.log");
        var stderrPath = Path.Combine(jobDirectory, "stderr.log");
        var metadataPath = Path.Combine(jobDirectory, "job.json");

        await File.WriteAllBytesAsync(stdoutPath, [], cancellationToken);
        await File.WriteAllBytesAsync(stderrPath, [], cancellationToken);
        await WriteLogGenerationAsync(
            stdoutPath,
            0,
            cancellationToken).ConfigureAwait(false);
        await WriteLogGenerationAsync(
            stderrPath,
            0,
            cancellationToken).ConfigureAwait(false);

        Process? process = null;
        Task? stdoutPump = null;
        Task? stderrPump = null;
        NamedPipeServerStream? inputPipe = null;
        NamedPipeServerStream? outputPipe = null;
        NamedPipeServerStream? errorPipe = null;
        StreamWriter? standardInput = null;
        StreamReader? standardOutput = null;
        StreamReader? standardError = null;
        string? temporaryCommandScript = null;
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        try
        {
            if (runAs == "service")
            {
                var startInfo = LiteralProcessStart.Create(
                    executable, cwd, arguments ?? [], environment, clearEnvironment,
                    out temporaryCommandScript);
                startInfo.RedirectStandardInput = true;
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                if (!process.Start())
                    throw new InvalidOperationException($"Failed to start process: {executable}");
                standardInput = process.StandardInput;
                standardOutput = process.StandardOutput;
                standardError = process.StandardError;
            }
            else
            {
                InteractiveUserContext user;
                try
                {
                    user = WindowsSessionLauncher.GetActiveInteractiveUser();
                }
                catch (InvalidOperationException inner)
                {
                    throw new InvalidOperationException(
                        "runAs=user needs a signed-in Windows user; sign in or use runAs=service. " +
                        inner.Message, inner);
                }

                if (string.IsNullOrWhiteSpace(workingDirectory))
                    cwd = user.UserProfile ?? throw new InvalidOperationException(
                        "The signed-in user's profile directory is unavailable.");
                var resolved = Path.IsPathRooted(executable)
                    ? executable
                    : SignedInUserToolchain.Resolve([executable]);
                if (string.IsNullOrWhiteSpace(resolved))
                    throw new FileNotFoundException(
                        $"{executable} was not found on the signed-in user's PATH.");
                var userFolder = Path.Combine(jobDirectory, "user");
                InteractiveUserProcessRunner.ProtectDirectory(userFolder, user.Sid);
                var scriptPath = Path.Combine(userFolder, "jobrunner.ps1");
                var requestPath = Path.Combine(userFolder, "request.json");
                var prefix = "talvora-job-" + jobId;
                await File.WriteAllTextAsync(scriptPath,
                    UserJobRelay.RunnerScript, new UTF8Encoding(false), cancellationToken);
                var request = new UserJobRelay.UserJobRequest(
                    resolved, cwd, arguments ?? [], environment ?? new Dictionary<string, string?>(),
                    clearEnvironment, prefix + "-in", prefix + "-out", prefix + "-err",
                    Path.Combine(userFolder, "child.pid"), Path.Combine(userFolder, "exit.code"));
                await File.WriteAllTextAsync(requestPath,
                    JsonSerializer.Serialize(request), new UTF8Encoding(false), cancellationToken);

                inputPipe = CreateJobPipe(prefix + "-in", PipeDirection.Out, user.Sid);
                outputPipe = CreateJobPipe(prefix + "-out", PipeDirection.In, user.Sid);
                errorPipe = CreateJobPipe(prefix + "-err", PipeDirection.In, user.Sid);
                var pwsh = CommandResolver.Resolve(
                    ["pwsh.exe", "pwsh"], [@"C:\Program Files\PowerShell\7\pwsh.exe"])
                    ?? throw new FileNotFoundException(
                        "PowerShell 7 is required for interactive user execution.");
                var launch = WindowsSessionLauncher.StartProcess(
                    pwsh,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy",
                     "Bypass", "-File", scriptPath, "-RequestPath", requestPath,
                     "-HelperAssemblyPath", typeof(UserJobRelay).Assembly.Location],
                    user.SessionId, userFolder, environment: null,
                    visible: false, newConsole: false, expectedUserSid: user.Sid);
                process = Process.GetProcessById(launch.ProcessId);
                try
                {
                    using var connectionTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectionTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    await Task.WhenAll(
                        inputPipe.WaitForConnectionAsync(connectionTimeout.Token),
                        outputPipe.WaitForConnectionAsync(connectionTimeout.Token),
                        errorPipe.WaitForConnectionAsync(connectionTimeout.Token));
                }
                catch (Exception ex) when (
                    ex is OperationCanceledException or IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        "The signed-in user's job relay did not connect.", ex);
                }
                standardInput = new StreamWriter(inputPipe, new UTF8Encoding(false))
                    { AutoFlush = true };
                standardOutput = new StreamReader(outputPipe, ConsoleOutputEncoding.Instance);
                standardError = new StreamReader(errorPipe, ConsoleOutputEncoding.Instance);
            }

            var metadata = new TalvoraJobMetadata(
                jobId,
                process.Id,
                "Running",
                null,
                executable,
                arguments ?? [],
                cwd,
                GetProcessStartedAtUtc(process),
                null,
                stdoutPath,
                stderrPath,
                metadataPath,
                runAs,
                temporaryCommandScript);

            await WriteMetadataAsync(metadata, cancellationToken);

            stdoutPump = PumpReaderAsync(standardOutput!, stdoutPath);
            stderrPump = PumpReaderAsync(standardError!, stderrPath);

            var runtime = new TalvoraJobRuntime
            {
                Process = process,
                StandardInput = standardInput!,
                Metadata = metadata,
                StdoutPump = stdoutPump,
                StderrPump = stderrPump,
                InputPipe = inputPipe,
                OutputPipe = outputPipe,
                ErrorPipe = errorPipe,
                TemporaryCommandScript = temporaryCommandScript,
            };

            if (!LiveJobs.TryAdd(jobId, runtime))
            {
                throw new InvalidOperationException($"Failed to register Talvora job: {jobId}");
            }

            _ = ObserveExitAsync(jobId, runtime);

            return new TalvoraJobStartResponse(
                metadata.JobId,
                metadata.ProcessId,
                metadata.State,
                metadata.Executable,
                metadata.Arguments,
                metadata.WorkingDirectory,
                metadata.StartedAtUtc,
                metadata.StdoutPath,
                metadata.StderrPath,
                metadata.MetadataPath);
        }
        catch
        {
            LiveJobs.TryRemove(jobId, out _);
            if (process is not null)
                await CleanupFailedStartAsync(process);
            inputPipe?.Dispose();
            outputPipe?.Dispose();
            errorPipe?.Dispose();
            await DrainFailedPumpsAsync(stdoutPump, stderrPump);
            process?.Dispose();
            LiteralProcessStart.DeleteTemporaryCommandScript(temporaryCommandScript);
            TryDeleteFailedJobDirectory(jobDirectory);
            throw;
        }
    }

    private static NamedPipeServerStream CreateJobPipe(
        string name, PipeDirection direction, string userSid)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(userSid),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, direction, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }

    private static DateTime GetProcessStartedAtUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return DateTime.UtcNow;
        }
    }

    private static async Task CleanupFailedStartAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
        }

        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static async Task DrainFailedPumpsAsync(Task? stdoutPump, Task? stderrPump)
    {
        var pumps = new[] { stdoutPump, stderrPump }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        if (pumps.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pumps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
        }
    }

    private static void TryDeleteFailedJobDirectory(string jobDirectory)
    {
        try
        {
            if (Directory.Exists(jobDirectory))
            {
                Directory.Delete(jobDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [McpServerTool(
        Name = "talvora_job_get",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobInfoResponse)),
     Description("Return current metadata and state for a Talvora background job. Jobs are identified by Talvora job ID and persisted under ProgramData.")]
    public static async Task<TalvoraJobInfoResponse> Get(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(jobId, cancellationToken);
        return await RefreshStateAsync(metadata, cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_job_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJobListResponse)),
     Description("List Talvora background jobs. includeExited=false returns only jobs that are still running. maxResults=0 requests the finite server maximum page; use resultOffset/nextResultOffset to continue while persisted jobs are unchanged.")]
    public static async Task<TalvoraJobListResponse> List(
        bool includeExited = true,
        int maxResults = 200,
        long resultOffset = 0,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0 || resultOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxResults and resultOffset cannot be negative.");
        }

        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteJobListResults
                : Math.Min(maxResults, AbsoluteJobListResults);

        var root = GetJobsRoot();
        if (!Directory.Exists(root))
        {
            return new TalvoraJobListResponse(
                0,
                [],
                resultOffset,
                false,
                null);
        }

        var jobs = new List<TalvoraJobInfoResponse>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "job.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var metadata = await ReadMetadataFileAsync(metadataPath, cancellationToken);
                var job = await RefreshStateAsync(metadata, cancellationToken);

                if (!includeExited &&
                    !string.Equals(job.State, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                jobs.Add(job);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        var ordered = jobs
            .OrderByDescending(job => job.StartedAtUtc)
            .ThenBy(job => job.JobId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resultOffset >= ordered.LongLength)
        {
            return new TalvoraJobListResponse(
                0,
                [],
                resultOffset,
                false,
                null);
        }

        var page = ordered
            .Skip(checked((int)resultOffset))
            .Take(effectiveMaxResults + 1)
            .ToArray();
        var truncated = page.Length > effectiveMaxResults;
        var result = truncated
            ? page[..effectiveMaxResults]
            : page;
        return new TalvoraJobListResponse(
            result.Length,
            result,
            resultOffset,
            truncated,
            truncated
                ? checked(resultOffset + result.Length)
                : null);
    }
}
