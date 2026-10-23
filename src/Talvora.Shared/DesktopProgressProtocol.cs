using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talvora.Shared;

public enum DesktopProgressKind
{
    Started,
    Running,
    Completed,
    Failed,
    Cancelled,
    Info,
    Warning,
}

public enum DesktopProgressLane
{
    Worklog,
    Alert,
}

public enum WorklogStepCategory
{
    SourceEdit,
    Verification,
    PackageBuild,
    Deployment,
    VersionControl,
    ServiceOperation,
    PackageOperation,
    FileMutation,
    NetworkOperation,
    ProcessOperation,
    Inspection,
    GenericExecution,
}

public enum WorklogStepState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Waiting,
}

public enum WorklogFailureKind
{
    InvalidArguments,
    StaleRevision,
    NotFound,
    AccessDenied,
    BuildErrors,
    TestFailures,
    Timeout,
    DeliveryGate,
    Other,
}

public enum WorklogCheckState
{
    Unknown,
    Running,
    Succeeded,
    Failed,
}

public sealed record DesktopWorklogFrame(
    string SessionId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    WorklogSessionHeader Header,
    WorklogStep Current,
    IReadOnlyList<WorklogTimelineEntry> Timeline,
    WorklogTotals Totals,
    DesktopProgressEvidence? Evidence,
    int ExpectedNextUpdateSeconds,
    DesktopProgressLane Lane = DesktopProgressLane.Worklog);

/// <summary>TaskNote: the agent's one-line task for this session (talvora_worklog_note, §6.2).</summary>
public sealed record WorklogSessionHeader(string? ProjectName, string ActorName, string? TaskNote = null);

/// <summary>
/// Purpose: the agent's own words for what this step changes (apply_patch "purpose", §6.2).
/// Changes: real per-file counts and changed places of a committed source edit (§6.2).
/// </summary>
public sealed record WorklogStep(
    WorklogStepCategory Category,
    WorklogStepState State,
    string Title,
    string? FactLine,
    WorklogOutcome? Outcome,
    double ElapsedSeconds,
    string? Purpose = null,
    IReadOnlyList<WorklogFileChange>? Changes = null);

/// <summary>One changed file: name, real added/removed line counts, declarations the hunks touched.</summary>
public sealed record WorklogFileChange(string File, int Added, int Removed, IReadOnlyList<string> Places);

public sealed record WorklogOutcome(
    WorklogFailureKind? FailureKind, string PlainReason, string? NextStep);

public sealed record WorklogTimelineEntry(
    DateTimeOffset TimestampUtc, WorklogStepState State, string Label);

public sealed record WorklogTotals(
    int Steps, double SessionSeconds, int FilesChanged,
    WorklogCheckState LastBuild, WorklogCheckState LastTests);

public sealed record DesktopProgressMessage(
    string OperationId,
    string ToolName,
    string Title,
    string Message,
    DesktopProgressKind Kind,
    DateTimeOffset TimestampUtc,
    double ElapsedSeconds,
    DesktopProgressEvidence? Evidence = null,
    DesktopProgressLane Lane = DesktopProgressLane.Worklog,
    long Sequence = 0,
    DesktopWorklogFrame? WorklogFrame = null,
    bool SessionClosed = false);

public sealed record DesktopProgressEvidence(
    string? Summary = null,
    IReadOnlyList<string>? Files = null,
    int? AddedLines = null,
    int? RemovedLines = null,
    string? CodePreview = null,
    string? Result = null);

