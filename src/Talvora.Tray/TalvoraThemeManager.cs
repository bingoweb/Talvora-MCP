using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Talvora.Shared;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Talvora.Tray;

internal enum TalvoraThemePreference { System, Gece, Gun }

internal static class TalvoraThemeManager
{
    private static readonly Uri GeceUri = new("pack://application:,,,/Talvora.Tray;component/Themes/Tokens.Gece.xaml");
    private static readonly Uri GunUri = new("pack://application:,,,/Talvora.Tray;component/Themes/Tokens.Gun.xaml");
    private static readonly Uri UiGeceUri = new("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Dark.xaml");
    private static readonly Uri UiGunUri = new("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static TalvoraThemePreference _preference = TalvoraThemePreference.System;
    private static Application? _application;
    private static Window? _watchedWindow;
    private static bool _watching;
    private static string? _smokePreferencePath;

    private sealed record ThemePreferenceDocument(string Preference);

    private static string PreferencePath => _smokePreferencePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Talvora", "ControlCenter", "theme-preference.json");

    internal static TalvoraThemePreference Preference => _preference;
    internal static string? SmokePreferencePath => _smokePreferencePath;

    internal static void Initialize(Application app, bool smokeTest = false)
    {
        _application = app;
        _smokePreferencePath = smokeTest
            ? Path.Combine(Path.GetTempPath(), "TalvoraUi1Smoke-" + Guid.NewGuid().ToString("N"), "theme-preference.json")
            : null;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        var preference = smokeTest
            ? ParsePreference(Environment.GetEnvironmentVariable("TALVORA_CONTROL_CENTER_SMOKE_THEME"))
            : ReadPreference();
        Apply(app, preference);
    }

    private static TalvoraThemePreference ParsePreference(string? value) =>
        Enum.TryParse<TalvoraThemePreference>(value, ignoreCase: true, out var preference) &&
        Enum.IsDefined(preference) ? preference : TalvoraThemePreference.System;

    private static TalvoraThemePreference ReadPreference()
    {
        try
        {
            if (!File.Exists(PreferencePath)) return TalvoraThemePreference.System;
            var saved = JsonFileStore.ReadBounded<ThemePreferenceDocument>(PreferencePath, 4096, JsonOptions);
            return ParsePreference(saved.Preference);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            TrayLog.Write("Control Center theme preference could not be read", ex);
            return TalvoraThemePreference.System;
        }
    }

    internal static async Task SetPreferenceAsync(TalvoraThemePreference preference, bool persist = true)
    {
        if (_application is null) return;
        Apply(_application, preference);
        if (!persist) return;

        try
        {
            await JsonFileStore.WriteAsync(
                PreferencePath, new ThemePreferenceDocument(preference.ToString()),
                JsonOptions, createBackup: false, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TrayLog.Write("Control Center theme preference could not be saved", ex);
        }
    }

    internal static void Apply(Application app, TalvoraThemePreference preference)
    {
        _application = app;
        _preference = preference;

        var system = ApplicationThemeManager.GetSystemTheme();
        var dark = preference switch
        {
            TalvoraThemePreference.Gece => true,
            TalvoraThemePreference.Gun => false,
            _ => system is not (SystemTheme.Light or SystemTheme.HCWhite),
        };

        var highContrast = SystemParameters.HighContrast;
        var theme = highContrast ? ApplicationTheme.HighContrast :
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        // WPF-UI requires its native Theme resources before resolving implicit button styles.
        // The manager owns this dictionary rather than hard-coding Light in ControlCenterTheme.xaml.
        var dictionaries = app.Resources.MergedDictionaries;
        if (!dictionaries.Any(d => d.Source == UiGeceUri || d.Source == UiGunUri))
            dictionaries.Insert(0, new ResourceDictionary { Source = dark ? UiGeceUri : UiGunUri });
        ApplicationThemeManager.Apply(theme, GetBackdrop(), updateAccent: false);
        SwapTokens(app, dark);
        ShellWindow.RefreshThemePalette(app);

        if (_watchedWindow is not null)
        {
            StopWatching(_watchedWindow);
            if (preference == TalvoraThemePreference.System)
            {
                SystemThemeWatcher.Watch(_watchedWindow);
                _watching = true;
            }
        }
    }

    internal static WindowBackdropType GetBackdrop() =>
        Environment.OSVersion.Version.Build >= 22000 && !SystemParameters.HighContrast
            ? WindowBackdropType.Mica : WindowBackdropType.None;

    internal static void Watch(Window window)
    {
        if (_watchedWindow is not null && _watchedWindow != window)
            StopWatching(_watchedWindow);
        _watchedWindow = window;
        if (_preference == TalvoraThemePreference.System && !_watching)
        {
            SystemThemeWatcher.Watch(window);
            _watching = true;
        }
    }

    internal static void UnWatch(Window window)
    {
        StopWatching(window);
        if (_watchedWindow == window) _watchedWindow = null;
    }

    private static void StopWatching(Window window)
    {
        if (_watching)
        {
            if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero)
                SystemThemeWatcher.UnWatch(window);
            _watching = false;
        }
    }

    private static void OnApplicationThemeChanged(ApplicationTheme theme, Color _)
    {
        if (_preference != TalvoraThemePreference.System || _application is null) return;
        var dark = theme switch
        {
            ApplicationTheme.Light => false,
            ApplicationTheme.HighContrast => ApplicationThemeManager.GetSystemTheme() is not SystemTheme.HCWhite,
            _ => true,
        };
        SwapTokens(_application, dark);
        ShellWindow.RefreshThemePalette(_application);
    }

    private static void SwapTokens(Application app, bool dark)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source == GeceUri || d.Source == GunUri);
        var next = new ResourceDictionary { Source = dark ? GeceUri : GunUri };
        if (current is null) dictionaries.Add(next);
        else dictionaries[dictionaries.IndexOf(current)] = next;
    }
}
