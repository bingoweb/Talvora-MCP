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
private static readonly HttpClient NormalHttpClient =
        TalvoraHttp.CreateClient(ignoreTlsErrors: false);

    private static readonly HttpClient InsecureHttpClient =
        TalvoraHttp.CreateClient(ignoreTlsErrors: true);

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
}
