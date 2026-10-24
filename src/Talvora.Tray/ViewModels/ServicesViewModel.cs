using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;

namespace Talvora.Tray.ViewModels;

/// <summary>A read-only, snapshot-driven view of registered MCP services.</summary>
internal sealed record ServiceRowViewModel(
    ManagedMcpDashboardState Source,
    string Name,
    string Role,
    ComponentVisualState State,
    string StateLabel,
    string LastChecked,
    string CheckedTooltip,
    bool IsManuallyStopped,
    System.Windows.Media.Imaging.BitmapSource? Logo)
{
    public bool CanStart => IsManuallyStopped || Source.Health == ControlCenterHealthState.Offline;
    public bool CanStop => !CanStart;
    public bool CanRestart => !CanStart;
    public bool CanRepair => !IsManuallyStopped && Source.Health != ControlCenterHealthState.Ready;
}

internal sealed class ServicesViewModel : ObservableObject
{
    private string _searchText = string.Empty;
    private string _stateFilter = "Tümü";
    private bool _isLoading = true;

    public ObservableCollection<ServiceRowViewModel> Rows { get; } = [];
    public ObservableCollection<ServiceRowViewModel> FilteredRows { get; } = [];

    public IReadOnlyList<string> Filters { get; } =
        ["Tümü", "Hazır", "Dikkat istiyor", "Ulaşılamıyor", "İsteğe bağlı kapalı", "Onarılıyor"];

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Filter(); }
    }
    public string StateFilter
    {
        get => _stateFilter;
        set { if (SetProperty(ref _stateFilter, value)) Filter(); }
    }
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    public bool HasRows => FilteredRows.Count > 0;

    public void ApplySnapshot(
        ControlCenterDashboardSnapshot snapshot,
        DateTimeOffset checkedAt,
        Func<ManagedMcpRegistration, bool> isManuallyStopped)
    {
        Rows.Clear();
        foreach (var source in snapshot.Mcps)
        {
            var manuallyStopped = isManuallyStopped(source.Registration);
            var optional = ManagedMcpAggregateHealthPolicy.IsIgnored(
                source.Registration, source.Health, manuallyStopped);
            var state = OverviewViewModel.VisualState(source.Health, optional);
            Rows.Add(new ServiceRowViewModel(
                source,
                source.Registration.DisplayName,
                GetRole(source.Registration),
                state,
                OverviewViewModel.Word(state),
                optional && source.Health != ControlCenterHealthState.Ready ? "—" : "az önce",
                checkedAt.ToLocalTime().ToString("G",
                    System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
                manuallyStopped,
                McpLogoResolver.TryResolve(source.Registration.Id) ??
                    (source.Registration.Id.StartsWith("talvora-", StringComparison.OrdinalIgnoreCase)
                        ? McpLogoResolver.TryResolve("talvora") : null)));
        }
        IsLoading = false;
        Filter();
    }

    private static string GetRole(ManagedMcpRegistration reg) =>
        string.Equals(reg.Id, "talvora", StringComparison.OrdinalIgnoreCase) ? "Yerel MCP" :
        string.Equals(reg.Id, "gitea", StringComparison.OrdinalIgnoreCase) ? "Yerel zincir" :
        reg.Id is "talvora-dev" or "talvora-admin" ? "Tünel" :
        !reg.AutoStart ? "İsteğe bağlı" :
        reg.Tunnel is not null ? "Tünel" : "Tasarım";

    private void Filter()
    {
        var query = SearchText.Trim();
        FilteredRows.Clear();
        foreach (var row in Rows.Where(row =>
            (StateFilter == "Tümü" || row.StateLabel == StateFilter) &&
            (query.Length == 0 ||
             row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             row.Role.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
        {
            FilteredRows.Add(row);
        }
        OnPropertyChanged(nameof(HasRows));
    }
}
