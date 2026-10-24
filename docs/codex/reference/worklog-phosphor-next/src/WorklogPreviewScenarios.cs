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
            ("09-one-step-receipt", OneStepReceipt(), false),
            ("10-details-open", DetailsOpen(), false),
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
        var model = Base("Classroom", "codex", WorklogVisualState.Running, "Kod değişikliği uygulanıyor · kiosk-routes.js, app.js ve 1 dosya");
        model.TaskNote = "Kiosk eşleştirme kodu sınırını düzelt";
        model.Purpose = "Bekleyen kod sınırı 5'ten 50'ye çıkıyor; sıra dolunca ekran 429 ile beklemeye alınıyor";
        model.SetTimeline(
        [
            new(WorklogRowState.Running, "14:02", "Kod değişikliği uygulanıyor · kiosk-routes.js, app.js ve 1 dosya"),
            new(WorklogRowState.Succeeded, "14:01", "Kaynaklar incelendi · 12 dosya"),
            new(WorklogRowState.Muted, "14:01", "Okuma başarısız · yanlış parametre"),
            new(WorklogRowState.Succeeded, "13:58", "Testler geçti · 1 837 test"),
        ]);
        Code(model, "diff · 14:02:11",
            "+++ b/backend/routes/kiosk-routes.js\n" +
            "@@ -38,7 +38,9 @@ router.post('/pairing-codes'\n" +
            " router.post('/pairing-codes', async (req, res) => {\n" +
            "-  const limit = 5;\n" +
            "+  const limit = 50;\n" +
            "+  if (pending.size >= limit) {\n" +
            "+    return res.status(429).set('Retry-After', '120').json({ error: 'Şu anda çok fazla bekleyen ekran kodu var. Lütfen birkaç dakika sonra tekrar deneyin.' });\n" +
            "   const code = createPairingCode();\n" +
            "+++ b/backend/app.js\n" +
            "@@ -12,3 +12,3 @@\n" +
            "-app.disable('x-powered-by')\n" +
            "+app.disable('x-powered-by');\n");
        model.Footer = "7 adım · 03:41 · 3 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel SuccessSession()
    {
        var model = Base("Talvora-MCP", "chatgpt·dev", WorklogVisualState.Succeeded, "Kalite kontrolü geçti · tam");
        model.TaskNote = "D12 — git pull oturum kullanıcısıyla çalışsın";
        model.FactLine = "56 kontrol · 0 hata";
        model.SetTimeline(
        [
            new(WorklogRowState.Succeeded, "16:21", "Kalite kontrolü geçti · tam"),
            new(WorklogRowState.Succeeded, "16:20", "Derleme başarılı · Talvora.csproj"),
            new(WorklogRowState.Succeeded, "16:18", "Kod değişikliği uygulandı · GitTools.cs ve 1 dosya"),
            new(WorklogRowState.Failed, "16:17", "Kod değişikliği uygulanamadı · dosya değişmişti"),
            new(WorklogRowState.Succeeded, "16:15", "Git işlemi tamamlandı · pull origin/main"),
            new(WorklogRowState.Succeeded, "16:14", "Görev bağlamı hazır"),
            new(WorklogRowState.Succeeded, "16:12", "Paketler hazır"),
        ]);
        model.Footer = "12 adım · 09:05 · 2 dosya değişti";
        return model;
    }

    private static WorklogCardViewModel BuildFailed()
    {
        var model = Base("Talvora-MCP", "chatgpt·dev", WorklogVisualState.Failed, "Derleme başarısız · Talvora.Tray.csproj · 3 hata");
        model.TaskNote = "UI1 — Kontrol Merkezi başlığı ve tema";
        model.FactLine = "3 hata · ControlCenterWindow.cs, WorklogTheme.xaml";
        model.OutcomeReason = "Derleme hata verdi. Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.";
        model.SetTimeline(
        [
            new(WorklogRowState.Failed, "17:44", "Derleme başarısız · Talvora.Tray.csproj · 3 hata"),
            new(WorklogRowState.Succeeded, "17:43", "Kod değişikliği uygulandı · ControlCenterWindow.cs"),
        ]);
        Code(model, "diff · 17:43:02",
            "+++ b/src/Talvora.Tray/ControlCenterWindow.cs\n" +
            "@@ -126,6 +126,8 @@ public ControlCenterWindow(bool smokeMode = false)\n" +
            "-        Title = \"Talvora Yönetim Merkezi\";\n" +
            "+        Title = \"Talvora Kontrol Merkezi\";\n" +
            "         Width = 1220;\n" +
            "… 2 satır daha · tamamı Çalışma günlüğünde\n");
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

    /// <summary>The showcase of §6.2/§6.3: what, where, how much and the exact characters that changed.</summary>
    private static WorklogCardViewModel SmallChange()
    {
        var model = Base("Talvora-MCP", "chatgpt·dev", WorklogVisualState.Succeeded, "Kod değişikliği uygulandı · GitTools.cs ve 1 dosya");
        model.TaskNote = "D12 — git pull oturum kullanıcısıyla çalışsın";
        model.Purpose = "git pull artık oturum kullanıcısının SSH anahtarını kullanıyor";
        model.FactLine = "2 dosya · +5 −1 · GitTools.cs, GitSafeDirectoryRegression.ps1";
        model.SetChanges(
        [
            ("GitTools.cs", 4, 0, ["ShouldUseInteractiveUserForRemoteGitAsync"]),
            ("GitSafeDirectoryRegression.ps1", 1, 1, []),
        ]);
        Code(model, "diff · 16:18:07",
            "+++ b/src/Talvora/Tools/GitTools.cs\n" +
            "@@ -790,6 +790,10 @@ private static async Task<bool> ShouldUseInteractiveUserForRemoteGitAsync(\n" +
            "                 string.Equals(\n" +
            "                     arguments[index],\n" +
            "                     \"fetch\",\n" +
            "-                    StringComparison.OrdinalIgnoreCase))\n" +
            "+                    StringComparison.OrdinalIgnoreCase) ||\n" +
            "+                string.Equals(\n" +
            "+                    arguments[index],\n" +
            "+                    \"pull\",\n" +
            "+                    StringComparison.OrdinalIgnoreCase))\n" +
            "+++ b/tests/GitSafeDirectoryRegression.ps1\n" +
            "@@ -36,1 +36,1 @@\n" +
            "-    $text -match '\"push\"[\\s\\S]*?\"fetch\"' -and\n" +
            "+    $text -match '\"push\"[\\s\\S]*?\"fetch\"[\\s\\S]*?\"pull\"' -and\n");
        model.SetTimeline(
        [
            new(WorklogRowState.Succeeded, "16:18", "Kod değişikliği uygulandı · GitTools.cs ve 1 dosya"),
            new(WorklogRowState.Succeeded, "16:15", "Git işlemi tamamlandı · pull origin/main"),
        ]);
        model.Footer = "6 adım · 02:44 · 2 dosya değişti";
        return model;
    }

    /// <summary>Exercises the real mapping path: protocol-v4 frame (with N10 fields) + unified diff → view model.</summary>
    private static WorklogCardViewModel FromFrame()
    {
        var at = new DateTimeOffset(2026, 10, 20, 11, 4, 30, TimeSpan.Zero);
        var frame = new Talvora.Shared.DesktopWorklogFrame(
            SessionId: "preview",
            Sequence: 1,
            TimestampUtc: at,
            Header: new Talvora.Shared.WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev", "Q6 — araç argümanları çağrıdan önce denetlensin"),
            Current: new Talvora.Shared.WorklogStep(
                Talvora.Shared.WorklogStepCategory.SourceEdit,
                Talvora.Shared.WorklogStepState.Succeeded,
                "Kod değişikliği uygulandı · Program.cs ve 1 dosya",
                "2 dosya · +5 −1 · Program.cs, ToolResultTextPolicy.cs",
                null,
                0.8,
                "Hatalı argümanlı çağrılar araç çalışmadan anlaşılır bir hatayla geri dönüyor",
                [
                    new("Program.cs", 4, 0, ["filters.AddCallToolFilter"]),
                    new("ToolResultTextPolicy.cs", 1, 1, []),
                ]),
            Timeline:
            [
                new(at, Talvora.Shared.WorklogStepState.Succeeded, "Kod değişikliği uygulandı · Program.cs ve 1 dosya"),
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

    /// <summary>
    /// A real one-step session exactly as installed N3b/N10 send it: receipt preview with "@@ FILE" and
    /// "---/+++" headers, declaration context from the first changed line, the code time from the change.
    /// </summary>
    private static WorklogCardViewModel OneStepReceipt()
    {
        var changed = new DateTimeOffset(2026, 10, 23, 20, 30, 12, TimeSpan.Zero);
        var frame = new Talvora.Shared.DesktopWorklogFrame(
            SessionId: "preview-one-step",
            Sequence: 3,
            TimestampUtc: changed.AddSeconds(40),
            Header: new Talvora.Shared.WorklogSessionHeader("d11live", "ChatGPT · Dev", "Tasarım denetimi — canlı kart örneği"),
            Current: new Talvora.Shared.WorklogStep(
                Talvora.Shared.WorklogStepCategory.SourceEdit,
                Talvora.Shared.WorklogStepState.Succeeded,
                "Kod değişikliği uygulandı · kiosk.js",
                "1 dosya · +1 −1 · kiosk.js",
                null,
                0.4,
                "Üst sınır 50 yerine 60 oldu",
                [new("kiosk.js", 1, 1, ["pair"])]),
            Timeline: [new(changed, Talvora.Shared.WorklogStepState.Succeeded, "Kod değişikliği uygulandı · kiosk.js")],
            Totals: new Talvora.Shared.WorklogTotals(1, 6, 1,
                Talvora.Shared.WorklogCheckState.Unknown, Talvora.Shared.WorklogCheckState.Unknown),
            Evidence: new Talvora.Shared.DesktopProgressEvidence(CodePreview:
                "@@ FILE kiosk.js\n" +
                "--- a/kiosk.js\n" +
                "+++ b/kiosk.js\n" +
                "@@ -1,4 +1,4 @@ function pair(req) {\n" +
                " const limit = 5;\n" +
                " function pair(req) {\n" +
                "-  return Math.min(limit * 10, 50);\n" +
                "+  return Math.min(limit * 10, 60);\n" +
                " }\n"),
            ExpectedNextUpdateSeconds: 15);
        var model = new WorklogCardViewModel();
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false, codeChangedUtc: changed);
        return model;
    }

    /// <summary>N6: a failed build with "ayrıntılar" open — technical name, result, files, duration, session.</summary>
    private static WorklogCardViewModel DetailsOpen()
    {
        var at = new DateTimeOffset(2026, 10, 24, 9, 12, 40, TimeSpan.Zero);
        var frame = new Talvora.Shared.DesktopWorklogFrame(
            SessionId: "3f9c2a71d0b84e55",
            Sequence: 12,
            TimestampUtc: at,
            Header: new Talvora.Shared.WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev", "UI1 — Kontrol Merkezi başlığı ve tema"),
            Current: new Talvora.Shared.WorklogStep(
                Talvora.Shared.WorklogStepCategory.Verification,
                Talvora.Shared.WorklogStepState.Failed,
                "Derleme başarısız · Talvora.Tray.csproj · 3 hata",
                "3 hata · ControlCenterWindow.cs, WorklogTheme.xaml",
                new Talvora.Shared.WorklogOutcome(
                    Talvora.Shared.WorklogFailureKind.BuildErrors,
                    "Derleme hata verdi. Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.",
                    null),
                18.4),
            Timeline:
            [
                new(at, Talvora.Shared.WorklogStepState.Failed, "Derleme başarısız · Talvora.Tray.csproj · 3 hata"),
                new(at.AddMinutes(-1), Talvora.Shared.WorklogStepState.Succeeded, "Kod değişikliği uygulandı · ControlCenterWindow.cs"),
            ],
            Totals: new Talvora.Shared.WorklogTotals(2, 58, 1,
                Talvora.Shared.WorklogCheckState.Failed, Talvora.Shared.WorklogCheckState.Unknown),
            Evidence: null,
            ExpectedNextUpdateSeconds: 15);
        var message = new Talvora.Shared.DesktopProgressMessage(
            frame.SessionId, "dotnet_build", frame.Current.Title, frame.Current.FactLine!,
            Talvora.Shared.DesktopProgressKind.Failed, at, 18.4,
            new Talvora.Shared.DesktopProgressEvidence(
                Summary: frame.Current.FactLine,
                Files: ["src/Talvora.Tray/ControlCenterWindow.cs", "src/Talvora.Tray/Worklog/WorklogTheme.xaml"],
                Result: "Sonuç: tamamlanamadı  //  kod 1"),
            Talvora.Shared.DesktopProgressLane.Worklog, 12, frame);
        var model = new WorklogCardViewModel { IsDetailsOpen = true };
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false, details: WorklogDetails.From(message));
        return model;
    }

    private static void Code(WorklogCardViewModel model, string header, string preview)
    {
        var (files, lines) = UnifiedDiffViewParser.Parse(preview);
        model.SetCode(header, files, lines);
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
