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
    long CaptureLimitBytes,
    bool ContinuationSupported,
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
    bool ResponseTruncated,
    long MessageLimitBytes,
    long ResponseLimitBytes,
    bool ContinuationSupported,
    long ElapsedMilliseconds);

[McpServerToolType]
public static partial class NetworkDiagnosticTools
{
    internal const long AbsoluteTcpResponseBytes =
        16L * 1024 * 1024;
    internal const long AbsoluteWebSocketMessageBytes =
        16L * 1024 * 1024;
    internal const long AbsoluteWebSocketResponseBytes =
        32L * 1024 * 1024;
    internal const int AbsoluteWebSocketMessages = 256;
}
