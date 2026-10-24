using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;

namespace Talvora.Tray.ViewModels;

internal sealed record OverviewServiceItem(
    ManagedMcpDashboardState Source,
    string Name,
    string Role,
    ComponentVisualState State,
    string StateLabel,
    string LastChecked,
    string CheckedTooltip,
    bool IsOptional,
    BitmapSource? Logo);

internal sealed record OverviewEventItem(string Time, string Title, string Service);

/// <summary>Immutable status facts from existing Control Center services, adapted to §5.1.</summary>
internal sealed class OverviewViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _statusLine = string.Empty;
    private string _readyFraction = string.Empty;
    private string _searchText = string.Empty;
    private string _stateFilter = "Tümü";
    private IReadOnlyList<ComponentVisualState> _segments = [];
    private bool _isLoading = true;
    private bool _isWide;
    private int _readyCount;
    private int _attentionCount;
    private int _optionalCount;
    private int _recoveryCount;
    private bool _hasRecovery;
    private bool _canRepair;
    private ManagedMcpDashboardState? _repairTarget;

    public ObservableCollection<OverviewServiceItem> Services { get; } = [];
    public ObservableCollection<OverviewServiceItem> FilteredServices { get; } = [];
    public ObservableCollection<OverviewEventItem> RecentEvents { get; } = [];
    public IReadOnlyList<string> Filters { get; } =
        ["Tümü", "Hazır", "Dikkat istiyor", "Ulaşılamıyor", "İsteğe bağlı kapalı"];

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    public string StatusLine { get => _statusLine; private set => SetProperty(ref _statusLine, value); }
    public string ReadyFraction { get => _readyFraction; private set => SetProperty(ref _readyFraction, value); }
    public IReadOnlyList<ComponentVisualState> Segments
    { get => _segments; private set => SetProperty(ref _segments, value); }
    public int ReadyCount { get => _readyCount; private set => SetProperty(ref _readyCount, value); }
    public int AttentionCount { get => _attentionCount; private set => SetProperty(ref _attentionCount, value); }
    public int OptionalCount { get => _optionalCount; private set => SetProperty(ref _optionalCount, value); }
    public int RecoveryCount { get => _recoveryCount; private set => SetProperty(ref _recoveryCount, value); }
    public bool HasRecovery { get => _hasRecovery; private set => SetProperty(ref _hasRecovery, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsWide { get => _isWide; set => SetProperty(ref _isWide, value); }
    public bool CanRepair { get => _canRepair; private set => SetProperty(ref _canRepair, value); }
    public ManagedMcpDashboardState? RepairTarget => _repairTarget;

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public string StateFilter
    {
        get => _stateFilter;
        set { if (SetProperty(ref _stateFilter, value)) ApplyFilter(); }
    }

    public void ApplySnapshot(ControlCenterDashboardSnapshot snapshot, DateTimeOffset checkedAt,
        IReadOnlyList<ControlCenterEventRecord> events)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ordered = snapshot.Mcps
            .Select(state => (State: state, Optional: ManagedMcpAggregateHealthPolicy.IsIgnored(
                state.Registration, state.Health,
                ManagedMcpSessionState.IsManuallyStopped(state.Registration.Id))))
            .ToArray();
        var narratorEntries = ordered.Select(row => new StatusNarratorEntry(
            row.State.Registration.DisplayName,
            VisualState(row.State.Health, row.Optional),
            row.Optional,
            row.State.StatusText)).ToArray();
        var message = StatusNarrator.Build(narratorEntries, checkedAt);

        Title = message.Title;
        Description = message.Detail;
        StatusLine = "Son kontrol " + checkedAt.ToLocalTime().ToString("HH:mm",
            System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) +
            " · " + (typeof(ControlCenterVersionService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0] ?? "Talvora");
        ReadyCount = snapshot.ReadyCount;
        AttentionCount = snapshot.AttentionCount + snapshot.OfflineCount;
        OptionalCount = ordered.Count(row => row.Optional &&
            row.State.Health != ControlCenterHealthState.Ready);
        ReadyFraction = $"{ReadyCount}/{snapshot.Mcps.Count}";
        Segments = narratorEntries.Select(entry => entry.State).ToArray();
        _repairTarget = ordered.Select(row => row.State).FirstOrDefault(state =>
            state.Health != ControlCenterHealthState.Ready &&
            !ManagedMcpSessionState.IsManuallyStopped(state.Registration.Id) &&
            state.Registration.AutoStart);
        CanRepair = _repairTarget is not null;

        Services.Clear();
        foreach (var row in ordered)
        {
            var state = VisualState(row.State.Health, row.Optional);
            Services.Add(new OverviewServiceItem(
                row.State, row.State.Registration.DisplayName, GetRole(row.State.Registration),
                state, Word(state), "az önce", checkedAt.ToLocalTime().ToString("G",
                    System.Globalization.CultureInfo.GetCultureInfo("tr-TR")), row.Optional,
                McpLogoResolver.TryResolve(row.State.Registration.Id)));
        }
        ApplyFilter();

        RecoveryCount = events
            .Where(entry => entry.LastOccurredAtUtc >= checkedAt.AddHours(-24) &&
                string.Equals(entry.Category, "recovery", StringComparison.OrdinalIgnoreCase))
            .Sum(entry => entry.Count);
        HasRecovery = RecoveryCount > 0;
        RecentEvents.Clear();
        foreach (var entry in events.OrderByDescending(item => item.LastOccurredAtUtc).Take(6))
            RecentEvents.Add(new OverviewEventItem(entry.LastOccurredAtUtc.ToLocalTime().ToString("HH:mm"),
                FileLog.RedactSensitiveData(entry.Title), entry.McpId ?? string.Empty));
        IsLoading = false;
    }

    public void MarkUnavailable()
    {
        IsLoading = false;
        Title = "Talvora hizmetine şu anda ulaşılamıyor. Bilgisayarınızdaki dosyalar etkilenmedi.";
        Description = string.Empty;
        Segments = [ComponentVisualState.Neutral];
    }

    internal static ComponentVisualState VisualState(ControlCenterHealthState state, bool optional) =>
        optional && state != ControlCenterHealthState.Ready ? ComponentVisualState.Neutral :
        state switch
        {
            ControlCenterHealthState.Ready => ComponentVisualState.Ready,
            ControlCenterHealthState.Attention => ComponentVisualState.Attention,
            ControlCenterHealthState.Offline => ComponentVisualState.Offline,
            _ => ComponentVisualState.Repairing,
        };

    internal static string Word(ComponentVisualState state) => state switch
    {
        ComponentVisualState.Ready => "Hazır",
        ComponentVisualState.Attention => "Dikkat istiyor",
        ComponentVisualState.Offline => "Ulaşılamıyor",
        ComponentVisualState.Repairing => "Onarılıyor",
        _ => "İsteğe bağlı kapalı",
    };

    private static string GetRole(ManagedMcpRegistration registration) =>
        string.Equals(registration.Id, "talvora", StringComparison.OrdinalIgnoreCase) ? "Yerel MCP" :
        string.Equals(registration.Id, "penpot", StringComparison.OrdinalIgnoreCase) ? "İsteğe bağlı" :
        string.Equals(registration.Id, "gitea", StringComparison.OrdinalIgnoreCase) ? "Yerel zincir" :
        registration.Tunnel is not null ? "Tünel" : "Tasarım";

    private void ApplyFilter()
    {
        var matches = Services.Where(item =>
            (StateFilter == "Tümü" || item.StateLabel == StateFilter) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
                item.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        FilteredServices.Clear();
        foreach (var item in matches) FilteredServices.Add(item);
    }
}
