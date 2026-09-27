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

public sealed record DesktopProgressMessage(
    string OperationId,
    string ToolName,
    string Title,
    string Message,
    DesktopProgressKind Kind,
    DateTimeOffset TimestampUtc,
    double ElapsedSeconds);

public static class DesktopProgressProtocol
{
    public const int Version = 1;
    public const string PipeNamePrefix = "Talvora.DesktopProgress.";

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

    public static string Serialize(DesktopProgressMessage message) =>
        JsonSerializer.Serialize(message, SerializerOptions);

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
            var candidate =
                JsonSerializer.Deserialize<DesktopProgressMessage>(
                    json,
                    SerializerOptions);
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.OperationId) ||
                string.IsNullOrWhiteSpace(candidate.ToolName) ||
                string.IsNullOrWhiteSpace(candidate.Title) ||
                string.IsNullOrWhiteSpace(candidate.Message))
            {
                return false;
            }

            message = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
