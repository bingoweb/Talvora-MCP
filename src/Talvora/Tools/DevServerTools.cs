using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraDevServerProbeResult(
    string Kind,
    bool Configured,
    bool Ready,
    string? Detail,
    int? StatusCode,
    long ElapsedMilliseconds);

public sealed record TalvoraDevServerStatusResponse(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    bool Ready,
    string? FailureReason,
    DateTime StartedAtUtc,
    TalvoraDevServerProbeResult TcpProbe,
    TalvoraDevServerProbeResult HttpProbe,
    string StdoutTail,
    string StderrTail,
    string StdoutPath,
    string StderrPath);

public sealed record TalvoraDevServerListItem(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    DateTime StartedAtUtc,
    string? TcpHost,
    int? TcpPort,
    string? HttpUrl,
    bool RequireAll);

public sealed record TalvoraDevServerListResponse(
    int Count,
    IReadOnlyList<TalvoraDevServerListItem> Servers);

public sealed record TalvoraDevServerStopResponse(
    string JobId,
    int ProcessId,
    bool Found,
    bool KillIssued,
    bool Exited,
    bool Deleted,
    string State,
    int? ExitCode);

internal sealed record TalvoraDevServerMetadata(
    string JobId,
    string? TcpHost,
    int? TcpPort,
    string? HttpUrl,
    string HttpMethod,
    Dictionary<string, string?> HttpHeaders,
    int[] ExpectedStatusCodes,
    bool RequireAll,
    bool IgnoreTlsErrors,
    int ProbeTimeoutSeconds,
    int PollIntervalMilliseconds,
    DateTime CreatedAtUtc);

