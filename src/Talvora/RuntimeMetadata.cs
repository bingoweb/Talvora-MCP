using System.Text.Json;

namespace Talvora;

public sealed record TalvoraRuntimeMetadata(
    string? SourceCommit,
    string? InstalledAtUtc)
{
    public static TalvoraRuntimeMetadata Empty { get; } = new(null, null);

    public static TalvoraRuntimeMetadata Load()
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
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
