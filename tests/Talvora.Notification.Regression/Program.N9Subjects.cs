using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>N9: every step title names what it works on (DESIGN-WORKLOG §6.1).</summary>
    private static async Task AssertN9SubjectsAsync()
    {
        static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

        async Task<DesktopProgressMessage> Complete(string tool, Dictionary<string, JsonElement>? arguments,
            object result)
        {
            var messages = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(messages);
            try
            {
                await notifier.RunToolCallAsync<object>(tool, arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None);
                return messages.Last(message => message.Kind is
                    DesktopProgressKind.Completed or DesktopProgressKind.Failed);
            }
            finally { await notifier.DisposeAsync(); }
        }

        void Expect(DesktopProgressMessage message, string title, string? fact = null)
        {
            if (message.WorklogFrame?.Current.Title != title)
                throw new InvalidOperationException($"N9: expected title '{title}', got '{message.WorklogFrame?.Current.Title}'.");
            if (fact is not null && message.WorklogFrame?.Current.FactLine != fact)
                throw new InvalidOperationException($"N9: expected fact '{fact}', got '{message.WorklogFrame?.Current.FactLine}'.");
        }

        var ok = new { exitCode = 0, timedOut = false, standardOutput = "PASS Build\n56 kontrol, 0 hata\nHepsi geçti.\n", standardError = "" };
        Expect(await Complete("talvora_user_run",
                Arguments(("executable", "pwsh"), ("arguments", new[] { "-NoProfile", "-File", "scripts/Invoke-QualityGate.ps1", "-Scope", "Full" }),
                    ("workingDirectory", "C:\\repo")), ok),
            "Kalite kontrolü geçti · tam", "56 kontrol · 0 hata");

        var plain = new { exitCode = 0, timedOut = false, standardOutput = "", standardError = "" };
        Expect(await Complete("talvora_run_powershell",
                Arguments(("script", "pwsh -NoProfile -File scripts/Deploy-Windows-Installer.ps1 2>&1 | Select-Object -Last 5")), plain),
            WorklogCopy.Deploy.Succeeded);
        Expect(await Complete("talvora_user_run",
                Arguments(("executable", "dotnet"), ("arguments", new[] { "build", "src/Talvora/Talvora.csproj", "-c", "Release" })), plain),
            "Derleme başarılı · Talvora.csproj");
        Expect(await Complete("talvora_user_run",
                Arguments(("executable", "npm.cmd"), ("arguments", new[] { "run", "build" })), plain),
            "npm build bitti");
        Expect(await Complete("talvora_git_run",
                Arguments(("arguments", new[] { "pull", "--ff-only", "origin", "main" })), plain),
            "Git işlemi tamamlandı · pull origin/main");
        Expect(await Complete("talvora_dotnet_build",
                Arguments(("target", "C:\\repo\\Talvora.slnx")), new { exitCode = 0, summary = new { errors = 0, warnings = 2 } }),
            "Derleme başarılı · Talvora.slnx");

        // Unknown steps use the tool's approved Turkish title instead of "İşlem tamamlandı".
        Expect(await Complete("talvora_env_set", Arguments(("name", "X")), new { success = true }),
            "Ortam değişkenini ayarla · tamamlandı");
        var failed = await Complete("talvora_run_powershell", Arguments(("script", "Get-Date")),
            new { exitCode = 1, timedOut = false, standardOutput = "", standardError = "x" });
        Expect(failed, "PowerShell çalıştır · tamamlanamadı");

        var untitled = TalvoraToolManifest.Names.Where(name => TalvoraToolTitles.Get(name) is null).ToArray();
        if (untitled.Length > 0)
            throw new InvalidOperationException("N9: tools without an approved Turkish title: " + string.Join(", ", untitled));

        foreach (var message in new[] { failed })
        {
            var text = message.WorklogFrame!.Current.Title;
            if (text.Contains("İşlem yürütülüyor", StringComparison.Ordinal) ||
                text.Contains("talvora_", StringComparison.Ordinal) || text.Contains("Get-Date", StringComparison.Ordinal))
                throw new InvalidOperationException("N9: title showed a generic word, a tool name or a command line.");
        }
    }
}