public static class DesktopProgressProtocol
{
    public const int Version = 4;
    public const string PipeNamePrefix = "Talvora.DesktopProgress.";
    public const int MaximumFrameBytes = 4 * 1024 * 1024;
    public const int MaximumOperationIdCharacters = 160;
    public const int MaximumToolNameCharacters = 256;
    public const int MaximumTitleCharacters = 512;
    public const int MaximumMessageCharacters = 12 * 1024;
    public const int MaximumEvidenceFiles = 32;
    public const int MaximumEvidenceFileCharacters = 2 * 1024;
    public const int MaximumEvidenceTextCharacters = 8 * 1024;
    public const int MaximumCodeCharacters = 512 * 1024;
    public const int MaximumSessionIdCharacters = 160;
    public const int MaximumProjectNameCharacters = 180;
    public const int MaximumActorNameCharacters = 80;
    public const int MaximumTimelineEntries = 30;
    public const int MaximumTaskNoteCharacters = 140;
    public const int MaximumPurposeCharacters = 140;
    public const int MaximumChangedFiles = 12;
    public const int MaximumPlacesPerFile = 4;
    public const int MaximumPlaceCharacters = 80;

    private const int FrameHeaderBytes = sizeof(int);

    private sealed record DesktopProgressEnvelope(
        int Version,
        DesktopProgressMessage Message);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static string GetPipeName(int sessionId) =>
        $"{PipeNamePrefix}{sessionId}";

    public static string Serialize(DesktopProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Encoding.UTF8.GetString(
            SerializeToBoundedPayload(message));
    }

    public static bool TryDeserialize(
        string json,
        out DesktopProgressMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var envelope =
                JsonSerializer.Deserialize<DesktopProgressEnvelope>(
                    json,
                    SerializerOptions);
            if (envelope is null ||
                envelope.Version != Version ||
                !IsValidMessage(envelope.Message))
            {
                return false;
            }

            message = envelope.Message;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static async ValueTask WriteFrameAsync(
        Stream stream,
        DesktopProgressMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = SerializeToBoundedPayload(message);

        var header = new byte[FrameHeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(
            header,
            payload.Length);
        await stream.WriteAsync(
                header.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(
                payload.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<DesktopProgressMessage?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[FrameHeaderBytes];
        var headerBytes = await ReadExactlyOrEndAsync(
                stream,
                header,
                cancellationToken)
            .ConfigureAwait(false);
        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != FrameHeaderBytes)
        {
            throw new InvalidDataException(
                "Desktop progress frame header was truncated.");
        }

        var payloadLength =
            BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength <= 0 ||
            payloadLength > MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"Desktop progress frame length is invalid: {payloadLength}.");
        }

        var payload = new byte[payloadLength];
        var payloadBytes = await ReadExactlyOrEndAsync(
                stream,
                payload,
                cancellationToken)
            .ConfigureAwait(false);
        if (payloadBytes != payloadLength)
        {
            throw new InvalidDataException(
                "Desktop progress frame payload was truncated.");
        }

        var json = Encoding.UTF8.GetString(payload);
        if (!TryDeserialize(json, out var message) ||
            message is null)
        {
            throw new InvalidDataException(
                "Desktop progress frame failed protocol validation.");
        }

        return message;
    }

    private static async ValueTask<int> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                    buffer[total..],
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            total += read;
        }

