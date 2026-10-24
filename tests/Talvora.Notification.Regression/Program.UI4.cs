using Talvora.Shared;
using Talvora.Tray;
using Talvora.Tray.ViewModels;

internal static partial class Program
{
    private static void AssertUi4ServiceDetails()
    {
        var now = new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);
        var core = new ManagedMcpRegistration
        {
            Id = "ui4-unit-core", DisplayName = "Talvora Çekirdeği",
            Description = "Yerel çekirdek",
            Endpoint = "http://127.0.0.1:7676/mcp"
        };
        var admin = core with
        {
            Id = "ui4-unit-admin", DisplayName = "Talvora Admin",
            Description = "ChatGPT Business için yönetim yüzeyi"
        };
        var source = new ControlCenterDashboardSnapshot(
            [
                new ManagedMcpDashboardState(core, ControlCenterHealthState.Ready, "Hazır", "Sağlam"),
                new ManagedMcpDashboardState(admin, ControlCenterHealthState.Attention, "Dikkat", "Yanıt gecikiyor.")
            ], "", "", 1, 1, 0);
        var model = new ServicesViewModel();
        model.ApplySnapshot(source, now, _ => false);
        if (model.Rows.Count != 2 || model.FilteredRows.Count != 2 ||
            model.Rows[0].Name != "Talvora Çekirdeği")
            throw new InvalidOperationException("UI4 service list did not map the snapshot.");
        model.SearchText = "Admin";
        if (model.FilteredRows.Count != 1 || model.FilteredRows[0].Name != "Talvora Admin")
            throw new InvalidOperationException("UI4 service name search is broken.");
        model.SearchText = "";
        model.StateFilter = "Dikkat istiyor";
        if (model.FilteredRows.Count != 1 || model.FilteredRows[0].Name != "Talvora Admin")
            throw new InvalidOperationException("UI4 service state filter is broken.");

        var detail = new ServiceDetailViewModel();
        detail.ApplyState(source.Mcps[1], now, manuallyStopped: false);
        if (detail.Name != "Talvora Admin" || !detail.CanRepair || !detail.CanStop ||
            detail.CanStart)
            throw new InvalidOperationException("UI4 attention/operation state is incorrect.");
        detail.BeginOperation("Yeniden başlat");
        detail.UpdateElapsed(TimeSpan.FromSeconds(3.4));
        if (!detail.IsBusy || detail.OperationElapsed != "00:00:03" ||
            detail.CanStop || detail.CanRestart || detail.CanRepair)
            throw new InvalidOperationException("UI4 operation did not lock actions or report real elapsed time.");
        detail.FinishOperation("Yeniden başlatıldı", succeeded: true);
        if (detail.IsBusy || detail.OperationResult != "Yeniden başlatıldı" ||
            !detail.HasOperationBanner)
            throw new InvalidOperationException("UI4 operation result did not persist.");
        detail.ApplyState(source.Mcps[1], now, manuallyStopped: true);
        if (detail.ManualStopMessage !=
                "Bu hizmeti siz durdurdunuz; Talvora otomatik başlatmayacak." ||
            !detail.CanStart || detail.CanStop || detail.CanRepair)
            throw new InvalidOperationException("UI4 manual stop semantics were lost.");
        detail.SetEvidence("Authorization: Bearer fake-ui4-test-value");
        if (detail.EvidenceText.Contains("fake-ui4-test-value", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UI4 technical evidence was not redacted.");

        var mixedLog = string.Join("\n", "--- tray.log ---", "MCP=other: unrelated operation",
            "MCP=ui4-unit-admin: selected service", "--- owned.log ---",
            "This record belongs to the service", "--- other.log ---", "foreign record");
        var isolated = ServiceEvidenceFilter.Filter(mixedLog, "ui4-unit-admin", ["owned.log"]);
        if (!isolated.Contains("selected service", StringComparison.Ordinal) ||
            !isolated.Contains("This record belongs", StringComparison.Ordinal) ||
            isolated.Contains("unrelated", StringComparison.Ordinal) ||
            isolated.Contains("foreign", StringComparison.Ordinal))
            throw new InvalidOperationException("UI4 selected service exposed another log source.");

        Console.WriteLine("UI4_DETAILS_GREEN");
    }
}
