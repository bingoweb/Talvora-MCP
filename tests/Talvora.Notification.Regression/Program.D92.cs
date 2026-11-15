using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    private static async Task AssertD92EveryStepPurposeAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d92-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        var historyPath = Path.Combine(directory, "worklog-sessions.jsonl");
        var recorded = new ConcurrentQueue<DesktopProgressMessage>();
        try
        {
            await using (var notifier = CreateNotifier(recorded))
            {
                foreach (var purpose in new string?[]
                {
                    "İlk değişikliğin nedeni kaydediliyor",
                    "İkinci işlem için kontrol yapılıyor",
                    null,
                })
                {
                    var arguments = new Dictionary<string, JsonElement>
                    {
                        ["repositoryPath"] = JsonSerializer.SerializeToElement(directory),
                        ["arguments"] = JsonSerializer.SerializeToElement(new[] { "status", "--short" }),
                    };
                    if (purpose is not null)
                        arguments["purpose"] = JsonSerializer.SerializeToElement(purpose);
                    await notifier.RunToolCallAsync<object>("talvora_git_run", arguments,
                        _ => ValueTask.FromResult<object>(new { exitCode = 0, standardOutput = "" }),
                        CancellationToken.None, "/mcp/dev", "ChatGPT");
                }
            }

            var completed = recorded.Where(message => message.Kind == DesktopProgressKind.Completed).ToArray();
            var last = completed.LastOrDefault();
            if (completed.Length != 3 || last?.WorklogFrame?.Timeline.Count != 3 ||
                last.WorklogFrame.Totals.Steps != 3 || last.WorklogFrame.Current.Purpose is not null)
                throw new InvalidOperationException("D92: three steps must share a session; the last step has no purpose.");

            string?[] expected = [null, "İkinci işlem için kontrol yapılıyor", "İlk değişikliğin nedeni kaydediliyor"];
            static void AssertPurposes(IEnumerable<object> entries, string?[] expected, string stage)
            {
                var rows = entries.ToArray();
                if (rows.Any(row => row.GetType().GetProperty("Purpose") is null))
                    throw new InvalidOperationException($"D92 RED {stage}: a timeline row has no Purpose property.");
                var actual = rows.Select(row => row.GetType().GetProperty("Purpose")!.GetValue(row) as string).ToArray();
                if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"D92 RED {stage}: expected [{string.Join(" / ", expected)}], got [{string.Join(" / ", actual)}].");
            }

            AssertPurposes(last.WorklogFrame.Timeline, expected, "notifier timeline");
            var store = new WorklogHistoryStore(historyPath);
            store.AppendTerminal(last with { SessionClosed = true });
            var saved = new WorklogHistoryStore(historyPath).LoadRecent().Single();
            AssertPurposes(saved.Timeline, expected, "saved session");
            AssertPurposes(saved.TimelineRows, expected, "history rows");

            // Earlier JSONL entries never had a timeline purpose property.
            var legacy = JsonNode.Parse(File.ReadAllText(historyPath))!.AsObject();
            foreach (var row in legacy["timeline"]!.AsArray())
                row!.AsObject().Remove("purpose");
            File.WriteAllText(historyPath, legacy.ToJsonString());
            var old = new WorklogHistoryStore(historyPath).LoadRecent().Single();
            AssertPurposes(old.Timeline, [null, null, null], "old JSONL session");
            AssertPurposes(old.TimelineRows, [null, null, null], "old history rows");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("D92_STEP_PURPOSES_GREEN");
    }
}
