using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D46: Get-CiStatus.ps1 exit 2 (CI still running) is a finished check; exit 1 is a failed one.</summary>
    private static async Task AssertD46CiStillRunningAsync()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["executable"] = JsonSerializer.SerializeToElement(@"C:\Program Files\PowerShell\7\pwsh.exe"),
            ["arguments"] = JsonSerializer.SerializeToElement(
                new[] { "-NoProfile", "-File", "scripts/Get-CiStatus.ps1", "-TimeoutMinutes", "0" }),
        };
        foreach (var (exitCode, kind, title) in new[]
        {
            (2, DesktopProgressKind.Completed, "GitHub denetimi sürüyor"),
            (1, DesktopProgressKind.Failed, "GitHub denetimi başarısız"),
            (0, DesktopProgressKind.Completed, "GitHub denetimi geçti"),
        })
        {
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(observed);
            try
            {
                var result = new CallToolResult
                {
                    Content = [],
                    StructuredContent = JsonSerializer.SerializeToElement(
                        new { exitCode, timedOut = false, standardOutput = "CI" }),
                };
                _ = await notifier.RunToolCallAsync("talvora_user_run", arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var terminal = observed.LastOrDefault(message =>
                    message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
                if (terminal is null || terminal.Kind != kind || !terminal.Title.StartsWith(title, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"D46: exit {exitCode} must end as {kind} \"{title}\", got {terminal?.Kind} \"{terminal?.Title}\".");
            }
            finally
            {
                await notifier.DisposeAsync();
            }
        }
        Console.WriteLine("D46_CI_STILL_RUNNING_GREEN");
    }
}
