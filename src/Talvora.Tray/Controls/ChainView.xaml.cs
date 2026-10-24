using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Talvora.Shared;
using Talvora.Tray.ViewModels;
using Point = System.Windows.Point;

namespace Talvora.Tray.Controls;

internal sealed record ChainNode(
    string Name,
    ComponentVisualState State,
    bool IsOptional,
    string StatusLabel,
    string PlainDetail,
    DateTimeOffset LastChecked,
    ManagedMcpRegistration? Parent,
    double X,
    double Y);

internal sealed class ChainLink : ObservableObject
{
    private bool _isFlowing;

    public ChainLink(PathGeometry geometry, bool bothReady)
    {
        Geometry = geometry;
        BothReady = bothReady;
    }

    public PathGeometry Geometry { get; }
    public bool BothReady { get; }
    public bool IsFlowing
    {
        get => _isFlowing;
        set => SetProperty(ref _isFlowing, value);
    }
}

/// <summary>§5.1/§5.2 constellation: one Canvas-based control used at two sizes.</summary>
internal sealed partial class ChainView : System.Windows.Controls.UserControl
{
    private bool _windowActive;
    private bool _isFull;
    private bool _isCompact;
    private IReadOnlyList<ChainNode> _source = [];
    private Window? _owner;
    public ObservableCollection<ChainNode> Nodes { get; } = [];
    public ObservableCollection<ChainLink> Links { get; } = [];
    public event Action<ChainNode>? NodeSelected;

