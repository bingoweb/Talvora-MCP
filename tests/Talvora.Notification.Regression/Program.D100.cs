using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD100FolderlessCallsFollowActorSessionAsync()
    {
        static Dictionary<string, JsonElement> Args(string root) =>
            new() { ["workspaceRoot"] = JsonSerializer.SerializeToElement(root) };

        static TalvoraDesktopProgressNotifier Notifier(
            ConcurrentQueue<DesktopProgressMessage> output, TimeSpan idle) =>
            new(NullLogger<TalvoraDesktopProgressNotifier>.Instance, output.Enqueue,
                static (_, _) => Task.CompletedTask, sessionIdleInterval: idle);

        static async Task<DesktopWorklogFrame> Step(
            TalvoraDesktopProgressNotifier notifier,
            ConcurrentQueue<DesktopProgressMessage> observed,
            string tool, Dictionary<string, JsonElement>? arguments,
            string client = "ChatGPT")
        {
            var before = observed.Count(message =>
                message.Kind == DesktopProgressKind.Completed &&
                !message.SessionClosed && message.WorklogFrame is not null);
            object result = tool == "talvora_apply_patch"
                ? new { success = true, status = "committed", files = Array.Empty<object>() }
                : new { exitCode = 0, standardOutput = "ok", standardError = "" };
            await notifier.RunToolCallAsync<object>(
                tool, arguments, _ => ValueTask.FromResult(result), CancellationToken.None,
                "/mcp/dev", client);
            for (var attempt = 0; attempt < 80; attempt++)
            {
                var completed = observed.Where(message =>
                    message.Kind == DesktopProgressKind.Completed &&
                    !message.SessionClosed && message.WorklogFrame is not null).ToArray();
                if (completed.Length > before)
                    return completed[^1].WorklogFrame!;
                await Task.Delay(20);
            }
            throw new InvalidOperationException("D100: the notifier did not deliver the completed frame.");
        }

        var root = Path.Combine(Path.GetTempPath(), "talvora-d100-" + Guid.NewGuid().ToString("N"));
        var anotherRoot = Path.Combine(Path.GetTempPath(), "talvora-d100-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(anotherRoot, ".git"));
        try
        {
            // a. A folder-less command belongs to the actor's still-open project card.
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = Notifier(observed, TimeSpan.FromSeconds(2)))
            {
                var initial = await Step(notifier, observed, "talvora_apply_patch", Args(root));
                var next = await Step(notifier, observed, "talvora_run_powershell", null);
                if (initial.Header.ProjectName != Path.GetFileName(root) ||
                    next.SessionId != initial.SessionId ||
                    next.Header.ProjectName != initial.Header.ProjectName ||
                    next.Totals.Steps != 2 || next.Timeline.Count != 2)
                    throw new InvalidOperationException(
                        $"D100 RED: folder-less command split the open project card: " +
                        $"{initial.SessionId} → {next.SessionId}, steps {next.Totals.Steps}.");

                // c. Another actor cannot borrow ChatGPT's open project.
                var claude = await Step(notifier, observed, "talvora_run_powershell", null, "Claude");
                if (claude.SessionId == initial.SessionId || claude.Header.ProjectName is not null ||
                    claude.Header.ActorName != "Claude")
                    throw new InvalidOperationException("D100: folder-less Claude joined ChatGPT's project.");

                // A named folder still selects its own project; newest same-actor card wins.
                var other = await Step(notifier, observed, "talvora_apply_patch", Args(anotherRoot));
                var latest = await Step(notifier, observed, "talvora_run_powershell", null);
                if (other.SessionId == initial.SessionId || latest.SessionId != other.SessionId ||
                    latest.Header.ProjectName != Path.GetFileName(anotherRoot))
                    throw new InvalidOperationException("D100: newest same-actor project was not selected.");
            }

            // b. A folder-less command with no actor session still starts the global card.
            var unscoped = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = Notifier(unscoped, TimeSpan.FromMilliseconds(300)))
            {
                var global = await Step(notifier, unscoped, "talvora_run_powershell", null);
                if (global.Header.ProjectName is not null ||
                    global.Header.ActorName != "ChatGPT · Dev" || global.Totals.Steps != 1)
                    throw new InvalidOperationException("D100: no-open-session call must stay global.");
            }

            // d. A project idle beyond the interval must not capture another folder-less call.
            var aged = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = Notifier(aged, TimeSpan.FromMilliseconds(300)))
            {
                var initial = await Step(notifier, aged, "talvora_apply_patch", Args(root));
                await Task.Delay(720);
                var global = await Step(notifier, aged, "talvora_run_powershell", null);
                if (global.SessionId == initial.SessionId || global.Header.ProjectName is not null)
                    throw new InvalidOperationException("D100: expired project captured a global command.");
            }

            // A quiet folder-less read must hold the matched project open without adding a step.
            var quiet = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = Notifier(quiet, TimeSpan.FromMilliseconds(300)))
            {
                var initial = await Step(notifier, quiet, "talvora_apply_patch", Args(root));
                await notifier.RunToolCallAsync<int>(
                    "talvora_tail_text", null,
                    async _ => { await Task.Delay(720); return 1; },
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                var next = await Step(notifier, quiet, "talvora_run_powershell", null);
                if (quiet.Any(message => message.SessionClosed &&
                        message.WorklogFrame?.SessionId == initial.SessionId) ||
                    next.SessionId != initial.SessionId || next.Totals.Steps != 2)
                    throw new InvalidOperationException(
                        "D100 RED: folder-less quiet read did not keep its project session open.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(anotherRoot, recursive: true);
        }
        Console.WriteLine("D100_FOLDERLESS_AGENT_SESSION_GREEN");
    }
}
