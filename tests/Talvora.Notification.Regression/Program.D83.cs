using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private sealed record D83Case(
        string Label, string Tool, string Executable, string[] Arguments,
        WorklogTitles Titles, string Category, WorklogSubject.OperationKind Kind);

    private static async Task AssertD83CommandNarrativesAsync()
    {
        var tests = new WorklogTitles("Testler çalışıyor", "Testler geçti", "Testler başarısız");
        var build = new WorklogTitles("Derleniyor", "Derleme başarılı", "Derleme başarısız");
        var cargo = new WorklogTitles("Cargo komutu çalışıyor", "Cargo komutu tamamlandı", "Cargo komutu hata ile bitti");
        var uv = new WorklogTitles("uv komutu çalışıyor", "uv komutu tamamlandı", "uv komutu hata ile bitti");
        static WorklogTitles Subject(WorklogTitles titles, string part) =>
            new(titles.Running + " · " + part, titles.Succeeded + " · " + part, titles.Failed + " · " + part);
        static WorklogTitles Script(string name) =>
            new(name + " çalışıyor", name + " bitti", name + " hata ile bitti");
        const string verify = "Verification";
        const string process = "ProcessOperation";
        var testKind = WorklogSubject.OperationKind.Tests;
        var buildKind = WorklogSubject.OperationKind.Build;
        var programKind = WorklogSubject.OperationKind.Program;
        var rows = new[]
        {
            new D83Case("cargo test", "talvora_cargo_run", "cargo", ["test"], tests, process, testKind),
            new D83Case("cargo nextest", "talvora_cargo_run", "cargo", ["nextest", "run"], tests, process, testKind),
            new D83Case("cargo +nightly test", "talvora_user_run", "cargo.exe", ["+nightly", "test"], tests, verify, testKind),
            new D83Case("cargo case insensitive", "talvora_user_run", "CARGO.EXE", ["TEST"], tests, verify, testKind),
            new D83Case("cargo build", "talvora_cargo_run", "cargo", ["build", "--release"], build, process, buildKind),
            new D83Case("cargo build through user", "talvora_user_run", @"C:\Tools\cargo.exe", ["build"], build, verify, buildKind),
            new D83Case("cargo check", "talvora_cargo_run", "cargo", ["check"], Subject(cargo, "check"), process, programKind),
            new D83Case("cargo +nightly clippy", "talvora_user_run", "cargo", ["+nightly", "clippy"], Subject(cargo, "clippy"), process, programKind),
            new D83Case("cargo without command", "talvora_cargo_run", "cargo", ["--version"], cargo, process, programKind),
            new D83Case("uv run pytest", "talvora_uv_run", "uv", ["run", "pytest", "-q"], tests, process, testKind),
            new D83Case("uv with pytest-cov", "talvora_user_run", "uv.exe", ["run", "--with", "pytest-cov", "pytest"], tests, verify, testKind),
            new D83Case("uv python unittest", "talvora_uv_run", "uv", ["run", "python", "-m", "unittest"], tests, process, testKind),
            new D83Case("uv python pytest", "talvora_user_run", "uv", ["run", "python", "-m", "pytest"], tests, verify, testKind),
            new D83Case("uv case insensitive", "talvora_run_process", "UV.EXE", ["RUN", "PYTEST"], tests, verify, testKind),
            new D83Case("uv sync", "talvora_uv_run", "uv", ["sync"], Subject(uv, "sync"), process, programKind),
            new D83Case("uv run tool.py", "talvora_uv_run", "uv", ["run", "python", "tool.py"], Subject(uv, "run"), process, programKind),
            new D83Case("uv without command", "talvora_uv_run", "uv", ["--version"], uv, process, programKind),
            new D83Case("python unittest", "talvora_user_run", "python.exe", ["-m", "unittest"], tests, verify, testKind),
            new D83Case("python pytest", "talvora_python_run", "python", ["-m", "pytest", "-q"], tests, process, testKind),
            new D83Case("python3 pytest", "talvora_run_process", "python3", ["-m", "pytest"], tests, verify, testKind),
            new D83Case("python3 case insensitive", "talvora_user_run", "PYTHON3.EXE", ["-m", "UNITTEST"], tests, verify, testKind),
            new D83Case("py unittest", "talvora_user_run", "py.exe", ["-m", "unittest"], tests, verify, testKind),
            new D83Case("pytest exe", "talvora_user_run", "pytest.exe", ["-q"], tests, verify, testKind),
            new D83Case("python tool", "talvora_user_run", "python", ["tool.py"], Script("tool.py"), process, programKind),
            new D83Case("python module pip", "talvora_user_run", "python", ["-m", "pip", "list"], Script("pip"), process, programKind),
        };
        static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] values) =>
            values.ToDictionary(v => v.Name, v => JsonSerializer.SerializeToElement(v.Value));
        var builder = typeof(TalvoraDesktopProgressNotifier).GetMethod(
            "BuildNarrative", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("D83: narrative builder missing.");
        foreach (var row in rows)
        {
            var options = row.Tool is "talvora_cargo_run" or "talvora_uv_run" or "talvora_python_run"
                ? Args(("arguments", row.Arguments))
                : Args(("executable", row.Executable), ("arguments", row.Arguments));
            var actual = WorklogSubject.ForExecutable(row.Executable, row.Arguments);
            if (actual.Titles != row.Titles || actual.Kind != row.Kind)
                throw new InvalidOperationException($"D83 {row.Label}: classifier expected {row.Kind} / {row.Titles}, got {actual.Kind} / {actual.Titles}.");
            var narrative = builder.Invoke(null, [row.Tool, options, null]);
            var category = narrative?.GetType().GetProperty("Category")?.GetValue(narrative)?.ToString();
            if (category != row.Category)
                throw new InvalidOperationException($"D83 {row.Label}: category expected {row.Category}, got {category}.");
            foreach (var (exit, expectedTitle, kind, expectedState) in new[]
            {
                (0, row.Titles.Succeeded, DesktopProgressKind.Completed, WorklogCheckState.Succeeded),
                (1, row.Titles.Failed, DesktopProgressKind.Failed, WorklogCheckState.Failed),
            })
            {
                var recorded = new ConcurrentQueue<DesktopProgressMessage>();
                await using var notifier = CreateNotifier(recorded);
                await notifier.RunToolCallAsync<object>(row.Tool, options,
                    _ => ValueTask.FromResult<object>(new
                    {
                        exitCode = exit, timedOut = false, standardOutput = "",
                        standardError = exit == 0 ? "" : "fixture failure",
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var first = recorded.First(message => message.Kind == DesktopProgressKind.Started);
                var ended = recorded.Last(message =>
                    message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
                if (first.Title != row.Titles.Running || ended.Title != expectedTitle ||
                    ended.Kind != kind || ended.WorklogFrame?.Current.Title != expectedTitle)
                    throw new InvalidOperationException(
                        $"D83 {row.Label}: {first.Title} -> {ended.Title}; expected {row.Titles.Running} -> {expectedTitle}.");
                if (exit == 0 && ended.Evidence?.Result?.StartsWith(
                        "Sonuç: tamamlandı · ", StringComparison.Ordinal) != true)
                    throw new InvalidOperationException($"D83 {row.Label}: measured result evidence was lost.");
                var totals = ended.WorklogFrame?.Totals
                    ?? throw new InvalidOperationException($"D83 {row.Label}: no worklog totals.");
                if (totals.LastTests != (row.Kind == testKind ? expectedState : WorklogCheckState.Unknown) ||
                    totals.LastBuild != (row.Kind == buildKind ? expectedState : WorklogCheckState.Unknown))
                    throw new InvalidOperationException(
                        $"D83 {row.Label}: test/build state {totals.LastTests}/{totals.LastBuild}, expected {row.Kind} {expectedState}.");
            }
        }
        // A detached start uses the same command classifier but may not claim a completed test.
        foreach (var tool in new[] { "talvora_job_start", "talvora_user_process_start" })
        {
            var launched = builder.Invoke(null, [tool,
                Args(("executable", "cargo.exe"), ("arguments", new[] { "test" })), null]);
            var titles = launched?.GetType().GetProperty("Titles")?.GetValue(launched) as WorklogTitles;
            if (titles?.Running != tests.Running ||
                titles.Succeeded != "cargo test arka planda başladı" ||
                titles.Failed != "cargo test başlatılamadı")
                throw new InvalidOperationException($"D83 {tool}: detached test launch must not claim completion.");
        }
        // The metadata-only list tool must not gain a narrative or lose its original title.
        var listTitle = TalvoraToolTitles.Get("talvora_list")
            ?? throw new InvalidOperationException("D83: list MCP title missing.");
        var fallback = builder.Invoke(null, ["talvora_list", Args(("path", @"C:\repo")), null]);
        var title = fallback?.GetType().GetProperty("Titles")?.GetValue(fallback) as WorklogTitles;
        if (title?.Succeeded != listTitle + " · tamamlandı")
            throw new InvalidOperationException("D83: generic tool fallback changed.");
        Console.WriteLine("D83_COMMAND_NARRATIVES_GREEN");
    }
}
