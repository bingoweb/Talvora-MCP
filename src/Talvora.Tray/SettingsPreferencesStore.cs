using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal enum SettingsMotionPreference { System, Reduced }
internal enum SettingsRailPreference { Automatic, Collapsed, Expanded }

internal sealed record SettingsPreferences(SettingsMotionPreference Motion, SettingsRailPreference Rail)
{
    public bool ShowCodePreview { get; init; } = true;
    public bool FailureAndAgentOnly { get; init; }
    public bool QuietMode { get; init; }

    public static SettingsPreferences Default { get; } =
        new(SettingsMotionPreference.System, SettingsRailPreference.Automatic);
}

/// <summary>UI6: non-secret per-user UI settings beside Control Center window placement.</summary>
internal static class SettingsPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static string? _smokePath;
    public static SettingsPreferences Current { get; private set; } = SettingsPreferences.Default;
    public static event EventHandler? Changed;

    internal static string PreferencePath => _smokePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Talvora", "ControlCenter", "settings-preferences.json");

    internal static void Initialize(bool smokeMode)
    {
        _smokePath = smokeMode
            ? Path.Combine(Path.GetTempPath(), "TalvoraUi6Smoke-" + Guid.NewGuid().ToString("N"), "settings-preferences.json")
            : null;
        Current = Load(PreferencePath);
    }

    public static SettingsPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return SettingsPreferences.Default;
            var saved = JsonFileStore.ReadBounded<SettingsPreferences>(path, 4096, JsonOptions);
            return Enum.IsDefined(saved.Motion) && Enum.IsDefined(saved.Rail)
                ? saved : SettingsPreferences.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            TrayLog.Write("Control Center settings preference could not be read", ex);
            return SettingsPreferences.Default;
        }
    }

    public static Task SaveAsync(string path, SettingsPreferences value) =>
        JsonFileStore.WriteAsync(path, value, JsonOptions, createBackup: false, CancellationToken.None);

    internal static async Task SetAsync(SettingsPreferences value)
    {
        if (!Enum.IsDefined(value.Motion) || !Enum.IsDefined(value.Rail)) return;
        Current = value;
        Changed?.Invoke(null, EventArgs.Empty);
        try
        {
            await SaveAsync(PreferencePath, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TrayLog.Write("Control Center settings preference could not be saved", ex);
        }
    }
}
