using System.Diagnostics;
using Talvora.Tools;
using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static async Task RunCodexIntegrationPolicyAsync()
    {
        foreach (var prompt in new[]
                 {
                     "--help",
                     "--dangerously-bypass-approvals-and-sandbox",
                     "ÇĞİÖŞÜ çğıöşü İstanbul öğretmen sınıf",
                 })
        {
            var arguments = CodexCliInvocationPolicy.CreateArguments(
                @"C:\workspace", prompt, "gpt-6.1-sol", "medium", "read-only");
            if (arguments[^2] != "--" || arguments[^1] != prompt ||
                arguments[0] != "-a" || arguments[1] != "never" ||
                !arguments.Contains("read-only") ||
                arguments.Count(item => item == prompt) != 1)
            {
                throw new InvalidOperationException(
                    "Codex argument separation or sandbox/approval policy regressed.");
            }
        }

        var autonomous = CodexCliInvocationPolicy.CreateArguments(
            @"C:\workspace", "Create a working application", "gpt-6.1-sol",
            "medium", "workspace-write",
            additionalWritableDirectories: [@"C:\workspace-assets"]);
        if (autonomous[0] != "--approve-for-me" ||
            !autonomous.Contains("--json") ||
            autonomous.Contains("--ephemeral") ||
            autonomous.Contains("--sandbox") ||
            !autonomous.Contains("sandbox_workspace_write.network_access=true") ||
            !autonomous.Contains("--add-dir") ||
            !autonomous.Contains(@"C:\workspace-assets") ||
            !autonomous.Contains("--skip-git-repo-check"))
        {
            throw new InvalidOperationException(
                "Default Codex coding mode is not autonomous, network-enabled and persistent.");
        }

        const string priorSession = "01a128c3-0c50-7aa0-a9b4-cd0f5f85a7e6";
        var resumed = CodexCliInvocationPolicy.CreateArguments(
            @"C:\workspace", "Continue", "gpt-6.1-sol", "medium",
            "workspace-write", resumeSessionId: priorSession);
        if (!resumed.Contains("resume") ||
            !resumed.Contains(priorSession) ||
            !resumed.Contains("sandbox_mode=\"workspace-write\"") ||
            resumed.Contains("--ephemeral") ||
            resumed.Contains("--cd"))
        {
            throw new InvalidOperationException(
                "Codex session continuation arguments are invalid.");
        }

        var ephemeral = CodexCliInvocationPolicy.CreateArguments(
            @"C:\workspace", "One-off", "gpt-6.1-sol", "medium",
            "workspace-write", preserveSession: false, approvalMode: "never",
            networkAccess: false);
        if (!ephemeral.Contains("--ephemeral") ||
            !ephemeral.Contains("sandbox_workspace_write.network_access=false") ||
            !ephemeral.Contains("-a") || ephemeral.Contains("--approve-for-me"))
        {
            throw new InvalidOperationException(
                "Explicit ephemeral/non-network coding settings were not honored.");
        }
        var unlimited = CodexCliInvocationPolicy.CreateArguments(
            @"C:\workspace", "Use full workspace", "gpt-6.1-sol", "medium",
            "danger-full-access", approvalMode: "never");
        if (!unlimited.Contains("danger-full-access"))
        {
            throw new InvalidOperationException(
                "Explicit full-access Codex mode is missing.");
        }
        var unsupportedResumeDir = false;
        try
        {
            _ = CodexCliInvocationPolicy.CreateArguments(
                @"C:\workspace", "Continue", "gpt-6.1-sol", "medium",
                "workspace-write", resumeSessionId: priorSession,
                additionalWritableDirectories: [@"C:\assets"]);
        }
        catch (ArgumentException)
        {
            unsupportedResumeDir = true;
        }
        if (!unsupportedResumeDir)
        {
            throw new InvalidOperationException(
                "Unsupported resume --add-dir was silently accepted.");
        }

        var parsed = CodexCliEventSummary.Parse(
            "{\"type\":\"thread.started\",\"thread_id\":\"" + priorSession + "\"}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"dotnet test\",\"exit_code\":0}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"mcp_tool_call\",\"server\":\"Talvora\",\"tool\":\"search\"}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Build and tests passed.\"}}\n" +
            "{\"type\":\"turn.completed\"}\n");
        if (!parsed.HasEvents || parsed.SessionId != priorSession ||
            parsed.FinalAnswer != "Build and tests passed." ||
            parsed.CompletedCommands != 1 || parsed.FailedCommands != 0 ||
            parsed.McpToolCalls != 1 ||
            !parsed.RecentActivities.Contains("Testleri çalıştırıyor") ||
            parsed.RecentActivities.Any(item => item.Contains("dotnet test",
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Codex structured session and activity parsing failed.");
        }
        var invalidTail = CodexCliEventSummary.Parse(
            "{\"type\":\"thread.started\",\"thread_id\":\"" + priorSession + "\"}\n" +
            "{\"type\":\"item.completed\",\"item\":");
        if (invalidTail.SessionId != priorSession)
        {
            throw new InvalidOperationException(
                "Incomplete bounded Codex JSONL damaged valid earlier events.");
        }

        var helpAnswer = CodexCliInvocationPolicy.SanitizeFinalAnswer(
            "To inspect usage run talvora --help.\n", "--help");
        var helpEcho = CodexCliInvocationPolicy.SanitizeFinalAnswer(
            "--help\r\n", "--help");
        if (!helpAnswer.Contains("talvora --help", StringComparison.Ordinal) ||
            helpEcho.Contains("--help", StringComparison.Ordinal) ||
            !helpEcho.Contains(CodexCliInvocationPolicy.PromptRedactionMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Codex short task sanitization removed useful CLI syntax.");
        }

        const string canary = "SYNTHETIC_PRIVATE_CANARY_9K4";
        var task = $"Türkçe çığırtkanlık\r\nsecond line {canary}";
        foreach (var echo in new[]
                 {
                     task,
                     task.Replace("\r\n", "\n", StringComparison.Ordinal),
                 })
        {
            var safe = CodexCliInvocationPolicy.SanitizeFinalAnswer(echo, task);
            if (safe.Contains(canary, StringComparison.Ordinal) ||
                !safe.Contains(CodexCliInvocationPolicy.PromptRedactionMarker,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Codex task-echo redaction failed for Unicode/newline variants.");
            }
        }
        var secretTask = $"private-prefix-{canary} password=only-a-test Ç";
        var secretAnswer = CodexCliInvocationPolicy.SanitizeFinalAnswer(
            secretTask, secretTask);
        if (secretAnswer.Contains(canary, StringComparison.Ordinal) ||
            !secretAnswer.Contains(CodexCliInvocationPolicy.PromptRedactionMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Redacting individual secrets before the full task leaked task text.");
        }
        if (CodexCliInvocationPolicy.SafeErrorSummary(0, false).Length != 0 ||
            !CodexCliInvocationPolicy.SafeErrorSummary(1, false)
                .Contains("code 1", StringComparison.Ordinal) ||
            CodexCliInvocationPolicy.SafeErrorSummary(1, true)
                .Contains(canary, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Codex error privacy policy regressed.");
        }

        const string originalConfig =
            "# user-owned settings\n" +
            "developer_instructions = \"\"\"\n" +
            "[mcp_servers.talvora_local]\n" +
            "the header above is a literal example, not a table\n" +
            "\"\"\"\n" +
            "literal_instructions = '''\n" +
            "[mcp_servers.talvora_local]\n" +
            "another sample inside a literal string\n" +
            "'''\n" +
            "[mcp_servers.\"talvora_local.env\"] # DIFFERENT quoted server\n" +
            "url = \"https://quoted.invalid\"\n" +
            "[mcp_servers.\"talvora_ local\"] # DIFFERENT spaced server\n" +
            "url = \"https://spaced.invalid\"\n" +
            "[mcp_servers.talvora_local] # old registration\n" +
            "url = \"https://old.invalid\"\n" +
            "description = \"\"\"\n" +
            "[other] # not a real header inside removed description\n" +
            "\"\"\"\n" +
            "[other] # unrelated commented table\n" +
            "keep = true\n" +
            "[mcp_servers.other]\n" +
            "url = \"https://other.invalid\"\n" +
            "[mcp_servers.\"talvora_local\".env] # remove stale child\n" +
            "OLD = \"do-not-retain\"\n" +
            "[[unrelated.arrays]] # more user settings\n" +
            "name = \"preserve me\"\n";
        var updated = CodexMcpConfiguration.UpsertTalvoraLocal(
            originalConfig, "http://127.0.0.1:7676/mcp/dev");
        const string expectedRegistration =
            "[mcp_servers.talvora_local]\n" +
            "url = \"http://127.0.0.1:7676/mcp/dev\"\n" +
            "enabled_tools = [\n" +
            "  \"talvora_source_edit_guide\", \"talvora_read_source\", \"talvora_apply_patch\",\n" +
            "  \"talvora_apply_edits\", \"talvora_structural_edit\", \"talvora_semantic_edit\",\n" +
            "  \"talvora_run_powershell\", \"talvora_git_run\",\n" +
            "]\n";
        if (!updated.Contains("keep = true", StringComparison.Ordinal) ||
            !updated.Contains("https://other.invalid", StringComparison.Ordinal) ||
            !updated.Contains("preserve me", StringComparison.Ordinal) ||
            !updated.Contains("https://quoted.invalid", StringComparison.Ordinal) ||
            !updated.Contains("https://spaced.invalid", StringComparison.Ordinal) ||
            !updated.Contains("another sample inside a literal string", StringComparison.Ordinal) ||
            !updated.Contains("literal example, not a table", StringComparison.Ordinal) ||
            updated.Contains("old.invalid", StringComparison.Ordinal) ||
            updated.Contains("do-not-retain", StringComparison.Ordinal) ||
            updated.Contains("not a real header inside removed description", StringComparison.Ordinal) ||
            // Two preserved examples inside multiline strings plus one real
            // newly appended header: three total textual occurrences.
            updated.Split("[mcp_servers.talvora_local]", StringSplitOptions.None).Length != 4 ||
            updated != CodexMcpConfiguration.UpsertTalvoraLocal(
                updated, "http://127.0.0.1:7676/mcp/dev") ||
            !updated.Replace("\r\n", "\n", StringComparison.Ordinal)
                .EndsWith(expectedRegistration, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Codex TOML upsert lost unrelated user settings or is not idempotent.");
        }

        const string otherServerFixture =
            "# User comment and exact CRLF\r\n" +
            "model = \"do-not-change\"\r\n" +
            "\r\n" +
            "[mcp_servers.other] # preserve table comment\r\n" +
            "url = \"https://another.example/mcp\"\r\n" +
            "enabled_tools = [\"unchanged\"]\r\n" +
            "\r\n" +
            "[features]\r\n" +
            "multi_agent = true\r\n";
        var withOtherServers = CodexMcpConfiguration.UpsertTalvoraLocal(
            otherServerFixture, "http://127.0.0.1:7676/mcp/dev");
        if (!withOtherServers.StartsWith(otherServerFixture, StringComparison.Ordinal) ||
            !withOtherServers.EndsWith(
                expectedRegistration.Replace("\n", "\r\n", StringComparison.Ordinal),
                StringComparison.Ordinal) ||
            withOtherServers != CodexMcpConfiguration.UpsertTalvoraLocal(
                withOtherServers, "http://127.0.0.1:7676/mcp/dev"))
        {
            throw new InvalidOperationException(
                "Codex registration changed unrelated TOML bytes or reordered other MCP servers.");
        }

        var temp = Path.Combine(Path.GetTempPath(),
            "Talvora-Codex-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var userData = Path.Combine(temp, "profile");
            if (CodexPreferencesStore.Read(userData) !=
                CodexPreferencesStore.Balanced)
            {
                throw new InvalidOperationException(
                    "New Codex users must initially use a balanced profile.");
            }
            await CodexPreferencesStore.WriteAsync(
                userData, CodexPreferencesStore.Trusted);
            if (CodexPreferencesStore.Read(userData) !=
                CodexPreferencesStore.Trusted)
            {
                throw new InvalidOperationException(
                    "Trusted development preference did not persist.");
            }
            await CodexPreferencesStore.WriteAsync(
                userData, CodexPreferencesStore.Balanced);
            if (CodexPreferencesStore.Read(userData) !=
                CodexPreferencesStore.Balanced)
            {
                throw new InvalidOperationException(
                    "Balanced development preference could not be restored.");
            }
            var windowsMode = CodexCliInvocationPolicy.CreateArguments(
                temp, "Build", "gpt-6.1-sol", "medium", "workspace-write",
                windowsSandboxMode: "unelevated");
            if (!windowsMode.Contains("windows.sandbox=\"unelevated\""))
            {
                throw new InvalidOperationException(
                    "Supported Windows sandbox mode override was not passed.");
            }
            var capturedPath = Path.Combine(temp, "bounded.txt");
            var content = "BEGIN-İstanbul-" + new string('x', 250_000) + "-SON-çöğüş";
            using (var reader = new StringReader(content))
            {
                var captured = await ProcessOutputPump.PumpToUtf8FileAsync(
                    reader, capturedPath, maximumCharacters: 2048);
                var written = await File.ReadAllTextAsync(capturedPath);
                if (captured.TotalCharacters != content.Length ||
                    !captured.Truncated || written.Length > 2300 ||
                    !written.Contains("BEGIN-İstanbul", StringComparison.Ordinal) ||
                    !written.EndsWith("-SON-çöğüş", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Interactive output pump lost UTF-8 or exceeded disk budget.");
                }
            }

            // A high-volume JSONL stream must keep reporting commands even
            // after its bounded stdout head has stopped growing.
            var session = "01a128db-1111-7111-8111-222222222222";
            var eventLines = new System.Text.StringBuilder();
            eventLines.AppendLine("{\"type\":\"thread.started\",\"thread_id\":\"" +
                session + "\"}");
            for (var index = 0; index < 900; index++)
            {
                eventLines.AppendLine(
                    "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"dotnet test\",\"exit_code\":0}}");
            }
            var journalPath = Path.Combine(temp, "codex-live-journal.json");
            using (var reader = new StringReader(eventLines.ToString()))
            {
                await ProcessOutputPump.PumpToUtf8FileAsync(
                    reader, Path.Combine(temp, "codex-bounded-output.txt"),
                    maximumCharacters: 512,
                    codexProgressPath: journalPath);
            }
            var journal = System.Text.Json.JsonSerializer
                .Deserialize<CodexCliEventResult>(
                    await File.ReadAllTextAsync(journalPath));
            if (journal?.SessionId != session ||
                journal.CompletedCommands != 900 ||
                journal.FailedCommands != 0)
            {
                throw new InvalidOperationException(
                    "Long-running Codex progress counters stopped at the stdout cap.");
            }
            var streamingPath = Path.Combine(temp, "codex-live-stream.json");
            using var eventReader = new BlockingTextReader(
                "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"dotnet test\",\"exit_code\":0}}\n");
            using var streamCancel = new CancellationTokenSource();
            var livePump = ProcessOutputPump.PumpToUtf8FileAsync(
                eventReader, Path.Combine(temp, "codex-event-head.txt"),
                maximumCharacters: 64,
                cancellationToken: streamCancel.Token,
                codexProgressPath: streamingPath);
            await eventReader.WaitingForNextRead.WaitAsync(TimeSpan.FromSeconds(5));
            var liveState = System.Text.Json.JsonSerializer
                .Deserialize<CodexCliEventResult>(
                    await File.ReadAllTextAsync(streamingPath));
            if (liveState?.CompletedCommands != 1)
            {
                throw new InvalidOperationException(
                    "Codex progress was not visible before child process completion.");
            }
            streamCancel.Cancel();
            try { await livePump; }
            catch (OperationCanceledException) { }

            // Windows PowerShell binds optional null string parameters as
            // empty strings. Explicitly verify an empty progress path is off.
            var noProgressOutput = Path.Combine(temp, "no-progress-pump.txt");
            using (var reader = new StringReader("NORMAL-CLI-OUTPUT"))
            {
                await ProcessOutputPump.PumpToUtf8FileAsync(
                    reader, noProgressOutput, maximumCharacters: 128,
                    codexProgressPath: string.Empty);
            }
            if (await File.ReadAllTextAsync(noProgressOutput) !=
                "NORMAL-CLI-OUTPUT")
            {
                throw new InvalidOperationException(
                    "An empty optional progress path broke normal Codex CLI output.");
            }
            var discardedPath = Path.Combine(temp, "discarded.txt");
            using (var reader = new StringReader("secret payload"))
            {
                var discarded = await ProcessOutputPump.PumpToUtf8FileAsync(
                    reader, discardedPath, maximumCharacters: 0);
                if (!discarded.Truncated || discarded.TotalCharacters != 14 ||
                    (await File.ReadAllTextAsync(discardedPath)).Length != 0)
                {
                    throw new InvalidOperationException(
                        "Codex diagnostic stderr was persisted despite discard policy.");
                }
            }
            var partialPath = Path.Combine(temp, "partial-before-timeout.txt");
            using var blockingReader = new BlockingTextReader("EARLY-OUTPUT-CANARY");
            using var cancellation = new CancellationTokenSource();
            var runningPump = ProcessOutputPump.PumpToUtf8FileAsync(
                blockingReader, partialPath, maximumCharacters: 64,
                cancellationToken: cancellation.Token);
            await blockingReader.WaitingForNextRead.WaitAsync(TimeSpan.FromSeconds(5));
            await using var partialStream = new FileStream(
                partialPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var partialReader = new StreamReader(
                partialStream, System.Text.Encoding.UTF8);
            if (!(await partialReader.ReadToEndAsync()).Contains(
                    "EARLY-OUTPUT-CANARY", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Interrupted child output was buffered but not persisted.");
            }
            cancellation.Cancel();
            try
            {
                await runningPump;
                throw new InvalidOperationException(
                    "Cancelled output pump incorrectly reported success.");
            }
            catch (OperationCanceledException)
            {
                // Expected: the already persisted prefix remains usable.
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
        await RunAgentJobRegistryAsync();
        await RunClaudeAgentJobRegistryAsync();
    }

    internal static async Task RunAgentJobRegistryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-R1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registry = new AgentJobRegistry(root);
            var work = Path.Combine(root, "workspace");
            Directory.CreateDirectory(work);
            var ended = new List<string>();
            var first = registry.Create("codex", work,
                onTerminal: value => ended.Add(value.State));
            registry.Start(first);
            if (registry.Get("codex", first.Metadata.JobId).Metadata.State != "running")
                throw new InvalidOperationException("Agent registry did not start a job.");
            try
            {
                registry.Create("claude", Path.Combine(work, "."));
                throw new InvalidOperationException("Same-directory job was accepted.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("çalışma", StringComparison.OrdinalIgnoreCase)) { }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 45" },
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Test helper could not start.");
            registry.RecordProcess(first, process.Id, process.StartTime.ToUniversalTime());
            var recovered = new AgentJobRegistry(root);
            await recovered.RecoverAsync();
            var orphaned = recovered.Get("codex", first.Metadata.JobId);
            if (orphaned.Metadata.State != "orphaned")
                throw new InvalidOperationException("Restart did not identify the live orphan.");
            recovered.Cancel(orphaned);
            if (!process.WaitForExit(10000) || recovered.Get("codex", first.Metadata.JobId).Metadata.State != "cancelled")
                throw new InvalidOperationException("Orphan cancellation did not kill the process.");

            using var reused = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 45" },
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("PID identity test could not start.");
            try
            {
                var other = registry.Create("claude", Path.Combine(root, "other"));
                registry.Start(other);
                registry.Progress(other, sessionId: "recover-this-session",
                    toolCalls: 5, failedTools: 1);
                registry.RecordProcess(other, reused.Id,
                    reused.StartTime.ToUniversalTime().AddMinutes(-1));
                var restarted = new AgentJobRegistry(root);
                await restarted.RecoverAsync();
                var stale = restarted.Get("claude", other.Metadata.JobId);
                if (stale.Metadata.State != "failed" ||
                    stale.Metadata.Error != AgentJobRegistry.InterruptedError ||
                    stale.Metadata.SessionId != "recover-this-session" ||
                    stale.Metadata.ToolCalls != 5)
                    throw new InvalidOperationException("Reused PID was accepted.");
                restarted.Cancel(stale);
                if (reused.HasExited) throw new InvalidOperationException("Unrelated PID was killed.");
            }
            finally { if (!reused.HasExited) reused.Kill(entireProcessTree: true); }

            var limits = new AgentJobRegistry(Path.Combine(root, "limits"));
            for (var i = 0; i < 4; i++)
            {
                var job = limits.Create(i % 2 == 0 ? "codex" : "claude",
                    Path.Combine(root, "unique-" + i));
                limits.Start(job);
            }
            try
            {
                limits.Create("codex", Path.Combine(root, "unique-5"));
                throw new InvalidOperationException("Fifth concurrent job was accepted.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("4", StringComparison.Ordinal)) { }
            var parallel = registry.Create("claude", work, allowParallel: true);
            registry.Start(parallel);
            registry.Finish(parallel, "completed");
            var cancelled = new List<string>();
            var queued = registry.Create("codex", Path.Combine(root, "cancel-queued"),
                onTerminal: value => cancelled.Add(value.State));
            registry.Cancel(queued);
            if (queued.Metadata.State != "cancelled")
                throw new InvalidOperationException("Queued cancellation failed.");
            var running = registry.Create("claude", Path.Combine(root, "cancel-running"),
                onTerminal: value => cancelled.Add(value.State));
            registry.Start(running);
            registry.Cancel(running);
            if (!running.CancellationToken.IsCancellationRequested)
                throw new InvalidOperationException("Running cancellation token was not set.");
            registry.Finish(running, "cancelled");
            if (cancelled.Count != 2 || cancelled.Any(state => state != "cancelled"))
                throw new InvalidOperationException("Cancelled desktop worklog hooks failed.");
            registry.Finish(first, "completed");
            if (ended.Count != 1 || ended[0] != "completed")
                throw new InvalidOperationException("Terminal worklog hook was not fired exactly once.");
            if (File.ReadAllText(Path.Combine(root, first.Metadata.JobId + ".json"))
                    .Contains("prompt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Registry persisted a prompt.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class BlockingTextReader(string firstChunk) : TextReader
    {
        private bool _first = true;
        private readonly TaskCompletionSource _waiting =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitingForNextRead => _waiting.Task;

        public override async ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                firstChunk.AsMemory().CopyTo(buffer);
                return firstChunk.Length;
            }
            _waiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
