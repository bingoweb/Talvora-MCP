using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;
using Talvora.Tray.Controls;

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

internal sealed record OverviewEventItem(string Time, string Title, string Service, string Kind = "", string Detail = "");

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
    private string _summary = string.Empty;
    private OverviewEventItem? _latestEvent;
    private IReadOnlyList<SwitchboardSocket> _sockets = [];
    private ControlCenterDashboardSnapshot? _snapshot;
    private TrafficFeed? _traffic;
    private readonly ControlCenterStateSinceTracker _stateSince = new();

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
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public OverviewEventItem? LatestEvent
    { get => _latestEvent; private set { if (SetProperty(ref _latestEvent, value)) OnPropertyChanged(nameof(HasLatestEvent)); } }
    public bool HasLatestEvent => _latestEvent is not null;
    /// <summary>SN3: the switchboard's sockets, rebuilt from the latest health snapshot and SN1 traffic feed.</summary>
    public IReadOnlyList<SwitchboardSocket> Sockets { get => _sockets; private set => SetProperty(ref _sockets, value); }
    public TrafficFeed? Traffic => _traffic;

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
        StatusLine = "Son kontrol " + checkedAt.ToLocalTime().ToString("HH:mm:ss",
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
                state, Word(state), checkedAt.ToLocalTime().ToString("HH:mm:ss",
                    System.Globalization.CultureInfo.GetCultureInfo("tr-TR")), checkedAt.ToLocalTime().ToString("G",
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
            RecentEvents.Add(new OverviewEventItem(entry.LastOccurredAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                FileLog.RedactSensitiveData(entry.Title),
                snapshot.Mcps.FirstOrDefault(state => string.Equals(state.Registration.Id, entry.McpId,
                    StringComparison.OrdinalIgnoreCase))?.Registration.DisplayName ?? entry.McpId ?? string.Empty,
                EventKind(entry.Category), FileLog.RedactSensitiveData(entry.Detail ?? string.Empty)));
        LatestEvent = RecentEvents.FirstOrDefault();
        Summary = string.Join(" · ", new[]
        {
            $"{ReadyCount} hazır",
            AttentionCount > 0 ? $"{AttentionCount} dikkat" : null,
            OptionalCount > 0 ? $"{OptionalCount} kapalı" : null,
            HasRecovery ? $"son 24 saatte {RecoveryCount} otomatik onarım" : null,
        }.Where(part => part is not null));
        _snapshot = snapshot;
        RebuildSockets(checkedAt);
        IsLoading = false;
    }

    /// <summary>SN1 feed in; returns the lines of calls finished since the previous read (one light each).</summary>
    public IReadOnlyList<string> ApplyTraffic(TrafficFeed? feed)
    {
        var previous = _traffic;
        _traffic = feed;
        RebuildSockets(DateTimeOffset.Now);
        if (feed is null || previous is null || previous.StartedAtUtc != feed.StartedAtUtc) return [];
        return feed.Recent.Where(call => call.Seq > previous.NextSeq).OrderBy(call => call.Seq)
            .Select(call => call.Line).ToArray();
    }

    internal static string EventKind(string? category) => category?.ToLowerInvariant() switch
    {
        "recovery" => "OTOMATİK ONARIM",
        "lifecycle" or "operation" => "YAŞAM DÖNGÜSÜ",
        "problem" or "error" or "failure" => "SORUN",
        _ => "BİLGİ",
    };

    // Registry id -> (socket name, measured line, outside title, outside sub). Gitea's calls are not seen by Talvora.
    private static readonly (string Id, string Name, string? Line, string? Outside, string? Sub)[] BoardLayout =
    [
        ("talvora", "Çekirdek", "core", "YEREL", null),
        ("talvora-dev", "Dev", "dev", "CHATGPT", "Dev"),
        ("talvora-admin", "Admin", "admin", "CHATGPT", "Admin"),
        ("gitea", "Gitea", null, "CHATGPT", "Gitea"),
        ("penpot", "Penpot", null, null, null),
    ];

    private void RebuildSockets(DateTimeOffset now)
    {
        if (_snapshot is null) return;
        var sockets = new List<SwitchboardSocket>();
        var states = _snapshot.Mcps
            .OrderBy(state => Array.FindIndex(BoardLayout, row => string.Equals(row.Id, state.Registration.Id,
                StringComparison.OrdinalIgnoreCase)) is var index && index >= 0 ? index : BoardLayout.Length)
            .ThenBy(state => state.Registration.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var state in states)
        {
            var optional = ManagedMcpAggregateHealthPolicy.IsIgnored(state.Registration, state.Health,
                ManagedMcpSessionState.IsManuallyStopped(state.Registration.Id));
            var visual = VisualState(state.Health, optional);
            var layout = BoardLayout.FirstOrDefault(row => string.Equals(row.Id, state.Registration.Id,
                StringComparison.OrdinalIgnoreCase));
            var name = layout.Id is null ? state.Registration.DisplayName : layout.Name;
            var line = layout.Line;
            TrafficFeedLine? measured = null;
            if (line is not null && _traffic?.Lines.TryGetValue(line, out var found) == true) measured = found;
            var sub = layout.Sub;
            if (layout.Id is not null && line == "core")
            {
                var clients = _traffic?.Clients
                    .Where(client => client.Line == "core" && client.LastSeenAtUtc >= now.ToUniversalTime().AddMinutes(-60))
                    .Select(client => client.Client).Distinct().Order(StringComparer.Ordinal).ToArray() ?? [];
                sub = clients.Length > 0 ? string.Join(" · ", clients) : "son saatte istemci yok";
            }
            sockets.Add(new SwitchboardSocket(
                state.Registration.Id, name,
                optional ? "isteğe bağlı" : null,
                visual, line, layout.Id is null ? null : layout.Outside, sub,
                measured?.Calls, measured?.LastCallAtUtc,
                _stateSince.Observe(state.Registration.Id, state.Health, now.ToUniversalTime()),
                state));
        }
        Sockets = sockets;
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
