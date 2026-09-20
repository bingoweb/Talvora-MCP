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
    IReadOnlyList<TalvoraDevServerListItem> Servers,
    long ResultOffset = 0,
    bool Truncated = false,
    long? NextResultOffset = null);

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
public static partial class DevServerTools
{
    internal const int AbsoluteDevServerListResults = 10_000;
}
