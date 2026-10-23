using Talvora.Shared;
using Talvora.Tray.Worklog;

internal static partial class Program
{
    /// <summary>
    /// N6: "ayrıntılar" shows technical evidence only from the received (already redacted) message; the tool
    /// name appears in the details rows and nowhere else on the card; secrets never reach rows or clipboard text.
    /// </summary>
    private static void AssertN6Details()
    {
        const string secret = "sk-live-0123456789abcdef0123456789abcdef";
        var at = DateTimeOffset.UtcNow;
        var frame = new DesktopWorklogFrame(
            "n6-session-0001", 7, at,
            new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev", "N6 — ayrıntılar"),
            new WorklogStep(WorklogStepCategory.Verification, WorklogStepState.Failed,
                "Derleme başarısız · Talvora.Tray.csproj", "3 hata · ControlCenterWindow.cs",
                new WorklogOutcome(WorklogFailureKind.BuildErrors, "Derleme hata verdi.", null), 18.4),
            [new(at, WorklogStepState.Failed, "Derleme başarısız · Talvora.Tray.csproj")],
            new WorklogTotals(1, 18, 0, WorklogCheckState.Failed, WorklogCheckState.Unknown),
            null, 15);
        var sent = new DesktopProgressMessage(
            frame.SessionId, "dotnet_build", frame.Current.Title, "3 hata", DesktopProgressKind.Failed, at, 18.4,
            new DesktopProgressEvidence(
                Files: ["src/Talvora.Tray/ControlCenterWindow.cs"],
                Result: $"Sonuç: tamamlanamadı  //  api_key={secret}"),
            DesktopProgressLane.Worklog, 7, frame);

        // Through the real wire contract: the protocol redacts before the tray ever sees the text.
        if (!DesktopProgressProtocol.TryDeserialize(DesktopProgressProtocol.Serialize(sent), out var received) || received is null)
            throw new InvalidOperationException("N6: the details fixture did not survive the protocol.");

        var model = new WorklogCardViewModel { IsDetailsOpen = true };
        WorklogCardMapper.Apply(model, received.WorklogFrame!, "MSI", stale: false, details: WorklogDetails.From(received));
        if (!model.HasDetails || !model.ShowDetails || model.DetailRows.Count < 4)
            throw new InvalidOperationException("N6: the details rows were not built.");
        if (model.DetailRows[0].Value != "Projeyi derle (talvora_dotnet_build)")
            throw new InvalidOperationException("N6: 'adım' must name the approved title and the technical tool: " + model.DetailRows[0].Value);
        var detailsText = string.Join("\n", model.DetailRows.Select(row => row.Value)) + "\n" + model.DetailsCopyText;
        if (detailsText.Contains(secret, StringComparison.Ordinal))
            throw new InvalidOperationException("N6: a secret reached the details rows or the copied text.");

        var plainText = string.Join("\n", new[]
            {
                model.Title, model.FactLine, model.ResultLine, model.OutcomeReason, model.OutcomeNextStep, model.Purpose,
                model.TaskNote, model.Footer,
            }.Concat(model.Timeline.Select(row => row.Label)));
        if (plainText.Contains("dotnet_build", StringComparison.Ordinal) || plainText.Contains("talvora_", StringComparison.Ordinal))
            throw new InvalidOperationException("N6: the technical tool name leaked outside the details panel.");

        model.IsDetailsOpen = false;
        if (model.ShowDetails || model.DetailsToggleText != "ayrıntılar ▸")
            throw new InvalidOperationException("N6: the expander did not close.");
    }
}
