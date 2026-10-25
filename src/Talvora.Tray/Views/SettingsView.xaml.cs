using System.Windows;
using System.Windows.Controls;
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
    }

    internal SettingsViewModel Model { get; }
    internal StepperCard SetupCard => (StepperCard)SetupHost.Content;

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
}
