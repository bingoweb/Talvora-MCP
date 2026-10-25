using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Talvora.Shared;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

/// <summary>UI5 event timeline and visibility-scoped 2-second raw-log tail.</summary>
internal sealed partial class EventsView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _rawLogTimer;
    private readonly SemaphoreSlim _rawLogReadGate = new(1, 1);
    private IReadOnlyList<ControlCenterEventRecord> _events = [];
    private IReadOnlyDictionary<string, string> _serviceNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private CancellationToken _lifetimeToken;
    private Window? _hostWindow;

    internal EventsView()
    {
        InitializeComponent();
        Model = new EventsViewModel();
        DataContext = Model;
        _rawLogTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _rawLogTimer.Tick += async (_, _) => await RefreshRawLogAsync();
        Model.PropertyChanged += OnModelChanged;
        IsVisibleChanged += (_, _) => UpdateRawLogTimerState();
        Loaded += (_, _) =>
        {
            var host = Window.GetWindow(this);
            if (!ReferenceEquals(_hostWindow, host))
            {
                if (_hostWindow is not null)
                    _hostWindow.StateChanged -= OnHostWindowStateChanged;
                _hostWindow = host;
                if (_hostWindow is not null)
                    _hostWindow.StateChanged += OnHostWindowStateChanged;
            }
            UpdateRawLogTimerState();
        };
        Unloaded += (_, _) =>
        {
            _rawLogTimer.Stop();
            if (_hostWindow is not null)
                _hostWindow.StateChanged -= OnHostWindowStateChanged;
            _hostWindow = null;
        };
    }

    internal EventsViewModel Model { get; }

    internal void FocusSearch()
    {
        EventSearchBox.Focus();
        EventSearchBox.SelectAll();
    }

    internal void SetLifetimeToken(CancellationToken token) => _lifetimeToken = token;

    internal void UpdateServices(ControlCenterDashboardSnapshot snapshot)
    {
        _serviceNames = snapshot.Mcps.ToDictionary(
            state => state.Registration.Id,
            state => state.Registration.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        if (_events.Count > 0)
            Model.ApplyEvents(_events, DateTimeOffset.Now, _serviceNames);
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var events = await ControlCenterEventStore.ReadRecentAsync(
                TimeSpan.FromDays(7), maxRecords: 250,
                cancellationToken: cancellationToken);
            if (cancellationToken.IsCancellationRequested || _lifetimeToken.IsCancellationRequested)
                return;
            _events = events;
            Model.ApplyEvents(events, DateTimeOffset.Now, _serviceNames);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested ||
                                                 _lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center UI5 event history read failed", ex);
            Model.IsLoading = false;
        }
    }

    internal void OpenRawLog() => Model.RawLogVisible = true;

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is EventsViewModel vm &&
            sender is System.Windows.Controls.RadioButton { Tag: string label })
            vm.SelectedCategory = label;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EventsViewModel.RawLogVisible)) return;
        UpdateRawLogTimerState();
        if (Model.RawLogVisible && _rawLogTimer.IsEnabled)
            _ = RefreshRawLogAsync();
    }

    private void UpdateRawLogTimerState()
    {
        if (IsVisible && Model.RawLogVisible &&
            _hostWindow?.WindowState != WindowState.Minimized &&
            !_lifetimeToken.IsCancellationRequested)
            _rawLogTimer.Start();
        else
            _rawLogTimer.Stop();
    }

    private void OnHostWindowStateChanged(object? sender, EventArgs e) =>
        UpdateRawLogTimerState();

    private async Task RefreshRawLogAsync()
    {
        if (!_rawLogTimer.IsEnabled || _lifetimeToken.IsCancellationRequested ||
            !await _rawLogReadGate.WaitAsync(0))
            return;
        try
        {
            var lines = await ControlCenterRawLogService.ReadTailAsync(
                cancellationToken: _lifetimeToken);
            if (_rawLogTimer.IsEnabled && IsVisible && Model.RawLogVisible &&
                _hostWindow?.WindowState != WindowState.Minimized &&
                !_lifetimeToken.IsCancellationRequested)
                Model.RawLogText = lines;
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center UI5 raw log tail failed", ex);
        }
        finally
        {
            _rawLogReadGate.Release();
        }
    }
}
