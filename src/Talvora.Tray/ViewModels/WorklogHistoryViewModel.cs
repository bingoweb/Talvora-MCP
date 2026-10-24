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

    public void Reload()
    {
        var previousSessionId = SelectedSession?.SessionId;
        var previousProject = ProjectFilter;
        var previousActor = ActorFilter;
        _all = _store.LoadRecent();
        Populate(ProjectOptions, _all.Select(s => s.Header.ProjectName));
        Populate(ActorOptions, _all.Select(s => s.Header.ActorName));
        ProjectFilter = ProjectOptions.Contains(previousProject) ? previousProject : "Tümü";
        ActorFilter = ActorOptions.Contains(previousActor) ? previousActor : "Tümü";
        ApplyFilters();
        if (previousSessionId is not null) SelectSession(previousSessionId);
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
        var selectedId = SelectedSession?.SessionId;
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
        SelectedSession = selectedId is null ? null :
            Sessions.FirstOrDefault(s => s.SessionId == selectedId);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
