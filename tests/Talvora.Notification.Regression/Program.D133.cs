using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD133CiBekleFollowedAsync()
    {
        const string executable = @"C:\Users\tayla\.claude\hooks\ci-bekle\bin\ci-bekle.exe";
        const string noVerdictTitle = "GitHub denetiminin sonucu alınamadı";
        const string noVerdictReason = "GitHub denetiminin sonucu alınamadı; ayrıntılar CI günlüğünde.";
        const string green = """
            CI d5f1c1d (bingoweb/Talvora-MCP):
            ✓ windows-ci (5 dk 9 sn)
            Sonuç: yeşil (tüm çalışmalar başarılı).
            Çalışma: https://github.com/bingoweb/Talvora-MCP/actions/runs/45482640778
            EXIT:0
            """;
        const string red = """
            CI d40e609 (bingoweb/TahtaPilot):
            ✗ ci (7 dk 12 sn, failure)
            Başarısız iş: gate — adım: Full gate (scripts/check.mjs)
                error: could not compile `tahtapilot-sahne` (bin "tahtapilot-sahne" test) due to 26 previous errors
                ##[error]Process completed with exit code 1.
            Sonuç: kırmızı (başarısız: ci).
            Çalışma: https://github.com/bingoweb/TahtaPilot/actions/runs/45462000351
            EXIT:1
            """;
        const string timeout = """
            CI d5f1c1d (bingoweb/Talvora-MCP):
            … windows-ci — 10 dakikada bitmedi (in_progress); süren işler: build
            Sonuç: belirsiz — CI 10 dakikada bitmedi.
            EXIT:2
            """;
        const string unavailable = """
            CI 13f3338 (bingoweb/Talvora-MCP):
            Sonuç: belirsiz — iptal edilen çalışmanın yerine geçen yok.
            EXIT:2
            """;

        var temporary = Path.Combine(Path.GetTempPath(), "talvora-d133-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(temporary, "workspace");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(Path.Combine(workspace, "artifacts"));

        Dictionary<string, JsonElement> Args(string[] argv) => new()
        {
            ["executable"] = JsonSerializer.SerializeToElement(executable),
            ["arguments"] = JsonSerializer.SerializeToElement(argv),
            ["workingDirectory"] = JsonSerializer.SerializeToElement(workspace),
        };

        string[] LogArguments(string log) =>
            ["--repo-dir", workspace, "--commit", "d5f1c1d",
                "--require-step", "Validate build and all test suites (including SourceEdit)", "--log", log];

        try
        {
            var startedMessages = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = CreateNotifier(startedMessages))
            {
                await notifier.RunToolCallAsync<object>("talvora_user_process_start",
                    Args(LogArguments("artifacts/a1-ci.log")),
                    _ => ValueTask.FromResult<object>(new { success = true }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                var started = startedMessages.LastOrDefault(m => m.Kind == DesktopProgressKind.Completed);
                if (started?.Title != "GitHub denetimi arka planda başladı")
                    throw new InvalidOperationException($"D133(a1): detached start title was '{started?.Title}'.");
            }

            async Task CheckAsync(string name, string sample, DesktopProgressKind kind,
                string title, string? fact = null, string? reason = null, string? technical = null,
                string? logArgument = null, bool equalsForm = false, bool checkLive = false)
            {
                var relative = logArgument ?? $"artifacts/{name}-ci.log";
                var samplePath = Path.Combine(workspace, name + "-sample.txt");
                var logPath = Path.GetFullPath(Path.Combine(workspace, relative));
                await File.WriteAllTextAsync(samplePath, sample);
                var startInfo = new ProcessStartInfo(@"C:\Program Files\PowerShell\7\pwsh.exe")
                {
                    WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
                };
                foreach (var arg in new string[] { "-NoProfile", "-Command",
                    $"Start-Sleep -Seconds 2; Copy-Item -LiteralPath '{samplePath.Replace("'", "''")}' -Destination '{logPath.Replace("'", "''")}'" })
                    startInfo.ArgumentList.Add(arg);
                string[] argv = equalsForm
                    ? ["--repo-dir", workspace, "--commit", "d5f1c1d",
                        "--require-step", "Validate build and all test suites (including SourceEdit)", "--log=" + relative]
                    : LogArguments(relative);
                var observed = new ConcurrentQueue<DesktopProgressMessage>();
                await using var notifier = CreateNotifier(observed);
                using var job = Process.Start(startInfo)!;
                await notifier.RunToolCallAsync<object>("talvora_user_process_start", Args(argv),
                    _ => ValueTask.FromResult<object>(new { success = true, processId = job.Id }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                if (checkLive)
                {
                    for (var i = 0; i < 30 && !observed.Any(m => m.Kind == DesktopProgressKind.Running); i++)
                        await Task.Delay(50);
                    if (!observed.Any(m => m.Kind == DesktopProgressKind.Running &&
                        m.Title == "GitHub denetimi sürüyor" &&
                        m.WorklogFrame?.Current.FactLine?.StartsWith("GitHub denetimi sürüyor · ", StringComparison.Ordinal) == true))
                        throw new InvalidOperationException("D133(a2): the live CI card must show its running fact.");
                }

                await job.WaitForExitAsync();
                for (var i = 0; i < 80 && !observed.Any(m =>
                    m.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed or DesktopProgressKind.Cancelled); i++)
                    await Task.Delay(50);
                var terminal = observed.LastOrDefault(m =>
                    m.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed or DesktopProgressKind.Cancelled);
                if (terminal?.Kind != kind || terminal.Title != title ||
                    (fact is not null && terminal.WorklogFrame?.Current.FactLine != fact) ||
                    (reason is not null && terminal.Message != reason) ||
                    (technical is not null && terminal.Evidence?.Result?.EndsWith(technical, StringComparison.Ordinal) != true))
                    throw new InvalidOperationException(
                        $"D133({name}): expected {kind} '{title}', observed {terminal?.Kind} '{terminal?.Title}', " +
                        $"fact '{terminal?.WorklogFrame?.Current.FactLine}', reason '{terminal?.Message}', result '{terminal?.Evidence?.Result}'.");
            }

            await CheckAsync("green", green, DesktopProgressKind.Completed, "GitHub denetimi geçti",
                fact: "5 dk 9 sn", checkLive: true);
            await CheckAsync("red", red, DesktopProgressKind.Failed, "GitHub denetimi başarısız",
                fact: "7 dk 12 sn", reason: "GitHub denetimi başarısız oldu; ayrıntılar CI günlüğünde.",
                technical: "✗ ci (7 dk 12 sn, failure)");
            await CheckAsync("timeout", timeout, DesktopProgressKind.Cancelled, "GitHub denetimi henüz bitmemişti",
                reason: "GitHub denetimi henüz bitmedi; sonucu bir sonraki adımda okunacak.",
                technical: "Sonuç: belirsiz — CI 10 dakikada bitmedi.");
            await CheckAsync("unavailable", unavailable, DesktopProgressKind.Cancelled, noVerdictTitle,
                reason: noVerdictReason, technical: "Sonuç: belirsiz — iptal edilen çalışmanın yerine geçen yok.");
            await CheckAsync("missing-exit", green[..green.LastIndexOf("EXIT:0", StringComparison.Ordinal)],
                DesktopProgressKind.Cancelled, noVerdictTitle, reason: noVerdictReason);
            await CheckAsync("panic", green.Replace("EXIT:0", "EXIT:101", StringComparison.Ordinal),
                DesktopProgressKind.Cancelled, noVerdictTitle, reason: noVerdictReason);
            await CheckAsync("equals", green, DesktopProgressKind.Completed, "GitHub denetimi geçti",
                fact: "5 dk 9 sn", logArgument: "artifacts/d133-ci.log", equalsForm: true);
            await CheckAsync("outside", green, DesktopProgressKind.Cancelled, noVerdictTitle,
                reason: noVerdictReason, logArgument: @"..\outside.log", checkLive: true);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }

        Console.WriteLine("D133_CI_BEKLE_FOLLOW_GREEN");
    }
}
