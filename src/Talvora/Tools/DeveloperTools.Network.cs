using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_tcp_connections",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpConnectionResponse)),
     Description("Return structured Windows TCP connections with local/remote endpoints, state, owning PID, and process name. Optional filters can target a port, PID, state, or text query.")]
    public static async Task<TalvoraTcpConnectionResponse> TcpConnections(
        int? localPort = null,
        int? processId = null,
        string? state = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadNetstatTcpAsync(cancellationToken);
        var filtered = FilterConnections(all, localPort, processId, state, query).ToArray();
        return new TalvoraTcpConnectionResponse(filtered.Length, filtered);
    }

    [McpServerTool(
        Name = "talvora_tcp_listeners",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpConnectionResponse)),
     Description("Return structured Windows TCP listeners with local endpoint, owning PID, and process name. Optional filters can target a local port, PID, or text query.")]
    public static async Task<TalvoraTcpConnectionResponse> TcpListeners(
        int? localPort = null,
        int? processId = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadNetstatTcpAsync(cancellationToken);
        var filtered = FilterConnections(all, localPort, processId, "LISTENING", query).ToArray();
        return new TalvoraTcpConnectionResponse(filtered.Length, filtered);
    }

    [McpServerTool(
        Name = "talvora_wait_tcp",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWaitTcpResponse)),
     Description("Wait until a TCP host:port becomes reachable. timeoutSeconds=0 waits without an overall timeout; attemptTimeoutMilliseconds=0 allows each connect attempt to use the overall cancellation only.")]
    public static async Task<TalvoraWaitTcpResponse> WaitTcp(
        string host,
        int port,
        int timeoutSeconds = 30,
        int pollIntervalMilliseconds = 250,
        int attemptTimeoutMilliseconds = 1500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        if (timeoutSeconds < 0 || pollIntervalMilliseconds < 0 || attemptTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException("Timeout values cannot be negative.");
        }

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            overall.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        string? lastError = null;

        while (!overall.IsCancellationRequested)
        {
            attempts++;
            try
            {
                using var client = new TcpClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                if (attemptTimeoutMilliseconds > 0)
                {
                    attempt.CancelAfter(TimeSpan.FromMilliseconds(attemptTimeoutMilliseconds));
                }

                await client.ConnectAsync(host, port, attempt.Token);
                stopwatch.Stop();
                return new TalvoraWaitTcpResponse(
                    host,
                    port,
                    true,
                    attempts,
                    stopwatch.ElapsedMilliseconds,
                    null);
            }
            catch (Exception ex) when (
                ex is SocketException or OperationCanceledException or IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                lastError = ex.Message;
            }

            if (overall.IsCancellationRequested)
            {
                break;
            }

            if (pollIntervalMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(pollIntervalMilliseconds, overall.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        return new TalvoraWaitTcpResponse(
            host,
            port,
            false,
            attempts,
            stopwatch.ElapsedMilliseconds,
            lastError);
    }

    private static IEnumerable<TalvoraTcpConnectionEntry> FilterConnections(
        IEnumerable<TalvoraTcpConnectionEntry> source,
        int? localPort,
        int? processId,
        string? state,
        string? query)
    {
        foreach (var item in source)
        {
            if (localPort is int requestedPort && item.LocalPort != requestedPort)
            {
                continue;
            }
            if (processId is int requestedPid && item.ProcessId != requestedPid)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(state) &&
                !string.Equals(item.State, state, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                var haystack = $"{item.LocalAddress}:{item.LocalPort} {item.RemoteAddress}:{item.RemotePort} {item.State} {item.ProcessId} {item.ProcessName}";
                if (!haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            yield return item;
        }
    }

    private static async Task<IReadOnlyList<TalvoraTcpConnectionEntry>> ReadNetstatTcpAsync(
        CancellationToken cancellationToken)
    {
        var netstat = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "netstat.exe");

        var processResult = await ProcessRunner.RunAsync(
            netstat,
            Environment.CurrentDirectory,
            ["-ano", "-p", "tcp"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"netstat.exe failed with exit code {processResult.ExitCode}: {processResult.StandardError}");
        }

        var result = new List<TalvoraTcpConnectionEntry>();
        foreach (var rawLine in processResult.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 5)
            {
                continue;
            }

            if (!TryParseEndpoint(parts[1], out var localAddress, out var localPort) ||
                !TryParseEndpoint(parts[2], out var remoteAddress, out var remotePort) ||
                !int.TryParse(parts[^1], out var pid))
            {
                continue;
            }

            var connectionState = parts.Length >= 5 ? parts[3] : string.Empty;
            string? processName = null;
            try
            {
                using var owner = Process.GetProcessById(pid);
                processName = owner.ProcessName;
            }
            catch
            {
            }

            result.Add(new TalvoraTcpConnectionEntry(
                "TCP",
                localAddress,
                localPort,
                remoteAddress,
                remotePort,
                connectionState,
                pid,
                processName));
        }

        return result;
    }

    private static bool TryParseEndpoint(
        string value,
        out string address,
        out int port)
    {
        address = string.Empty;
        port = 0;

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var bracket = value.LastIndexOf(']');
            if (bracket < 0 || bracket + 2 > value.Length)
            {
                return false;
            }

            address = value[1..bracket];
            var portText = value[(bracket + 2)..];
            return portText == "*" || int.TryParse(portText, out port);
        }

        var separator = value.LastIndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        address = value[..separator];
        var text = value[(separator + 1)..];
        return text == "*" || int.TryParse(text, out port);
    }
}
