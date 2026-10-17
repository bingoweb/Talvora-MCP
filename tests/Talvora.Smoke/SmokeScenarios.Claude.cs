using System.Text;
using System.Text.Json;
using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static async Task RunClaudeIntegrationPolicyAsync()
    {
        var arguments = ClaudeCliArguments.Create("--help", "sonnet", "high",
            "bypassPermissions", "saved-session",
            extraArguments: ["--mcp-config", "{\"mcpServers\":{}}", "--plugin-dir", "example-plugin"]);
        if (arguments[^2] != "--" || arguments[^1] != "--help" ||
            !arguments.Contains("--resume") || !arguments.Contains("bypassPermissions") ||
            arguments.Contains("--tools") || arguments.Contains("--allowedTools") ||
            !ClaudeCliArguments.Create("work").Contains("auto"))
        {
            throw new InvalidOperationException("Claude argument-vector/native capability contract regressed.");
        }

        var root = Path.Combine(Path.GetTempPath(), "Talvora-Claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "progress.json");
            var capture = Path.Combine(root, "stdout.txt");
            var events = new StringBuilder();
            events.AppendLine("{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"saved-session\"}");
            for (var i = 0; i < 3000; i++)
            {
                events.AppendLine("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"PRIVATE_COMMAND\"}}]}}");
            }
            events.AppendLine("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Çalışma tamamlandı\",\"permission_denials\":[]}");
            using (var reader = new StringReader(events.ToString()))
            {
                await ProcessOutputPump.PumpToUtf8FileAsync(reader, capture, 4096,
                    CancellationToken.None, null, path);
            }
            var progress = JsonSerializer.Deserialize<ClaudeCliProgress>(await File.ReadAllTextAsync(path))
                ?? throw new InvalidOperationException("Missing Claude summary.");
            if (progress.ToolCalls != 3000 || progress.SessionId != "saved-session" ||
                !progress.HasResult || progress.IsError || progress.FinalAnswer != "Çalışma tamamlandı" ||
                (await File.ReadAllTextAsync(path)).Contains("PRIVATE_COMMAND", StringComparison.Ordinal) ||
                new FileInfo(capture).Length > 6000)
            {
                throw new InvalidOperationException("Claude progress lost events after stdout truncation or leaked command text.");
            }
            var denied = new ClaudeCliStreamingProgress();
            await denied.ObserveAsync(
                "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"permission_denials\":[{}]}\n".AsMemory(),
                path, CancellationToken.None);
            if (denied.Snapshot().PermissionDenials != 1) { throw new InvalidOperationException("Permission denial lost."); }
            var failure = new ClaudeCliStreamingProgress();
            await failure.ObserveAsync(
                "{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"errors\":[\"password=private-canary-123\"]}\n".AsMemory(),
                path, CancellationToken.None);
            if (!failure.Snapshot().IsError ||
                failure.Snapshot().Error!.Contains("private-canary", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Structured failure/redaction contract failed.");
            }

            // Exercise the actual user-session launcher, PowerShell helper and six-argument pump binding.
            var user = WindowsSessionLauncher.GetActiveInteractiveUser();
            var pwsh = CommandResolver.Resolve(["pwsh.exe"], [@"C:\Program Files\PowerShell\7\pwsh.exe"])
                ?? throw new InvalidOperationException("PowerShell unavailable.");
            ClaudeCliProgress? live = null;
            var run = await InteractiveUserProcessRunner.RunAsync(pwsh, user.UserProfile!,
                ["-NoProfile", "-NonInteractive", "-Command",
                    "[Console]::WriteLine('{" + "\"type\":\"result\",\"subtype\":\"success\",\"result\":\"USER_RUNNER_OK\"}')"],
                timeoutSeconds: 30, interactiveUser: user,
                onClaudeProgress: value => live = value, closeStandardInput: true);
            if (run.ExitCode != 0 || run.TimedOut || live?.FinalAnswer != "USER_RUNNER_OK")
            {
                throw new InvalidOperationException("Claude user-session runner failed: " + run.StandardError);
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await InteractiveUserProcessRunner.RunAsync(pwsh, user.UserProfile!,
                    ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
                    timeoutSeconds: 30, interactiveUser: user, cancellationToken: cancel.Token,
                    closeStandardInput: true);
                throw new InvalidOperationException("Cancellation did not stop the user process.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
        finally { Directory.Delete(root, recursive: true); }
        if (!TalvoraMcpToolSurfacePolicy.IsFocusedSurfaceReviewComplete())
        {
            throw new InvalidOperationException("Claude manifest count mismatch.");
        }
    }
}
