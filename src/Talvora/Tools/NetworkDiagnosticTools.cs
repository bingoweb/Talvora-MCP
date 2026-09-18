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

public sealed record TalvoraNetworkInterfaceEntry(
    string Id,
    string Name,
    string Description,
    string InterfaceType,
    string OperationalStatus,
    long Speed,
    string? DnsSuffix,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    IReadOnlyList<string> DhcpServers);

public sealed record TalvoraNetworkInterfacesResponse(
    int Count,
    IReadOnlyList<TalvoraNetworkInterfaceEntry> Interfaces);

public sealed record TalvoraDnsLookupResponse(
    string Query,
    string HostName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Addresses);

public sealed record TalvoraPingResponse(
    string Host,
    string Status,
    string? Address,
    long RoundtripTimeMilliseconds,
    int ReplyBytes,
    int? TimeToLive,
    bool? DontFragment);

public sealed record TalvoraTcpExchangeResponse(
    string Host,
    int Port,
    string LocalEndpoint,
    string RemoteEndpoint,
    int BytesSent,
    long BytesReceived,
    bool ResponseTruncated,
    string ResponseMode,
    string Response,
    long ElapsedMilliseconds);

public sealed record TalvoraTlsCertificateInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    string SerialNumber,
    DateTime NotBefore,
    DateTime NotAfter,
    string? DnsName,
    string SignatureAlgorithm,
    string PublicKeyAlgorithm,
    int PublicKeySize);

public sealed record TalvoraTlsChainElement(
    string Subject,
    string Issuer,
    string Thumbprint,
    string Status);

public sealed record TalvoraTlsInspectResponse(
    string Host,
    int Port,
    string ServerName,
    string SslProtocol,
    string CipherSuite,
    string? NegotiatedApplicationProtocol,
    string PolicyErrors,
    bool ChainValid,
    IReadOnlyList<TalvoraTlsChainElement> Chain,
    TalvoraTlsCertificateInfo Certificate,
    long ElapsedMilliseconds);

public sealed record TalvoraWebSocketMessage(
    string MessageType,
    bool EndOfMessage,
    long Bytes,
    bool Truncated,
    string Data);

public sealed record TalvoraWebSocketExchangeResponse(
    string Url,
    string State,
    string? SubProtocol,
    int BytesSent,
    int MessagesReceived,
    IReadOnlyList<TalvoraWebSocketMessage> Messages,
    long ElapsedMilliseconds);

