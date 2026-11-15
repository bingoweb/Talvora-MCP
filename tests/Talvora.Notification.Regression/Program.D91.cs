using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private sealed record D91Case(string Label, string? Path, object Result, string ExpectedTitle);

    private static async Task AssertD91MissingDeleteNarrativeAsync()
    {
        const string missingName = "olmayan-dosya.txt";
        var path = @"C:\scratch\" + missingName;
        var cases = new[]
        {
            new D91Case("missing object", path,
                new { path, deleted = false, kind = "missing" }, "Zaten yoktu · " + missingName),
            new D91Case("deleted true", path,
                new { path, deleted = true, kind = "file" }, "Silindi · " + missingName),
            new D91Case("deleted property absent", path,
                new { success = true }, "Silindi · " + missingName),
            new D91Case("missing without subject", null,
                new { deleted = false, kind = "missing" }, "Zaten yoktu"),
            new D91Case("string false is not boolean", path,
                new { deleted = "false", kind = "missing" }, "Silindi · " + missingName),
            new D91Case("structured missing", path,
                new CallToolResult
                {
                    Content = [],
                    StructuredContent = JsonSerializer.SerializeToElement(
                        new { path, deleted = false, kind = "missing" }),
                },
                "Zaten yoktu · " + missingName),
        };

        var builder = typeof(TalvoraDesktopProgressNotifier).GetMethod(
            "BuildNarrative", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("D91: narrative builder missing.");

        foreach (var row in cases)
        {
            Dictionary<string, JsonElement>? arguments = row.Path is null
                ? null
                : new(StringComparer.Ordinal)
                {
                    ["path"] = JsonSerializer.SerializeToElement(row.Path),
                };

            var expectedRunning = "Siliniyor" + (row.Path is null ? "" : " · " + missingName);
            var expectedFailed = "Silinemedi" + (row.Path is null ? "" : " · " + missingName);
            var narrative = builder.Invoke(null, ["talvora_delete", arguments, null])
                ?? throw new InvalidOperationException("D91: narrative unavailable.");
            var titles = narrative.GetType().GetProperty("Titles")?.GetValue(narrative) as WorklogTitles;
            var category = narrative.GetType().GetProperty("Category")?.GetValue(narrative)?.ToString();
            var subject = narrative.GetType().GetProperty("Subject")?.GetValue(narrative) as string;
            if (titles?.Running != expectedRunning || titles.Failed != expectedFailed ||
                titles.Succeeded != "Silindi" + (row.Path is null ? "" : " · " + missingName) ||
                category != "FileMutation" ||
                subject != (row.Path is null ? null : missingName))
                throw new InvalidOperationException($"D91 {row.Label}: original file narrative changed.");

            var recorded = new ConcurrentQueue<DesktopProgressMessage>();
            await using var notifier = CreateNotifier(recorded);
            await notifier.RunToolCallAsync<object>(
                "talvora_delete", arguments,
                _ => ValueTask.FromResult(row.Result),
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            var started = recorded.FirstOrDefault(m => m.Kind == DesktopProgressKind.Started);
            var ended = recorded.LastOrDefault(m => m.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
            if (started?.Title != expectedRunning ||
                ended?.Kind != DesktopProgressKind.Completed ||
                ended.Title != row.ExpectedTitle ||
                ended.WorklogFrame?.Current.Title != row.ExpectedTitle)
                throw new InvalidOperationException(
                    $"D91 RED {row.Label}: got {started?.Title} -> {ended?.Title} ({ended?.Kind}); " +
                    $"expected {expectedRunning} -> {row.ExpectedTitle}.");
        }

        Console.WriteLine("D91_MISSING_DELETE_GREEN");
    }
}
