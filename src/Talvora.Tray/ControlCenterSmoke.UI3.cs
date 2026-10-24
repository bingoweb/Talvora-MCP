using System.Diagnostics;
using System.Windows;
using Talvora.Shared;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    private static void AssertUi3VisualFixture(ShellWindow window, string variant)
    {
        var core = Registration("talvora", "Talvora Çekirdeği");
        var dev = Registration("talvora-dev", "Talvora Dev");
        var admin = Registration("talvora-admin", "Talvora Admin");
        var gitea = Registration("gitea", "Gitea");
        var penpot = Registration("penpot", "Penpot") with { AutoStart = false };
        var states = new[]
        {
            State(core, ControlCenterHealthState.Ready, "Hazır"),
            State(dev, ControlCenterHealthState.Ready, "Hazır"),
            State(admin, ControlCenterHealthState.Attention, "Yanıt gecikiyor."),
            State(gitea, ControlCenterHealthState.Ready, "Hazır"),
            State(penpot, ControlCenterHealthState.Offline, "İsteğe bağlı kapalı"),
        };
        var snapshot = new ControlCenterDashboardSnapshot(
            states, "1 bileşen dikkat istiyor", "", 3, 1, 0);
        var component = new ManagedMcpComponentRegistration
        {
            Id = "control-plane", DisplayName = "Bağlantı denetimi",
            Kind = "tunnel-health", Required = true,
        };
        var delivery = new ManagedMcpComponentRegistration
        {
            Id = "response-delivery", DisplayName = "Yanıt teslimi",
            Kind = "tunnel-health", Required = true,
        };
        var details = new (ManagedMcpRegistration Owner, ManagedMcpComponentState State)[]
        {
            (admin, new ManagedMcpComponentState(component,
                ControlCenterHealthState.Attention, "Yanıt gecikiyor.", "Kayıtlı denetim sonucu")),
            (admin, new ManagedMcpComponentState(delivery,
                ControlCenterHealthState.Ready, "Hazır", "Yanıt teslimi hazır")),
        };
        window.ShellModel.SelectPage(0);
        if (window.OverviewContentHost.Content is not Views.OverviewView overview)
            throw new InvalidOperationException("UI3 overview fixture did not open.");
        overview.ApplySnapshot(snapshot, DateTimeOffset.Now, []);
        overview.ApplyComponents(snapshot, details);
        window.UpdateLayout();
        if (overview.Model.AttentionCount != 1 || overview.Model.OptionalCount != 1 ||
            overview.Model.Segments.Count != 5 || overview.Model.ReadyFraction != "3/5")
            throw new InvalidOperationException("UI3 overview rendered unrelated status facts.");
        SaveVisualSnapshotIfRequested(window, "ui3-overview-" + variant);

        window.ShellModel.SelectPage(3);
        if (window.ChainContentHost.Content is not Views.ChainView chain)
            throw new InvalidOperationException("UI3 full chain fixture did not open.");
        chain.ApplySnapshot(snapshot, details);
        var selected = chain.FullChain.Nodes.FirstOrDefault(node =>
            node.Name == "Bağlantı denetimi")
            ?? throw new InvalidOperationException("UI3 full chain omitted real component health.");
        window.UpdateLayout();
        var button = EnumerateVisualElements(chain)
            .OfType<System.Windows.Controls.Button>()
            .FirstOrDefault(item => item.Tag is Controls.ChainNode node &&
                node.Name == selected.Name)
            ?? throw new InvalidOperationException("UI3 full chain node is not clickable.");
        button.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateLayout();
        if (chain.DetailsSheet.Visibility != Visibility.Visible ||
            chain.SelectedName.Text != selected.Name ||
            chain.SelectedStatus.Text != "Dikkat istiyor" ||
            chain.RepairLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("UI3 selected chain layer shows another component or unsupported repair.");
        SaveVisualSnapshotIfRequested(window, "ui3-chain-" + variant);
        window.ShellModel.SelectPage(0);
    }

    private static ManagedMcpRegistration Registration(string id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        Description = name,
        Endpoint = "http://127.0.0.1:7676/mcp",
    };

    private static ManagedMcpDashboardState State(ManagedMcpRegistration registration,
        ControlCenterHealthState health, string text) =>
        new(registration, health, text, text);

    private static async Task MeasureUi3IdleCpuAsync(ShellWindow window)
    {
        window.ShellModel.SelectPage(0);
        // Exclude the asynchronous initial health probes and the first WPF layout.
        await Task.Delay(2500);
        using var process = Process.GetCurrentProcess();
        var start = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        await Task.Delay(3000);
        watch.Stop();
        var cpu = (process.TotalProcessorTime - start).TotalMilliseconds /
            Math.Max(1, watch.Elapsed.TotalMilliseconds) /
            Math.Max(1, Environment.ProcessorCount) * 100.0;
        TrayLog.Write($"UI3 overview idle CPU: {cpu:0.00}% over {watch.ElapsedMilliseconds} ms.");
    }
}
