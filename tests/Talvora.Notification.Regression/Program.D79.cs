using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD79TitlesAsync()
    {
        static Dictionary<string, JsonElement> Args(params (string Key, object Value)[] values) =>
            values.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value));

        var buildNarrative = typeof(TalvoraDesktopProgressNotifier).GetMethod(
            "BuildNarrative", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("D79: narrative builder missing.");

        async Task Check(string tool, Dictionary<string, JsonElement>? arguments,
            string running, string succeeded, string failed, string category)
        {
            var narrative = buildNarrative.Invoke(null, [tool, arguments, null]);
            var actualCategory = narrative?.GetType().GetProperty("Category")?.GetValue(narrative)?.ToString();
            if (actualCategory != category)
                throw new InvalidOperationException($"D79 {tool}: expected category {category}, got {actualCategory}.");

            foreach (var (exitCode, kind, title) in new[]
            {
                (0, DesktopProgressKind.Completed, succeeded),
                (1, DesktopProgressKind.Failed, failed),
            })
            {
                var recorded = new ConcurrentQueue<DesktopProgressMessage>();
                await using var notifier = CreateNotifier(recorded);
                object result = new
                {
                    exitCode,
                    timedOut = false,
                    standardOutput = "",
                    standardError = exitCode == 0 ? "" : "failure",
                };
                await notifier.RunToolCallAsync<object>(tool, arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None);
                var started = recorded.First(message => message.Kind == DesktopProgressKind.Started);
                var finished = recorded.Last(message =>
                    message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
                if (started.Title != running || finished.Kind != kind || finished.Title != title ||
                    finished.WorklogFrame?.Current.Title != title)
                    throw new InvalidOperationException(
                        $"D79 {tool}: expected '{running}' -> '{title}', got '{started.Title}' -> '{finished.Title}'.");
                if (exitCode == 0 && finished.Evidence?.Result?.StartsWith(
                        "Sonuç: tamamlandı · ", StringComparison.Ordinal) != true)
                    throw new InvalidOperationException($"D79 {tool}: measured duration was lost.");
            }
        }

        await Check("talvora_agent_brief", Args(("workspaceRoot", @"C:\repo")),
            "Proje talimatları alınıyor", "Proje talimatları alındı", "Proje talimatları alınamadı",
            "GenericExecution");
        await Check("talvora_gh_run", Args(("arguments", new[]
            { "run", "view", "43582882274", "--json", "jobs", "secret-token" })),
            "GitHub komutu çalışıyor · run view", "GitHub komutu tamamlandı · run view",
            "GitHub komutu hata ile bitti · run view", "ProcessOperation");
        await Check("talvora_gh_run", Args(("arguments", new[] { "pr" })),
            "GitHub komutu çalışıyor · pr", "GitHub komutu tamamlandı · pr",
            "GitHub komutu hata ile bitti · pr", "ProcessOperation");
        await Check("talvora_gh_run", Args(("arguments", new[] { "--verbose", "43582882274" })),
            "GitHub komutu çalışıyor", "GitHub komutu tamamlandı",
            "GitHub komutu hata ile bitti", "ProcessOperation");
        await Check("talvora_ping", null,
            "Bağlantı denetleniyor", "Bağlantı denetlendi", "Bağlantı denetlenemedi",
            "GenericExecution");
        await Check("talvora_note_write", Args(("path", @"C:\Vault\2026-11-14 Talvora denetim 57.md")),
            "Kasaya not yazılıyor · 2026-11-14 Talvora denetim 57.md",
            "Kasaya not yazıldı · 2026-11-14 Talvora denetim 57.md",
            "Kasaya not yazılamadı · 2026-11-14 Talvora denetim 57.md", "GenericExecution");
        await Check("talvora_note_write", null,
            "Kasaya not yazılıyor", "Kasaya not yazıldı", "Kasaya not yazılamadı",
            "GenericExecution");
        await Check("talvora_workspace_inspect", Args(("workspaceRoot", @"C:\repo")),
            "Proje inceleniyor", "Proje incelendi", "Proje incelenemedi",
            "GenericExecution");

        var unlistedTitle = TalvoraToolTitles.Get("talvora_list")
            ?? throw new InvalidOperationException("D79: unlisted tool must keep its MCP title.");
        var unlisted = buildNarrative.Invoke(null,
            ["talvora_list", Args(("path", @"C:\repo")), null]);
        var unlistedTitles = unlisted?.GetType().GetProperty("Titles")?.GetValue(unlisted) as WorklogTitles;
        if (unlistedTitles?.Running != unlistedTitle ||
            unlistedTitles.Succeeded != unlistedTitle + " · tamamlandı" ||
            unlistedTitles.Failed != unlistedTitle + " · tamamlanamadı")
            throw new InvalidOperationException("D79: unlisted tools must retain their generic titles.");
        Console.WriteLine("D79_TITLES_GREEN");
    }
}
