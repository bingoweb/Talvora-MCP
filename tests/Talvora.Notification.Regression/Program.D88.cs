using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private sealed record D88Case(
        string Tool, string ArgumentName, string ArgumentPath, string FileName,
        string Running, string Succeeded, string Failed);

    private static async Task AssertD88FileOperationTitlesAsync()
    {
        var cases = new[]
        {
            new D88Case("talvora_delete", "path", @"C:\scratch\broken",
                "broken", "Siliniyor", "Silindi", "Silinemedi"),
            new D88Case("talvora_copy", "source", @"C:\scratch\from.txt",
                "from.txt", "Kopyalanıyor", "Kopyalandı", "Kopyalanamadı"),
            new D88Case("talvora_move", "destination", @"C:\scratch\to.txt",
                "to.txt", "Taşınıyor", "Taşındı", "Taşınamadı"),
            new D88Case("talvora_write_text", "path", @"C:\scratch\written.txt",
                "written.txt", "Dosya yazılıyor", "Dosya yazıldı", "Dosya yazılamadı"),
            new D88Case("talvora_append_text", "path", @"C:\scratch\notes.txt",
                "notes.txt", "Dosyaya ekleniyor", "Dosyaya eklendi", "Dosyaya eklenemedi"),
            new D88Case("talvora_file_hash", "path", @"C:\scratch\other.bin",
                "other.bin", "Dosyalar düzenleniyor", "Dosyalar düzenlendi",
                "Dosya işlemi tamamlanamadı"),
        };
        var build = typeof(TalvoraDesktopProgressNotifier).GetMethod(
            "BuildNarrative", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("D88: narrative builder not found.");
        static Dictionary<string, JsonElement> Options(string argumentName, string path) =>
            new(StringComparer.Ordinal)
            {
                [argumentName] = JsonSerializer.SerializeToElement(path),
            };
        foreach (var row in cases)
        {
            var args = Options(row.ArgumentName, row.ArgumentPath);
            var narrative = build.Invoke(null, [row.Tool, args, null])
                ?? throw new InvalidOperationException($"D88 {row.Tool}: no narrative.");
            var type = narrative.GetType();
            var titles = type.GetProperty("Titles")?.GetValue(narrative) as WorklogTitles;
            var category = type.GetProperty("Category")?.GetValue(narrative)?.ToString();
            var subject = type.GetProperty("Subject")?.GetValue(narrative) as string;
            var suffix = " · " + row.FileName;
            if (titles is null || titles.Running != row.Running + suffix ||
                titles.Succeeded != row.Succeeded + suffix ||
                titles.Failed != row.Failed + suffix ||
                subject != row.FileName || category != "FileMutation")
            {
                throw new InvalidOperationException(
                    $"D88 {row.Tool}: expected {row.Running}/{row.Succeeded}/{row.Failed}{suffix} in FileMutation, " +
                    $"got {titles?.Running}/{titles?.Succeeded}/{titles?.Failed} ({category}, {subject}).");
            }

            // file_hash deliberately stays silent in the desktop timeline; assert its
            // fallback title through BuildNarrative without changing visibility policy.
            if (row.Tool == "talvora_file_hash")
                continue;
            var events = new ConcurrentQueue<DesktopProgressMessage>();
            await using var notifier = CreateNotifier(events);
            await notifier.RunToolCallAsync<object>(
                row.Tool, args,
                _ => ValueTask.FromResult<object>(new { success = true }),
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            var started = events.First(e => e.Kind == DesktopProgressKind.Started);
            var ended = events.Last(e => e.Kind == DesktopProgressKind.Completed);
            if (started.Title != titles.Running || ended.Title != titles.Succeeded ||
                ended.WorklogFrame?.Current.Title != titles.Succeeded)
                throw new InvalidOperationException($"D88 {row.Tool}: actual successful worklog title differs.");
        }

        // No file path: no empty separator. Both the changed and generic branch preserve this rule.
        foreach (var tool in new[] { "talvora_delete", "talvora_file_hash" })
        {
            var narrative = build.Invoke(null, [tool, null, null])
                ?? throw new InvalidOperationException($"D88 {tool}: no path narrative.");
            var titles = narrative.GetType().GetProperty("Titles")?.GetValue(narrative) as WorklogTitles;
            var expected = tool == "talvora_delete" ? "Silindi" : "Dosyalar düzenlendi";
            if (titles?.Succeeded != expected)
                throw new InvalidOperationException($"D88 {tool}: suffix present with no filename.");
        }
        var preferred = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(@"C:\scratch\preferred.txt"),
            ["source"] = JsonSerializer.SerializeToElement(@"C:\scratch\source.txt"),
            ["destination"] = JsonSerializer.SerializeToElement(@"C:\scratch\destination.txt"),
        };
        var chosen = build.Invoke(null, ["talvora_copy", preferred, null])
            ?? throw new InvalidOperationException("D88: subject priority unavailable.");
        var copyTitles = chosen.GetType().GetProperty("Titles")?.GetValue(chosen) as WorklogTitles;
        if (copyTitles?.Succeeded != "Kopyalandı · preferred.txt")
            throw new InvalidOperationException("D88: path > source > destination priority changed.");

        Console.WriteLine("D88_FILE_OPERATION_TITLES_GREEN");
    }
}
