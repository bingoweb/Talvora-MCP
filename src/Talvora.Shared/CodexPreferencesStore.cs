using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

/// <summary>
/// User-scoped coding profile. Kept separate from Codex's own config.toml:
/// users can request a trusted profile without changing Windows security
/// policies, the machine-wide Codex install, or the Talvora service identity.
/// </summary>
public static class CodexPreferencesStore
{
    public const string Balanced = "balanced";
    public const string Trusted = "trusted";
    public const string DefaultProfile = Balanced;

    private sealed record Settings(int SchemaVersion, string DefaultProfile);

    public static string FilePath(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(localAppData, "Talvora", "Codex", "preferences.json");
    }

    public static string Read(string localAppData)
    {
        var path = FilePath(localAppData);
        if (!File.Exists(path)) { return Balanced; }
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 8 * 1024) { return Balanced; }
            var settings = JsonSerializer.Deserialize<Settings>(
                File.ReadAllText(path, Encoding.UTF8));
            return settings?.DefaultProfile is Balanced or Trusted
                ? settings.DefaultProfile
                : Balanced;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Balanced;
        }
    }

    public static async Task WriteAsync(
        string localAppData,
        string profile,
        CancellationToken cancellationToken = default)
    {
        if (profile is not (Balanced or Trusted))
        {
            throw new ArgumentException(
                "Codex profile must be balanced or trusted.", nameof(profile));
        }
        var path = FilePath(localAppData);
        await AtomicFile.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new Settings(1, profile)),
            new UTF8Encoding(false),
            createBackup: File.Exists(path),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
