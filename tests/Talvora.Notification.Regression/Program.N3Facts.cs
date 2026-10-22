using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertN3FactsAsync()
    {
        static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

        async Task<DesktopProgressMessage> Complete(string tool, object result,
            Dictionary<string, JsonElement>? arguments = null)
        {
            var messages = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(messages);
            try
            {
                await notifier.RunToolCallAsync<object>(tool, arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None);
                if (arguments?.ContainsKey("inputFormat") == true)
                {
                    var initial = messages.First(message => message.Kind == DesktopProgressKind.Started);
                    if (initial.Evidence?.Files?.Count != 2 || initial.Evidence.AddedLines is not null ||
                        initial.Evidence.RemovedLines is not null)
                        throw new InvalidOperationException("N3: unified-diff headers or unapplied initial counts were wrong.");
                }
                return messages.Last(message => message.Kind is
                    DesktopProgressKind.Completed or DesktopProgressKind.Failed);
            }
            finally { await notifier.DisposeAsync(); }
        }

        const string diff = "--- a/a.cs\n+++ b/a.cs\n@@ -1 +1 @@\n-old\n+new\n--- a/b.cs\n+++ b/b.cs\n@@ -1 +1 @@\n-old\n+new\n";
        var patchArguments = Arguments(("workspaceRoot", "C:\\repo"), ("patch", diff), ("inputFormat", "unified-diff"));
        var rejected = await Complete("talvora_apply_patch",
            new { success = false, status = "rejected", files = Array.Empty<object>(),
                error = new { code = "SOURCE_EDIT_REVISION_MISMATCH" } }, patchArguments);
        if (rejected.Evidence?.AddedLines is not null || rejected.Evidence?.RemovedLines is not null ||
            rejected.WorklogFrame?.Current.FactLine?.Contains("+", StringComparison.Ordinal) == true)
            throw new InvalidOperationException("N3: a rejected patch claimed requested line counts.");

        var files = Enumerable.Range(0, 20).Select(index => new
        {
            operation = "update", newPath = $"C:\\repo\\{index}.cs",
            addedLines = 2, removedLines = 1,
        }).ToArray();
        var committed = await Complete("talvora_apply_patch",
            new { success = true, status = "committed", files }, patchArguments);
        var fact = committed.WorklogFrame?.Current.FactLine ?? string.Empty;
        if (!fact.Contains("20 dosya", StringComparison.Ordinal) ||
            !fact.Contains("+40", StringComparison.Ordinal) ||
            !fact.Contains("−20", StringComparison.Ordinal) ||
            !fact.Contains("ve 18 dosya", StringComparison.Ordinal) ||
            committed.WorklogFrame?.Totals.FilesChanged != 20)
            throw new InvalidOperationException("N3: receipt totals were missing or used the capped file list.");
        var twoFiles = await Complete("talvora_apply_patch",
            new { success = true, status = "committed", files = files.Take(2).ToArray() }, patchArguments);
        if (twoFiles.WorklogFrame?.Current.FactLine?.Contains("2 dosya", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("N3: a two-file unified diff did not report two applied files.");

        var build = await Complete("talvora_dotnet_build", new
        {
            exitCode = 1,
            summary = new { errors = 3, warnings = 0, diagnostics = new[] {
                new { file = "A.cs" }, new { file = "B.cs" }, new { file = "C.cs" } } },
        });
        if (build.WorklogFrame?.Current.Title != "Derleme başarısız · 3 hata" ||
            build.WorklogFrame.Current.FactLine?.Contains("A.cs", StringComparison.Ordinal) != true ||
            build.WorklogFrame.Current.FactLine?.Contains("B.cs", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("N3: failed build omitted the real errors or diagnostic file names.");

        var rawDiagnostics = string.Join("\n", new[] {
            "A.cs(1,1): error CS1001: first",
            "B.cs(2,1): error CS1002: second",
            "C.cs(3,1): error CS1003: third" });
        var rawBuild = await Complete("talvora_dotnet_build", new {
            exitCode = 1, standardOutput = rawDiagnostics + "\n" + rawDiagnostics + "\n3 Error(s)\n0 Warning(s)",
            standardError = "" });
        if (rawBuild.WorklogFrame?.Current.Title != "Derleme başarısız · 3 hata")
            throw new InvalidOperationException("N3: repeated console diagnostics overcounted build errors.");

        var tests = await Complete("talvora_dotnet_test", new
        {
            exitCode = 1,
            summary = new { errors = 0, warnings = 0, testsPassed = 148, testsFailed = 4,
                failedTests = new[] { "ParserTests.Push", "ParserTests.Pull", "ParserTests.Fetch", "ParserTests.Hidden" } },
        });
        var testsFact = tests.WorklogFrame?.Current.FactLine ?? string.Empty;
        if (!testsFact.Contains("4 test başarısız:", StringComparison.Ordinal) ||
            !testsFact.Contains("ParserTests.Fetch", StringComparison.Ordinal) ||
            testsFact.Contains("ParserTests.Hidden", StringComparison.Ordinal))
            throw new InvalidOperationException("N3: failed tests did not show the real total and at most three names.");

        var commit = await Complete("talvora_git_run", new
        {
            exitCode = 0, standardOutput = "[main a1b2c3d] fix(ui): actual subject\n 1 file changed",
            standardError = "", arguments = new[] { "commit", "-m", "requested subject" },
        }, Arguments(("arguments", new[] { "commit", "-m", "requested subject" })));
        if (commit.WorklogFrame?.Current.FactLine != "a1b2c3d · fix(ui): actual subject")
            throw new InvalidOperationException("N3: commit facts did not use the actual commit hash and subject.");

        var push = await Complete("talvora_git_run", new
        {
            exitCode = 0, standardOutput = "", standardError = "To https://example.invalid/repo\n abc..def main -> main",
            arguments = new[] { "push", "github", "main" },
        }, Arguments(("arguments", new[] { "push", "github", "main" })));
        if (push.WorklogFrame?.Current.FactLine != "github/main")
            throw new InvalidOperationException("N3: push facts omitted the actual result destination.");
    }
}
