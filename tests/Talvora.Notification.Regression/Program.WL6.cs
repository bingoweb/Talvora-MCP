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
                var legacySessions = (System.Collections.IDictionary)typeof(TalvoraDesktopProgressNotifier)
                    .GetField("_sessions", System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)!.GetValue(notifier)!;
                if (legacySessions.Count != 0)
                    throw new InvalidOperationException("D71: a keyless legacy replay must not leave a stale session behind.");
            }

            static Dictionary<string, JsonElement> D71Args(params (string Name, object Value)[] values) =>
                values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));
            var project = Path.Combine(temporary, "D71-project");
            Directory.CreateDirectory(Path.Combine(project, ".git"));
            var projectArgs = D71Args(("workspaceRoot", project));
            var originalMessages = new ConcurrentQueue<DesktopProgressMessage>();
            string originalSessionId;
            WorklogFollower.PendingDeploy savedSession;
            await using (var original = CreateNotifier(originalMessages))
            {
                await original.RunToolCallAsync<object>("talvora_worklog_note",
                    D71Args(("workspaceRoot", project), ("text", "D71 — Aynı oturum sürsün")),
                    _ => ValueTask.FromResult<object>(new { accepted = true }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                await original.RunToolCallAsync<object>("talvora_apply_patch",
                    D71Args(("workspaceRoot", project), ("patch", "two files"),
                        ("purpose", "İki dosya güncelleniyor")),
                    _ => ValueTask.FromResult<object>(new
                    {
                        success = true, status = "committed",
                        files = new object[]
                        {
                            new { operation = "update", newPath = Path.Combine(project, "a.cs"), addedLines = 1, removedLines = 0 },
                            new { operation = "update", newPath = Path.Combine(project, "b.cs"), addedLines = 1, removedLines = 0 },
                        },
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                await original.RunToolCallAsync<object>("talvora_dotnet_build",
                    projectArgs, _ => ValueTask.FromResult<object>(new { success = true, exitCode = 0 }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                await original.RunToolCallAsync<object>("talvora_dotnet_test",
                    projectArgs, _ => ValueTask.FromResult<object>(new { success = true, exitCode = 0 }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");

                var before = originalMessages.Last(message =>
                    message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
                originalSessionId = before.SessionId;
                if (before.Totals.Steps != 3 || before.Totals.FilesChanged != 2 ||
                    before.Totals.LastBuild != WorklogCheckState.Succeeded ||
                    before.Totals.LastTests != WorklogCheckState.Succeeded ||
                    before.Header.TaskNote != "D71 — Aynı oturum sürsün")
                    throw new InvalidOperationException("D71 setup: initial agent worklog must hold three steps, two files and a note.");

                var pathProperty = typeof(TalvoraDesktopProgressNotifier)
                    .GetProperty("PendingDeployWritePath",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pathProperty is null)
                    throw new InvalidOperationException("D71: deploy writer needs a testable pending-file path.");
                pathProperty.SetValue(original, pendingPath);
                await original.RunToolCallAsync<object>("talvora_run_powershell",
                    D71Args(("workspaceRoot", project), ("script", "& ./scripts/Deploy-Windows-Installer.ps1"),
                        ("purpose", "Yeni sürüm kuruluyor")),
                    _ => ValueTask.FromResult<object>(new
                    {
                        exitCode = 0,
                        standardOutput = "{\"launched\":true,\"sourceCommit\":\"abcdef1234567890\"}\n",
                        standardError = "",
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                if (!File.Exists(pendingPath))
                    throw new InvalidOperationException("D71: deploy did not persist its session.");
                savedSession = await WorklogFollower.TakePendingDeployAsync(pendingPath, CancellationToken.None)
                    ?? throw new InvalidOperationException("D71: deploy snapshot was unreadable.");
                if (savedSession.SessionKey != Path.GetFullPath(project) + "\u0000ChatGPT · Dev" ||
                    savedSession.BeganUtc == default || savedSession.Steps != 3 ||
                    savedSession.Timeline.Length != 3 || savedSession.ChangedFiles.Length != 2 ||
                    savedSession.TaskNote != "D71 — Aynı oturum sürsün" ||
                    savedSession.LastBuild != WorklogCheckState.Succeeded ||
                    savedSession.LastTests != WorklogCheckState.Succeeded)
                    throw new InvalidOperationException("D71: persisted snapshot lost the original session facts.");
                var bounded = savedSession with
                {
                    ChangedFiles = Enumerable.Range(0, 500)
                        .Select(index => "src/long-path-" + index.ToString("D4") +
                            new string('x', 80) + ".cs").ToArray(),
                };
                await WorklogFollower.WritePendingDeployAsync(pendingPath, bounded, CancellationToken.None);
                if (new FileInfo(pendingPath).Length is <= 16_384 or > 262_144)
                    throw new InvalidOperationException("D71 setup: large snapshot must fit only the new 256 KB limit.");
                var large = await WorklogFollower.TakePendingDeployAsync(pendingPath, CancellationToken.None);
                if (large?.ChangedFiles.Length != 500)
                    throw new InvalidOperationException("D71: a valid snapshot larger than 16 KB must be accepted.");
                await WorklogFollower.WritePendingDeployAsync(pendingPath, savedSession, CancellationToken.None);
            }

            var resumedMessages = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var resumed = CreateNotifier(resumedMessages))
            {
                await resumed.ReplayPendingDeploymentAsync(pendingPath, "abcdef1234567890");
                var deploy = resumedMessages.Last(message =>
                    message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
                if (deploy.SessionId != originalSessionId || deploy.Totals.Steps != 4 ||
                    deploy.Totals.FilesChanged != 2 || deploy.Timeline.Count != 4 ||
                    deploy.Header.TaskNote != "D71 — Aynı oturum sürsün" ||
                    deploy.Totals.LastBuild != WorklogCheckState.Succeeded ||
                    deploy.Totals.LastTests != WorklogCheckState.Succeeded ||
                    deploy.Timeline[0].Label != "Yeni sürüm çalışıyor · abcdef1" ||
                    !deploy.Timeline.Skip(1).Select(row => row.Label).SequenceEqual(
                        originalMessages.Last(message =>
                            message.Kind == DesktopProgressKind.Completed).WorklogFrame!
                            .Timeline.Select(row => row.Label)))
                    throw new InvalidOperationException("D71: replay must restore the original session, four steps and newest-first history.");

                await resumed.RunToolCallAsync<object>("talvora_git_run",
                    D71Args(("repositoryPath", project), ("arguments", new[] { "status", "--short" }),
                        ("purpose", "Dağıtım sonrası Git durumu denetleniyor")),
                    _ => ValueTask.FromResult<object>(new { exitCode = 0, standardOutput = "", standardError = "" }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                var next = resumedMessages.Last(message =>
                    message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
                if (next.SessionId != originalSessionId || next.Totals.Steps != 5 ||
                    next.Timeline.Count != 5 || next.Totals.FilesChanged != 2 ||
                    next.Current.Purpose != "Dağıtım sonrası Git durumu denetleniyor" ||
                    resumedMessages.Last(message => message.Kind == DesktopProgressKind.Completed).SessionClosed)
                    throw new InvalidOperationException("D71: next agent step must continue the live session with its purpose and five rows.");

                var sessions = (System.Collections.IDictionary)typeof(TalvoraDesktopProgressNotifier)
                    .GetField("_sessions", System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)!.GetValue(resumed)!;
                var ids = sessions.Values.Cast<object>().Select(session =>
                    (string)session.GetType().GetProperty("Id")!.GetValue(session)!).ToArray();
                if (ids.Length != ids.Distinct(StringComparer.Ordinal).Count())
                    throw new InvalidOperationException("D71: the notifier holds two sessions with one id.");

                var followedStart = new System.Diagnostics.ProcessStartInfo(
                    @"C:\Program Files\PowerShell\7\pwsh.exe")
                {
                    WorkingDirectory = temporary, UseShellExecute = false, CreateNoWindow = true,
                };
                var followedArgs = new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 3" };
                foreach (var argument in followedArgs) followedStart.ArgumentList.Add(argument);
                using var followedProcess = System.Diagnostics.Process.Start(followedStart)!;
                await resumed.RunToolCallAsync<object>("talvora_user_process_start",
                    D71Args(("executable", followedStart.FileName), ("arguments", followedArgs),
                        ("workingDirectory", project), ("purpose", "Arka plan işi izleniyor")),
                    _ => ValueTask.FromResult<object>(new { success = true, processId = followedProcess.Id }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                for (var attempt = 0; attempt < 40 &&
                    !resumedMessages.Any(message => message.Kind == DesktopProgressKind.Running &&
                        message.WorklogFrame?.Totals.Steps == 5 &&
                        message.WorklogFrame.Current.FactLine is not null);
                    attempt++) await Task.Delay(50);
                var liveFrame = resumedMessages.LastOrDefault(message =>
                    message.Kind == DesktopProgressKind.Running &&
                    message.WorklogFrame?.Totals.Steps == 5 &&
                    message.WorklogFrame.Current.FactLine is not null)?.WorklogFrame;
                if (liveFrame?.SessionId != originalSessionId ||
                    liveFrame.Current.Purpose != "Arka plan işi izleniyor")
                    throw new InvalidOperationException("D71: the followed step lost its live fact or session.");
                await followedProcess.WaitForExitAsync();
            }

            var racedMessages = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var raced = CreateNotifier(racedMessages))
            {
                await raced.RunToolCallAsync<object>("talvora_git_run",
                    D71Args(("repositoryPath", project), ("arguments", new[] { "status", "--short" })),
                    _ => ValueTask.FromResult<object>(new { exitCode = 0, standardOutput = "", standardError = "" }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                await WorklogFollower.WritePendingDeployAsync(pendingPath, savedSession, CancellationToken.None);
                await raced.ReplayPendingDeploymentAsync(pendingPath, "abcdef1234567890");
                var racedReplay = racedMessages.Last(message =>
                    message.Kind == DesktopProgressKind.Completed).WorklogFrame!;
                var racedSessions = (System.Collections.IDictionary)typeof(TalvoraDesktopProgressNotifier)
                    .GetField("_sessions", System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)!.GetValue(raced)!;
                if (racedReplay.SessionId != originalSessionId ||
                    racedReplay.Totals.Steps != 2 || racedReplay.Timeline.Count != 2 ||
                    racedReplay.Timeline[0].Label != "Yeni sürüm çalışıyor · abcdef1" ||
                    racedSessions.Count != 1)
                    throw new InvalidOperationException("D71: replay must finish in a live session when the new call arrived first.");
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
            await AssertD72FollowerAsync(workspace, temporary);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
        Console.WriteLine("WL6_FOLLOWER_GREEN");
    }

    private static async Task AssertD72FollowerAsync(string workspace, string temporary)
    {
        static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));
        static string? Technical(WorklogFollower.Outcome value) =>
            (string?)value.GetType().GetProperty("TechnicalResult")?.GetValue(value);
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);
        var enclosing = WorklogFollower.LiveFact(WorklogFollower.JobKind.QualityGate,
            "START PowerShellRegressions\nSTART HealthHandle\nPASS HealthHandle 1.1 sn\n",
            started);
        if (!enclosing.Contains("şu an: PowerShell test betikleri", StringComparison.Ordinal))
            throw new InvalidOperationException("D72(d): the last passed stage must give way to the still-open enclosing stage.");
        var last = WorklogFollower.LiveFact(WorklogFollower.JobKind.QualityGate,
            "START HealthHandle\nPASS HealthHandle 1.1 sn\n", started);
        if (!last.Contains("şu an: Sağlık ve tanıtıcı denetimi", StringComparison.Ordinal))
            throw new InvalidOperationException("D72(d): with no open stage, fall back to the last started one.");

        var failed = await WorklogFollower.FollowProcessAsync(
            _ => Task.FromResult<int?>(7), null, WorklogFollower.JobKind.Other, _ => { },
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(2), CancellationToken.None);
        if (failed.Succeeded || failed.Fact is null || failed.Fact.Contains("çıkış kodu", StringComparison.Ordinal) ||
            Technical(failed) != "Sonuç: tamamlanamadı  //  kod 7")
            throw new InvalidOperationException("D72(b): a failed job exposes the code only in technical evidence.");

        var smokeLog = Path.Combine(temporary, "d72-smoke.log");
        await File.WriteAllTextAsync(smokeLog,
            "TALVORA MCP SMOKE RED\nFAIL metadata policy: secret=SECRET-123 " +
            new string('x', 220) + "\n");
        var smoke = await WorklogFollower.FollowProcessAsync(
            _ => Task.FromResult<int?>(1), smokeLog, WorklogFollower.JobKind.Smoke, _ => { },
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(2), CancellationToken.None);
        var meta = await WorklogFollower.FollowProcessAsync(
            _ => Task.FromResult<int?>(1), smokeLog, WorklogFollower.JobKind.MetadataSmoke, _ => { },
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(2), CancellationToken.None);
        if (smoke.Title != "Kurulu sürüm testi başarısız" ||
            smoke.Reason != "Kurulu sürüm testi bir adımda durdu; ayrıntı günlükte." ||
            meta.Title != "Araç tanımları denetimi başarısız" ||
            meta.Reason != "Araç tanımları denetimi bir adımda durdu; ayrıntı günlükte." ||
            Technical(smoke) is not { Length: 160 } ||
            Technical(smoke)!.Contains("SECRET-123", StringComparison.Ordinal))
            throw new InvalidOperationException("D72(b): smoke errors must have approved titles, reasons and redacted bounded details.");
        await File.WriteAllTextAsync(smokeLog, "TALVORA MCP SMOKE GREEN\n");
        var smokeGreen = await WorklogFollower.FollowProcessAsync(
            _ => Task.FromResult<int?>(0), smokeLog, WorklogFollower.JobKind.Smoke, _ => { },
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(2), CancellationToken.None);
        if (smokeGreen.Title != "Kurulu sürüm testi geçti" ||
            smokeGreen.Fact!.Contains("Kurulu sürüm testi", StringComparison.Ordinal))
            throw new InvalidOperationException("D72(b): a passing smoke must show only its elapsed time as the fact.");

        var ciCount = 0;
        var measuredCi = await WorklogFollower.FollowCiAsync(
            _ => Task.FromResult((++ciCount == 1 ? 2 : 0,
                "CI başarılı (4 dk 12 sn): https://github.com/bingoweb/Talvora-MCP/actions/runs/1")),
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(30), CancellationToken.None);
        if (!measuredCi.Succeeded || measuredCi.Fact != "4 dk 12 sn")
            throw new InvalidOperationException("D72(c): CI must show the run duration, not the follower duration.");
        var ciFailure = await WorklogFollower.FollowCiAsync(
            _ => Task.FromResult((1, "CI başarısız (failure, 1 sa 3 dk): https://example.com\nFAIL first line")),
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(30), CancellationToken.None);
        if (ciFailure.Title != "GitHub denetimi başarısız" ||
            ciFailure.Fact != "1 sa 3 dk" ||
            ciFailure.Reason != "GitHub denetimi başarısız oldu; ayrıntılar CI günlüğünde." ||
            Technical(ciFailure) != "FAIL first line")
            throw new InvalidOperationException("D72(c): a completed failed CI must use the measured duration and technical failure.");
        var unavailableCalls = 0;
        var unavailable = await WorklogFollower.FollowCiAsync(
            _ => Task.FromResult((++unavailableCalls == 2 ? 5 : 1, "gh: network failure")),
            started, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(30), CancellationToken.None);
        if (unavailableCalls != 3 || !unavailable.TimedOut ||
            unavailable.Title != "GitHub denetiminin sonucu alınamadı" ||
            unavailable.Reason != "GitHub'a ulaşılamadı; sonuç bir sonraki denetimde okunacak.")
            throw new InvalidOperationException("D72(c): three consecutive unverified CI checks must cancel, not claim a failed run.");

        var project = Path.Combine(temporary, "d72-project");
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        await using (var notifier = CreateNotifier(observed))
        {
            var ciArgs = Args(("executable", @"C:\Program Files\PowerShell\7\pwsh.exe"),
                ("workingDirectory", workspace),
                ("arguments", new[] { "-NoProfile", "-File", "scripts/Get-CiStatus.ps1", "-TimeoutMinutes", "0" }));
            await notifier.RunToolCallAsync<object>("talvora_user_run", ciArgs,
                _ => ValueTask.FromResult<object>(new { exitCode = 2, standardOutput = "CI: running" }),
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            const string diff = "@@ FILE Demo.cs\n--- a/Demo.cs\n+++ b/Demo.cs\n@@ -1 +1 @@\n-old\n+new\n";
            await notifier.RunToolCallAsync<object>("talvora_apply_patch",
                Args(("workspaceRoot", workspace), ("patch", "test"), ("purpose", "Dosya düzenlemesi kaydediliyor")),
                _ => ValueTask.FromResult<object>(new
                {
                    success = true, status = "committed",
                    files = new[] { new { operation = "update", newPath = "Demo.cs",
                        addedLines = 1, removedLines = 1, codePreview = diff } },
                }), CancellationToken.None, "/mcp/dev", "ChatGPT");
            var patchMessage = observed.LastOrDefault(m =>
                m.ToolName == "apply_patch" && m.Kind == DesktopProgressKind.Completed);
            if (patchMessage?.WorklogFrame?.Current.Purpose != "Dosya düzenlemesi kaydediliyor" ||
                patchMessage.WorklogFrame.Current.Changes?.Count != 1 ||
                patchMessage.Evidence?.CodePreview != diff || patchMessage.SessionClosed)
                throw new InvalidOperationException("D72(a): a finished edit must show its own purpose, changes and diff during a CI follow.");
        }

        var gateMessages = new ConcurrentQueue<DesktopProgressMessage>();
        await using (var notifier = CreateNotifier(gateMessages))
        {
            var gateLog = Path.Combine(project, "d72-gate.log");
            var processArgs = new[] { "-NoProfile", "-Command",
                "Start-Sleep -Seconds 4; 'START Restore','PASS Restore 0.1 sn','2 kontrol, 0 hata','Hepsi geçti.' *> '" +
                    gateLog + "'; # Invoke-QualityGate.ps1" };
            var startInfo = new System.Diagnostics.ProcessStartInfo(@"C:\Program Files\PowerShell\7\pwsh.exe")
            {
                WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var arg in processArgs) startInfo.ArgumentList.Add(arg);
            using var job = System.Diagnostics.Process.Start(startInfo)!;
            await notifier.RunToolCallAsync<object>("talvora_user_process_start",
                Args(("executable", startInfo.FileName), ("arguments", processArgs),
                    ("workingDirectory", project), ("purpose", "Kalite kapısı arka planda izleniyor")),
                _ => ValueTask.FromResult<object>(new { success = true, processId = job.Id }),
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            await notifier.RunToolCallAsync<object>("talvora_git_run",
                Args(("repositoryPath", project), ("arguments", new[] { "fetch", "origin" }),
                    ("purpose", "Uzak depo denetleniyor")),
                _ => ValueTask.FromResult<object>(new { exitCode = 128, standardError = "fatal: unreachable" }),
                CancellationToken.None, "/mcp/dev", "ChatGPT");
            var failedGit = gateMessages.LastOrDefault(m => m.Kind == DesktopProgressKind.Failed);
            if (failedGit?.WorklogFrame?.Current.Purpose != "Uzak depo denetleniyor")
                throw new InvalidOperationException("D72(a): a failed foreground step must appear while the gate is followed.");
            await Task.Delay(2200);
            if (gateMessages.Any(m => m.Sequence > failedGit.Sequence &&
                m.Kind == DesktopProgressKind.Running &&
                m.Title.Contains("Kalite kontrolü", StringComparison.Ordinal)))
                throw new InvalidOperationException("D72(a): the old background job must not retake the card after a newer step.");
            await job.WaitForExitAsync();
            for (var i = 0; i < 50 && !gateMessages.Any(m =>
                     m.Kind == DesktopProgressKind.Completed &&
                     m.Title.Contains("Kalite kontrolü", StringComparison.Ordinal)); i++)
                await Task.Delay(50);
            var gateFinished = gateMessages.LastOrDefault(m =>
                m.Kind == DesktopProgressKind.Completed &&
                m.Title.Contains("Kalite kontrolü", StringComparison.Ordinal));
            if (gateFinished?.Title.StartsWith("Kalite kontrolü geçti", StringComparison.Ordinal) != true ||
                gateFinished.WorklogFrame?.Timeline.Count != 2 ||
                gateFinished.WorklogFrame.Current.FactLine?.Contains("2 kontrol", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("D72(a): the followed gate's final success must not be replaced by an earlier failed step.");
        }
    }
}
