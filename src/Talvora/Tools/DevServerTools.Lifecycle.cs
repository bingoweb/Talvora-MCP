using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DevServerTools
{
[McpServerTool(
        Name = "talvora_dev_server_start",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDevServerStatusResponse)),
     Description("Start any executable through Talvora's persistent job system and wait for optional TCP/HTTP readiness probes. No executable/argument/host/URL allowlist is applied. If both probes are configured, requireAll controls whether both or either must become ready.")]
    public static async Task<TalvoraDevServerStatusResponse> Start(
        [Description("Executable path or command resolvable by Windows.")] string executable,
        string[]? arguments = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool clearEnvironment = false,
        string? tcpHost = null,
        int? tcpPort = null,
        string? httpUrl = null,
        string httpMethod = "GET",
        Dictionary<string, string?>? httpHeaders = null,
        int[]? expectedStatusCodes = null,
        bool requireAll = true,
        bool ignoreTlsErrors = false,
        int timeoutSeconds = 60,
        int probeTimeoutSeconds = 3,
        int pollIntervalMilliseconds = 250,
        bool stopOnFailure = true,
        int logTailBytes = 16 * 1024,
        CancellationToken cancellationToken = default)
    {
        ValidateProbeConfiguration(
            tcpHost,
            tcpPort,
            httpUrl,
            httpMethod,
            expectedStatusCodes,
            timeoutSeconds,
            probeTimeoutSeconds,
            pollIntervalMilliseconds,
            logTailBytes);

        var normalizedTcpHost = tcpPort.HasValue
            ? string.IsNullOrWhiteSpace(tcpHost)
                ? "127.0.0.1"
                : tcpHost.Trim()
            : null;

        var normalizedHttpUrl =
            string.IsNullOrWhiteSpace(httpUrl)
                ? null
                : new Uri(httpUrl, UriKind.Absolute).AbsoluteUri;

        var job = await JobTools.Start(
            executable,
            arguments,
            workingDirectory,
            environment,
            clearEnvironment,
            cancellationToken);

        var metadata = new TalvoraDevServerMetadata(
            job.JobId,
            normalizedTcpHost,
            tcpPort,
            normalizedHttpUrl,
            httpMethod.Trim(),
            new Dictionary<string, string?>(
                httpHeaders ?? new Dictionary<string, string?>(),
                StringComparer.OrdinalIgnoreCase),
            expectedStatusCodes?.Distinct().Order().ToArray() ?? [],
            requireAll,
            ignoreTlsErrors,
            probeTimeoutSeconds,
            pollIntervalMilliseconds,
            DateTime.UtcNow);

        try
        {
            await WriteMetadataAsync(metadata, cancellationToken);
            return await WaitInternalAsync(
                metadata,
                timeoutSeconds,
                stopOnFailure,
                logTailBytes,
                cancellationToken);
        }
        catch
        {
            try
            {
                await JobTools.Stop(
                    job.JobId,
                    entireProcessTree: true,
                    timeoutSeconds: 15,
                    CancellationToken.None);
            }
            catch
            {
            }

            throw;
        }
    }

    [McpServerTool(
        Name = "talvora_dev_server_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDevServerStatusResponse)),
     Description("Return one persisted dev-server job, perform one TCP/HTTP readiness check using its stored probe definition, and include bounded stdout/stderr tails. The dev-server definition survives Talvora service restarts.")]
    public static async Task<TalvoraDevServerStatusResponse> Get(
        string jobId,
        int logTailBytes = 16 * 1024,
        CancellationToken cancellationToken = default)
    {
        ValidateLogTailBytes(logTailBytes);
        var metadata = await ReadMetadataAsync(jobId, cancellationToken);
        return await BuildStatusAsync(
            metadata,
            failureReason: null,
            logTailBytes,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dev_server_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDevServerListResponse)),
     Description("List persisted Talvora dev-server definitions with their current job state. includeExited=false returns only jobs whose root process is still running. maxResults=0 means unlimited.")]
    public static async Task<TalvoraDevServerListResponse> List(
        bool includeExited = true,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var root = GetMetadataRoot();
        if (!Directory.Exists(root))
        {
            return new TalvoraDevServerListResponse(0, []);
        }

        var items = new List<TalvoraDevServerListItem>();

        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata =
                    await ReadMetadataFileAsync(path, cancellationToken);
                var job =
                    await JobTools.Get(
                        metadata.JobId,
                        cancellationToken);

                if (!includeExited &&
                    !string.Equals(
                        job.State,
                        "Running",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(
                    new TalvoraDevServerListItem(
                        metadata.JobId,
                        job.ProcessId,
                        job.State,
                        job.ExitCode,
                        job.StartedAtUtc,
                        metadata.TcpHost,
                        metadata.TcpPort,
                        metadata.HttpUrl,
                        metadata.RequireAll));
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException or
                FileNotFoundException)
            {
            }
        }

        IEnumerable<TalvoraDevServerListItem> ordered =
            items
                .OrderByDescending(item => item.StartedAtUtc)
                .ThenBy(
                    item => item.JobId,
                    StringComparer.OrdinalIgnoreCase);

        if (maxResults > 0)
        {
            ordered = ordered.Take(maxResults);
        }

        var result = ordered.ToArray();
        return new TalvoraDevServerListResponse(
            result.Length,
            result);
    }

    [McpServerTool(
        Name = "talvora_dev_server_wait",
        Destructive = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDevServerStatusResponse)),
     Description("Wait for a persisted dev-server job's stored TCP/HTTP readiness definition. timeoutSeconds=0 waits indefinitely. stopOnFailure can terminate the complete job process tree if readiness times out.")]
    public static async Task<TalvoraDevServerStatusResponse> Wait(
        string jobId,
        int timeoutSeconds = 60,
        bool stopOnFailure = false,
        int logTailBytes = 16 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }

        ValidateLogTailBytes(logTailBytes);

        var metadata =
            await ReadMetadataAsync(
                jobId,
                cancellationToken);

        return await WaitInternalAsync(
            metadata,
            timeoutSeconds,
            stopOnFailure,
            logTailBytes,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dev_server_stop",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDevServerStopResponse)),
     Description("Stop a persisted dev-server job using Talvora's job process-tree termination. deleteArtifacts=true also removes job logs/metadata and the dev-server definition after the process exits.")]
    public static async Task<TalvoraDevServerStopResponse> Stop(
        string jobId,
        bool entireProcessTree = true,
        int timeoutSeconds = 15,
        bool deleteArtifacts = false,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }

        _ = await ReadMetadataAsync(jobId, cancellationToken);

        var stop = await JobTools.Stop(
            jobId,
            entireProcessTree,
            timeoutSeconds,
            cancellationToken);

        var deleted = false;
        if (deleteArtifacts && stop.Exited)
        {
            var deletion = await JobTools.Delete(
                jobId,
                stopIfRunning: false,
                stopTimeoutSeconds: timeoutSeconds,
                cancellationToken);

            deleted = deletion.Deleted;
            TryDeleteMetadata(jobId);
        }

        return new TalvoraDevServerStopResponse(
            stop.JobId,
            stop.ProcessId,
            stop.Found,
            stop.KillIssued,
            stop.Exited,
            deleted,
            stop.State,
            stop.ExitCode);
    }

    private static async Task<TalvoraDevServerStatusResponse>
        WaitInternalAsync(
            TalvoraDevServerMetadata metadata,
            int timeoutSeconds,
            bool stopOnFailure,
            int logTailBytes,
            CancellationToken cancellationToken)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await BuildStatusAsync(
                metadata,
                failureReason: null,
                logTailBytes,
                cancellationToken);

            if (status.Ready)
            {
                return status;
            }

            if (!string.Equals(
                    status.State,
                    "Running",
                    StringComparison.OrdinalIgnoreCase))
            {
                return status with
                {
                    FailureReason =
                        "process-exited-before-readiness",
                };
            }

            if (timeoutSeconds > 0 &&
                stopwatch.Elapsed >=
                TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (stopOnFailure)
                {
                    try
                    {
                        await JobTools.Stop(
                            metadata.JobId,
                            entireProcessTree: true,
                            timeoutSeconds: 15,
                            cancellationToken);
                    }
                    catch
                    {
                    }
                }

                return await BuildStatusAsync(
                    metadata,
                    failureReason: "readiness-timeout",
                    logTailBytes,
                    cancellationToken);
            }

            var delay =
                TimeSpan.FromMilliseconds(
                    metadata.PollIntervalMilliseconds);

            if (timeoutSeconds > 0)
            {
                var remaining =
                    TimeSpan.FromSeconds(timeoutSeconds) -
                    stopwatch.Elapsed;
                if (remaining < delay)
                {
                    delay = remaining;
                }
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    delay,
                    cancellationToken);
            }
        }
    }

    private static async Task<TalvoraDevServerStatusResponse>
        BuildStatusAsync(
            TalvoraDevServerMetadata metadata,
            string? failureReason,
            int logTailBytes,
            CancellationToken cancellationToken)
    {
        var job =
            await JobTools.Get(
                metadata.JobId,
                cancellationToken);

        TalvoraDevServerProbeResult tcpProbe;
        TalvoraDevServerProbeResult httpProbe;

        if (string.Equals(
                job.State,
                "Running",
                StringComparison.OrdinalIgnoreCase))
        {
            (tcpProbe, httpProbe) =
                await ProbeAsync(
                    metadata,
                    cancellationToken);
        }
        else
        {
            tcpProbe = NotRunningProbe(
                "tcp",
                metadata.TcpPort.HasValue);
            httpProbe = NotRunningProbe(
                "http",
                metadata.HttpUrl is not null);
        }

        var configured = new List<bool>();
        if (tcpProbe.Configured)
        {
            configured.Add(tcpProbe.Ready);
        }
        if (httpProbe.Configured)
        {
            configured.Add(httpProbe.Ready);
        }

        var probesReady =
            configured.Count == 0 ||
            (metadata.RequireAll
                ? configured.All(value => value)
                : configured.Any(value => value));

        var ready =
            string.Equals(
                job.State,
                "Running",
                StringComparison.OrdinalIgnoreCase) &&
            probesReady;

        var stdoutTail =
            await ReadTailAsync(
                job.StdoutPath,
                logTailBytes,
                cancellationToken);
        var stderrTail =
            await ReadTailAsync(
                job.StderrPath,
                logTailBytes,
                cancellationToken);

        return new TalvoraDevServerStatusResponse(
            job.JobId,
            job.ProcessId,
            job.State,
            job.ExitCode,
            ready,
            failureReason,
            job.StartedAtUtc,
            tcpProbe,
            httpProbe,
            stdoutTail,
            stderrTail,
            job.StdoutPath,
            job.StderrPath);
    }
}
