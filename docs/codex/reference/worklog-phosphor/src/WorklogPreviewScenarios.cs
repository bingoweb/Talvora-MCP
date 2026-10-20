using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Talvora.Tray.Worklog;

/// <summary>
/// Deterministic preview renders for design review (`Talvora.Tray.exe --worklog-preview &lt;outDir&gt;`).
/// Fixture data only; never shown on the user's desktop.
/// </summary>
internal static class WorklogPreviewScenarios
{
    internal const double CardWidth = 460;

    internal static IReadOnlyList<(string Name, WorklogCardViewModel Model, bool HighContrast)> All()
    {
        return
        [
            ("01-running-edit", RunningEdit(), false),
            ("02-success-session", SuccessSession(), false),
            ("03-build-failed", BuildFailed(), false),
            ("04-codex-running", CodexRunning(), false),
            ("05-waiting", Waiting(), false),
            ("06-small-change", SmallChange(), false),
            ("07-from-real-frame", FromFrame(), false),
            ("08-high-contrast", RunningEdit(), true),
        ];
    }

    /// <summary>Renders every scenario as PNG (150 % scale) into <paramref name="outputDirectory"/>.</summary>
    internal static IReadOnlyList<string> RenderAll(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        foreach (var (name, model, highContrast) in All())
        {
            model.AnimationsEnabled = false;
            var view = new WorklogCardView { DataContext = model, Width = CardWidth };
            view.ApplyContrast(highContrast);
            view.Measure(new System.Windows.Size(CardWidth, double.PositiveInfinity));
            view.Arrange(new Rect(view.DesiredSize));
            view.UpdateLayout();

            const double scale = 1.5;
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(view.ActualWidth * scale),
                (int)Math.Ceiling(view.ActualHeight * scale),
                96 * scale,
                96 * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(outputDirectory, name + ".png");
            using (var file = File.Create(path))
            {
                encoder.Save(file);
            }

            written.Add(path);
        }

        return written;
    }

