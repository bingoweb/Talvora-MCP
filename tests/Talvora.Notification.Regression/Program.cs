using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Talvora;
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    public static async Task<int> Main()
    {
        if (Environment.GetCommandLineArgs().Contains("--n7-only", StringComparer.Ordinal))
        {
            AssertN7History();
            return 0;
        }
        if (Environment.GetCommandLineArgs().Contains("--n4-only", StringComparer.Ordinal))
        {
            await AssertN4AgentEventsAsync();
            Console.WriteLine("N4_AGENT_EVENTS_GREEN");
            return 0;
        }
        await DesktopProgressProtocol.AssertContractAsync();
        DesktopProgressPresentationState.AssertContract();
        ManagedMcpAggregateHealthPolicy.AssertContract();
        AssertUnifiedVersionContract();
        AssertUpstreamRecoveryContract();
        await AssertWorkVisibilityFilteringAsync();
        AssertN1ManifestCopy();
        await AssertN1WorklogCopyAsync();
        await AssertN2ProjectSessionsAsync();
        await AssertN3FactsAsync();
        await AssertN9SubjectsAsync();
        await AssertN10ChangesAsync();
        await AssertRemotionWorklogAsync();
        AssertN6Details();
        AssertN7History();
        AssertTrayIconArt();
        await AssertShellCommandsAsync();
        await AssertN3bCodeTerminalAsync();
        await AssertN4AgentEventsAsync();
        await AssertDiffEvidenceCoverageAsync();
        await AssertSemanticMessagingAndCanonicalToolAsync();
        await AssertConcurrentCancellationTruthAsync();
        await AssertConcurrentFailurePrecedenceAsync();
        await AssertQueuePressureAndDrainAsync();
        await AssertCatastrophicTerminalOverloadIsObservableAsync();
        await AssertBoundedShutdownAsync();
        await AssertUncooperativeDeliveryCannotBlockShutdownAsync();
        await AssertWriteCancellationAsync();
        await AssertForeignPipeServerIsRejectedAsync();
        await AssertInstalledTrayPipeIsTrustedWhenRunningAsync();
        await AssertPipeReadTimeoutRecoveryAsync();
        await AssertOffScreenPinnedPlacementIsNormalizedAsync();

        Console.WriteLine(
            "DESKTOP_PROGRESS_BEHAVIOR_GREEN");
        return 0;
    }

    private static void AssertUnifiedVersionContract()
    {
        var serviceVersion = TalvoraRuntimeMetadata.InformationalVersion;
        var trayVersion = typeof(ControlCenterVersionService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!serviceVersion.StartsWith("3.0.0-dev+", StringComparison.Ordinal) ||
            !string.Equals(serviceVersion, trayVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Service and Control Center must use one assembly informational version.");
        }
    }

    private static void AssertUpstreamRecoveryContract()
    {
        const string upstreamJson = "{\"schema_version\":1,\"live\":true,\"ready\":false,\"components\":{\"control-plane\":{\"status\":\"degraded\"},\"response-delivery\":{\"status\":\"ok\"}}}";
        using var fixture = JsonDocument.Parse(upstreamJson);
        var health = ManagedMcpTunnelHealthService.ParseSnapshot(fixture.RootElement);
        var runtime = new ManagedMcpTunnelRuntimeStatus(false, true, true, true, "fixture");
        if (ControlCenterRepairService.ClassifyFault(health, true, runtime) !=
            ManagedMcpRecoveryFaultKind.UpstreamDegraded)
        {
            throw new InvalidOperationException("Upstream degradation classified incorrectly.");
        }
        const string localJson = "{\"schema_version\":1,\"live\":true,\"ready\":false,\"components\":{\"control-plane\":{\"status\":\"degraded\"},\"response-delivery\":{\"status\":\"degraded\"}}}";
        using var localFixture = JsonDocument.Parse(localJson);
        var localHealth = ManagedMcpTunnelHealthService.ParseSnapshot(
            localFixture.RootElement);
        if (ControlCenterRepairService.ClassifyFault(localHealth, true, runtime) !=
                ManagedMcpRecoveryFaultKind.LocalFault ||
            ControlCenterRepairService.ClassifyFault(health, false, runtime) !=
                ManagedMcpRecoveryFaultKind.LocalFault ||
            ControlCenterRepairService.ClassifyFault(
                health, true, runtime with { ProcessRunning = false }) !=
                ManagedMcpRecoveryFaultKind.LocalFault)
        {
            throw new InvalidOperationException("A local fault was mistaken for an upstream outage.");
        }
        var delays = new[] { 1, 2, 5, 10, 10 };
        for (var index = 0; index < delays.Length; index++)
        {
            if (ControlCenterRepairService.GetUpstreamRetryDelay(index + 1) !=
                TimeSpan.FromMinutes(delays[index]))
            {
                throw new InvalidOperationException("Incorrect upstream recovery delay.");
            }
        }
        AssertRecoveryLogContract();
    }

    private static void AssertRecoveryLogContract()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "Talvora-R4-Log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "recovery.log");
        try
        {
            TrayLog.PathOverrideForTests = path;
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                try { throw new InvalidOperationException("synthetic upstream outage"); }
                catch (InvalidOperationException ex)
                {
                    TrayLog.Write("R4 attempt " + attempt, ex);
                }
            }
            TrayLog.Write("R4 duplicate probe");
            TrayLog.Write("R4 duplicate probe");
            var lines = File.ReadAllLines(path);
            if (lines.Count(line => line.Contains(" | Stack:", StringComparison.Ordinal)) != 1 ||
                lines.Count(line => line.Contains("R4 attempt ", StringComparison.Ordinal)) != 6 ||
                lines.Count(line => line.Contains("R4 duplicate probe", StringComparison.Ordinal)) != 1)
            {
                throw new InvalidOperationException("Recovery log incident deduplication failed.");
            }
        }
        finally
        {
            TrayLog.PathOverrideForTests = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertWorkVisibilityFilteringAsync()
    {
        var observed =
            new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            _ = await notifier.RunToolCallAsync<int>(
                "talvora_service_get",
                arguments: null,
                _ => ValueTask.FromResult(1),
                CancellationToken.None);
            if (!observed.IsEmpty)
            {
                throw new InvalidOperationException(
                    "Passive polling produced a desktop worklog.");
            }

            _ = await notifier.RunToolCallAsync<int>(
                "talvora_read_source",
                arguments: null,
                _ => ValueTask.FromResult(2),
                CancellationToken.None);
            _ = await notifier.RunToolCallAsync<object>(
                "talvora_search_text",
                arguments: null,
                _ => ValueTask.FromResult<object>(new { success = false }),
                CancellationToken.None);
            if (!observed.IsEmpty)
            {
                throw new InvalidOperationException(
                    "N1: inspection success or failure must not produce a frame.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
    }

    private static async Task AssertDiffEvidenceCoverageAsync()
    {
        var observed =
            new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            var oldBlock = string.Join(
                '\n',
                Enumerable.Range(1, 1000)
                    .Select(index => $"old-{index:00}();"));
            var newBlock = string.Join(
                '\n',
                Enumerable.Range(1, 1000)
                    .Select(index => $"new-{index:00}();"));
            var arguments = new Dictionary<string, JsonElement>
            {
                ["workspaceRoot"] =
                    JsonSerializer.SerializeToElement("C:\\repo"),
                ["changes"] =
                    JsonSerializer.SerializeToElement(
                        new[]
                        {
                            new
                            {
                                operation = "update",
                                path = "src/Test.cs",
                                edits = new[]
                                {
                                    new
                                    {
                                        expectedText = oldBlock,
                                        newText = newBlock,
                                    },
                                },
                            },
                        }),
            };

            _ = await notifier.RunToolCallAsync<object>(
                "talvora_apply_edits",
                arguments,
                _ => ValueTask.FromResult<object>(new
                {
                    success = true, status = "committed",
                    files = new[] { new { newPath = "src/Test.cs", addedLines = 1000, removedLines = 1000,
                        codePreview = Talvora.WorklogDiff.TextDiffBuilder.Build(oldBlock, newBlock, "src/Test.cs", "src/Test.cs").Preview } },
                }),
                CancellationToken.None);

            var evidence = observed
                .Where(message => message.Kind == DesktopProgressKind.Completed)
                .Select(message => message.Evidence)
                .FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate?.CodePreview));
            var preview = evidence?.CodePreview ?? string.Empty;
            if (!preview.Contains(
                    "@@ FILE src/Test.cs",
                    StringComparison.Ordinal) ||
                !preview.Contains(
                    "-old-400();",
                    StringComparison.Ordinal) ||
                !preview.Contains(
                    "… 1600 satır daha · tamamı Çalışma günlüğünde",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Structured source edits did not expose a sufficiently rich real diff preview.");
            }

            var messageWithCode = observed.First(message => message.Evidence == evidence);
            await using var frame = new MemoryStream();
            await DesktopProgressProtocol.WriteFrameAsync(frame, messageWithCode);
            frame.Position = 0;
            var restoredCode = await DesktopProgressProtocol.ReadFrameAsync(frame);
            if (restoredCode?.Evidence?.CodePreview != preview)
            {
                throw new InvalidOperationException("A large real diff lost code during IPC round-trip.");
            }

            if (evidence?.AddedLines != 1000 ||
                evidence.RemovedLines != 1000)
            {
                throw new InvalidOperationException(
                    "Structured edit evidence line counts were not preserved.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
    }

    private static async Task AssertSemanticMessagingAndCanonicalToolAsync()
    {
        var sourceObserved =
            new ConcurrentQueue<DesktopProgressMessage>();
        var sourceNotifier = CreateNotifier(sourceObserved);
        try
        {
            _ = await sourceNotifier.RunToolCallAsync<int>(
                "talvora_apply_patch",
                arguments: null,
                _ => ValueTask.FromResult(1),
                CancellationToken.None);

            var sourceMessages = sourceObserved.ToArray();
            if (sourceMessages.Length == 0 ||
                sourceMessages.Any(message =>
                    !string.Equals(
                        message.ToolName,
                        "apply_patch",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Desktop worklog did not preserve the canonical source-edit tool name.");
            }

            if (sourceMessages.Any(message =>
                    message.Message.Contains(
                        "Åimdi sÄ±radaki adÄ±ma",
                        StringComparison.OrdinalIgnoreCase) ||
                    message.Message.Contains(
                        "Son yaptÄ±klarÄ±m",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Desktop worklog repeated the current action or promised an unverified next step.");
            }
        }
        finally
        {
            await sourceNotifier.DisposeAsync();
        }

        var failureObserved =
            new ConcurrentQueue<DesktopProgressMessage>();
        var failureNotifier = CreateNotifier(failureObserved);
        try
        {
            _ = await failureNotifier.RunToolCallAsync<object>(
                "talvora_dotnet_build",
                arguments: null,
                _ => ValueTask.FromResult<object>(
                    new { success = false }),
                CancellationToken.None);

            var terminal = failureObserved
                .Where(message => IsTerminal(message.Kind))
                .Last();
            if (!string.Equals(
                    terminal.ToolName,
                    "dotnet_build",
                    StringComparison.Ordinal) ||
                terminal.Kind != DesktopProgressKind.Failed)
            {
                throw new InvalidOperationException(
                    "Desktop worklog failure did not preserve canonical tool identity and failure truth.");
            }

            var forbiddenPromises = new[]
            {
                "yeniden deneyeceÄŸim",
                "dÃ¼zelteceÄŸim",
                "kontrol edip",
            };
            if (forbiddenPromises.Any(phrase =>
                    terminal.Message.Contains(
                        phrase,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Desktop worklog failure copy promised future work that had not happened.");
            }
        }
        finally
        {
            await failureNotifier.DisposeAsync();
        }
    }

    private static async Task AssertN1WorklogCopyAsync()
    {
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            _ = await notifier.RunToolCallAsync<int>(
                "talvora_apply_patch",
                null,
                _ => ValueTask.FromResult(1),
                CancellationToken.None);
            var terminal = observed.Where(x => IsTerminal(x.Kind)).Last();
            if (terminal.Title != "Kod değişikliği uygulandı" ||
                terminal.Message != "Adım başarıyla tamamlandı.")
            {
                throw new InvalidOperationException("N1 success title or message mismatch.");
            }
            AssertN1FrameCopy(observed);
            var errorResult = new CallToolResult
            {
                StructuredContent = JsonSerializer.SerializeToElement(
                    new { success = false, error = new { code = "EXPECTED_REVISION_MISMATCH" } }),
            };
            if (!WorklogFailureClassifier.Describe(errorResult, "apply_patch")
                    .StartsWith("Dosya bu arada değişmişti", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("N1 revision conflict reason mismatch.");
            }
            _ = await notifier.RunToolCallAsync(
                "talvora_apply_patch", null,
                _ => ValueTask.FromResult(errorResult), CancellationToken.None);
            terminal = observed.Where(x => IsTerminal(x.Kind)).Last();
            if (terminal.Title != "Kod değişikliği uygulanamadı" ||
                !terminal.Message.StartsWith("Dosya bu arada değişmişti", StringComparison.Ordinal))
                throw new InvalidOperationException("N1 structured failure terminal mismatch.");
            AssertN1FrameCopy(observed);
            observed.Clear();
            notifier.BeginCodexJob("n1-timeout", "C:\\repo");
            notifier.EndCodexJob("n1-timeout", "timed_out", TimeSpan.FromSeconds(10));
            terminal = observed.Where(x => IsTerminal(x.Kind)).Last();
            if (terminal.Title != "Codex görevi tamamlanamadı" ||
                terminal.Message != "Görev ayrılan sürede bitmedi; yapılan değişiklikler tamamlanmış sayılmıyor.")
                throw new InvalidOperationException("N1 Codex timeout message mismatch.");
            AssertN1FrameCopy(observed);
        }
        finally
        {
            await notifier.DisposeAsync();
        }
    }

    private static void AssertN1FrameCopy(IEnumerable<DesktopProgressMessage> frames)
    {
        foreach (var frame in frames)
        {
            var text = frame.Title + "\n" + frame.Message;
            if (new[] { "Son yaptıklarım", "Amaç:", "Geçen süre", "Sonuç:",
                "apply_patch", "talvora_", "paralel adım" }
                .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                System.Text.RegularExpressions.Regex.IsMatch(
                    text, "gelistir|dogrul|cagri|gecen sure|tamamladi",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException("N1 copy contains forbidden text.");
        }
    }

    private static void AssertN1ManifestCopy()
    {
        var lookup = typeof(TalvoraDesktopProgressNotifier).GetMethod(
            "BuildNarrative", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("N1 missing narrative mapping.");
        foreach (var toolName in TalvoraToolManifest.Names)
        {
            var narrative = lookup.Invoke(null, [toolName, null, null]);
            var titlesProperty = narrative?.GetType().GetProperty("Titles");
            if (titlesProperty?.GetValue(narrative) is not WorklogTitles titles)
                throw new InvalidOperationException("N1 missing copy category: " + toolName);

            var canonical = toolName.StartsWith("talvora_", StringComparison.OrdinalIgnoreCase)
                ? toolName["talvora_".Length..] : toolName;
            var strings = new[] { titles.Running, titles.Succeeded, titles.Failed,
                WorklogCopy.RunningFallback, WorklogCopy.SucceededFallback };
            // Whole words only (N9): "Klasörü listele" is Turkish, not the tool name "list".
            var namePattern = new System.Text.RegularExpressions.Regex(
                @"(?<![\p{L}\p{N}_])(" + System.Text.RegularExpressions.Regex.Escape(toolName) + "|" +
                System.Text.RegularExpressions.Regex.Escape(canonical) + @")(?![\p{L}\p{N}_])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (strings.Any(text => namePattern.IsMatch(text)))
                throw new InvalidOperationException("N1 technical tool name visible: " + toolName);
        }
        var args = new Dictionary<string, JsonElement>
        {
            ["arguments"] = JsonSerializer.SerializeToElement(
                new[] { "-c", "core.quotepath=false", "PUSH", "origin", "main" }),
        };
        var classified = lookup.Invoke(null, ["talvora_git_run", args, null]);
        var values = classified?.GetType().GetProperty("Titles")?.GetValue(classified) as WorklogTitles;
        if (values?.Succeeded != WorklogCopy.Push.Succeeded + " · origin/main")
            throw new InvalidOperationException("N1 Git subcommand classification mismatch.");
    }

    private static async Task AssertN2ProjectSessionsAsync()
    {
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        var project = Path.Combine(
            Path.GetTempPath(), "Talvora-N2-project-" + Guid.NewGuid().ToString("N"));
        var other = Path.Combine(
            Path.GetTempPath(), "Talvora-N2-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(other);
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        Directory.CreateDirectory(Path.Combine(other, ".git"));
        try
        {
            foreach (var root in new[] { project, project, other })
            {
                _ = await notifier.RunToolCallAsync(
                    "notification_n2_session",
                    new Dictionary<string, JsonElement>
                    {
                        ["workspaceRoot"] = JsonSerializer.SerializeToElement(root),
                    },
                    _ => ValueTask.FromResult(1),
                    CancellationToken.None);
            }

            var terminals = observed.Where(message =>
                message.Kind == DesktopProgressKind.Completed).ToArray();
            if (terminals.Length != 3 ||
                terminals[0].OperationId != terminals[1].OperationId ||
                terminals[1].OperationId == terminals[2].OperationId ||
                terminals[0].WorklogFrame?.Timeline.Count != 1 ||
                terminals[1].WorklogFrame?.Timeline.Count != 2 ||
                terminals[2].WorklogFrame?.Timeline.Count != 1 ||
                terminals[0].WorklogFrame?.Header.ProjectName !=
                    Path.GetFileName(project) ||
                terminals[2].WorklogFrame?.Header.ProjectName !=
                    Path.GetFileName(other))
            {
                throw new InvalidOperationException(
                    "N2 worklog calls did not share a project session or isolate another project.");
            }
            notifier.BeginClaudeJob("n2-claude");
            notifier.UpdateClaudeJob("n2-claude", 1, TimeSpan.FromSeconds(25));
            var claude = observed.Last().WorklogFrame;
            if (claude?.Header.ActorName != "Claude" ||
                claude.ExpectedNextUpdateSeconds != 60 ||
                claude.Current.State != WorklogStepState.Running)
            {
                throw new InvalidOperationException(
                    "N2 Claude heartbeat did not carry its 60-second update expectation.");
            }
            notifier.EndClaudeJob("n2-claude", "completed", TimeSpan.FromSeconds(25));
        }
        finally
        {
            await notifier.DisposeAsync();
            Directory.Delete(project, recursive: true);
            Directory.Delete(other, recursive: true);
        }
    }

    private static async Task AssertConcurrentCancellationTruthAsync()
    {
        var observed =
            new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            using var cancelledCall =
                new CancellationTokenSource();
            var secondGate =
                new TaskCompletionSource<int>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var first = notifier.RunToolCallAsync<int>(
                    "notification_cancel_a",
                    arguments: null,
                    async token =>
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            token);
                        return 1;
                    },
                    cancelledCall.Token)
                .AsTask();
            var second = notifier.RunToolCallAsync<int>(
                    "notification_cancel_b",
                    arguments: null,
                    token => new ValueTask<int>(
                        secondGate.Task.WaitAsync(token)),
                    CancellationToken.None)
                .AsTask();

            await Task.Delay(50);
            cancelledCall.Cancel();
            try
            {
                _ = await first;
                throw new InvalidOperationException(
                    "Cancelled notification call unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
            }

            if (!observed.Any(message =>
                    message.Kind == DesktopProgressKind.Running &&
                    message.Message == "Devam ediyor.") ||
                observed.Any(message =>
                    message.Message.Contains("paralel adım", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Concurrent cancellation was not represented as continuing work.");
            }

            secondGate.TrySetResult(2);
            _ = await second;

            var cancellationTerminal = observed
                .Where(message => IsTerminal(message.Kind))
                .Last();
            if (cancellationTerminal.Kind !=
                DesktopProgressKind.Cancelled)
            {
                throw new InvalidOperationException(
                    "Concurrent cancellation was lost from the final worklog state.");
            }

            var cancelledGeneration =
                cancellationTerminal.OperationId;
            _ = await notifier.RunToolCallAsync<int>(
                "notification_next_generation",
                arguments: null,
                _ => ValueTask.FromResult(3),
                CancellationToken.None);
            var nextTerminal = observed
                .Where(message => IsTerminal(message.Kind))
                .Last();
            if (nextTerminal.Kind !=
                    DesktopProgressKind.Completed ||
                !string.Equals(
                    nextTerminal.OperationId,
                    cancelledGeneration,
                    StringComparison.Ordinal) ||
                nextTerminal.WorklogFrame is null ||
                cancellationTerminal.WorklogFrame is null ||
                nextTerminal.WorklogFrame.Timeline.Count <=
                    cancellationTerminal.WorklogFrame.Timeline.Count)
            {
                throw new InvalidOperationException(
                    "Sequential worklog calls did not retain their session timeline.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
    }

    private static async Task AssertConcurrentFailurePrecedenceAsync()
    {
        var observed =
            new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            var failReady = NewSignal();
            var successReady = NewSignal();
            var releaseFailure = NewSignal();
            var releaseSuccess = NewSignal();

            var failing = notifier.RunToolCallAsync<int>(
                    "notification_failure_a",
                    arguments: null,
                    async token =>
                    {
                        failReady.TrySetResult(true);
                        await releaseFailure.Task.WaitAsync(token);
                        throw new InvalidOperationException(
                            "expected regression failure");
                    },
                    CancellationToken.None)
                .AsTask();
            var succeeding = notifier.RunToolCallAsync<int>(
                    "notification_failure_b",
                    arguments: null,
                    async token =>
                    {
                        successReady.TrySetResult(true);
                        await releaseSuccess.Task.WaitAsync(token);
                        return 4;
                    },
                    CancellationToken.None)
                .AsTask();

            await Task.WhenAll(
                failReady.Task,
                successReady.Task);
            releaseFailure.TrySetResult(true);
            try
            {
                _ = await failing;
                throw new InvalidOperationException(
                    "Expected failing notification call completed successfully.");
            }
            catch (InvalidOperationException ex) when (
                ex.Message == "expected regression failure")
            {
            }

            if (observed.Last().Kind !=
                DesktopProgressKind.Running)
            {
                throw new InvalidOperationException(
                    "A parallel failure prematurely terminated an active worklog.");
            }

            releaseSuccess.TrySetResult(true);
            _ = await succeeding;
            var terminal = observed
                .Where(message => IsTerminal(message.Kind))
                .Last();
            if (terminal.Kind != DesktopProgressKind.Failed)
            {
                throw new InvalidOperationException(
                    "Failure precedence was lost after concurrent work completed.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
    }

    private static async Task AssertQueuePressureAndDrainAsync()
    {
        var observed =
            new ConcurrentQueue<DesktopProgressMessage>();
        var delivered =
            new ConcurrentQueue<DesktopProgressMessage>();
        var deliveryStarted = NewSignal();
        var releaseDelivery = NewSignal();
        var notifier = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            observed.Enqueue,
            async (message, token) =>
            {
                deliveryStarted.TrySetResult(true);
                await releaseDelivery.Task.WaitAsync(token);
                delivered.Enqueue(message);
            },
            shutdownDrainTimeout: TimeSpan.FromSeconds(2));

        try
        {
            _ = await notifier.RunToolCallAsync<int>(
                "notification_pressure_0",
                arguments: null,
                _ => ValueTask.FromResult(0),
                CancellationToken.None);
            await deliveryStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            const int additionalCalls = 320;
            for (var index = 1;
                 index <= additionalCalls;
                 index++)
            {
                _ = await notifier.RunToolCallAsync<int>(
                    $"notification_pressure_{index}",
                    arguments: null,
                    _ => ValueTask.FromResult(index),
                    CancellationToken.None);
            }

            if (notifier.DroppedProgressMessages <= 0 ||
                notifier.DroppedTerminalMessages != 0)
            {
                throw new InvalidOperationException(
                    "Queue pressure did not preserve the terminal delivery lane.");
            }

            releaseDelivery.TrySetResult(true);
            await notifier.DisposeAsync();

            var deliveredTerminalCount = delivered.Count(
                message => IsTerminal(message.Kind));
            if (deliveredTerminalCount !=
                additionalCalls + 1)
            {
                throw new InvalidOperationException(
                    $"Terminal delivery drain mismatch. Expected={additionalCalls + 1}; Actual={deliveredTerminalCount}.");
            }

            if (notifier.PendingTerminalMessages != 0)
            {
                throw new InvalidOperationException(
                    $"Terminal backlog was not fully drained. Remaining={notifier.PendingTerminalMessages}.");
            }

            var terminalSeen = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var message in delivered)
            {
                if (IsTerminal(message.Kind))
                {
                    terminalSeen.Add(message.OperationId);
                    continue;
                }

                if (terminalSeen.Contains(message.OperationId))
                {
                    throw new InvalidOperationException(
                        $"A stale progress frame was delivered after terminal state. Operation={message.OperationId}.");
                }
            }
        }
        finally
        {
            releaseDelivery.TrySetResult(true);
        }
    }

    private static async Task AssertCatastrophicTerminalOverloadIsObservableAsync()
    {
        var deliveryStarted = NewSignal();
        var releaseDelivery = NewSignal();
        var notifier = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            deliveryObserver: null,
            async (_, token) =>
            {
                deliveryStarted.TrySetResult(true);
                await releaseDelivery.Task.WaitAsync(token);
            },
            shutdownDrainTimeout: TimeSpan.FromSeconds(2));

        try
        {
            _ = await notifier.RunToolCallAsync<int>(
                "notification_overload_seed",
                arguments: null,
                _ => ValueTask.FromResult(0),
                CancellationToken.None);
            await deliveryStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2));

            const int overloadCalls = 1_100;
            for (var index = 0; index < overloadCalls; index++)
            {
                _ = await notifier.RunToolCallAsync<int>(
                    $"notification_overload_{index}",
                    arguments: null,
                    _ => ValueTask.FromResult(index),
                    CancellationToken.None);
            }

            if (notifier.DroppedTerminalMessages <= 0)
            {
                throw new InvalidOperationException(
                    "Catastrophic terminal overload was not observable.");
            }

            if (notifier.PendingTerminalMessages > 1_024)
            {
                throw new InvalidOperationException(
                    $"Terminal backlog exceeded its hard bound: {notifier.PendingTerminalMessages}.");
            }
        }
        finally
        {
            releaseDelivery.TrySetResult(true);
            await notifier.DisposeAsync();
        }
    }

    private static async Task AssertBoundedShutdownAsync()
    {
        var deliveryStarted = NewSignal();
        var notifier = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            deliveryObserver: null,
            async (_, token) =>
            {
                deliveryStarted.TrySetResult(true);
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    token);
            },
            shutdownDrainTimeout: TimeSpan.FromMilliseconds(250));

        _ = await notifier.RunToolCallAsync<int>(
            "notification_shutdown_stall",
            arguments: null,
            _ => ValueTask.FromResult(1),
            CancellationToken.None);
        await deliveryStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        await notifier.DisposeAsync();
        stopwatch.Stop();
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
        {
            throw new InvalidOperationException(
                $"Desktop progress shutdown exceeded its bounded drain contract: {stopwatch.Elapsed}.");
        }
    }

    private static async Task AssertUncooperativeDeliveryCannotBlockShutdownAsync()
    {
        var deliveryStarted = NewSignal();
        var neverCompletes = NewSignal();
        var notifier = new TalvoraDesktopProgressNotifier(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            deliveryObserver: null,
            async (_, _) =>
            {
                deliveryStarted.TrySetResult(true);
                await neverCompletes.Task;
            },
            shutdownDrainTimeout: TimeSpan.FromMilliseconds(150),
            forcedShutdownTimeout: TimeSpan.FromMilliseconds(150));

        _ = await notifier.RunToolCallAsync<int>(
            "notification_uncooperative_shutdown",
            arguments: null,
            _ => ValueTask.FromResult(1),
            CancellationToken.None);
        await deliveryStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        await notifier.DisposeAsync();
        stopwatch.Stop();
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                $"Uncooperative desktop progress delivery blocked shutdown: {stopwatch.Elapsed}.");
        }
    }

    private static async Task AssertWriteCancellationAsync()
    {
        var message = new DesktopProgressMessage(
            "write-cancellation",
            "self-test",
            "Write",
            "Cancellation",
            DesktopProgressKind.Running,
            DateTimeOffset.UtcNow,
            ElapsedSeconds: 0,
            Lane: DesktopProgressLane.Worklog,
            Sequence: 1);
        await using var stream = new BlockingWriteStream();
        using var cts =
            new CancellationTokenSource(
                TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await DesktopProgressProtocol.WriteFrameAsync(
                stream,
                message,
                cts.Token);
            throw new InvalidOperationException(
                "Desktop progress framed write ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
        stopwatch.Stop();
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
        {
            throw new InvalidOperationException(
                $"Desktop progress framed write cancellation exceeded its test budget: {stopwatch.Elapsed}.");
        }
    }

    private static async Task AssertForeignPipeServerIsRejectedAsync()
    {
        var fakePipeId = 1_500_000_000 +
            Random.Shared.Next(1, 100_000_000);
        var pipeName = DesktopProgressProtocol.GetPipeName(fakePipeId);
        await using var fakeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var acceptTask = fakeServer.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await acceptTask;

        var message = new DesktopProgressMessage(
            "foreign-pipe-test",
            "self-test",
            "Pipe",
            "Untrusted server",
            DesktopProgressKind.Info,
            DateTimeOffset.UtcNow,
            ElapsedSeconds: 0,
            Lane: DesktopProgressLane.Alert,
            Sequence: 1);
        var delivered = await TalvoraDesktopProgressNotifier
            .TryWriteToVerifiedTrayPipeAsync(
                client,
                Process.GetCurrentProcess().SessionId,
                message,
                timeout.Token);
        if (delivered)
        {
            throw new InvalidOperationException(
                "A pipe served by a foreign executable received desktop progress.");
        }

        await client.DisposeAsync();
        var buffer = new byte[1];
        if (await fakeServer.ReadAsync(buffer, timeout.Token) != 0)
        {
            throw new InvalidOperationException(
                "The foreign pipe server received notification bytes.");
        }
    }

    private static async Task AssertInstalledTrayPipeIsTrustedWhenRunningAsync()
    {
        var trayProcesses = Process.GetProcessesByName("Talvora.Tray");
        try
        {
            if (trayProcesses.Length == 0)
            {
                // The CI runner does not have a logged-on Talvora tray.
                return;
            }

            // LocalSystem resolves the interactive session; any other identity already runs in it.
            var sessionId = IsRunningAsLocalSystem()
                ? WindowsSessionLauncher.GetDefaultInteractiveUser().SessionId
                : Process.GetCurrentProcess().SessionId;
            if (!trayProcesses.Any(process =>
                    process.SessionId == sessionId))
            {
                return;
            }

            await using var client = new NamedPipeClientStream(
                ".",
                DesktopProgressProtocol.GetPipeName(sessionId),
                PipeDirection.Out,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeout.Token);
            if (!TalvoraDesktopProgressNotifier.IsTrustedTrayPipeServer(
                    client.SafePipeHandle,
                    sessionId))
            {
                throw new InvalidOperationException(
                    "The installed Talvora tray pipe was not trusted.");
            }
        }
        finally
        {
            foreach (var process in trayProcesses)
            {
                process.Dispose();
            }
        }
    }

    private static bool IsRunningAsLocalSystem()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid) == true;
    }

    private static async Task AssertPipeReadTimeoutRecoveryAsync()
    {
        var sessionId = 1_500_000_000 +
            Random.Shared.Next(1, 100_000_000);
        var received =
            new TaskCompletionSource<DesktopProgressMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new DesktopProgressPipeListener(
            sessionId,
            message => received.TrySetResult(message),
            readTimeout: TimeSpan.FromMilliseconds(250));
        listener.Start();

        await using (var stalled = new NamedPipeClientStream(
            ".",
            DesktopProgressProtocol.GetPipeName(sessionId),
            PipeDirection.Out,
            PipeOptions.Asynchronous))
        {
            using var connect =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(2));
            await stalled.ConnectAsync(connect.Token);
            await Task.Delay(500);
        }

        var expected = new DesktopProgressMessage(
            "pipe-recovery",
            "self-test",
            "Pipe",
            "Recovered",
            DesktopProgressKind.Info,
            DateTimeOffset.UtcNow,
            ElapsedSeconds: 0,
            Lane: DesktopProgressLane.Alert,
            Sequence: 1);

        await using (var client = new NamedPipeClientStream(
            ".",
            DesktopProgressProtocol.GetPipeName(sessionId),
            PipeDirection.Out,
            PipeOptions.Asynchronous))
        {
            using var connect =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(2));
            await client.ConnectAsync(connect.Token);
            await DesktopProgressProtocol.WriteFrameAsync(
                client,
                expected,
                connect.Token);
        }

        var actual = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        if (actual.OperationId != expected.OperationId)
        {
            throw new InvalidOperationException(
                "Desktop progress listener did not recover after a stalled IPC client.");
        }
    }

    private static async Task AssertOffScreenPinnedPlacementIsNormalizedAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"Talvora.Notification.Placement.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var placementPath = Path.Combine(
            root,
            "desktop-progress-placement.json");

        try
        {
            await File.WriteAllTextAsync(
                placementPath,
                JsonSerializer.Serialize(
                    new
                    {
                        version = 2,
                        isPinned = true,
                        leftPixels = 2_000_000_000,
                        topPixels = 2_000_000_000,
                        hasUserSize = true,
                        widthDip = 598.0,
                        heightDip = 237.33333333333331,
                    }));

            Exception? threadFailure = null;
            var completed = NewSignal();
            var thread = new Thread(
                () =>
                {
                    ControlCenterApplication? application = null;
                    try
                    {
                        application =
                            new ControlCenterApplication(
                                smokeTest: true);
                        var window = new DesktopProgressNotificationWindow();
                        try
                        {
                            window.ApplyPreferredSize(560, 700);
                            var code = "+++ b/big.cs\n@@ -1,1 +1,1000 @@\n" +
                                string.Join('\n', Enumerable.Range(1, 1000).Select(index => $"+changed{index}();"));
                            window.Update(WorklogTestFrames.Running("resize-regression", code));
                            window.Show();
                            window.UpdateLayout();
                            if (Math.Abs(window.ActualWidth - 560) > 0.75)
                            {
                                throw new InvalidOperationException("The card must keep the user's width.");
                            }

                            var heightWithCode = window.ActualHeight;
                            window.Update(WorklogTestFrames.Running("resize-regression", codePreview: null, sequence: 2));
                            window.UpdateLayout();
                            if (window.ActualHeight >= heightWithCode || Math.Abs(window.ActualWidth - 560) > 0.75)
                            {
                                throw new InvalidOperationException("A frame without code must shrink the card and keep its width.");
                            }

                            window.Update(WorklogTestFrames.Running("resize-regression", code, sequence: 3));
                            window.UpdateLayout();
                            if (Math.Abs(window.ActualHeight - heightWithCode) > 1)
                            {
                                throw new InvalidOperationException("Returning code must re-open the terminal to the same height.");
                            }
                        }
                        finally
                        {
                            window.Close();
                        }

                        using var presenter =
                            new DesktopProgressNotificationPresenter(
                                application,
                                placementPath);

                        var n2Frame = new DesktopWorklogFrame(
                            "n2-session-a", 20, DateTimeOffset.UtcNow,
                            new WorklogSessionHeader("Project A", "ChatGPT · Dev"),
                            new WorklogStep(WorklogStepCategory.SourceEdit,
                                WorklogStepState.Running, "Kod güncelleniyor", null, null, 1),
                            [],
                            new WorklogTotals(0, 1, 0,
                                WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                            null, 15);
                        var n2Message = new DesktopProgressMessage(
                            "n2-call-a", "apply_patch", "Kod güncelleniyor", "Devam ediyor.",
                            DesktopProgressKind.Started, DateTimeOffset.UtcNow, 1,
                            Sequence: 20, WorklogFrame: n2Frame);
                        presenter.Publish(n2Message);
                        var sharedWindow = application.Windows
                            .OfType<DesktopProgressNotificationWindow>()
                            .Single(item => item.SessionId == "n2-session-a");
                        var nextFrame = n2Frame with
                        {
                            Sequence = 21,
                            Timeline =
                            [
                                new WorklogTimelineEntry(DateTimeOffset.UtcNow,
                                    WorklogStepState.Succeeded, "Kod güncellendi"),
                            ],
                        };
                        presenter.Publish(n2Message with
                        {
                            OperationId = "n2-call-b",
                            Kind = DesktopProgressKind.Running,
                            Sequence = 21,
                            WorklogFrame = nextFrame,
                        });
                        var secondWindow = application.Windows
                            .OfType<DesktopProgressNotificationWindow>()
                            .Single(item => item.SessionId == "n2-session-a");
                        if (!ReferenceEquals(sharedWindow, secondWindow) ||
                            nextFrame.Timeline.Count <= n2Frame.Timeline.Count)
                        {
                            throw new InvalidOperationException(
                                "N2 sequential calls created a second window or lost timeline history.");
                        }

                        var claudeFrame = nextFrame with
                        {
                            Sequence = 22,
                            Header = new WorklogSessionHeader("Project A", "Claude"),
                            ExpectedNextUpdateSeconds = 60,
                        };
                        presenter.Publish(n2Message with
                        {
                            Kind = DesktopProgressKind.Running,
                            Sequence = 22,
                            WorklogFrame = claudeFrame,
                        });
                        var lastUpdateField = typeof(DesktopProgressNotificationWindow)
                            .GetField("lastFrameReceivedUtc", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        lastUpdateField.SetValue(sharedWindow,
                            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(25));
                        typeof(DesktopProgressNotificationWindow)
                            .GetMethod("CheckForStaleWork", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(sharedWindow, null);
                        var staleField = typeof(DesktopProgressNotificationWindow)
                            .GetField("isStale", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        if ((bool)staleField.GetValue(sharedWindow)!)
                        {
                            throw new InvalidOperationException(
                                "N2 Claude 25-second heartbeat falsely triggered stale display.");
                        }

                        var completedFrame = nextFrame with
                        {
                            Sequence = 23,
                            Current = nextFrame.Current with
                            {
                                State = WorklogStepState.Succeeded,
                                Title = "Kod güncellendi",
                            },
                        };
                        presenter.Publish(n2Message with
                        {
                            Kind = DesktopProgressKind.Completed,
                            Sequence = 23,
                            WorklogFrame = completedFrame,
                            SessionClosed = true,
                        });
                        var dismissField = typeof(DesktopProgressNotificationWindow)
                            .GetField("dismissAfter", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        if ((TimeSpan)dismissField.GetValue(sharedWindow)! != TimeSpan.FromMinutes(3) ||
                            !sharedWindow.IsTerminal)
                        {
                            throw new InvalidOperationException(
                                "N2 successful session did not retain the three-minute close lifetime.");
                        }
                        var projectB = n2Frame with
                        {
                            SessionId = "n2-session-b",
                            Sequence = 24,
                            Header = new WorklogSessionHeader("Project B", "ChatGPT · Dev"),
                        };
                        presenter.Publish(n2Message with
                        {
                            OperationId = "n2-call-c",
                            Sequence = 24,
                            WorklogFrame = projectB,
                        });
                        if (application.Windows
                            .OfType<DesktopProgressNotificationWindow>()
                            .Any(item => item.SessionId == "n2-session-b" &&
                                ReferenceEquals(item, sharedWindow)))
                        {
                            throw new InvalidOperationException(
                                "N2 different projects shared the same window instance.");
                        }
                    }
                    catch (Exception ex)
                    {
                        threadFailure = ex;
                    }
                    finally
                    {
                        application?.Shutdown();
                        completed.TrySetResult(true);
                    }
                });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            try
            {
                await completed.Task.WaitAsync(
                    TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Desktop placement regression exceeded the hosted-runner STA budget. ThreadState={thread.ThreadState}.",
                    ex);
            }
            if (!thread.Join(TimeSpan.FromSeconds(2)))
            {
                throw new InvalidOperationException(
                    "Desktop placement regression STA thread did not exit.");
            }

            if (threadFailure is not null)
            {
                throw new InvalidOperationException(
                    "Desktop placement regression failed while restoring placement.",
                    threadFailure);
            }

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(placementPath));
            var restored = document.RootElement;
            if (restored
                .GetProperty("isPinned")
                .GetBoolean())
            {
                throw new InvalidOperationException(
                    "An off-screen pinned desktop placement was not persisted back as automatic placement.");
            }

            if (restored
                    .GetProperty("leftPixels")
                    .GetInt32() != 2_000_000_000 ||
                restored
                    .GetProperty("topPixels")
                    .GetInt32() != 2_000_000_000 ||
                Math.Abs(
                    restored
                        .GetProperty("widthDip")
                        .GetDouble() -
                    598.0) > 0.001 ||
                Math.Abs(
                    restored
                        .GetProperty("heightDip")
                        .GetDouble() -
                    237.33333333333331) > 0.001)
            {
                throw new InvalidOperationException(
                    "Normalizing an off-screen pin changed the persisted size or anchor evidence.");
            }
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    private static TalvoraDesktopProgressNotifier CreateNotifier(
        ConcurrentQueue<DesktopProgressMessage> observed) =>
        new(
            NullLogger<TalvoraDesktopProgressNotifier>.Instance,
            observed.Enqueue,
            static (_, _) => Task.CompletedTask,
            shutdownDrainTimeout: TimeSpan.FromSeconds(1));

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsTerminal(
        DesktopProgressKind kind) =>
        kind is
            DesktopProgressKind.Completed or
            DesktopProgressKind.Failed or
            DesktopProgressKind.Cancelled or
            DesktopProgressKind.Info or
            DesktopProgressKind.Warning;

    private sealed class BlockingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new(Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken));

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}

internal static class WorklogTestFrames
{
    internal static DesktopWorklogFrame Running(string sessionId, string? codePreview, long sequence = 1) =>
        new(
            SessionId: sessionId,
            Sequence: sequence,
            TimestampUtc: DateTimeOffset.UtcNow,
            Header: new WorklogSessionHeader("Talvora-MCP", "Codex"),
            Current: new WorklogStep(
                WorklogStepCategory.SourceEdit,
                WorklogStepState.Running,
                "Kod değişikliği uygulanıyor",
                "1 dosya · +1000 −0 · big.cs",
                null,
                1),
            Timeline: [],
            Totals: new WorklogTotals(1, 1, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
            Evidence: codePreview is null ? null : new DesktopProgressEvidence(CodePreview: codePreview),
            ExpectedNextUpdateSeconds: 15);
}
