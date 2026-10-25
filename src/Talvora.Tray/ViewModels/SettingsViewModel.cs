using CommunityToolkit.Mvvm.ComponentModel;

namespace Talvora.Tray.ViewModels;

internal sealed record SettingsSetupStep(
    int Number, string Title, string Sentence, string Glyph, string StateText, bool IsActive);

/// <summary>UI6: approved setup phases and persisted appearance choices, independent of WPF controls.</summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private static readonly string[] Titles =
    [
        "Talvora çekirdeği", "Güvenli tüneller", "ChatGPT Business bağlantısı"
    ];
    private static readonly string[] Sentences =
    [
        "Talvora çekirdeği, araçları bu bilgisayarda güvenle çalıştıran yerel hizmettir.",
        "Güvenli tüneller ChatGPT'nin bu bilgisayardaki Talvora'ya güvenle ulaşmasını sağlar.",
        "Bu bağlantı, Talvora'yı çalışma alanınızdaki ChatGPT'ye tanıtır."
    ];

    private IReadOnlyList<SettingsSetupStep> _steps = [];
    private bool _allComplete;
    private SettingsPreferences _preferences = SettingsPreferences.Default;

    public SettingsViewModel()
    {
        UpdateSetup(false, new ControlCenterSetupState(false, true, true, 0, [], "", ""));
    }

    public IReadOnlyList<SettingsSetupStep> Steps
    {
        get => _steps;
        private set
        {
            if (!SetProperty(ref _steps, value)) return;
            OnPropertyChanged(nameof(StepOne));
            OnPropertyChanged(nameof(StepTwo));
            OnPropertyChanged(nameof(StepThree));
        }
    }

    public SettingsSetupStep StepOne => Steps[0];
    public SettingsSetupStep StepTwo => Steps[1];
    public SettingsSetupStep StepThree => Steps[2];

    public bool AllComplete
    {
        get => _allComplete;
        private set
        {
            if (SetProperty(ref _allComplete, value))
                OnPropertyChanged(nameof(ShowSteps));
        }
    }
    public bool ShowSteps => !AllComplete;

    // UI6b: S3 folder-permission notice (approved P-12 text), refreshed with the setup card.
    private bool _showAclNotice;
    public bool ShowAclNotice { get => _showAclNotice; set => SetProperty(ref _showAclNotice, value); }
    public string AclNoticeText => ProgramDataAclCheck.NoticeText;

    public SettingsPreferences Preferences
    {
        get => _preferences;
        set
        {
            if (!SetProperty(ref _preferences, value)) return;
            OnPropertyChanged(nameof(ThemeChoice));
            OnPropertyChanged(nameof(MotionChoice));
            OnPropertyChanged(nameof(RailChoice));
        }
    }

    public int ThemeChoice { get => (int)TalvoraThemeManager.Preference; }
    public int MotionChoice => (int)Preferences.Motion;
    public int RailChoice => (int)Preferences.Rail;

    public void UpdateTheme() => OnPropertyChanged(nameof(ThemeChoice));

    public void UpdateSetup(bool coreReady, ControlCenterSetupState setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var completion = new[]
        {
            coreReady,
            setup.MissingTunnelRegistrationIds.Count == 0 &&
                !setup.RuntimeFoundationMissing,
            !setup.NeedsAdminCredential,
        };
        var firstIncomplete = Array.FindIndex(completion, value => !value);
        AllComplete = firstIncomplete == -1;
        Steps = Enumerable.Range(0, 3).Select(index =>
        {
            var done = completion[index];
            var active = index == firstIncomplete;
            return new SettingsSetupStep(index + 1, Titles[index], Sentences[index],
                done ? "●" : active ? "▲" : "○",
                done ? "Tamam" : active ? "Sırada" : "Bekliyor", active);
        }).ToArray();
    }
}
