using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;

namespace Talvora.Tray.ViewModels;

internal sealed record ServiceDetailComponent(
    string Name, string Status, ComponentVisualState State, string Detail);
internal sealed record ServiceDetailEvent(string Time, string Title, string Detail);

/// <summary>UI4: a service's status, events, evidence and real-time operation banner.</summary>
internal sealed class ServiceDetailViewModel : ObservableObject
{
    private ManagedMcpDashboardState? _source;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _role = string.Empty;
    private string _statusLabel = string.Empty;
    private string _statusDetail = string.Empty;
    private ComponentVisualState _state = ComponentVisualState.Neutral;
    private string _lastChecked = string.Empty;
    private string _lastSuccess = string.Empty;
    private string _version = string.Empty;
    private string _manualStopMessage = string.Empty;
    private string _evidenceText = string.Empty;
    private bool _isBusy;
    private bool _hasOperationBanner;
    private bool _operationSucceeded;
    private string _operationName = string.Empty;
    private string _operationElapsed = "00:00";
    private string _operationResult = string.Empty;
    private bool _canStart;
    private bool _canStop;
    private bool _canRestart;
    private bool _canRepair;
    private bool _isLoading;

    public ObservableCollection<ServiceDetailEvent> Events { get; } = [];
    public ObservableCollection<ServiceDetailComponent> Components { get; } = [];

