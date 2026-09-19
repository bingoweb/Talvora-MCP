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
        Name = "talvora_websocket_exchange",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWebSocketExchangeResponse)),
     Description("Connect to any ws/wss endpoint, apply arbitrary request headers and subprotocols, optionally send one text or binary message, receive caller-selected messages, and return text/base64 payloads. maxMessageBytes=0 means unlimited.")]
    public static async Task<TalvoraWebSocketExchangeResponse> WebSocketExchange(
        string url,
        Dictionary<string, string>? headers = null,
        string[]? subProtocols = null,
        string? text = null,
        string? base64 = null,
        int receiveMessages = 1,
        long maxMessageBytes = 2 * 1024 * 1024,
        int timeoutSeconds = 30,
        bool closeAfter = true,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException("A valid ws:// or wss:// URL is required.", nameof(url));
        }
        if (text is not null && base64 is not null)
        {
            throw new ArgumentException("Provide either text or base64, not both.");
        }
        if (receiveMessages < 0 || maxMessageBytes < 0 || timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException("receiveMessages, maxMessageBytes, and timeoutSeconds cannot be negative.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var socket = new ClientWebSocket();

        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            socket.Options.SetRequestHeader(pair.Key, pair.Value);
        }

        foreach (var protocol in subProtocols ?? [])
        {
            if (!string.IsNullOrWhiteSpace(protocol))
            {
                socket.Options.AddSubProtocol(protocol);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        await socket.ConnectAsync(uri, timeout.Token);

        var bytesSent = 0;
        if (text is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            bytesSent = bytes.Length;
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                timeout.Token);
        }
        else if (base64 is not null)
        {
            var bytes = Convert.FromBase64String(base64);
            bytesSent = bytes.Length;
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                timeout.Token);
        }

        var messages = new List<TalvoraWebSocketMessage>();
        var buffer = new byte[64 * 1024];

        for (var messageIndex = 0;
             messageIndex < receiveMessages && socket.State == WebSocketState.Open;
             messageIndex++)
        {
            using var memory = new MemoryStream();
            var truncated = false;
            WebSocketMessageType messageType = WebSocketMessageType.Binary;
            var endOfMessage = false;

            do
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    timeout.Token);

                messageType = result.MessageType;
                endOfMessage = result.EndOfMessage;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    messages.Add(new TalvoraWebSocketMessage(
                        "Close",
                        true,
                        0,
                        false,
                        socket.CloseStatusDescription ?? string.Empty));
                    break;
                }

                if (maxMessageBytes == 0)
                {
                    memory.Write(buffer, 0, result.Count);
                }
                else
                {
                    var remaining = maxMessageBytes - memory.Length;
                    if (remaining > 0)
                    {
                        var toWrite = (int)Math.Min(result.Count, remaining);
                        memory.Write(buffer, 0, toWrite);
                        if (toWrite < result.Count)
                        {
                            truncated = true;
                        }
                    }
                    else
                    {
                        truncated = true;
                    }
                }
            }
            while (!endOfMessage);

            if (messageType == WebSocketMessageType.Close)
            {
                break;
            }

            var bytes = memory.ToArray();
            messages.Add(new TalvoraWebSocketMessage(
                messageType.ToString(),
                endOfMessage,
                bytes.LongLength,
                truncated,
                messageType == WebSocketMessageType.Text
                    ? Encoding.UTF8.GetString(bytes)
                    : Convert.ToBase64String(bytes)));
        }

        if (closeAfter && socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Talvora exchange complete",
                    timeout.Token);
            }
            catch
            {
                socket.Abort();
            }
        }

        stopwatch.Stop();

        return new TalvoraWebSocketExchangeResponse(
            uri.ToString(),
            socket.State.ToString(),
            socket.SubProtocol,
            bytesSent,
            messages.Count,
            messages,
            stopwatch.ElapsedMilliseconds);
    }
}
