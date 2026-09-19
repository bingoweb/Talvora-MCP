using System.Collections.Concurrent;
using System.ComponentModel;
using Talvora.Shared;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraJobStartResponse(
    string JobId,
    int ProcessId,
    string State,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

public sealed record TalvoraJobInfoResponse(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    DateTime? ExitedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

public sealed record TalvoraJobListResponse(
    int Count,
    IReadOnlyList<TalvoraJobInfoResponse> Jobs);

public sealed record TalvoraJobOutputResponse(
    string JobId,
    string Stream,
    long Offset,
    long NextOffset,
    long Length,
    bool EndOfStream,
    string Text);

public sealed record TalvoraJobStdinResponse(
    string JobId,
    int ProcessId,
    int CharactersWritten,
    bool NewLineAppended);

public sealed record TalvoraJobStopResponse(
    string JobId,
    int ProcessId,
    bool Found,
    bool KillIssued,
    bool Exited,
    string State,
    int? ExitCode);

public sealed record TalvoraJobDeleteResponse(
    string JobId,
    bool Found,
    bool Stopped,
    bool Deleted);

internal sealed record TalvoraJobMetadata(
    string JobId,
    int ProcessId,
    string State,
    int? ExitCode,
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    DateTime StartedAtUtc,
    DateTime? ExitedAtUtc,
    string StdoutPath,
    string StderrPath,
    string MetadataPath);

internal sealed class TalvoraJobRuntime : IDisposable
{
    public required Process Process { get; init; }
    public required StreamWriter StandardInput { get; init; }
    public required TalvoraJobMetadata Metadata { get; init; }
    public required Task StdoutPump { get; init; }
    public required Task StderrPump { get; init; }

    public void Dispose()
    {
        try { StandardInput.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        try { Process.Dispose(); } catch (ObjectDisposedException) { }
    }
}

[McpServerToolType]
public static partial class JobTools
{
    private static readonly ConcurrentDictionary<string, TalvoraJobRuntime> LiveJobs =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
