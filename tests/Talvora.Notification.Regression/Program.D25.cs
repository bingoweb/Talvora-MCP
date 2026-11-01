using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>
    /// D25: a session that restarts after its idle interval starts from zero. Files, build and test results of the
    /// previous session must never be shown as this session's facts (no fabricated telemetry).
    /// </summary>
    private static async Task AssertD25SessionRestartResetsTotalsAsync()
    {
        static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] values) =>
            values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

        var project = Path.Combine(Path.GetTempPath(), "Talvora-D25-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var messages = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(messages);
        try
        {
            var receipt = new
            {
                success = true, status = "committed",
                files = new object[] { new { operation = "update", newPath = project + "\\a.cs", addedLines = 1, removedLines = 0 } },
            };
            await notifier.RunToolCallAsync<object>("talvora_apply_patch",
                Arguments(("workspaceRoot", project), ("patch", "x")),
                _ => ValueTask.FromResult<object>(receipt), CancellationToken.None);
            await notifier.RunToolCallAsync<object>("talvora_dotnet_build", Arguments(("workspaceRoot", project)),
                _ => ValueTask.FromResult<object>(new { success = true, exitCode = 0 }), CancellationToken.None);
            var before = messages.Last(message => message.WorklogFrame is not null).WorklogFrame!;
            if (before.Totals.FilesChanged != 1 || before.Totals.LastBuild != WorklogCheckState.Succeeded)
                throw new InvalidOperationException("D25: the first session must count its own file and build.");

            // Age every session past the idle interval, as if the agent had been quiet for ten minutes.
            var sessions = (IDictionary)typeof(TalvoraDesktopProgressNotifier)
                .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(notifier)!;
            foreach (var session in sessions.Values)
            {
                session!.GetType().GetProperty("LastActivityUtc")!.SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-10));
            }

            await notifier.RunToolCallAsync<object>("talvora_dotnet_test", Arguments(("workspaceRoot", project)),
                _ => ValueTask.FromResult<object>(new { success = true, exitCode = 0 }), CancellationToken.None);
            var after = messages.Last(message => message.WorklogFrame is not null).WorklogFrame!;
            if (after.Totals.Steps != 1 || after.Totals.FilesChanged != 0 ||
                after.Totals.LastBuild != WorklogCheckState.Unknown || after.Totals.LastTests != WorklogCheckState.Succeeded)
                throw new InvalidOperationException(
                    $"D25: a restarted session carried old facts: steps={after.Totals.Steps} files={after.Totals.FilesChanged} " +
                    $"build={after.Totals.LastBuild} tests={after.Totals.LastTests}.");
        }
        finally
        {
            await notifier.DisposeAsync();
            Directory.Delete(project, recursive: true);
        }
    }
}
