using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Talvora;
using Talvora.Shared;
using Talvora.Tray;

internal static class Program
{
    public static async Task<int> Main()
    {
        await DesktopProgressProtocol.AssertContractAsync();
        DesktopProgressPresentationState.AssertContract();
        await AssertWorkVisibilityFilteringAsync();
        await AssertDiffEvidenceCoverageAsync();
        await AssertSemanticMessagingAndCanonicalToolAsync();
        await AssertConcurrentCancellationTruthAsync();
        await AssertConcurrentFailurePrecedenceAsync();
        await AssertQueuePressureAndDrainAsync();
        await AssertCatastrophicTerminalOverloadIsObservableAsync();
        await AssertBoundedShutdownAsync();
        await AssertUncooperativeDeliveryCannotBlockShutdownAsync();
        await AssertWriteCancellationAsync();
        await AssertPipeReadTimeoutRecoveryAsync();
        await AssertOffScreenPinnedPlacementIsNormalizedAsync();

        Console.WriteLine(
            "DESKTOP_PROGRESS_BEHAVIOR_GREEN");
        return 0;
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
            if (observed.IsEmpty)
            {
                throw new InvalidOperationException(
                    "Meaningful source inspection was incorrectly suppressed from the desktop worklog.");
            }

            if (observed.Any(message =>
                    !string.Equals(
                        message.ToolName,
                        "read_source",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Source inspection did not preserve canonical tool identity.");
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

            _ = await notifier.RunToolCallAsync<int>(
                "talvora_apply_edits",
                arguments,
                _ => ValueTask.FromResult(1),
                CancellationToken.None);

            var evidence = observed
                .Select(message => message.Evidence)
                .FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate?.CodePreview));
            var preview = evidence?.CodePreview ?? string.Empty;
            if (!preview.Contains(
                    "@@ FILE src/Test.cs",
                    StringComparison.Ordinal) ||
                !preview.Contains(
                    "-old-1000();",
                    StringComparison.Ordinal) ||
                !preview.Contains(
                    "+new-1000();",
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
                    message.Message.Contains(
                        "durduruldu",
                        StringComparison.OrdinalIgnoreCase)))
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
                string.Equals(
                    nextTerminal.OperationId,
                    cancelledGeneration,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A completed next worklog generation did not receive a fresh identity.");
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
                            window.ApplyPreferredSize(420, 240);
                            var code = string.Join('\n', Enumerable.Range(1, 1000).Select(index => $"+changed-{index}();"));
                            var message = new DesktopProgressMessage(
                                "resize-regression", "apply_patch", "Kod gÃ¼ncelleniyor", "GerÃ§ek kod deÄŸiÅŸiklikleri",
                                DesktopProgressKind.Running, DateTimeOffset.UtcNow, 1,
                                new DesktopProgressEvidence(AddedLines: 1000, CodePreview: code), Sequence: 1);
                            window.Update(message);
                            window.Show();
                            window.UpdateLayout();
                            if (window.ActualWidth < 959 || window.ActualHeight < 719 ||
                                window.SizeToContent != System.Windows.SizeToContent.Manual)
                            {
                                throw new InvalidOperationException("Saved compact size blocked automatic two-axis diff expansion.");
                            }
                            var width = window.ActualWidth;
                            var height = window.ActualHeight;
                            window.Update(message with { Kind = DesktopProgressKind.Completed, Sequence = 2 });
                            window.UpdateLayout();
                            if (Math.Abs(window.ActualWidth - width) > 0.5 || Math.Abs(window.ActualHeight - height) > 0.5)
                            {
                                throw new InvalidOperationException("Terminal update shrank the expanded diff window.");
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
                        presenter.Publish(new DesktopProgressMessage(
                            "compact-regression", "read_source", "Kaynaklar inceleniyor", "Ä°nceleme tamamlandÄ±.",
                            DesktopProgressKind.Completed, DateTimeOffset.UtcNow, 1, Sequence: 1));
                        var compactWindow = application.Windows.OfType<DesktopProgressNotificationWindow>().Single();
                        compactWindow.UpdateLayout();
                        if (compactWindow.ActualWidth > 501 || compactWindow.ActualHeight > 300)
                        {
                            throw new InvalidOperationException("A notification without code inherited oversized code geometry.");
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
