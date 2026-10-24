using System.Windows;
using Talvora.Shared;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    private static void AssertUi4VisualFixture(ShellWindow window, string variant)
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
        var snapshot = new ControlCenterDashboardSnapshot(states, "", "", 3, 1, 1);
        window.ShellModel.SelectPage(1);
        if (window.ServicesContentHost.Content is not Views.ServicesView services)
            throw new InvalidOperationException("UI4 route did not load services.");

        services.ApplySnapshot(snapshot, DateTimeOffset.Now);
        services.Model.SearchText = "Admin";
        if (services.Model.FilteredRows.Count != 1 ||
            services.Model.FilteredRows[0].Name != "Talvora Admin")
            throw new InvalidOperationException("UI4 service search did not select Admin.");
        services.Model.SearchText = "";
        services.Model.StateFilter = "Dikkat istiyor";
        if (services.Model.FilteredRows.Count != 1)
            throw new InvalidOperationException("UI4 attention filter selected the wrong services.");
        services.Model.StateFilter = "Tümü";
        window.UpdateLayout();
        if (services.Model.FilteredRows.Count != states.Length)
            throw new InvalidOperationException("UI4 service list omitted one fixture.");
        SaveVisualSnapshotIfRequested(window, "ui4-services-" + variant);

        services.OpenDetail(states[2]);
        var detail = services.Detail;
        var component = new ManagedMcpComponentRegistration
        {
            Id = "control-plane", DisplayName = "Güvenli tünel",
            Kind = "tunnel-health", Required = true
        };
        detail.Model.SetDetails(
            [new ManagedMcpComponentState(component, ControlCenterHealthState.Attention,
                "Dikkat istiyor", "Yanıt gecikiyor.")],
            [new ControlCenterEventRecord
            {
                McpId = admin.Id, Title = "Yeniden deneme",
                Detail = "OpenAI tarafı yavaş yanıt veriyor.",
                LastOccurredAtUtc = DateTimeOffset.UtcNow
            }], "3.0.0");
        detail.Model.SetEvidence("Sürüm: 3.0.0" + Environment.NewLine + "Kayıt: Talvora Admin");
        window.UpdateLayout();
        if (detail.Model.Name != "Talvora Admin" || !detail.Model.CanRepair ||
            services.Detail.DetailTabs.Items.Count != 3)
            throw new InvalidOperationException("UI4 detail did not load approved actions/tabs.");
        SaveVisualSnapshotIfRequested(window, "ui4-detail-" + variant);
        services.BackToList();
        window.ShellModel.SelectPage(0);
    }

    private static async Task AssertUi4LifecycleSmokeAsync(ShellWindow window)
    {
        var fixtureId = "ui4-smoke-" + Guid.NewGuid().ToString("N");
        var registration = Registration(fixtureId, "UI4 Test MCP") with
        {
            HealthEndpoint = "http://127.0.0.1:7676/healthz",
            Components = [],
            Tunnel = null,
            ProtocolProbe = null,
        };
        var state = State(registration, ControlCenterHealthState.Ready, "Hazır");
        var snapshot = new ControlCenterDashboardSnapshot([state], "", "", 1, 0, 0);
        try
        {
            window.ShellModel.SelectPage(1);
            if (window.ServicesContentHost.Content is not Views.ServicesView services)
                throw new InvalidOperationException("UI4 live fixture did not open services.");

            services.ApplySnapshot(snapshot, DateTimeOffset.UtcNow);
            services.OpenDetail(state);
            if (!services.Detail.Model.CanStop)
                throw new InvalidOperationException("UI4 test MCP cannot be stopped from the detail.");
            window.SetUi4SmokeStopApproval(false);
            Click(services.Detail.StopButton);
            await window.AwaitUi4OperationForSmokeAsync();
            if (ManagedMcpSessionState.IsManuallyStopped(fixtureId))
                throw new InvalidOperationException("UI4 declined stop changed the manual intent.");

            window.SetUi4SmokeStopApproval(true);
            Click(services.Detail.StopButton);
            await window.AwaitUi4OperationForSmokeAsync();
            if (!ManagedMcpSessionState.IsManuallyStopped(fixtureId) ||
                !services.Detail.Model.HasManualStop || !services.Detail.Model.CanStart ||
                !services.Detail.Model.OperationSucceeded)
                throw new InvalidOperationException("UI4 UI stop failed or lost manual-stop notice.");

            Click(services.Detail.StartButton);
            await window.AwaitUi4OperationForSmokeAsync();
            if (ManagedMcpSessionState.IsManuallyStopped(fixtureId) ||
                !services.Detail.Model.OperationSucceeded)
                throw new InvalidOperationException("UI4 UI start did not clear manual-stop intent.");

            Click(services.Detail.RestartButton);
            await window.AwaitUi4OperationForSmokeAsync();
            if (!services.Detail.Model.OperationSucceeded ||
                ManagedMcpSessionState.IsManuallyStopped(fixtureId))
                throw new InvalidOperationException("UI4 UI restart failed.");

            var degraded = state with { Health = ControlCenterHealthState.Attention };
            services.Detail.Model.ApplyState(degraded, DateTimeOffset.Now, manuallyStopped: false);
            Click(services.Detail.RepairButton);
            await window.AwaitUi4OperationForSmokeAsync();
            if (!services.Detail.Model.OperationSucceeded ||
                services.Detail.Model.Source?.Health != ControlCenterHealthState.Ready)
                throw new InvalidOperationException("UI4 UI repair did not verify the harmless test MCP.");

            if (services.Detail.Model.OperationElapsed == string.Empty)
                throw new InvalidOperationException("UI4 operation banner did not expose elapsed time.");
            TrayLog.Write("UI4 test MCP lifecycle buttons and repair GREEN.");
            services.BackToList();
            window.ShellModel.SelectPage(0);
        }
        finally
        {
            ManagedMcpSessionState.ClearManualStop(fixtureId);
        }

        static void Click(System.Windows.Controls.Button button) =>
            button.RaiseEvent(new RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
}
