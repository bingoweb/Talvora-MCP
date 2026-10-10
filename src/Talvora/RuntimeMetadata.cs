using System.Text.Json;
using Talvora.Shared;

namespace Talvora;

public sealed record TalvoraRuntimeMetadata(
    string? SourceCommit,
    string? InstalledAtUtc)
{
    private const int MaximumRuntimeMetadataBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<TalvoraRuntimeMetadata> Cached = new(
        LoadFromDisk,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static TalvoraRuntimeMetadata Empty { get; } = new(null, null);

    public static TalvoraRuntimeMetadata Load() => Cached.Value;

    private static TalvoraRuntimeMetadata LoadFromDisk() =>
        ReadFromPath(Path.Combine(AppContext.BaseDirectory, "talvora-runtime.json"));

    internal static TalvoraRuntimeMetadata ReadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var metadata = JsonFileStore.ReadBounded<TalvoraRuntimeMetadata>(
                path,
                MaximumRuntimeMetadataBytes,
                JsonOptions);

            return metadata ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
        catch (InvalidDataException)
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
