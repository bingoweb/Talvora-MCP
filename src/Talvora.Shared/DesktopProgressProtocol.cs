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
    long Sequence = 0);

public sealed record DesktopProgressEvidence(
    string? Summary = null,
    IReadOnlyList<string>? Files = null,
    int? AddedLines = null,
    int? RemovedLines = null,
    string? CodePreview = null,
    string? Result = null);

public static class DesktopProgressProtocol
{
    public const int Version = 3;
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
        };
    }

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
