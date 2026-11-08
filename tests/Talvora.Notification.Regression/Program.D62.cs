using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D62: detached process launches never claim the command already passed.</summary>
    private static async Task AssertD62DetachedStartIsNotResultAsync()
    {
        var cases = new (string Tool, string Executable, string[] Arguments, string ExpectedTitle)[]
        {
            ("talvora_user_process_start", @"C:\Program Files\PowerShell\7\pwsh.exe",
                ["-NoProfile", "-Command", "& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/D62-gate.log; exit $LASTEXITCODE"],
                "Kalite kontrolü arka planda başladı · tam"),
            ("talvora_user_run", @"C:\Program Files\PowerShell\7\pwsh.exe",
                ["-NoProfile", "-Command", "& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/D62-gate.log; exit $LASTEXITCODE"],
                "Kalite kontrolü geçti · tam"),
            ("talvora_user_process_start", @"C:\Program Files\PowerShell\7\pwsh.exe",
                ["-NoProfile", "-Command", "& ./scripts/Build-Windows-Installer.ps1 *> artifacts/D62-build.log"],
                "Kurulum paketi hazırlığı arka planda başladı"),
            ("talvora_user_run", @"C:\Program Files\PowerShell\7\pwsh.exe",
                ["-NoProfile", "-Command", "& dotnet run --project tests/Talvora.Smoke/Talvora.Smoke.csproj -c Release -- --metadata-policy-only http://127.0.0.1:7676/mcp"],
                "Talvora.Smoke bitti"),
            ("talvora_job_start", @"C:\Windows\System32\whoami.exe",
                [], "whoami.exe arka planda başladı"),
            ("talvora_user_run", "dotnet",
                ["run", "--project", "tests/Talvora.Smoke/Talvora.Smoke.csproj", "-c", "Release"],
                "Talvora.Smoke bitti"),
        };
        foreach (var (tool, executable, argv, expected) in cases)
        {
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            await using var notifier = CreateNotifier(observed);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["executable"] = JsonSerializer.SerializeToElement(executable),
                ["arguments"] = JsonSerializer.SerializeToElement(argv),
            };
            var result = new CallToolResult
            {
                Content = [],
                StructuredContent = JsonSerializer.SerializeToElement(
                    new { success = true, exitCode = 0, processId = 1234 }),
            };
            _ = await notifier.RunToolCallAsync(tool, arguments,
                _ => ValueTask.FromResult(result), CancellationToken.None, "/mcp/dev", "ChatGPT");
            var terminal = observed.LastOrDefault(message =>
                message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
            if (terminal?.Kind != DesktopProgressKind.Completed || terminal.Title != expected)
                throw new InvalidOperationException(
                    $"D62: {tool} {executable} should say '{expected}', observed {terminal?.Kind} '{terminal?.Title}'.");
        }
        Console.WriteLine("D62_DETACHED_START_IS_NOT_RESULT_GREEN");
    }
}
