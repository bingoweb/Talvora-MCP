using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

/// <summary>UI6 Settings: the setup card and persisted appearance preferences.</summary>
internal sealed partial class SettingsView : System.Windows.Controls.UserControl
{
    private readonly Action<SettingsRailPreference> _applyRail;
    private readonly Action _synchronizeShellTheme;
    private bool _syncingSelections;

    internal SettingsView(
        SettingsViewModel model,
        StepperCard stepper,
        Action<SettingsRailPreference> applyRail,
        Action synchronizeShellTheme)
    {
        InitializeComponent();
        Model = model;
        _applyRail = applyRail;
        _synchronizeShellTheme = synchronizeShellTheme;
        DataContext = model;
        SetupHost.Content = stepper;
        Model.Preferences = SettingsPreferencesStore.Current;
        SynchronizeTheme();
        SynchronizeAppearance();
        var about = GetUi7AboutMetadata(typeof(SettingsView).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        AboutVersion.Text = about.Version;
        AboutCommit.Text = about.ShortCommit;
    }

    internal SettingsViewModel Model { get; }
    internal StepperCard SetupCard => (StepperCard)SetupHost.Content;

    internal const string ReadmeUrl =
        "https://github.com/bingoweb/Talvora-MCP/blob/main/README.md";
    internal const string SecurityUrl =
        "https://github.com/bingoweb/Talvora-MCP/blob/main/SECURITY.md";

    internal static (string Version, string ShortCommit) GetUi7AboutMetadata(string? informationalVersion)
    {
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? "Bilinmiyor" : informationalVersion;
        var separator = version.IndexOf('+');
        var commit = separator >= 0 ? version[(separator + 1)..] : string.Empty;
        return (version, commit.Length >= 7 ? commit[..7] : "—");
    }

    internal void SynchronizeTheme()
    {
        _syncingSelections = true;
        Model.UpdateTheme();
        ThemeSetting.SelectedIndex = (int)TalvoraThemeManager.Preference;
        _syncingSelections = false;
    }

    private void SynchronizeAppearance()
    {
        _syncingSelections = true;
        MotionSetting.SelectedIndex = (int)Model.Preferences.Motion;
        RailSetting.SelectedIndex = (int)Model.Preferences.Rail;
        CodePreviewSetting.IsChecked = Model.Preferences.ShowCodePreview;
        FailureAgentSetting.IsChecked = Model.Preferences.FailureAndAgentOnly;
        QuietModeSetting.IsChecked = Model.Preferences.QuietMode;
        _syncingSelections = false;
    }

    private async void OnChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelections || sender is not System.Windows.Controls.ComboBox source || source.SelectedIndex < 0)
            return;

        if (ReferenceEquals(source, ThemeSetting))
        {
            await TalvoraThemeManager.SetPreferenceAsync((TalvoraThemePreference)source.SelectedIndex);
            Model.UpdateTheme();
            _synchronizeShellTheme();
            return;
        }

        var current = SettingsPreferencesStore.Current;
        var next = ReferenceEquals(source, MotionSetting)
            ? current with { Motion = (SettingsMotionPreference)source.SelectedIndex }
            : current with { Rail = (SettingsRailPreference)source.SelectedIndex };
        await SettingsPreferencesStore.SetAsync(next);
        Model.Preferences = next;
        if (ReferenceEquals(source, RailSetting)) _applyRail(next.Rail);
    }

    private async void OnNotificationToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingSelections || sender is not Wpf.Ui.Controls.ToggleSwitch source)
            return;

        var current = SettingsPreferencesStore.Current;
        var next = ReferenceEquals(source, CodePreviewSetting)
            ? current with { ShowCodePreview = source.IsChecked == true }
            : ReferenceEquals(source, FailureAgentSetting)
                ? current with { FailureAndAgentOnly = source.IsChecked == true }
                : current with { QuietMode = source.IsChecked == true };
        await SettingsPreferencesStore.SetAsync(next);
        Model.Preferences = next;
    }
}