    public ChainView()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (!ReferenceEquals(window, _owner))
            {
                if (_owner is not null)
                {
                    _owner.Activated -= OnWindowActivated;
                    _owner.Deactivated -= OnWindowDeactivated;
                    _owner.StateChanged -= OnWindowStateChanged;
                }
                _owner = window;
                if (_owner is not null)
                {
                    _owner.Activated += OnWindowActivated;
                    _owner.Deactivated += OnWindowDeactivated;
                    _owner.StateChanged += OnWindowStateChanged;
                }
            }
            _windowActive = window is { IsActive: true, WindowState: not WindowState.Minimized };
            UpdateMotion();
        };
        Unloaded += (_, _) =>
        {
            _windowActive = false;
            UpdateMotion();
        };
        IsVisibleChanged += (_, _) => UpdateMotion();
        SizeChanged += (_, _) =>
        {
            var width = Window.GetWindow(this)?.ActualWidth ?? ActualWidth;
            var compact = width > 0 && width < 1000;
            if (compact != _isCompact)
            {
                _isCompact = compact;
                Reflow();
            }
        };
    }

    public void Apply(
        ControlCenterDashboardSnapshot snapshot,
        IReadOnlyList<(ManagedMcpRegistration Owner, ManagedMcpComponentState State)> components,
        bool full)
    {
        _isFull = full;
        var now = DateTimeOffset.Now;
        var core = snapshot.Mcps.FirstOrDefault(s =>
            string.Equals(s.Registration.Id, "talvora", StringComparison.OrdinalIgnoreCase));
        var adminDev = snapshot.Mcps.Where(s =>
            s.Registration.DisplayName.Contains("Dev", StringComparison.OrdinalIgnoreCase) ||
            s.Registration.DisplayName.Contains("Admin", StringComparison.OrdinalIgnoreCase)).ToArray();
        var tunnels = snapshot.Mcps.Where(s => s.Registration.Tunnel is not null).ToArray();
        var delivery = components.FirstOrDefault(s => string.Equals(s.State.Component.Id,
            "response-delivery", StringComparison.OrdinalIgnoreCase));
        var baseNodes = new List<ChainNode>
        {
            Create("Bu bilgisayar", core, now),
            Create("Talvora çekirdeği", core, now),
            CreateAggregate("Dev/Admin yüzeyleri", adminDev, now),
            CreateAggregate("Güvenli tüneller", tunnels, now),
            delivery.State is { } deliveryHealth ?
                CreateComponent("ChatGPT Business", delivery.Owner, deliveryHealth, now) :
                new ChainNode("ChatGPT Business", ComponentVisualState.Neutral, false, "Bekliyor",
                    string.Empty, now, null, 0, 0),
        };
        var gitea = snapshot.Mcps.FirstOrDefault(s =>
            string.Equals(s.Registration.Id, "gitea", StringComparison.OrdinalIgnoreCase));
        if (gitea is not null) baseNodes.Add(Create("Gitea", gitea, now));
        var penpot = snapshot.Mcps.FirstOrDefault(s =>
            string.Equals(s.Registration.Id, "penpot", StringComparison.OrdinalIgnoreCase));
        if (penpot is not null)
            baseNodes.Add(Create("Penpot", penpot, now,
                optional: penpot.Health != ControlCenterHealthState.Ready));

        if (full)
        {
            foreach (var (registration, health) in components)
            {
                if (string.Equals(health.Component.Id, "response-delivery", StringComparison.OrdinalIgnoreCase))
                    continue;
                baseNodes.Add(CreateComponent(PlainComponentName(health.Component), registration, health, now));
            }
        }
        _source = baseNodes;
        Reflow();
    }

    private static ChainNode Create(string label, ManagedMcpDashboardState? state,
        DateTimeOffset when, bool optional = false)
    {
        var visual = state is null ? ComponentVisualState.Neutral :
            OverviewViewModel.VisualState(state.Health, optional);
        return new ChainNode(label, visual, optional, OverviewViewModel.Word(visual),
            state is null ? string.Empty : FileLog.RedactSensitiveData(state.StatusText),
            when, state?.Registration, 0, 0);
    }

    private static ChainNode CreateAggregate(string label,
        IReadOnlyList<ManagedMcpDashboardState> states, DateTimeOffset when)
    {
        var visual = states.Count == 0 ? ComponentVisualState.Neutral :
            states.Any(s => s.Health == ControlCenterHealthState.Offline) ? ComponentVisualState.Offline :
            states.Any(s => s.Health == ControlCenterHealthState.Attention) ? ComponentVisualState.Attention :
            states.All(s => s.Health == ControlCenterHealthState.Ready) ? ComponentVisualState.Ready :
            ComponentVisualState.Repairing;
        return new ChainNode(label, visual, false, OverviewViewModel.Word(visual),
            string.Join("; ", states.Select(s => FileLog.RedactSensitiveData(s.StatusText))),
            when, states.FirstOrDefault()?.Registration, 0, 0);
    }

    private static ChainNode CreateComponent(string label, ManagedMcpRegistration owner,
        ManagedMcpComponentState health, DateTimeOffset when)
    {
        var state = OverviewViewModel.VisualState(health.Health, !health.Component.Required);
        return new ChainNode(label, state, !health.Component.Required,
            OverviewViewModel.Word(state), FileLog.RedactSensitiveData(health.StatusText),
            when, owner, 0, 0);
    }

    private static string PlainComponentName(ManagedMcpComponentRegistration component) =>
        component.Id.ToLowerInvariant() switch
        {
            "control-plane" => "Bağlantı denetimi",
            "response-delivery" => "Yanıt teslimi",
            "queue" => "İstek kuyruğu",
            "dispatcher" => "İş dağıtımı",
            "backend" => "Arka uç",
            "proxy" => "Proxy",
            "mcp" => "Talvora çekirdeği",
            "tunnel" => "Güvenli tüneller",
            _ => component.DisplayName,
        };

    private void Reflow()
    {
        var count = _source.Count;
        var vertical = _isCompact;
        const int columns = 5;
        Graph.Width = vertical ? 330 : _isFull ? 670 : 1100;
        Graph.Height = vertical ? Math.Max(220, count * 96) :
            Math.Max(220, (int)Math.Ceiling(count / (double)columns) * 115);
        Nodes.Clear();
        Links.Clear();
        for (var index = 0; index < count; index++)
        {
            var column = vertical ? 0 : index % columns;
            var row = vertical ? index : index / columns;
            var node = _source[index] with
            {
                X = vertical ? 96 : 8 + column * (_isFull ? 132 : 218),
                Y = vertical ? 16 + row * 96 : 16 + row * 115,
            };
            Nodes.Add(node);
            if (index == 0) continue;
            // Gitea branches from the core; the technical layers branch from the tunnel.
            var prior = Nodes[index == 5 ? 1 : index == 6 ? 5 :
                _isFull && index >= 7 ? 3 : index - 1];
            var start = new Point(prior.X + 68, prior.Y + 7);
            var end = new Point(node.X + 68, node.Y + 7);
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new LineSegment(end, true));
            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            Links.Add(new ChainLink(geometry,
                prior.State == ComponentVisualState.Ready && node.State == ComponentVisualState.Ready));
        }
        UpdateMotion();
    }

    private void UpdateMotion()
    {
        var allowed = _windowActive && IsVisible &&
            SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        foreach (var link in Links)
            link.IsFlowing = allowed && link.BothReady;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _windowActive = _owner?.WindowState != WindowState.Minimized;
        UpdateMotion();
    }
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _windowActive = false;
        UpdateMotion();
    }
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        _windowActive = _owner is { IsActive: true, WindowState: not WindowState.Minimized };
        UpdateMotion();
    }

    private void OnNodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ChainNode node })
            NodeSelected?.Invoke(node);
    }
}
