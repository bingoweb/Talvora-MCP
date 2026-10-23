using System.Collections.Concurrent;
using System.Diagnostics;
using Talvora;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class Program
{
    /// <summary>RM1/RM2: run status comes from the real result/log files; the card shows real frames and output.</summary>
    private static async Task AssertRemotionWorklogAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-RM-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "talvora-tanitim");
        Directory.CreateDirectory(Path.Combine(project, "out"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var runId = Guid.NewGuid().ToString("N");
        var resultPath = Path.Combine(root, runId + ".json");
        var logPath = Path.Combine(root, runId + ".log");
        var output = Path.Combine(project, "out", "TalvoraIntro-20261023-165600.mp4");
        var pid = Environment.ProcessId;
        try
        {
            File.WriteAllText(resultPath, $$"""{"runId":"{{runId}}","action":"render","projectRoot":{{System.Text.Json.JsonSerializer.Serialize(project)}},"state":"running","pid":{{pid}}}""");
            File.WriteAllText(logPath, "> npx remotion render\n\u001b[90mRendered 12/90\u001b[39m\nRendered 45/90\n");
            var running = RemotionTools.ReadStatus(runId, resultPath, logPath);
            if (running.State != "running" || running.FramesDone != 45 || running.FramesTotal != 90 || running.LogTail.Contains('\u001b'))
                throw new InvalidOperationException("RM1: running status, frame counter or ANSI stripping is wrong.");

            var messages = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(messages);
            try
            {
                notifier.BeginRemotionRun(runId, "render", project, "TalvoraIntro", resultPath, logPath);
                await WaitUntilAsync(() => messages.Any(message => message.WorklogFrame?.Current.FactLine == "45/90 kare"));
                var progress = messages.Last(message => message.WorklogFrame?.Current.FactLine == "45/90 kare").WorklogFrame!;
                if (progress.Current.Title != "Animasyon oluşturuluyor · TalvoraIntro" || progress.Header.ProjectName != "talvora-tanitim")
                    throw new InvalidOperationException("RM2: running card title or project is wrong: " + progress.Current.Title);

                File.WriteAllBytes(output, new byte[2048]);
                File.WriteAllText(resultPath, $$"""{"runId":"{{runId}}","action":"render","projectRoot":{{System.Text.Json.JsonSerializer.Serialize(project)}},"state":"succeeded","pid":{{pid}},"outputPath":{{System.Text.Json.JsonSerializer.Serialize(output)}}}""");
                await WaitUntilAsync(() => messages.Any(message => message.Kind == DesktopProgressKind.Completed));
                var done = messages.Last(message => message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
                if (done.Current.Title != "Animasyon hazır · TalvoraIntro" ||
                    done.Current.FactLine != "TalvoraIntro-20261023-165600.mp4 · 2 KB")
                    throw new InvalidOperationException($"RM2: finished card was '{done.Current.Title}' / '{done.Current.FactLine}'.");
            }
            finally { await notifier.DisposeAsync(); }

            // A failed run exposes the compiler error block, never the stack frames.
            File.WriteAllText(resultPath, $$"""{"runId":"{{runId}}","action":"compositions","state":"failed","pid":{{pid}},"error":"npx remotion compositions failed with exit code 1."}""");
            File.WriteAllText(logPath,
                "> npx remotion compositions src/index.ts --quiet\nError:   × Module build failed (from builtin:swc-loader):\n" +
                "  ╰─▶   × Syntax Error\n    at async cli (C:\\x\\index.js:109:13)\nFAILED: npx remotion compositions failed with exit code 1.\n");
            var failed = RemotionTools.ReadStatus(runId, resultPath, logPath);
            if (failed.State != "failed" || failed.Diagnostics is null ||
                !failed.Diagnostics.StartsWith("Error:", StringComparison.Ordinal) ||
                failed.Diagnostics.Contains("    at ", StringComparison.Ordinal) ||
                failed.Diagnostics.Contains("FAILED:", StringComparison.Ordinal))
                throw new InvalidOperationException("RM1: failure diagnostics were wrong: " + failed.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(20))
                throw new TimeoutException("RM2: the worklog did not reach the expected frame within 20 s.");
            await Task.Delay(100);
        }
    }
}
