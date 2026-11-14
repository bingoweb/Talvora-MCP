using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class Program
{
    private static async Task AssertD82CompactDeployAsync()
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var script = await File.ReadAllTextAsync(Path.Combine(repository,
            "scripts", "Deploy-Windows-Installer.ps1"));
        if (Regex.Matches(script,
            @"(?m)^\s*launched = \$true\s*\r?\n\s*sourceCommit = \$sourceCommit\s*\r?\n\s*taskInstance = \$taskInstanceGuid").Count != 2)
            throw new InvalidOperationException("D82: both deploy hand-offs must put launched, sourceCommit and taskInstance first.");

        var temp = Path.Combine(Path.GetTempPath(), "talvora-d82-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, ".git"));
        var pendingPath = Path.Combine(temp, "pending-deploy.json");
        const string commit = "abcdef0123456789abcdef0123456789abcdef01";
        var handoff = JsonSerializer.Serialize(new
        {
            launched = true,
            sourceCommit = commit,
            taskInstance = "{DA45A623-AF35-492E-9747-E50F34A065C8}",
            waitForCompletion = false,
            taskName = "Talvora Canonical Deploy",
            installer = @"C:\Users\fixture\Talvora-MCP\artifacts\installer\Talvora-Setup.exe",
            manifest = @"C:\Users\fixture\Talvora-MCP\artifacts\installer\Talvora-Setup.manifest.json",
            sha256 = new string('a', 64),
            sourceHeadCommit = new string('b', 40),
            sourceIndexTree = new string('c', 40),
            runtimeInputsSha256 = new string('d', 64),
            note = "Installer is running outside the Talvora service process tree. Reconnect and validate system_info/version-root after the service switch.",
        }) + "\n";
        var compact = ToolOutputCompactor.Compact(handoff, 600);
        if (handoff.Length < 800 || compact.Length >= handoff.Length ||
            !compact.Contains("characters omitted", StringComparison.Ordinal) ||
            !compact.Contains("\"launched\":true", StringComparison.Ordinal) ||
            !compact.Contains("\"sourceCommit\":\"" + commit + "\"", StringComparison.Ordinal))
            throw new InvalidOperationException("D82: fixture must be a genuine split 600-character hand-off with early fields.");

        static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

        try
        {
            async Task Check(string output, bool shouldPersist, string name)
            {
                if (File.Exists(pendingPath)) File.Delete(pendingPath);
                var recorded = new ConcurrentQueue<DesktopProgressMessage>();
                await using var notifier = CreateNotifier(recorded);
                var pathProperty = typeof(TalvoraDesktopProgressNotifier).GetProperty(
                    "PendingDeployWritePath", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("D82: pending path override missing.");
                pathProperty.SetValue(notifier, pendingPath);
                await notifier.RunToolCallAsync<object>("talvora_run_powershell",
                    Args(("workspaceRoot", temp), ("script", "& ./scripts/Deploy-Windows-Installer.ps1"),
                        ("purpose", "Yeni sürüm kuruluyor")),
                    _ => ValueTask.FromResult<object>(new
                    {
                        exitCode = 0,
                        standardOutput = output,
                        standardError = "",
                    }), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var exists = File.Exists(pendingPath);
                if (exists != shouldPersist)
                    throw new InvalidOperationException($"D82 {name}: pending deploy presence = {exists}, expected {shouldPersist}.");
                if (exists)
                {
                    var pending = await WorklogFollower.TakePendingDeployAsync(pendingPath, CancellationToken.None);
                    if (pending?.SourceCommit != commit)
                        throw new InvalidOperationException($"D82 {name}: persisted the wrong source commit.");
                }
            }

            await Check(compact, true, "compacted 600");
            await Check(ToolOutputCompactor.Compact(
                handoff.Replace("\"launched\":true", "\"launched\" : true", StringComparison.Ordinal)
                    .Replace("\"sourceCommit\":\"", "\"sourceCommit\" : \"", StringComparison.Ordinal), 600),
                true, "whitespace around colon");
            await Check(ToolOutputCompactor.Compact(
                handoff.Replace("\"launched\":true", "\"launched\":false", StringComparison.Ordinal), 600),
                false, "not launched");
            await Check(ToolOutputCompactor.Compact(
                handoff.Replace("\"sourceCommit\":\"" + commit + "\"", "\"sourceCommit\":\"\"", StringComparison.Ordinal), 600),
                false, "missing commit");
            await Check(ToolOutputCompactor.Compact(
                handoff.Replace("\"sourceCommit\":\"" + commit + "\"", "\"sourceCommit\":\"123456\"", StringComparison.Ordinal), 600),
                false, "commit shorter than seven hex characters");
            Console.WriteLine("D82_DEPLOY_COMPACT_GREEN");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }
}