    public ManagedMcpDashboardState? Source => _source;
    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    public string Role { get => _role; private set => SetProperty(ref _role, value); }
    public string StatusLabel { get => _statusLabel; private set => SetProperty(ref _statusLabel, value); }
    public string StatusDetail { get => _statusDetail; private set => SetProperty(ref _statusDetail, value); }
    public ComponentVisualState State { get => _state; private set => SetProperty(ref _state, value); }
    public string LastChecked { get => _lastChecked; private set => SetProperty(ref _lastChecked, value); }
    public string LastSuccess { get => _lastSuccess; private set => SetProperty(ref _lastSuccess, value); }
    public string Version { get => _version; set => SetProperty(ref _version, value); }
    public string ManualStopMessage { get => _manualStopMessage; private set => SetProperty(ref _manualStopMessage, value); }
    public string EvidenceText { get => _evidenceText; private set => SetProperty(ref _evidenceText, value); }
    public bool HasManualStop => ManualStopMessage.Length != 0;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasOperationBanner { get => _hasOperationBanner; private set => SetProperty(ref _hasOperationBanner, value); }
    public bool OperationSucceeded { get => _operationSucceeded; private set => SetProperty(ref _operationSucceeded, value); }
    public bool OperationIsRunning => HasOperationBanner && IsBusy;
    public bool OperationIsFinished => HasOperationBanner && !IsBusy;
    public string OperationName { get => _operationName; private set => SetProperty(ref _operationName, value); }
    public string OperationElapsed { get => _operationElapsed; private set => SetProperty(ref _operationElapsed, value); }
    public string OperationResult { get => _operationResult; private set => SetProperty(ref _operationResult, value); }
    public bool CanStart { get => _canStart; private set => SetProperty(ref _canStart, value); }
    public bool CanStop { get => _canStop; private set => SetProperty(ref _canStop, value); }
    public bool CanRestart { get => _canRestart; private set => SetProperty(ref _canRestart, value); }
    public bool CanRepair { get => _canRepair; private set => SetProperty(ref _canRepair, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public void ApplyState(ManagedMcpDashboardState source, DateTimeOffset checkedAt, bool manuallyStopped)
    {
        _source = source;
        Name = source.Registration.DisplayName;
        Description = FileLog.RedactSensitiveData(source.Registration.Description);
        Role = source.Registration.Tunnel is not null ||
            source.Registration.Id is "talvora-dev" or "talvora-admin" ? "Tünel" :
            source.Registration.Id.Equals("talvora", StringComparison.OrdinalIgnoreCase) ? "Yerel MCP" :
            source.Registration.AutoStart ? "Yerel zincir" : "İsteğe bağlı";
        State = OverviewViewModel.VisualState(source.Health,
            ManagedMcpAggregateHealthPolicy.IsIgnored(source.Registration, source.Health, manuallyStopped));
        StatusLabel = OverviewViewModel.Word(State);
        StatusDetail = FileLog.RedactSensitiveData(source.StatusText + " — " + source.Detail);
        LastChecked = "Son kontrol " + checkedAt.ToLocalTime().ToString("HH:mm:ss",
            System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        ManualStopMessage = manuallyStopped
            ? "Bu hizmeti siz durdurdunuz; Talvora otomatik başlatmayacak."
            : string.Empty;
        OnPropertyChanged(nameof(HasManualStop));
        RefreshActionState();
    }

    public void SetDetails(
        IReadOnlyList<ManagedMcpComponentState> components,
        IReadOnlyList<ControlCenterEventRecord> events,
        string version)
    {
        Components.Clear();
        foreach (var item in components)
            Components.Add(new ServiceDetailComponent(
                item.Component.DisplayName, FileLog.RedactSensitiveData(item.StatusText),
                OverviewViewModel.VisualState(item.Health,
                    !item.Component.Required && item.Health != ControlCenterHealthState.Ready),
                FileLog.RedactSensitiveData(item.Detail)));

        Events.Clear();
        foreach (var entry in events
            .Where(x => string.Equals(x.McpId, _source?.Registration.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastOccurredAtUtc)
            .Take(40))
        {
            Events.Add(new ServiceDetailEvent(
                entry.LastOccurredAtUtc.ToLocalTime().ToString("dd.MM HH:mm"),
                FileLog.RedactSensitiveData(entry.Title),
                FileLog.RedactSensitiveData(entry.Detail)));
        }
        var lastSuccess = events
            .Where(entry =>
                string.Equals(entry.McpId, _source?.Registration.Id, StringComparison.OrdinalIgnoreCase) &&
                entry.Severity == ControlCenterEventSeverity.Info &&
                entry.Category == "connection")
            .OrderByDescending(entry => entry.LastOccurredAtUtc).FirstOrDefault();
        LastSuccess = lastSuccess is null ? "" :
            "Son başarılı bağlantı " + lastSuccess.LastOccurredAtUtc.ToLocalTime().ToString("HH:mm");
        Version = FileLog.RedactSensitiveData(version);
        IsLoading = false;
    }

    public void SetEvidence(string value)
    {
        EvidenceText = FileLog.RedactSensitiveData(value);
    }

    public void BeginOperation(string operation)
    {
        IsBusy = true;
        HasOperationBanner = true;
        OperationSucceeded = false;
        OperationName = operation;
        OperationResult = string.Empty;
        OperationElapsed = "00:00";
        NotifyOperationState();
        RefreshActionState();
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        if (!IsBusy) return;
        OperationElapsed = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public void FinishOperation(string result, bool succeeded)
    {
        IsBusy = false;
        OperationResult = FileLog.RedactSensitiveData(result);
        OperationSucceeded = succeeded;
        NotifyOperationState();
        RefreshActionState();
    }

    public void ResetBanner()
    {
        IsBusy = false;
        HasOperationBanner = false;
        OperationResult = string.Empty;
        RefreshActionState();
        NotifyOperationState();
    }

    private void NotifyOperationState()
    {
        OnPropertyChanged(nameof(OperationIsRunning));
        OnPropertyChanged(nameof(OperationIsFinished));
    }

    private void RefreshActionState()
    {
        var stopped = HasManualStop || _source?.Health == ControlCenterHealthState.Offline;
        var available = _source is not null && !IsBusy;
        CanStart = available && stopped;
        CanStop = available && !stopped;
        CanRestart = available && !stopped;
        CanRepair = available && !HasManualStop &&
            _source?.Health is ControlCenterHealthState.Attention or ControlCenterHealthState.Offline;
    }
}
