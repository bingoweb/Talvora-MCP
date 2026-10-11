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
            originalConfig, "http://127.0.0.1:7676/mcp");
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
                updated, "http://127.0.0.1:7676/mcp"))
        {
            throw new InvalidOperationException(
                "Codex TOML upsert lost unrelated user settings or is not idempotent.");
        }

        var temp = Path.Combine(Path.GetTempPath(),
            "Talvora-Codex-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
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
