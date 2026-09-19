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

public sealed record TalvoraPathInfoResponse(
    string Path,
    bool Exists,
    string Kind,
    long? Length,
    string Attributes,
    DateTime? CreationTimeUtc,
    DateTime? LastWriteTimeUtc,
    DateTime? LastAccessTimeUtc,
    string? LinkTarget);

public sealed record TalvoraFileSearchEntry(
    string Path,
    string Name,
    string Kind,
    long? Length,
    DateTime LastWriteTimeUtc);

public sealed record TalvoraFileSearchResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraFileSearchEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record TalvoraTextSearchMatch(
    string Path,
    int LineNumber,
    int Column,
    string Match,
    string Line);

public sealed record TalvoraTextSearchResponse(
    string Root,
    int FilesScanned,
    int MatchCount,
    bool Truncated,
    IReadOnlyList<TalvoraTextSearchMatch> Matches,
    IReadOnlyList<string> Errors);

public sealed record TalvoraFileHashResponse(
    string Path,
    string Algorithm,
    string Hash,
    long Length);

public sealed record TalvoraReadBytesResponse(
    string Path,
    long Offset,
    int Count,
    long FileLength,
    string Base64);

public sealed record TalvoraWriteBytesResponse(
    string Path,
    int BytesWritten,
    long FileLength,
    bool Appended,
    long? Offset);

public sealed record TalvoraReplaceTextResponse(
    string Path,
    int Matches,
    int Replacements,
    bool Changed,
    string? BackupPath);

public sealed record TalvoraHttpResponse(
    string Method,
    string Url,
    int StatusCode,
    string? ReasonPhrase,
    string Version,
    IReadOnlyDictionary<string, string[]> Headers,
    string ResponseMode,
    string? Body,
    long BodyBytes,
    bool BodyTruncated,
    long ElapsedMilliseconds);

public sealed record TalvoraTcpConnectionEntry(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int ProcessId,
    string? ProcessName);

public sealed record TalvoraTcpConnectionResponse(
    int Count,
    IReadOnlyList<TalvoraTcpConnectionEntry> Connections);

public sealed record TalvoraWaitTcpResponse(
    string Host,
    int Port,
    bool Connected,
    int Attempts,
    long ElapsedMilliseconds,
    string? LastError);

public sealed record TalvoraProjectEntry(
    string Path,
    string Type,
    string Name);

public sealed record TalvoraProjectDiscoverResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraProjectEntry> Projects,
    IReadOnlyList<string> Errors);

public sealed record TalvoraCommandResolveResponse(
    string Command,
    bool Found,
    IReadOnlyList<string> Paths);

[McpServerToolType]
public static partial class DeveloperTools
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
