using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD101CiPollProgressAsync()
    {
        var polls = 0;
        var facts = new List<string>();
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = await WorklogFollower.FollowCiAsync(
            async _ =>
            {
                await Task.Delay(5);
                return (++polls < 3
                    ? (2, "CI: çalışma hâlâ 'in_progress'")
                    : (0, "CI başarılı (1,5 sn): https://example.invalid/check"));
            },
            started, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(5),
            CancellationToken.None, fact => facts.Add(fact));

        if (!result.Succeeded || result.Title != "GitHub denetimi geçti" ||
            result.Fact != "1,5 sn" || polls != 3 || facts.Count != 3 ||
            facts.Any(fact =>
                !fact.StartsWith("GitHub denetimi sürüyor · ", StringComparison.Ordinal) ||
                fact.Length <= "GitHub denetimi sürüyor · ".Length))
            throw new InvalidOperationException(
                $"D101 RED: {polls} CI polls must each produce a measured running fact; " +
                $"facts=[{string.Join("; ", facts)}], result={result.Title}.");
    }

    private static async Task AssertD101FollowedStepsStayFreshAsync()
    {
        await AssertD101CiPollProgressAsync();
        static Dictionary<string, JsonElement> Args(params (string Key, object Value)[] values) =>
            values.ToDictionary(item => item.Key,
                item => JsonSerializer.SerializeToElement(item.Value));

        static async Task<DesktopWorklogFrame> LatestRunningFactAsync(
            ConcurrentQueue<DesktopProgressMessage> observed, string? title = null)
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                var frame = observed.LastOrDefault(message =>
                    message.Kind == DesktopProgressKind.Running &&
                    message.WorklogFrame?.Current.FactLine is not null &&
                    (title is null || message.WorklogFrame.Current.Title == title))?.WorklogFrame;
                if (frame is not null) return frame;
                await Task.Delay(25);
            }
            throw new InvalidOperationException(
                "D101 RED: no followed-running frame was delivered. Frames: " +
                string.Join(" | ", observed.Select(message =>
                    $"{message.Kind}: {message.WorklogFrame?.Current.Title}, " +
                    $"fact={message.WorklogFrame?.Current.FactLine}, " +
                    $"budget={message.WorklogFrame?.ExpectedNextUpdateSeconds}")));
        }

        var directory = Path.Combine(Path.GetTempPath(), "talvora-d101-" + Guid.NewGuid().ToString("N"));
        var pending = Path.Combine(directory, "pending-deploy.json");
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(directory, "scripts", "Get-CiStatus.ps1"),
            "# D101 isolated follower test\n");
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        try
        {
            await using (var notifier = CreateNotifier(observed))
            {
                typeof(TalvoraDesktopProgressNotifier)
                    .GetProperty("PendingDeployWritePath",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(notifier, pending);
                await notifier.RunToolCallAsync<object>("talvora_run_powershell",
                    Args(("workspaceRoot", directory),
                        ("script", "& ./scripts/Deploy-Windows-Installer.ps1"),
                        ("purpose", "Yeni sürüme geçiş izleniyor")),
                    _ => ValueTask.FromResult<object>(new
                    {
                        exitCode = 0,
                        standardOutput = "{\"launched\":true,\"sourceCommit\":\"abcdef1234567890\"}",
                        standardError = "",
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var deployment = await LatestRunningFactAsync(observed);
                if (deployment.ExpectedNextUpdateSeconds < 120 ||
                    deployment.Current.State != WorklogStepState.Running)
                    throw new InvalidOperationException(
                        $"D101 RED deploy hand-over: expected >=120 seconds, got " +
                        deployment.ExpectedNextUpdateSeconds);
                if (!File.Exists(pending))
                    throw new InvalidOperationException("D101: deploy hand-over did not persist its snapshot.");

                var ciArgs = Args(
                    ("executable", @"C:\Program Files\PowerShell\7\pwsh.exe"),
                    ("workingDirectory", directory),
                    ("arguments", new[] { "-NoProfile", "-File", "scripts/Get-CiStatus.ps1",
                        "-TimeoutMinutes", "0" }));
                await notifier.RunToolCallAsync("talvora_user_run", ciArgs,
                    _ => ValueTask.FromResult(new CallToolResult
                    {
                        Content = [],
                        StructuredContent = JsonSerializer.SerializeToElement(new
                        {
                            exitCode = 2, timedOut = false,
                            standardOutput = "CI: süre doldu, çalışma hâlâ 'in_progress'",
                            standardError = "",
                        }),
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var ci = await LatestRunningFactAsync(observed, "GitHub denetimi sürüyor");
                if (ci.Current.State != WorklogStepState.Running ||
                    ci.ExpectedNextUpdateSeconds < 120 ||
                    ci.Current.Title != "GitHub denetimi sürüyor" ||
                    ci.Current.FactLine is null ||
                    !ci.Current.FactLine.StartsWith("GitHub denetimi sürüyor · ", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"D101 RED CI follow: '{ci.Current.Title}' " +
                        $"'{ci.Current.FactLine}', update budget {ci.ExpectedNextUpdateSeconds}.");
            }

            var ordinary = new ConcurrentQueue<DesktopProgressMessage>();
            await using (var notifier = CreateNotifier(ordinary))
            {
                await notifier.RunToolCallAsync<object>("talvora_git_run",
                    Args(("repositoryPath", directory), ("arguments", new[] { "status", "--short" })),
                    _ => ValueTask.FromResult<object>(new { exitCode = 0, standardOutput = "" }),
                    CancellationToken.None, "/mcp/dev", "ChatGPT");
                var last = ordinary.LastOrDefault(message => message.Kind == DesktopProgressKind.Completed);
                if (last?.WorklogFrame?.ExpectedNextUpdateSeconds != 15)
                    throw new InvalidOperationException(
                        $"D101: ordinary ChatGPT steps must keep 15 seconds; got " +
                        last?.WorklogFrame?.ExpectedNextUpdateSeconds);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("D101_FOLLOWED_BUDGET_GREEN");
    }
}
