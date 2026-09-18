using System.Text.Json;

namespace Talvora;

public sealed record TalvoraRuntimeMetadata(
    string? SourceCommit,
    string? InstalledAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<TalvoraRuntimeMetadata> Cached = new(
        LoadFromDisk,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static TalvoraRuntimeMetadata Empty { get; } = new(null, null);

    public static TalvoraRuntimeMetadata Load() => Cached.Value;

    private static TalvoraRuntimeMetadata LoadFromDisk()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "talvora-runtime.json");
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<TalvoraRuntimeMetadata>(
                File.ReadAllText(path),
                JsonOptions);

            return metadata ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }
    }
}