[McpServerToolType]
public static class NetworkDiagnosticTools
{
    [McpServerTool(
        Name = "talvora_network_interfaces",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraNetworkInterfacesResponse)),
     Description("Return structured local network-interface information including addresses, gateways, DNS servers, DHCP servers, status, type, and link speed.")]
    public static TalvoraNetworkInterfacesResponse NetworkInterfaces()
    {
        var entries = new List<TalvoraNetworkInterfaceEntry>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var properties = networkInterface.GetIPProperties();

                entries.Add(new TalvoraNetworkInterfaceEntry(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.NetworkInterfaceType.ToString(),
                    networkInterface.OperationalStatus.ToString(),
                    networkInterface.Speed,
                    string.IsNullOrWhiteSpace(properties.DnsSuffix) ? null : properties.DnsSuffix,
                    properties.UnicastAddresses
                        .Select(address => address.Address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.GatewayAddresses
                        .Select(gateway => gateway.Address.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.DnsAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.DhcpServerAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()));
            }
            catch (NetworkInformationException)
            {
                entries.Add(new TalvoraNetworkInterfaceEntry(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.NetworkInterfaceType.ToString(),
                    networkInterface.OperationalStatus.ToString(),
                    networkInterface.Speed,
                    null,
                    [],
                    [],
                    [],
                    []));
            }
        }

        return new TalvoraNetworkInterfacesResponse(entries.Count, entries);
    }

    [McpServerTool(
        Name = "talvora_dns_lookup",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDnsLookupResponse)),
     Description("Resolve any host name or IP address using the Windows/.NET DNS stack and return canonical host name, aliases, and addresses.")]
    public static async Task<TalvoraDnsLookupResponse> DnsLookup(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }

        var result = await Dns.GetHostEntryAsync(host, cancellationToken);

        return new TalvoraDnsLookupResponse(
            host,
            result.HostName,
            result.Aliases,
            result.AddressList.Select(address => address.ToString()).ToArray());
    }

    [McpServerTool(
        Name = "talvora_ping",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPingResponse)),
     Description("Send an ICMP echo request to any host with caller-controlled timeout, TTL, Don't Fragment flag, and payload size.")]
    public static async Task<TalvoraPingResponse> PingHost(
        string host,
        int timeoutMilliseconds = 4000,
        int ttl = 128,
        bool dontFragment = false,
        int payloadBytes = 32,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }
        if (timeoutMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }
        if (ttl is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }
        if (payloadBytes is < 0 or > 65500)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var ping = new Ping();
        var buffer = new byte[payloadBytes];
        var options = new PingOptions(ttl, dontFragment);
        var reply = await ping.SendPingAsync(host, timeoutMilliseconds, buffer, options)
            .WaitAsync(cancellationToken);

        return new TalvoraPingResponse(
            host,
            reply.Status.ToString(),
            reply.Address?.ToString(),
            reply.RoundtripTime,
            reply.Buffer?.Length ?? 0,
            reply.Options?.Ttl,
            reply.Options?.DontFragment);
    }

    [McpServerTool(
        Name = "talvora_tcp_exchange",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpExchangeResponse)),
     Description("Open a raw TCP connection to any host:port, send text or base64 bytes, optionally half-close the send side, and capture the response as text or base64. maxResponseBytes=0 means unlimited.")]
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

            if (maxResponseBytes == 0)
            {
                memory.Write(buffer, 0, read);
                continue;
            }

            var remaining = maxResponseBytes - memory.Length;
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
            responseMode,
            response,
            stopwatch.ElapsedMilliseconds);
    }

    [McpServerTool(
        Name = "talvora_tls_inspect",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTlsInspectResponse)),
     Description("Perform a TLS client handshake to any host:port and return negotiated protocol/cipher/ALPN plus remote certificate and chain details. allowInvalidCertificate=true keeps the diagnostic handshake going while still reporting policy errors.")]
    public static async Task<TalvoraTlsInspectResponse> TlsInspect(
        string host,
        int port = 443,
        string? serverName = null,
        string[]? applicationProtocols = null,
        bool allowInvalidCertificate = false,
        int timeoutSeconds = 30,
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
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var effectiveServerName = string.IsNullOrWhiteSpace(serverName)
            ? host
            : serverName;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        SslPolicyErrors policyErrors = SslPolicyErrors.None;

        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(host, port, timeout.Token);

        await using var network = client.GetStream();
        using var ssl = new SslStream(
            network,
            leaveInnerStreamOpen: false,
            (_, _, _, errors) =>
            {
                policyErrors = errors;
                return allowInvalidCertificate || errors == SslPolicyErrors.None;
            });

        var authenticationOptions = new SslClientAuthenticationOptions
        {
            TargetHost = effectiveServerName,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        if (applicationProtocols is { Length: > 0 })
        {
            authenticationOptions.ApplicationProtocols = applicationProtocols
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new SslApplicationProtocol(value))
                .ToList();
        }

        await ssl.AuthenticateAsClientAsync(authenticationOptions, timeout.Token);
        stopwatch.Stop();

        if (ssl.RemoteCertificate is null)
        {
            throw new AuthenticationException("TLS peer did not present a certificate.");
        }

        var certificateBytes = ssl.RemoteCertificate.Export(X509ContentType.Cert);
        using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        var chainValid = chain.Build(certificate);

        var chainElements = chain.ChainElements
            .Cast<X509ChainElement>()
            .Select(element => new TalvoraTlsChainElement(
                element.Certificate.Subject,
                element.Certificate.Issuer,
                element.Certificate.Thumbprint ?? string.Empty,
                string.Join(
                    "; ",
                    element.ChainElementStatus.Select(status =>
                        $"{status.Status}: {status.StatusInformation.Trim()}"))))
            .ToArray();

        var publicKeySize = 0;
        try
        {
            using var key = certificate.GetRSAPublicKey();
            publicKeySize = key?.KeySize ?? 0;
        }
        catch
        {
            try
            {
                using var key = certificate.GetECDsaPublicKey();
                publicKeySize = key?.KeySize ?? 0;
            }
            catch
            {
            }
        }

        var certInfo = new TalvoraTlsCertificateInfo(
            certificate.Subject,
            certificate.Issuer,
            certificate.Thumbprint ?? string.Empty,
            certificate.SerialNumber,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
            certificate.SignatureAlgorithm?.FriendlyName ?? certificate.SignatureAlgorithm?.Value ?? string.Empty,
            certificate.PublicKey.Oid?.FriendlyName ?? certificate.PublicKey.Oid?.Value ?? string.Empty,
            publicKeySize);

        var alpn = ssl.NegotiatedApplicationProtocol.Protocol.IsEmpty
            ? null
            : Encoding.ASCII.GetString(ssl.NegotiatedApplicationProtocol.Protocol.Span);

        return new TalvoraTlsInspectResponse(
            host,
            port,
            effectiveServerName,
            ssl.SslProtocol.ToString(),
            ssl.NegotiatedCipherSuite.ToString(),
            alpn,
            policyErrors.ToString(),
            chainValid,
            chainElements,
            certInfo,
            stopwatch.ElapsedMilliseconds);
    }

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