        return total;
    }

    public static async Task AssertContractAsync(
        CancellationToken cancellationToken = default)
    {
        var original = new DesktopProgressMessage(
            "protocol-self-test",
            "self-test",
            "Protocol",
            "Round trip",
            DesktopProgressKind.Running,
            DateTimeOffset.UtcNow,
            ElapsedSeconds: 1.25,
            new DesktopProgressEvidence(
                Summary: "evidence",
                Files: ["a.cs"],
                Result: "ok"),
            DesktopProgressLane.Worklog,
            Sequence: 42);

        await using var roundTrip = new MemoryStream();
        await WriteFrameAsync(
            roundTrip,
            original,
            cancellationToken);
        roundTrip.Position = 0;
        var restored = await ReadFrameAsync(
            roundTrip,
            cancellationToken);
        if (restored is null ||
            restored.OperationId != original.OperationId ||
            restored.Sequence != original.Sequence ||
            restored.Kind != original.Kind ||
            restored.Lane != original.Lane)
        {
            throw new InvalidOperationException(
                "Desktop progress framed protocol round-trip failed.");
        }

        var structured = original with
        {
            WorklogFrame = new DesktopWorklogFrame(
                "session-v4", 42, DateTimeOffset.UtcNow,
                new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                new WorklogStep(WorklogStepCategory.SourceEdit,
                    WorklogStepState.Running, "Kod güncelleniyor", null, null, 1.25),
                [new WorklogTimelineEntry(DateTimeOffset.UtcNow,
                    WorklogStepState.Succeeded, "Dosya incelendi")],
                new WorklogTotals(1, 1.25, 0,
                    WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                null, 15),
        };
        if (!TryDeserialize(Serialize(structured), out var parsedV4) ||
            parsedV4?.WorklogFrame?.SessionId != "session-v4" ||
            parsedV4.WorklogFrame.Timeline.Count != 1 ||
            parsedV4.WorklogFrame.ExpectedNextUpdateSeconds != 15)
        {
            throw new InvalidOperationException(
                "Desktop progress v4 structured worklog round-trip failed.");
        }
        var oldVersion = Serialize(structured).Replace(
            $"\"version\":{Version}", "\"version\":3",
            StringComparison.Ordinal);
        if (TryDeserialize(oldVersion, out _))
        {
            throw new InvalidOperationException(
                "Desktop progress v4 accepted a v3 wire frame.");
        }
        var clippedV4 = structured with
        {
            WorklogFrame = structured.WorklogFrame with
            {
                Header = new WorklogSessionHeader(
                    new string('x', MaximumProjectNameCharacters + 20), "Claude"),
                Timeline = Enumerable.Range(0, 40).Select(_ =>
                    new WorklogTimelineEntry(DateTimeOffset.UtcNow,
                        WorklogStepState.Running, "İş sürüyor")).ToArray(),
            },
        };
        if (!TryDeserialize(Serialize(clippedV4), out var clippedStructured) ||
            clippedStructured?.WorklogFrame?.Header.ProjectName?.Length >
                MaximumProjectNameCharacters ||
            clippedStructured?.WorklogFrame?.Timeline.Count > MaximumTimelineEntries)
        {
            throw new InvalidOperationException(
                "Desktop progress v4 failed to bound project or timeline fields.");
        }

        var incompatible = Serialize(original).Replace(
            $"\"version\":{Version}",
            $"\"version\":{Version + 1}",
            StringComparison.Ordinal);
        if (TryDeserialize(incompatible, out _))
        {
            throw new InvalidOperationException(
                "Desktop progress protocol accepted an incompatible version.");
        }

        await using var oversized = new MemoryStream();
        var oversizedHeader = new byte[FrameHeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(
            oversizedHeader,
            MaximumFrameBytes + 1);
        await oversized.WriteAsync(
            oversizedHeader,
            cancellationToken);
        oversized.Position = 0;
        try
        {
            _ = await ReadFrameAsync(
                oversized,
                cancellationToken);
            throw new InvalidOperationException(
                "Desktop progress protocol accepted an oversized frame.");
        }
        catch (InvalidDataException)
        {
        }

        await using var truncated = new MemoryStream();
        var truncatedHeader = new byte[FrameHeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(
            truncatedHeader,
            16);
        await truncated.WriteAsync(
            truncatedHeader,
            cancellationToken);
        await truncated.WriteAsync(
            new byte[3],
            cancellationToken);
        truncated.Position = 0;
        try
        {
            _ = await ReadFrameAsync(
                truncated,
                cancellationToken);
            throw new InvalidOperationException(
                "Desktop progress protocol accepted a truncated frame.");
        }
        catch (InvalidDataException)
        {
        }

        var oversizedText = original with
        {
            Message = new string(
                'x',
                MaximumMessageCharacters + 512),
        };
        await using var clippedFrame = new MemoryStream();
        await WriteFrameAsync(
            clippedFrame,
            oversizedText,
            cancellationToken);
        clippedFrame.Position = 0;
        var clipped = await ReadFrameAsync(
            clippedFrame,
            cancellationToken);
        if (clipped is null ||
            clipped.Message.Length > MaximumMessageCharacters)
        {
            throw new InvalidOperationException(
                "Desktop progress sender-side bounds were not preserved.");
        }

        var secretBearing = original with
        {
            Message =
                "Authorization: Bearer abcdefghijklmnopqrstuvwxyz",
            Evidence = new DesktopProgressEvidence(
                Files:
                [
                    "C:\\temp\\ghp_123456789012345678901234.txt",
                ],
                Result:
                    "api_key=sk-proj-abcdefghijklmnop"),
        };
        var secretJson = Serialize(secretBearing);
        if (secretJson.Contains(
                "abcdefghijklmnopqrstuvwxyz",
                StringComparison.Ordinal) ||
            secretJson.Contains(
                "ghp_123456789012345678901234",
                StringComparison.Ordinal) ||
            secretJson.Contains(
                "sk-proj-abcdefghijklmnop",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Desktop progress protocol emitted sensitive evidence without redaction.");
        }
    }

    private static byte[] SerializeToBoundedPayload(
        DesktopProgressMessage message)
    {
        var normalized = NormalizeForWire(message);
        var payload = SerializeEnvelope(normalized);
        if (payload.Length <= MaximumFrameBytes)
        {
            return payload;
        }

        var reducedEvidence = normalized.Evidence is null
            ? null
            : new DesktopProgressEvidence(
                Summary: RedactAndClip(
                    normalized.Evidence.Summary,
                    2 * 1024),
                Files: normalized.Evidence.Files?
                    .Take(8)
                    .Select(file =>
                        RedactAndClip(file, 768) ?? string.Empty)
                    .ToArray(),
                AddedLines: normalized.Evidence.AddedLines,
                RemovedLines: normalized.Evidence.RemovedLines,
                CodePreview: RedactAndClip(
                    normalized.Evidence.CodePreview,
                    2 * 1024),
                Result: RedactAndClip(
                    normalized.Evidence.Result,
                    2 * 1024));
        var reduced = normalized with
        {
            Message = RedactAndClip(normalized.Message, 4 * 1024) ?? string.Empty,
            Evidence = reducedEvidence,
        };
        payload = SerializeEnvelope(reduced);
        if (payload.Length <= MaximumFrameBytes)
        {
            return payload;
        }

        var essential = normalized with
        {
            Title = RedactAndClip(normalized.Title, 256) ?? string.Empty,
            Message = RedactAndClip(normalized.Message, 2 * 1024) ?? string.Empty,
            Evidence = null,
        };
        payload = SerializeEnvelope(essential);
        if (payload.Length <= MaximumFrameBytes)
        {
            return payload;
        }

        throw new InvalidDataException(
            $"Desktop progress payload exceeds the {MaximumFrameBytes}-byte frame limit after deterministic reduction.");
    }

    private static byte[] SerializeEnvelope(
        DesktopProgressMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new DesktopProgressEnvelope(
                Version,
                message),
            SerializerOptions);

    private static DesktopProgressMessage NormalizeForWire(
        DesktopProgressMessage message)
    {
        var evidence = message.Evidence is null
            ? null
            : new DesktopProgressEvidence(
                RedactAndClip(
                    message.Evidence.Summary,
                    MaximumEvidenceTextCharacters),
                message.Evidence.Files?
                    .Take(MaximumEvidenceFiles)
                    .Select(file =>
                        RedactAndClip(
                            file,
                            MaximumEvidenceFileCharacters) ??
                        string.Empty)
                    .ToArray(),
                message.Evidence.AddedLines,
                message.Evidence.RemovedLines,
                RedactAndClip(
                    message.Evidence.CodePreview,
                    MaximumCodeCharacters),
                RedactAndClip(
                    message.Evidence.Result,
                    MaximumEvidenceTextCharacters));

        return message with
        {
            OperationId =
                Clip(
                    message.OperationId,
                    MaximumOperationIdCharacters) ??
                string.Empty,
            ToolName =
                Clip(
                    message.ToolName,
                    MaximumToolNameCharacters) ??
                string.Empty,
            Title =
                RedactAndClip(
                    message.Title,
                    MaximumTitleCharacters) ??
                string.Empty,
            Message =
                RedactAndClip(
                    message.Message,
                    MaximumMessageCharacters) ??
                string.Empty,
            Evidence = evidence,
            WorklogFrame = message.WorklogFrame is null
                ? null
                : NormalizeWorklogFrame(message.WorklogFrame),
        };
    }

    private static DesktopWorklogFrame NormalizeWorklogFrame(
        DesktopWorklogFrame frame) => frame with
    {
        SessionId = Clip(frame.SessionId, MaximumSessionIdCharacters) ?? string.Empty,
        Header = frame.Header with
        {
            ProjectName = RedactAndClip(frame.Header.ProjectName, MaximumProjectNameCharacters),
            ActorName = RedactAndClip(frame.Header.ActorName, MaximumActorNameCharacters) ?? string.Empty,
            TaskNote = RedactAndClip(frame.Header.TaskNote, MaximumTaskNoteCharacters),
        },
        Current = frame.Current with
        {
            Title = RedactAndClip(frame.Current.Title, MaximumTitleCharacters) ?? string.Empty,
            FactLine = RedactAndClip(frame.Current.FactLine, MaximumMessageCharacters),
            Purpose = RedactAndClip(frame.Current.Purpose, MaximumPurposeCharacters),
            Changes = frame.Current.Changes?
                .Where(change => change is not null)
                .Take(MaximumChangedFiles)
                .Select(change => change with
                {
                    File = RedactAndClip(change.File, MaximumEvidenceFileCharacters) ?? string.Empty,
                    Added = Math.Max(0, change.Added),
                    Removed = Math.Max(0, change.Removed),
                    Places = (change.Places ?? [])
                        .Select(place => RedactAndClip(place, MaximumPlaceCharacters))
                        .Where(place => !string.IsNullOrWhiteSpace(place))
                        .Select(place => place!)
                        .Take(MaximumPlacesPerFile)
                        .ToArray(),
                })
                .ToArray(),
            Outcome = frame.Current.Outcome is null ? null : frame.Current.Outcome with
            {
                PlainReason = RedactAndClip(frame.Current.Outcome.PlainReason, MaximumMessageCharacters) ?? string.Empty,
                NextStep = RedactAndClip(frame.Current.Outcome.NextStep, MaximumMessageCharacters),
            },
        },
        Timeline = frame.Timeline
            .Take(MaximumTimelineEntries)
            .Select(entry => entry with
            {
                Label = RedactAndClip(entry.Label, MaximumTitleCharacters) ?? string.Empty,
            })
            .ToArray(),
        Evidence = frame.Evidence is null ? null : new DesktopProgressEvidence(
            Summary: RedactAndClip(frame.Evidence.Summary, MaximumEvidenceTextCharacters),
            Files: frame.Evidence.Files?.Take(MaximumEvidenceFiles)
                .Select(file => RedactAndClip(file, MaximumEvidenceFileCharacters) ?? string.Empty).ToArray(),
            AddedLines: frame.Evidence.AddedLines,
            RemovedLines: frame.Evidence.RemovedLines,
            CodePreview: RedactAndClip(frame.Evidence.CodePreview, MaximumCodeCharacters),
            Result: RedactAndClip(frame.Evidence.Result, MaximumEvidenceTextCharacters)),
    };

    private static bool IsValidMessage(
        DesktopProgressMessage? message)
    {
        if (message is null ||
            string.IsNullOrWhiteSpace(message.OperationId) ||
            string.IsNullOrWhiteSpace(message.ToolName) ||
            string.IsNullOrWhiteSpace(message.Title) ||
            string.IsNullOrWhiteSpace(message.Message) ||
            message.Sequence <= 0 ||
            !Enum.IsDefined(message.Kind) ||
            !Enum.IsDefined(message.Lane) ||
            !double.IsFinite(message.ElapsedSeconds) ||
            message.ElapsedSeconds < 0 ||
            message.OperationId.Length > MaximumOperationIdCharacters ||
            message.ToolName.Length > MaximumToolNameCharacters ||
            message.Title.Length > MaximumTitleCharacters ||
            message.Message.Length > MaximumMessageCharacters)
        {
            return false;
        }

        if (message.WorklogFrame is { } frame)
        {
            if (string.IsNullOrWhiteSpace(frame.SessionId) ||
                frame.SessionId.Length > MaximumSessionIdCharacters ||
                frame.Sequence <= 0 ||
                frame.Header is null ||
                frame.Current is null ||
                frame.Timeline is null ||
                frame.Totals is null ||
                frame.Header.ProjectName?.Length > MaximumProjectNameCharacters ||
                string.IsNullOrWhiteSpace(frame.Header.ActorName) ||
                frame.Header.ActorName.Length > MaximumActorNameCharacters ||
                !Enum.IsDefined(frame.Current.Category) ||
                !Enum.IsDefined(frame.Current.State) ||
                !Enum.IsDefined(frame.Lane) ||
                frame.Lane != message.Lane ||
                string.IsNullOrWhiteSpace(frame.Current.Title) ||
                frame.Current.Title.Length > MaximumTitleCharacters ||
                frame.Current.FactLine?.Length > MaximumMessageCharacters ||
                frame.Header.TaskNote?.Length > MaximumTaskNoteCharacters ||
                frame.Current.Purpose?.Length > MaximumPurposeCharacters ||
                (frame.Current.Changes?.Count ?? 0) > MaximumChangedFiles ||
                frame.Current.Changes?.Any(change =>
                    change is null ||
                    string.IsNullOrWhiteSpace(change.File) ||
                    change.File.Length > MaximumEvidenceFileCharacters ||
                    change.Added < 0 ||
                    change.Removed < 0 ||
                    change.Places is null ||
                    change.Places.Count > MaximumPlacesPerFile ||
                    change.Places.Any(place => place is null || place.Length > MaximumPlaceCharacters)) is true ||
                !double.IsFinite(frame.Current.ElapsedSeconds) ||
                frame.Current.ElapsedSeconds < 0 ||
                frame.ExpectedNextUpdateSeconds is < 1 or > 3600 ||
                frame.Timeline.Count > MaximumTimelineEntries ||
                frame.Timeline.Any(entry =>
                    entry is null || !Enum.IsDefined(entry.State) ||
                    string.IsNullOrWhiteSpace(entry.Label) ||
                    entry.Label.Length > MaximumTitleCharacters) ||
                frame.Totals.Steps < 0 ||
                !double.IsFinite(frame.Totals.SessionSeconds) ||
                frame.Totals.SessionSeconds < 0 ||
                frame.Totals.FilesChanged < 0 ||
                !Enum.IsDefined(frame.Totals.LastBuild) ||
                !Enum.IsDefined(frame.Totals.LastTests) ||
                (frame.Current.Outcome is { } outcome &&
                 ((outcome.FailureKind is { } failureKind &&
                   !Enum.IsDefined(failureKind)) ||
                  outcome.PlainReason is null ||
                  outcome.PlainReason.Length > MaximumMessageCharacters ||
                  outcome.NextStep?.Length > MaximumMessageCharacters)))
            {
                return false;
            }
        }

        if (message.Evidence is not { } evidence)
        {
            return true;
        }

        if ((evidence.Files?.Count ?? 0) > MaximumEvidenceFiles ||
            evidence.Files?.Any(file =>
                file is null ||
                file.Length > MaximumEvidenceFileCharacters) is true ||
            evidence.Summary?.Length > MaximumEvidenceTextCharacters ||
            evidence.CodePreview?.Length > MaximumCodeCharacters ||
            evidence.Result?.Length > MaximumEvidenceTextCharacters)
        {
            return false;
        }

        return true;
    }

    private static string? Clip(
        string? value,
        int maximumCharacters)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length <= maximumCharacters)
        {
            return value;
        }

        if (maximumCharacters <= 1)
        {
            return value[..maximumCharacters];
        }

        return value[..(maximumCharacters - 1)] + "…";
    }

    private static string? RedactAndClip(
        string? value,
        int maximumCharacters)
    {
        if (value is null)
        {
            return null;
        }

        return Clip(
            FileLog.RedactSensitiveData(value),
            maximumCharacters);
    }
}
