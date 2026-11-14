using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class Program
{
    private static async Task AssertWl5MeasuredTitlesAsync()
    {
        static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] values) =>
            values.ToDictionary(item => item.Name, item => JsonSerializer.SerializeToElement(item.Value));

        async Task Check(
            string scenario, string tool, Dictionary<string, JsonElement> arguments,
            object result, string running, string finished, string? measured = null,
            string? command = null, bool noParsedFact = false)
        {
            var recorded = new ConcurrentQueue<DesktopProgressMessage>();
            await using var notifier = CreateNotifier(recorded);
            await notifier.RunToolCallAsync<object>(tool, arguments,
                _ => ValueTask.FromResult(result), CancellationToken.None);
            var started = recorded.First(message => message.Kind == DesktopProgressKind.Started);
            var ended = recorded.Last(message => message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
            var timeline = ended.WorklogFrame?.Timeline.FirstOrDefault()?.Label;
            if (started.Title != running || ended.Title != finished || timeline != finished)
                throw new InvalidOperationException(
                    $"WL5 {scenario}: titles: '{started.Title}' -> '{ended.Title}', timeline '{timeline}', expected '{running}' -> '{finished}'.");
            if (measured is not null && ended.WorklogFrame?.Current.FactLine != measured)
                throw new InvalidOperationException(
                    $"WL5 {scenario}: measured '{ended.WorklogFrame?.Current.FactLine}' instead of '{measured}'.");
            if (noParsedFact && (ended.WorklogFrame?.Current.FactLine is not null ||
                ended.Evidence?.Summary is { } fact && !fact.StartsWith("Komut: ", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"D73 {scenario}: no parsed success fact is allowed: '{ended.WorklogFrame?.Current.FactLine}' / '{ended.Evidence?.Summary}'.");
            if (noParsedFact && ended.Kind == DesktopProgressKind.Completed &&
                ended.Evidence?.Result?.StartsWith("Sonuç: tamamlandı · ", StringComparison.Ordinal) != true)
                throw new InvalidOperationException(
                    $"D73 {scenario}: a successful quiet result must use the measured elapsed time.");
            if (command is not null && ended.Evidence?.Summary?.Contains(command, StringComparison.Ordinal) != true)
                throw new InvalidOperationException(
                    $"WL5 {scenario}: technical evidence missing '{command}': '{ended.Evidence?.Summary}'.");
            if (ended.WorklogFrame?.Current.FactLine?.Contains("Komut: ", StringComparison.Ordinal) == true)
                throw new InvalidOperationException("WL5: executable names must never appear in the visible fact line.");
            if (command is not null && (ended.Title.Contains("pwsh.exe", StringComparison.Ordinal) ||
                ended.Title.Contains("git ", StringComparison.Ordinal) ||
                ended.Title.Contains("npm ", StringComparison.Ordinal)))
                throw new InvalidOperationException("WL5: command name leaked into a title: " + ended.Title);
            if (ended.Kind == DesktopProgressKind.Completed &&
                (ended.Evidence?.Result is not { } text ||
                 !text.StartsWith("Sonuç: ", StringComparison.Ordinal) ||
                 text == "Sonuç: başarılı"))
                throw new InvalidOperationException($"WL5 {scenario}: result lacks measured evidence: '{ended.Evidence?.Result}'.");
            if (measured is not null && ended.Kind == DesktopProgressKind.Completed &&
                ended.Evidence?.Result != "Sonuç: " + measured)
                throw new InvalidOperationException(
                    $"WL5 {scenario}: result '{ended.Evidence?.Result}' does not match actual measured '{measured}'.");
        }

        if (WorklogSubject.FormatDuration(TimeSpan.FromMilliseconds(800)) != "0,8 sn" ||
            WorklogSubject.FormatDuration(TimeSpan.FromMilliseconds(12400)) != "12,4 sn" ||
            WorklogSubject.FormatDuration(TimeSpan.FromSeconds(252)) != "4 dk 12 sn" ||
            WorklogSubject.FormatDuration(TimeSpan.FromMinutes(63)) != "1 sa 3 dk")
            throw new InvalidOperationException("WL5: stopwatch duration must follow Turkish decimal and unit notation.");
        var noOutput = new { exitCode = 0, standardOutput = "", standardError = "" };
        await Check("inline", "talvora_run_powershell",
            Args(("script", "Get-Date")), noOutput, "Komut çalışıyor", "Komut tamamlandı",
            command: "Komut: pwsh.exe");
        await Check("test ps1", "talvora_user_run",
            Args(("executable", "pwsh.exe"), ("arguments", new[] { "-File", "tests/ModernizationPolicyRegression.ps1" })),
            new { exitCode = 0, standardOutput = "MODERNIZATION_POLICY_GREEN\n", standardError = "" },
            "Test betiği çalışıyor · ModernizationPolicyRegression.ps1",
            "Test betiği geçti · ModernizationPolicyRegression.ps1",
            command: "Komut: pwsh.exe");
        await Check(".NET test project", "talvora_user_run",
            Args(("executable", "dotnet"), ("arguments", new[] {
                "test", "tests/Talvora.Notification.Regression/Talvora.Notification.Regression.csproj" })),
            new { exitCode = 0, standardOutput = "Passed: 42, Failed: 0, Skipped: 0, Total: 42\n", standardError = "" },
            "Testler çalışıyor · Talvora.Notification.Regression",
            "Testler geçti · Talvora.Notification.Regression",
            "42 test geçti · 0 başarısız", "Komut: dotnet");
        await Check("git fetch new ref", "talvora_git_run",
            Args(("arguments", new[] { "fetch", "origin" })),
            new { exitCode = 0, standardOutput = "", standardError = "   abcd123..abcd456  main -> origin/main\n" },
            "Uzak depo denetleniyor · origin", "Uzak depo denetlendi · origin",
            "1 dalda yeni commit", "Komut: git fetch");
        await Check("git fetch unchanged", "talvora_git_run",
            Args(("arguments", new[] { "fetch", "origin" })), noOutput,
            "Uzak depo denetleniyor · origin", "Uzak depo denetlendi · origin",
            "yeni commit yok");
        var failedGit = new { exitCode = 128, standardOutput = "", standardError = "fatal: 'x' does not appear to be a git repository" };
        await Check("D73 failed fetch", "talvora_git_run",
            Args(("arguments", new[] { "fetch", "origin" })), failedGit,
            "Uzak depo denetleniyor · origin", "Uzak depo denetlenemedi · origin",
            command: "Komut: git fetch", noParsedFact: true);
        await Check("D73 quiet fetch", "talvora_git_run",
            Args(("arguments", new[] { "fetch", "-q", "origin" })), noOutput,
            "Uzak depo denetleniyor · origin", "Uzak depo denetlendi · origin",
            command: "Komut: git fetch", noParsedFact: true);
        await Check("D73 quiet long fetch", "talvora_git_run",
            Args(("arguments", new[] { "fetch", "--quiet", "origin" })), noOutput,
            "Uzak depo denetleniyor · origin", "Uzak depo denetlendi · origin",
            noParsedFact: true);
        await Check("git pull up to date", "talvora_git_run",
            Args(("arguments", new[] { "pull", "--ff-only", "origin", "main" })),
            new { exitCode = 0, standardOutput = "Already up to date.\n", standardError = "" },
            "Uzak değişiklikler alınıyor · origin/main", "Zaten güncel · origin/main",
            command: "Komut: git pull");
        await Check("git pull fast-forward", "talvora_git_run",
            Args(("arguments", new[] { "pull", "--ff-only", "origin", "main" })),
            new { exitCode = 0, standardOutput = "Updating abcd123..abcd456\nFast-forward\n 3 files changed, 4 insertions(+), 1 deletion(-)\n", standardError = "" },
            "Uzak değişiklikler alınıyor · origin/main", "Uzak değişiklikler alındı · origin/main",
            "3 dosya · +4 −1");
        await Check("git diff stats", "talvora_git_run",
            Args(("arguments", new[] { "diff", "--stat" })),
            new { exitCode = 0, standardOutput = " a.cs | 11 ++++++++---\n 2 files changed, 8 insertions(+), 3 deletions(-)\n", standardError = "" },
            "Değişiklikler inceleniyor", "Değişiklikler incelendi",
            "2 dosya · +8 −3", "Komut: git diff");
        await Check("git add explicit", "talvora_git_run",
            Args(("arguments", new[] { "add", "--", "a.cs", "b.cs" })), noOutput,
            "Dosyalar kayda hazırlanıyor", "Dosyalar kayda hazırlandı", "2 dosya");
        await Check("D73 failed add", "talvora_git_run",
            Args(("arguments", new[] { "add", "--", "a.cs" })), failedGit,
            "Dosyalar kayda hazırlanıyor", "Dosyalar kayda hazırlanamadı",
            noParsedFact: true);
        await Check("git add all staged", "talvora_git_run",
            Args(("arguments", new[] { "add", "-A" })),
            new { exitCode = 0, standardOutput = "", standardError = "", stagedFileCount = 3 },
            "Dosyalar kayda hazırlanıyor", "Dosyalar kayda hazırlandı", "3 dosya");
        await Check("git status files", "talvora_git_run",
            Args(("arguments", new[] { "status", "--short" })),
            new { exitCode = 0, standardOutput = " M a.cs\n?? b.cs\n", standardError = "" },
            "Çalışma alanı denetleniyor", "Çalışma alanı denetlendi",
            "2 değişmiş dosya");
        await Check("git status clean", "talvora_git_run",
            Args(("arguments", new[] { "status", "--short" })), noOutput,
            "Çalışma alanı denetleniyor", "Çalışma alanı denetlendi",
            "değişiklik yok");
        await Check("D73 failed status", "talvora_git_run",
            Args(("arguments", new[] { "status", "--short" })), failedGit,
            "Çalışma alanı denetleniyor", "Çalışma alanı denetlenemedi",
            noParsedFact: true);
        await Check("D73 status branch and file", "talvora_git_run",
            Args(("arguments", new[] { "status", "--short", "--branch" })),
            new { exitCode = 0, standardOutput = "## main...origin/main\n M a.cs\n", standardError = "" },
            "Çalışma alanı denetleniyor", "Çalışma alanı denetlendi", "1 değişmiş dosya");
        await Check("D73 status branch clean", "talvora_git_run",
            Args(("arguments", new[] { "status", "--short", "--branch" })),
            new { exitCode = 0, standardOutput = "## main...origin/main\n", standardError = "" },
            "Çalışma alanı denetleniyor", "Çalışma alanı denetlendi", "değişiklik yok");
        await Check("git log", "talvora_git_run",
            Args(("arguments", new[] { "log", "-2" })),
            new { exitCode = 0, standardOutput = "commit abcdef123\nAuthor: Test\n\ncommit abcdef456\nAuthor: Test\n", standardError = "" },
            "Geçmiş okunuyor", "Geçmiş okundu", "2 commit");
        await Check("git commit", "talvora_git_run",
            Args(("arguments", new[] { "commit", "-m", "A message" })),
            new { exitCode = 0, standardOutput = "[main a1b2c3d] A message\n 2 files changed, 3 insertions(+), 1 deletion(-)\n", standardError = "" },
            "Değişiklikler kaydediliyor", "Değişiklikler kaydedildi",
            "a1b2c3d · 2 dosya · +3 −1", "Komut: git commit");
        await Check("git push ref", "talvora_git_run",
            Args(("arguments", new[] { "push", "origin", "main" })),
            new { exitCode = 0, standardOutput = "", standardError = "To ssh://example.invalid/repo\n   a1b2c3d..b4c5d6e  main -> main\n" },
            "Uzak depoya gönderiliyor · origin/main", "Uzak depoya gönderildi · origin/main",
            "a1b2c3d..b4c5d6e", "Komut: git push");
        await Check("git push unchanged", "talvora_git_run",
            Args(("arguments", new[] { "push", "origin", "main" })),
            new { exitCode = 0, standardOutput = "Everything up-to-date\n", standardError = "" },
            "Uzak depoya gönderiliyor · origin/main", "Uzak depoya gönderildi · origin/main",
            "zaten güncel");
        await Check("git other", "talvora_git_run",
            Args(("arguments", new[] { "remote", "-v" })), noOutput,
            "Depo işlemi yapılıyor", "Depo işlemi tamamlandı",
            command: "Komut: git remote");
        await Check("npm script", "talvora_user_run",
            Args(("executable", "npm.cmd"), ("arguments", new[] { "run", "build" })), noOutput,
            "Komut çalışıyor", "Komut tamamlandı", command: "Komut: npm run build");
        await Check("PowerShell failure", "talvora_run_powershell",
            Args(("script", "throw 'Betiğin ilk hata satırı'")),
            new { exitCode = 1, standardOutput = "", standardError = "Betiğin ilk hata satırı\n   at line 2\n" },
            "Komut çalışıyor", "Komut hata ile bitti");
        await Check("D73 cmd inline failure", "talvora_user_run",
            Args(("executable", "cmd.exe"), ("arguments", new[] { "/d", "/c", "exit", "3" })),
            new { exitCode = 3, standardOutput = "", standardError = "command failed" },
            "Komut çalışıyor", "Komut hata ile bitti", command: "Komut: cmd.exe");
        await Check("D73 dotnet build measured only", "talvora_dotnet_build",
            Args(("target", "C:\\repo\\Talvora.slnx"), ("configuration", "Release")),
            new { exitCode = 0, summary = new { errors = 0, warnings = 2 } },
            "Derleniyor · Talvora.slnx", "Derleme başarılı · Talvora.slnx",
            "0 hata · 2 uyarı");
        var gateArguments = new[] { "-NoProfile", "-File", "scripts/Invoke-QualityGate.ps1" };
        foreach (var (scope, suffix) in new[] { ("", "tam"), ("Full", "tam"), ("Fast", "hızlı"), ("Changed", "değişenler") })
        {
            var options = scope.Length == 0 ? gateArguments : [.. gateArguments, "-Scope", scope];
            await Check("D73 gate " + (scope.Length == 0 ? "default" : scope), "talvora_user_run",
                Args(("executable", "pwsh.exe"), ("arguments", options)),
                new { exitCode = 0, standardOutput = "8 kontrol, 0 hata\nHepsi geçti.\n", standardError = "" },
                "Kalite kontrolü çalışıyor · " + suffix,
                "Kalite kontrolü geçti · " + suffix, "8 kontrol · 0 hata");
        }

        // The count for git add -A is measured by the real read-only staged diff, never guessed.
        var repository = Path.Combine(Path.GetTempPath(), "talvora-wl5-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            var initialized = await ProcessRunner.RunAsync("git", repository,
                ["init", "-q"], timeoutSeconds: 30);
            if (initialized.ExitCode != 0)
                throw new InvalidOperationException("WL5: temporary Git fixture could not initialize.");
            await File.WriteAllTextAsync(Path.Combine(repository, "a.cs"), "class A {}");
            await File.WriteAllTextAsync(Path.Combine(repository, "b.cs"), "class B {}");
            var staged = await GitTools.Run(repository, ["add", "-A"], explicitAdmin: true);
            if (staged.ExitCode != 0 || staged.StagedFileCount != 2)
                throw new InvalidOperationException("WL5: git add -A did not report the two actually staged paths.");
        }
        finally
        {
            // Git objects are read-only on Windows; clear the fixture's attributes before cleanup.
            foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(repository, recursive: true);
        }
        Console.WriteLine("WL5_MEASURED_RESULTS_GREEN");
    }
}