[McpServerToolType]
public static class DevServerTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HttpClient NormalHttpClient =
        CreateHttpClient(ignoreTlsErrors: false);

    private static readonly HttpClient InsecureHttpClient =
        CreateHttpClient(ignoreTlsErrors: true);

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

    private static async Task<(
        TalvoraDevServerProbeResult Tcp,
        TalvoraDevServerProbeResult Http)> ProbeAsync(
            TalvoraDevServerMetadata metadata,
            CancellationToken cancellationToken)
    {
        var tcpTask =
            ProbeTcpAsync(
                metadata,
                cancellationToken);
        var httpTask =
            ProbeHttpAsync(
                metadata,
                cancellationToken);

        await Task.WhenAll(tcpTask, httpTask);
        return (
            await tcpTask,
            await httpTask);
    }

    private static async Task<TalvoraDevServerProbeResult>
        ProbeTcpAsync(
            TalvoraDevServerMetadata metadata,
            CancellationToken cancellationToken)
    {
        if (!metadata.TcpPort.HasValue)
        {
            return new TalvoraDevServerProbeResult(
                "tcp",
                false,
                false,
                null,
                null,
                0);
        }

        var stopwatch = Stopwatch.StartNew();

        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        if (metadata.ProbeTimeoutSeconds > 0)
        {
            timeout.CancelAfter(
                TimeSpan.FromSeconds(
                    metadata.ProbeTimeoutSeconds));
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(
                metadata.TcpHost!,
                metadata.TcpPort.Value,
                timeout.Token);

            stopwatch.Stop();

            return new TalvoraDevServerProbeResult(
                "tcp",
                true,
                true,
                $"{metadata.TcpHost}:{metadata.TcpPort.Value}",
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "tcp",
                true,
                false,
                "probe-timeout",
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "tcp",
                true,
                false,
                ex.SocketErrorCode.ToString(),
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "tcp",
                true,
                false,
                ex.Message,
                null,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<TalvoraDevServerProbeResult>
        ProbeHttpAsync(
            TalvoraDevServerMetadata metadata,
            CancellationToken cancellationToken)
    {
        if (metadata.HttpUrl is null)
        {
            return new TalvoraDevServerProbeResult(
                "http",
                false,
                false,
                null,
                null,
                0);
        }

        var stopwatch = Stopwatch.StartNew();

        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        if (metadata.ProbeTimeoutSeconds > 0)
        {
            timeout.CancelAfter(
                TimeSpan.FromSeconds(
                    metadata.ProbeTimeoutSeconds));
        }

        try
        {
            using var request =
                new HttpRequestMessage(
                    new HttpMethod(metadata.HttpMethod),
                    metadata.HttpUrl);

            foreach (var pair in metadata.HttpHeaders)
            {
                if (pair.Value is not null)
                {
                    request.Headers.TryAddWithoutValidation(
                        pair.Key,
                        pair.Value);
                }
            }

            var client = metadata.IgnoreTlsErrors
                ? InsecureHttpClient
                : NormalHttpClient;

            using var response =
                await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

            stopwatch.Stop();

            var statusCode =
                (int)response.StatusCode;
            var ready =
                metadata.ExpectedStatusCodes.Length == 0 ||
                metadata.ExpectedStatusCodes.Contains(
                    statusCode);

            return new TalvoraDevServerProbeResult(
                "http",
                true,
                ready,
                $"{statusCode} {response.ReasonPhrase}".Trim(),
                statusCode,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "http",
                true,
                false,
                "probe-timeout",
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "http",
                true,
                false,
                ex.Message,
                ex.StatusCode.HasValue
                    ? (int)ex.StatusCode.Value
                    : null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TalvoraDevServerProbeResult(
                "http",
                true,
                false,
                ex.Message,
                null,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static TalvoraDevServerProbeResult NotRunningProbe(
        string kind,
        bool configured) =>
        new(
            kind,
            configured,
            false,
            configured
                ? "job-not-running"
                : null,
            null,
            0);

    private static async Task<string> ReadTailAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ValidateLogTailBytes(maxBytes);

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        var length = stream.Length;
        var start =
            maxBytes == 0
                ? 0
                : Math.Max(
                    0,
                    length - maxBytes);

        stream.Position = start;

        var requested =
            checked(
                (int)Math.Min(
                    int.MaxValue,
                    length - start));

        var buffer = new byte[requested];
        var read = 0;

        while (read < requested)
        {
            var current =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        read,
                        requested - read),
                    cancellationToken);
            if (current == 0)
            {
                break;
            }

            read += current;
        }

        return Encoding.UTF8.GetString(
            buffer,
            0,
            read);
    }

    private static void ValidateProbeConfiguration(
        string? tcpHost,
        int? tcpPort,
        string? httpUrl,
        string httpMethod,
        int[]? expectedStatusCodes,
        int timeoutSeconds,
        int probeTimeoutSeconds,
        int pollIntervalMilliseconds,
        int logTailBytes)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }

        if (probeTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probeTimeoutSeconds));
        }

        if (pollIntervalMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollIntervalMilliseconds));
        }

        ValidateLogTailBytes(logTailBytes);

        if (tcpPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tcpPort));
        }

        if (!tcpPort.HasValue &&
            !string.IsNullOrWhiteSpace(tcpHost))
        {
            throw new ArgumentException(
                "tcpPort is required when tcpHost is specified.");
        }

        if (!string.IsNullOrWhiteSpace(httpUrl))
        {
            var uri = new Uri(
                httpUrl,
                UriKind.Absolute);

            if (uri.Scheme is not "http" and not "https")
            {
                throw new ArgumentException(
                    "httpUrl must use http or https.",
                    nameof(httpUrl));
            }

            if (string.IsNullOrWhiteSpace(httpMethod))
            {
                throw new ArgumentException(
                    "httpMethod cannot be empty.",
                    nameof(httpMethod));
            }
        }

        foreach (var statusCode in
                 expectedStatusCodes ?? [])
        {
            if (statusCode is < 100 or > 599)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedStatusCodes),
                    "HTTP status codes must be between 100 and 599.");
            }
        }
    }

    private static void ValidateLogTailBytes(
        int logTailBytes)
    {
        if (logTailBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logTailBytes));
        }
    }

    private static HttpClient CreateHttpClient(
        bool ignoreTlsErrors)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
        };

        if (ignoreTlsErrors)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler
                    .DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static string GetMetadataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "DevServers");

    private static string GetMetadataPath(
        string jobId)
    {
        if (!Guid.TryParseExact(
                jobId,
                "N",
                out _))
        {
            throw new ArgumentException(
                "Invalid Talvora job ID.",
                nameof(jobId));
        }

        return Path.Combine(
            GetMetadataRoot(),
            jobId + ".json");
    }

    private static async Task WriteMetadataAsync(
        TalvoraDevServerMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path =
            GetMetadataPath(
                metadata.JobId);
        var directory =
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Dev-server metadata directory could not be resolved.");

        Directory.CreateDirectory(directory);

        var json =
            JsonSerializer.Serialize(
                metadata,
                JsonOptions);

        var tempPath =
            path + "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        await File.WriteAllTextAsync(
            tempPath,
            json,
            new UTF8Encoding(false),
            cancellationToken);

        File.Move(
            tempPath,
            path,
            overwrite: true);
    }

    private static async Task<TalvoraDevServerMetadata>
        ReadMetadataAsync(
            string jobId,
            CancellationToken cancellationToken)
    {
        var path =
            GetMetadataPath(jobId);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Talvora dev-server definition was not found.",
                path);
        }

        return await ReadMetadataFileAsync(
            path,
            cancellationToken);
    }

    private static async Task<TalvoraDevServerMetadata>
        ReadMetadataFileAsync(
            string path,
            CancellationToken cancellationToken)
    {
        var json =
            await File.ReadAllTextAsync(
                path,
                cancellationToken);

        return JsonSerializer.Deserialize<
                   TalvoraDevServerMetadata>(
                   json,
                   JsonOptions)
               ?? throw new JsonException(
                   $"Invalid Talvora dev-server metadata: {path}");
    }

    private static void TryDeleteMetadata(
        string jobId)
    {
        try
        {
            var path = GetMetadataPath(jobId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