    private static WorklogCardViewModel RunningEdit()
    {
        var model = Base("Classroom", "codex", WorklogVisualState.Running, "Kod değişikliği uygulanıyor");
        model.FactLine = "3 dosya · +41 −6 · kiosk-routes.js, kiosk-devices.js ve 1 dosya";
        model.SetTimeline(
        [
            new(WorklogRowState.Running, "14:02", "Kod değişikliği uygulanıyor"),
            new(WorklogRowState.Succeeded, "14:01", "Kaynaklar incelendi · 12 dosya"),
            new(WorklogRowState.Muted, "14:01", "Okuma başarısız · yanlış parametre"),
            new(WorklogRowState.Succeeded, "13:58", "Testler geçti · 1 837 test"),
        ]);
        model.SetCode(
            "diff · 14:02:11",
            [new("kiosk-routes.js", 38, 5), new("kiosk-devices.js", 2, 0), new("app.js", 1, 1)],
            [
                new(DiffLineKind.File, string.Empty, "backend/routes/kiosk-routes.js"),
                new(DiffLineKind.Hunk, string.Empty, "@@ -38,7 +38,9 @@ router.post('/pairing-codes'"),
                new(DiffLineKind.Context, "38", "router.post('/pairing-codes', async (req, res) => {"),
                new(DiffLineKind.Removed, "39", "  const limit = 5;"),
                new(DiffLineKind.Added, "39", "  const limit = 50;"),
                new(DiffLineKind.Added, "40", "  if (pending.size >= limit) {"),
                new(DiffLineKind.Added, "41", "    return res.status(429).set('Retry-After', '120').json({ error: 'Şu anda çok fazla bekleyen ekran kodu var. Lütfen birkaç dakika sonra tekrar deneyin.' });"),
                new(DiffLineKind.Context, "42", "  const code = createPairingCode();"),
                new(DiffLineKind.File, string.Empty, "backend/app.js"),
                new(DiffLineKind.Hunk, string.Empty, "@@ -12,3 +12,3 @@"),
                new(DiffLineKind.Removed, "13", "app.disable('x-powered-by')"),
                new(DiffLineKind.Added, "13", "app.disable('x-powered-by');"),
            ]);
        model.Footer = "7 adım · 03:41 · 3 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel SuccessSession()
    {
        var model = Base("Talvora-MCP", "chatgpt·dev", WorklogVisualState.Succeeded, "Testler geçti");
        model.FactLine = "148 test geçti · Talvora.Notification.Regression";
        model.SetTimeline(
        [
            new(WorklogRowState.Succeeded, "16:21", "Testler geçti · 148 test"),
            new(WorklogRowState.Succeeded, "16:20", "Derleme başarılı · 0 hata · 0 uyarı"),
            new(WorklogRowState.Succeeded, "16:18", "Kod değişikliği uygulandı · 2 dosya"),
            new(WorklogRowState.Failed, "16:17", "Kod değişikliği uygulanamadı · dosya değişmişti"),
            new(WorklogRowState.Succeeded, "16:15", "Kaynaklar incelendi · 4 dosya"),
            new(WorklogRowState.Succeeded, "16:14", "Kaynaklar incelendi · 9 dosya"),
            new(WorklogRowState.Succeeded, "16:12", "Paketler hazır"),
        ]);
        model.Footer = "12 adım · 09:05 · 2 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel BuildFailed()
    {
        var model = Base("Talvora-MCP", "chatgpt·dev", WorklogVisualState.Failed, "Derleme başarısız");
        model.FactLine = "3 hata · ControlCenterWindow.cs (2), WorklogTheme.xaml (1)";
        model.OutcomeReason = "Derleme 3 hata verdi.";
        model.OutcomeNextStep = "Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.";
        model.SetTimeline(
        [
            new(WorklogRowState.Failed, "17:44", "Derleme başarısız · 3 hata"),
            new(WorklogRowState.Succeeded, "17:43", "Kod değişikliği uygulandı · 1 dosya"),
        ]);
        model.SetCode(
            "diff · 17:43:02",
            [new("ControlCenterWindow.cs", 4, 2)],
            [
                new(DiffLineKind.File, string.Empty, "src/Talvora.Tray/ControlCenterWindow.cs"),
                new(DiffLineKind.Hunk, string.Empty, "@@ -126,6 +126,8 @@ public ControlCenterWindow(bool smokeMode = false)"),
                new(DiffLineKind.Removed, "126", "        Title = \"Talvora Yönetim Merkezi\";"),
                new(DiffLineKind.Added, "126", "        Title = \"Talvora Kontrol Merkezi\";"),
                new(DiffLineKind.Context, "127", "        Width = 1220;"),
                new(DiffLineKind.Note, string.Empty, "… 2 satır daha · tamamı Çalışma günlüğünde"),
            ]);
        model.Footer = "2 adım · 00:58 · 1 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel CodexRunning()
    {
        var model = Base("Classroom", "codex", WorklogVisualState.Running, "Codex Classroom üzerinde çalışıyor");
        model.FactLine = "Testleri çalıştırıyor · 23 komut, 4 araç çağrısı";
        model.SetTimeline(
        [
            new(WorklogRowState.Running, "09:31", "Testleri çalıştırıyor"),
            new(WorklogRowState.Succeeded, "09:29", "Dosya düzenledi · time-provider.js"),
            new(WorklogRowState.Succeeded, "09:27", "Kaynakları inceledi"),
        ]);
        model.Footer = "23 adım · 12:40 · 4 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel Waiting()
    {
        var model = Base("Classroom", "codex", WorklogVisualState.Waiting, "Yanıt bekleniyor");
        model.FactLine = "Codex 1 dk 10 sn'dir yeni bilgi göndermedi · son: 14:21:03";
        model.Footer = "31 adım · 18:02";
        return model;
    }

    private static WorklogCardViewModel SmallChange()
    {
        var model = Base("TahtaPilot", "chatgpt·dev", WorklogVisualState.Succeeded, "Kod değişikliği uygulandı");
        model.FactLine = "1 dosya · +1 −1 · tahta.css";
        model.SetCode(
            "diff · 10:05:44",
            [new("tahta.css", 1, 1)],
            [
                new(DiffLineKind.File, string.Empty, "public/css/tahta.css"),
                new(DiffLineKind.Removed, "212", "  font-size: 12px;"),
                new(DiffLineKind.Added, "212", "  font-size: 18px;"),
            ]);
        model.Footer = "1 adım · 00:02 · 1 dosya değişti";
        return model;
    }

    /// <summary>Exercises the real mapping path: protocol-v4 frame + unified diff preview → view model.</summary>
    private static WorklogCardViewModel FromFrame()
    {
        var at = new DateTimeOffset(2026, 10, 20, 11, 4, 30, TimeSpan.Zero);
        var frame = new Talvora.Shared.DesktopWorklogFrame(
            SessionId: "preview",
            Sequence: 1,
            TimestampUtc: at,
            Header: new Talvora.Shared.WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
            Current: new Talvora.Shared.WorklogStep(
                Talvora.Shared.WorklogStepCategory.Generic,
                Talvora.Shared.WorklogStepState.Succeeded,
                "Kod değişikliği uygulandı",
                "2 dosya · +3 −1 · ToolArgumentValidator.cs, Program.cs",
                null,
                0.8),
            Timeline:
            [
                new(at, Talvora.Shared.WorklogStepState.Succeeded, "Kod değişikliği uygulandı · 2 dosya"),
                new(at.AddMinutes(-2), Talvora.Shared.WorklogStepState.Failed, "Kod değişikliği uygulanamadı · dosya değişmişti"),
            ],
            Totals: new Talvora.Shared.WorklogTotals(4, 151, 2,
                Talvora.Shared.WorklogCheckState.Unknown, Talvora.Shared.WorklogCheckState.Unknown),
            Evidence: new Talvora.Shared.DesktopProgressEvidence(CodePreview:
                "diff --git a/src/Talvora/Program.cs b/src/Talvora/Program.cs\n" +
                "--- a/src/Talvora/Program.cs\n" +
                "+++ b/src/Talvora/Program.cs\n" +
                "@@ -55,3 +55,7 @@ filters.AddCallToolFilter(next => async (\n" +
                "         request,\n" +
                "         cancellationToken) =>\n" +
                "     {\n" +
                "+        if (ToolArgumentValidator.TryCreateError(request) is { } invalidArguments)\n" +
                "+        {\n" +
                "+            return invalidArguments;\n" +
                "+        }\n" +
                "+++ b/src/Talvora/ToolResultTextPolicy.cs\n" +
                "@@ -14,1 +14,1 @@\n" +
                "-    structured.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)\n" +
                "+    structured.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||\n"),
            ExpectedNextUpdateSeconds: 15);
        var model = new WorklogCardViewModel();
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false);
        return model;
    }

    private static WorklogCardViewModel Base(string project, string actor, WorklogVisualState state, string title) =>
        new()
        {
            Prompt = $"talvora@MSI:~/{project}",
            Actor = actor,
            State = state,
            Title = title,
        };
}
