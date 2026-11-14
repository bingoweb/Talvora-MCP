using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Talvora.Tray.ViewModels;

/// <summary>Read-only history navigation, with independent search and filters (§8).</summary>
internal sealed class WorklogHistoryViewModel : ObservableObject
{
    private readonly WorklogHistoryStore _store;
    private IReadOnlyList<WorklogHistorySession> _all = [];
    private string _searchText = string.Empty;
    private string _projectFilter = "Tümü";
    private string _actorFilter = "Tümü";
    private string _outcomeFilter = "Tümü";
    private WorklogHistorySession? _selectedSession;

    public WorklogHistoryViewModel(WorklogHistoryStore store)
    {
        _store = store;
    }

    public ObservableCollection<WorklogHistorySession> Sessions { get; } = [];
    public ObservableCollection<string> ProjectOptions { get; } = ["Tümü"];
    public ObservableCollection<string> ActorOptions { get; } = ["Tümü"];
    public IReadOnlyList<string> OutcomeOptions { get; } =
        ["Tümü", "Tamamlandı", "Başarısız", "İptal edildi"];

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilters(); }
    }

    public string ProjectFilter
    {
        get => _projectFilter;
        set { if (SetProperty(ref _projectFilter, value)) ApplyFilters(); }
    }

    public string ActorFilter
    {
        get => _actorFilter;
        set { if (SetProperty(ref _actorFilter, value)) ApplyFilters(); }
    }

    public string OutcomeFilter
    {
        get => _outcomeFilter;
        set { if (SetProperty(ref _outcomeFilter, value)) ApplyFilters(); }
    }

    public WorklogHistorySession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedSession is not null;
    public bool IsEmpty => Sessions.Count == 0;

    // WL3: an empty list says why: nothing recorded yet, or nothing matches the search or the filters.
    public string EmptyText =>
        _all.Count == 0 ? "Henüz kayıtlı çalışma yok." :
        !string.IsNullOrWhiteSpace(SearchText) ? "Aramayla eşleşen çalışma yok." :
        "Bu filtrelerle eşleşen çalışma yok.";

    public void Reload()
    {
        var previousKey = SelectedSession?.HistoryKey;
        var previousProject = ProjectFilter;
        var previousActor = ActorFilter;
        _all = _store.LoadRecent();
        Populate(ProjectOptions, _all.Select(s => s.Header.ProjectName));
        Populate(ActorOptions, _all.Select(s => s.Header.ActorName));
        ProjectFilter = ProjectOptions.Contains(previousProject) ? previousProject : "Tümü";
        ActorFilter = ActorOptions.Contains(previousActor) ? previousActor : "Tümü";
        ApplyFilters();
        if (previousKey is not null) SelectedSession = Sessions.FirstOrDefault(s => s.HistoryKey == previousKey);
    }

    public void SelectSession(string sessionId) =>
        SelectedSession = Sessions.FirstOrDefault(s => s.SessionId == sessionId);

    private static void Populate(ObservableCollection<string> target, IEnumerable<string?> names)
    {
        var values = names.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase);
        target.Clear();
        target.Add("Tümü");
        foreach (var value in values) target.Add(value);
    }

    private void ApplyFilters()
    {
        var selectedKey = SelectedSession?.HistoryKey;
        var matching = _all.Where(session =>
            (ProjectFilter == "Tümü" ||
             string.Equals(session.Header.ProjectName, ProjectFilter, StringComparison.CurrentCultureIgnoreCase)) &&
            (ActorFilter == "Tümü" ||
             string.Equals(session.Header.ActorName, ActorFilter, StringComparison.CurrentCultureIgnoreCase)) &&
            (OutcomeFilter == "Tümü" ||
             string.Equals(session.OutcomeLabel, OutcomeFilter, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             new[] { session.Header.ProjectName, session.Header.ActorName, session.Header.TaskNote,
                     session.Current.Title, session.Current.FactLine }
                 .Any(value => value?.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) == true)));
        Sessions.Clear();
        foreach (var session in matching) Sessions.Add(session);
        SelectedSession = selectedKey is null ? null :
            Sessions.FirstOrDefault(s => s.HistoryKey == selectedKey);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
