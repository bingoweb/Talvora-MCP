using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;

namespace Talvora.Tray.ViewModels;

internal sealed record EventServiceFilter(string? Id, string Name);
internal sealed record EventTimelineItem(
    string Time, string Glyph, ControlCenterEventSeverity Severity,
    string Title, string Detail, string ServiceName, int Count)
{
    public bool HasService => !string.IsNullOrWhiteSpace(ServiceName);
}
internal sealed record EventDayGroup(string Title, IReadOnlyList<EventTimelineItem> Items);

/// <summary>UI5: filter and group event-store records without changing the persisted store.</summary>
internal sealed class EventsViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private IReadOnlyList<ControlCenterEventRecord> _records = [];
    private IReadOnlyDictionary<string, string> _serviceNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _now;
    private string _searchText = string.Empty;
    private string _selectedCategory = "Tümü";
    private string? _selectedServiceId;
    private EventServiceFilter? _selectedServiceOption;
    private bool _updatingServiceOptions;
    private string _rawLogText = string.Empty;
    private bool _rawLogVisible;
    private bool _isLoading = true;

    public ObservableCollection<EventDayGroup> Groups { get; } = [];
    public ObservableCollection<EventServiceFilter> ServiceOptions { get; } =
        [new(null, "Tümü")];
    public IReadOnlyList<string> Categories { get; } =
        ["Tümü", "Sorunlar", "Onarımlar", "Yaşam döngüsü"];
    public EventsViewModel() => _selectedServiceOption = ServiceOptions[0];
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RebuildGroups(); }
    }
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) RebuildGroups(); }
    }
    public string? SelectedServiceId
    {
        get => _selectedServiceId;
        set
        {
            if (!SetProperty(ref _selectedServiceId, value)) return;
            _selectedServiceOption = ServiceOptions.FirstOrDefault(option =>
                string.Equals(option.Id, value, StringComparison.OrdinalIgnoreCase))
                ?? ServiceOptions[0];
            OnPropertyChanged(nameof(SelectedServiceOption));
            RebuildGroups();
        }
    }
    public EventServiceFilter? SelectedServiceOption
    {
        get => _selectedServiceOption;
        set
        {
            if (_updatingServiceOptions) return;
            if (SetProperty(ref _selectedServiceOption, value)) SelectedServiceId = value?.Id;
        }
    }
    public bool HasEvents => Groups.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasEvents;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText) &&
        SelectedCategory == "Tümü" && SelectedServiceId is null
            ? "Son 7 günde kaydedilmiş olay yok."
            : "Bu filtrelerle eşleşen olay yok.";
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
                OnPropertyChanged(nameof(ShowEmptyState));
        }
    }
    public bool RawLogVisible { get => _rawLogVisible; set => SetProperty(ref _rawLogVisible, value); }
    public string RawLogText
    {
        get => _rawLogText;
        set => SetProperty(ref _rawLogText, FileLog.RedactSensitiveData(value));
    }

    public void ApplyEvents(
        IReadOnlyList<ControlCenterEventRecord> records,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> serviceNames)
    {
        _records = records;
        _now = now;
        _serviceNames = serviceNames;
        _updatingServiceOptions = true;
        try
        {
            ServiceOptions.Clear();
            ServiceOptions.Add(new EventServiceFilter(null, "Tümü"));
            foreach (var service in serviceNames.OrderBy(item => item.Value, StringComparer.CurrentCultureIgnoreCase))
                ServiceOptions.Add(new EventServiceFilter(service.Key, service.Value));
        }
        finally
        {
            _updatingServiceOptions = false;
        }
        if (SelectedServiceId is not null &&
            !ServiceOptions.Any(service => string.Equals(service.Id, SelectedServiceId,
                StringComparison.OrdinalIgnoreCase)))
            SelectedServiceId = null;
        _selectedServiceOption = ServiceOptions.FirstOrDefault(option =>
            string.Equals(option.Id, SelectedServiceId, StringComparison.OrdinalIgnoreCase))
            ?? ServiceOptions[0];
        OnPropertyChanged(nameof(SelectedServiceOption));
        IsLoading = false;
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        var query = SearchText.Trim();
        var today = _now.Date;
        var matched = _records
            .Where(entry => SelectedCategory switch
            {
                "Sorunlar" => entry.Severity is ControlCenterEventSeverity.Warning
                    or ControlCenterEventSeverity.Error,
                "Onarımlar" => string.Equals(entry.Category, "recovery",
                    StringComparison.OrdinalIgnoreCase),
                "Yaşam döngüsü" => string.Equals(entry.Category, "operation",
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Category, "startup", StringComparison.OrdinalIgnoreCase),
                _ => true
            })
            .Where(entry => SelectedServiceId is null ||
                string.Equals(entry.McpId, SelectedServiceId, StringComparison.OrdinalIgnoreCase))
            .Where(entry => query.Length == 0 ||
                entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (entry.McpId?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .OrderByDescending(entry => entry.LastOccurredAtUtc)
            .GroupBy(entry => entry.LastOccurredAtUtc.ToOffset(_now.Offset).Date)
            .OrderByDescending(group => group.Key)
            .Select(group => new EventDayGroup(
                group.Key == today ? "Bugün" :
                group.Key == today.AddDays(-1) ? "Dün" :
                group.Key.ToString("d MMMM dddd", Turkish),
                group.Select(entry => new EventTimelineItem(
                    entry.LastOccurredAtUtc.ToOffset(_now.Offset).ToString("HH:mm", Turkish),
                    entry.Severity switch
                    {
                        ControlCenterEventSeverity.Error => "✕",
                        ControlCenterEventSeverity.Warning => "▲",
                        _ => "●"
                    },
                    entry.Severity,
                    FileLog.RedactSensitiveData(entry.Title),
                    FileLog.RedactSensitiveData(entry.Detail),
                    entry.McpId is { } id && _serviceNames.TryGetValue(id, out var name)
                        ? name : string.Empty,
                    entry.Count)).ToArray()))
            .ToArray();
        Groups.Clear();
        foreach (var group in matched)
            Groups.Add(group);
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateText));
    }
}
