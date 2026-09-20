using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class NetworkDiagnosticTools
{
[McpServerTool(
        Name = "talvora_tcp_exchange",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpExchangeResponse)),
     Description("Open a raw TCP connection to any host:port, send text or base64 bytes, optionally half-close the send side, and capture the response as text or base64. maxResponseBytes=0 requests the finite server capture maximum. A truncated response cannot be resumed because the exchange connection is disposed after the tool call.")]
    public static async Task<TalvoraTcpExchangeResponse> TcpExchange(
        string host,
        int port,
        string? text = null,
        string? base64 = null,
        string encoding = "utf-8",
        bool shutdownSend = false,
        string responseMode = "text",
        long maxResponseBytes = 2 * 1024 * 1024,
        int timeoutSeconds = 30,
        int idleReadTimeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        if (text is not null && base64 is not null)
        {
            throw new ArgumentException("Provide either text or base64, not both.");
        }
        if (maxResponseBytes < 0 || timeoutSeconds < 0 || idleReadTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException("Response and timeout limits cannot be negative.");
        }

        responseMode = responseMode.Trim().ToLowerInvariant();
        if (responseMode is not ("text" or "base64"))
        {
            throw new ArgumentOutOfRangeException(nameof(responseMode), "responseMode must be text or base64.");
        }

        var effectiveMaxResponseBytes =
            maxResponseBytes == 0
                ? AbsoluteTcpResponseBytes
                : Math.Min(
                    maxResponseBytes,
                    AbsoluteTcpResponseBytes);

        var selectedEncoding = Encoding.GetEncoding(encoding);
        var payload = base64 is not null
            ? Convert.FromBase64String(base64)
            : selectedEncoding.GetBytes(text ?? string.Empty);

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            overall.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(host, port, overall.Token);

        await using var stream = client.GetStream();
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, overall.Token);
            await stream.FlushAsync(overall.Token);
        }

        if (shutdownSend)
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }

        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var truncated = false;

        while (true)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
            if (idleReadTimeoutMilliseconds > 0)
            {
                readTimeout.CancelAfter(TimeSpan.FromMilliseconds(idleReadTimeoutMilliseconds));
            }

            int read;
            try
            {
                read = await stream.ReadAsync(buffer, readTimeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                !overall.IsCancellationRequested &&
                idleReadTimeoutMilliseconds > 0)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            var remaining =
                effectiveMaxResponseBytes -
                memory.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var toWrite = (int)Math.Min(read, remaining);
            memory.Write(buffer, 0, toWrite);

            if (toWrite < read)
            {
                truncated = true;
                break;
            }
        }

        stopwatch.Stop();
        var bytes = memory.ToArray();
        var response = responseMode == "base64"
            ? Convert.ToBase64String(bytes)
            : selectedEncoding.GetString(bytes);

        return new TalvoraTcpExchangeResponse(
            host,
            port,
            client.Client.LocalEndPoint?.ToString() ?? string.Empty,
            client.Client.RemoteEndPoint?.ToString() ?? string.Empty,
            payload.Length,
            bytes.LongLength,
            truncated,
            effectiveMaxResponseBytes,
            false,
            responseMode,
            response,
            stopwatch.ElapsedMilliseconds);
    }
}
