using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertWl6FollowerAsync()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var gateText = await File.ReadAllTextAsync(Path.Combine(workspace, "scripts", "Invoke-QualityGate.ps1"));
        var names = Regex.Matches(gateText, @"Invoke-QualityCheck\s+['""](?<name>[A-Za-z]+)['""]|Name\s*=\s*['""](?<name>SourceEditSemantic|SourceEdit|Notification|IntegrationPolicy)['""]")
            .Select(match => match.Groups["name"].Value)
            .Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[] { "Restore", "Build", "PowerShellParse", "PackageManager",
            "Context7", "Delivery", "SourceEditSemantic", "SourceEdit", "Notification",
            "IntegrationPolicy", "ParallelPhase", "PowerShellRegressions",
            "ControlCenter", "BusinessBootstrap", "HealthHandle", "NativeInstallerRuntime" })
            names.Add(name);
        foreach (var name in names)
            if (string.IsNullOrWhiteSpace(WorklogFollower.StageLabel(name)))
                throw new InvalidOperationException("WL6: missing Turkish gate stage " + name);
        if (WorklogFollower.StageLabel("PS:QualityGateRegression") !=
            "PowerShell test betiği · QualityGateRegression.ps1")
            throw new InvalidOperationException("WL6: failed quiet PowerShell stage has no Turkish name.");

        var temporary = Path.Combine(Path.GetTempPath(), "talvora-wl6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var log = Path.Combine(temporary, "gate.log");
            await File.WriteAllTextAsync(log, "START Restore\n");
            var args = new[] { "-Command", "& ./scripts/Invoke-QualityGate.ps1 *> gate.log; exit $LASTEXITCODE" };
            if (WorklogFollower.ResolveLogPath(args, temporary) != log ||
                WorklogFollower.ResolveLogPath(new[] { "-Command", "tool *> ../outside.log" }, temporary) is not null)
                throw new InvalidOperationException("WL6: log path must be confined to the workspace.");

            var exit = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var live = new ConcurrentQueue<string>();
            var follow = WorklogFollower.FollowProcessAsync(
                token => exit.Task.WaitAsync(token), log, WorklogFollower.JobKind.QualityGate,
                line => live.Enqueue(line), DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(15),
                TimeSpan.FromMinutes(2), CancellationToken.None);
            await Task.Delay(55);
            await File.AppendAllTextAsync(log,
                "PASS Restore 1.0 sn\nSTART ParallelPhase 3\n");
            await Task.Delay(55);
            await File.AppendAllTextAsync(log,
                "PASS ParallelPhase (3 checks) 1.1 sn\n3 kontrol, 0 hata\nHepsi geçti.\n");
            exit.SetResult(0);
            var completed = await follow.WaitAsync(TimeSpan.FromSeconds(4));
            if (!completed.Succeeded || completed.Fact is null ||
                !completed.Fact.Contains("3 kontrol · 0 hata", StringComparison.Ordinal) ||
                !live.Any(line => line.Contains("Paketlerin geri yüklenmesi", StringComparison.Ordinal)) ||
                !live.Any(line => line.Contains("Paralel testler (3 kontrol birlikte)", StringComparison.Ordinal)))
                throw new InvalidOperationException("WL6: a growing gate log did not report its real stages and final counts.");

            await File.WriteAllTextAsync(log, "x".PadRight(90_000, 'x') + "\nSTART Build\n");
            var tail = await WorklogFollower.ReadTailAsync(log, CancellationToken.None);
            if (tail.Length > 65_536 || !tail.Contains("START Build", StringComparison.Ordinal))
                throw new InvalidOperationException("WL6: read-while-writing must inspect only the last 64 KB.");

            var ciCount = 0;
            var ci = await WorklogFollower.FollowCiAsync(
                _ => Task.FromResult((++ciCount < 3 ? 2 : 0, "CI başarılı")),
                DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMinutes(30), CancellationToken.None);
            if (ciCount != 3 || !ci.Succeeded || ci.Title != "GitHub denetimi geçti")
                throw new InvalidOperationException("WL6: CI must stay running until its real exit result.");
            var ciTimedOut = await WorklogFollower.FollowCiAsync(
                _ => throw new InvalidOperationException("An expired CI check must not rerun."),
                DateTimeOffset.UtcNow.AddMinutes(-31), TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMinutes(30), CancellationToken.None);
            if (!ciTimedOut.TimedOut || ciTimedOut.Succeeded ||
                ciTimedOut.Title != "GitHub denetimi henüz bitmemişti")
                throw new InvalidOperationException("WL6: the 30-minute CI limit must cancel, not invent success.");

            var cancellationObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<int?> FakeNeverExits(CancellationToken token)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult(true);
                    throw;
                }
                return 0;
            }
            var timedOut = await WorklogFollower.FollowProcessAsync(
                FakeNeverExits, null, WorklogFollower.JobKind.Other, _ => { },
                DateTimeOffset.UtcNow.AddHours(-3), TimeSpan.FromMilliseconds(10),
                TimeSpan.FromHours(2), CancellationToken.None);
            if (!timedOut.TimedOut || timedOut.Succeeded)
                throw new InvalidOperationException("WL6: process watching must expire after two hours.");
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var pendingPath = Path.Combine(temporary, "pending-deploy.json");
            var pending = new WorklogFollower.PendingDeploy(
                "saved-session", "saved-operation", "abcdef1234567890",
                DateTimeOffset.UtcNow.AddSeconds(-4), "ChatGPT · Dev", "Talvora-MCP");
            await WorklogFollower.WritePendingDeployAsync(pendingPath, pending, CancellationToken.None);
            var saved = await WorklogFollower.TakePendingDeployAsync(pendingPath, CancellationToken.None);
            if (saved is null || File.Exists(pendingPath) ||
                saved.OperationId != pending.OperationId || saved.SessionId != pending.SessionId ||
                WorklogFollower.CompleteDeploy(saved, "abcdef1234567890").Title != "Yeni sürüm çalışıyor · abcdef1" ||
                WorklogFollower.CompleteDeploy(saved, "old-commit").Succeeded)
                throw new InvalidOperationException("WL6: persisted deploy must finish once, on its original IDs, only on matching HEAD.");

            var recovered = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = CreateNotifier(recovered))
            {
                await WorklogFollower.WritePendingDeployAsync(pendingPath, pending, CancellationToken.None);
                await notifier.ReplayPendingDeploymentAsync(pendingPath, "abcdef1234567890");
                var replayed = recovered.LastOrDefault(message =>
                    message.Kind == DesktopProgressKind.Completed);
                if (replayed?.WorklogFrame?.SessionId != "saved-session" ||
                    replayed.Title != "Yeni sürüm çalışıyor · abcdef1")
                    throw new InvalidOperationException("WL6: the restarted notifier must reuse persisted session ID and measured result.");
            }

            var childLog = Path.Combine(temporary, "child.log");
            var childArgs = new[] { "-NoProfile", "-Command",
                "Start-Sleep -Milliseconds 600; Write-Output 'finished' *> " + childLog };
            var start = new System.Diagnostics.ProcessStartInfo(
                @"C:\Program Files\PowerShell\7\pwsh.exe")
            {
                WorkingDirectory = temporary, UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in childArgs) start.ArgumentList.Add(arg);
            using var child = System.Diagnostics.Process.Start(start)!;
            var messages = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = CreateNotifier(messages))
            {
                var request = new Dictionary<string, JsonElement>
                {
                    ["executable"] = JsonSerializer.SerializeToElement(start.FileName),
                    ["arguments"] = JsonSerializer.SerializeToElement(childArgs),
                    ["workingDirectory"] = JsonSerializer.SerializeToElement(temporary),
                };
                await notifier.RunToolCallAsync<object>("talvora_user_process_start", request,
                    _ => ValueTask.FromResult<object>(new { success = true, processId = child.Id }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                if (messages.Any(message => message.Kind is
                    DesktopProgressKind.Completed or DesktopProgressKind.Failed))
                    throw new InvalidOperationException("WL6: a successful process launch cannot complete the step.");
                await child.WaitForExitAsync();
                for (var attempt = 0; attempt < 40 &&
                     !messages.Any(message => message.Kind == DesktopProgressKind.Completed);
                     attempt++) await Task.Delay(50);
                var ended = messages.LastOrDefault(message => message.Kind == DesktopProgressKind.Completed);
                if (ended is null || ended.Title != "Komut tamamlandı" ||
                    ended.WorklogFrame?.Current.FactLine is null)
                    throw new InvalidOperationException("WL6: a followed real process must finish with its own Turkish title and measured result.");
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
        Console.WriteLine("WL6_FOLLOWER_GREEN");
    }
}
