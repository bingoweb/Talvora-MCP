using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task ConcurrentWriterAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("race.txt", "before\n");
        var path = fixture.PathInWorkspace("race.txt");

        var injector = new DelegateFaultInjector(
            before: async (index, file, cancellationToken) =>
            {
                if (index == 0)
                {
                    await File.WriteAllTextAsync(
                        file.Change.FullPath,
                        "external\n",
                        cancellationToken);
                }
            });
        var engine = fixture.CreateEngine(injector);
        var revision = await RevisionAsync(engine, path);

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "race.txt",
                    revision,
                    0,
                    0,
                    0,
                    6,
                    "after",
                    "before"),
            ],
            true,
            CancellationToken.None);

        AssertEqual(
            "rolled-back",
            result.Status,
            "Concurrent writer should terminate as rolled-back transaction.");
        AssertError(
            result,
            SourceEditCodes.Conflict);
        AssertEqual(
            "external\n",
            await File.ReadAllTextAsync(path),
            "Rollback overwrote concurrent external content.");
    }

    private static async Task InjectedCommitFailureAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("first.txt", "first0\n");
        await fixture.WriteUtf8Async("second.txt", "second0\n");
        var first = fixture.PathInWorkspace("first.txt");
        var second = fixture.PathInWorkspace("second.txt");

        var injector = new DelegateFaultInjector(
            after: (index, _, _) =>
            {
                if (index == 0)
                {
                    throw new IOException(
                        "Injected failure after first commit step.");
                }

                return ValueTask.CompletedTask;
            });
        var engine = fixture.CreateEngine(injector);
        var firstRevision = await RevisionAsync(engine, first);
        var secondRevision = await RevisionAsync(engine, second);

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "first.txt",
                    firstRevision,
                    0,
                    0,
                    0,
                    6,
                    "first1",
                    "first0"),
                Update(
                    "second.txt",
                    secondRevision,
                    0,
                    0,
                    0,
                    7,
                    "second1",
                    "second0"),
            ],
            true,
            CancellationToken.None);

        AssertEqual(
            "rolled-back",
            result.Status,
            "Injected commit failure did not roll back.");
        AssertEqual(
            "first0\n",
            await File.ReadAllTextAsync(first),
            "First file was not rolled back.");
        AssertEqual(
            "second0\n",
            await File.ReadAllTextAsync(second),
            "Second file changed despite earlier commit failure.");
    }

    private static async Task CrashRecoveryRollbackAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("one.txt", "one0\n");
        await fixture.WriteUtf8Async("two.txt", "two0\n");
        var one = fixture.PathInWorkspace("one.txt");
        var two = fixture.PathInWorkspace("two.txt");

        var injector = new DelegateFaultInjector(
            after: (index, _, _) =>
            {
                if (index == 0)
                {
                    throw new SourceEditSimulatedCrashException(
                        "Simulated crash after first committed file.");
                }

                return ValueTask.CompletedTask;
            });
        var crashingEngine = fixture.CreateEngine(injector);
        var oneRevision = await RevisionAsync(crashingEngine, one);
        var twoRevision = await RevisionAsync(crashingEngine, two);
        var transactionId = NewTransactionId();

        var crashed = false;
        try
        {
            _ = await crashingEngine.ApplyEditsAsync(
                fixture.Root,
                transactionId,
                [
                    Update(
                        "one.txt",
                        oneRevision,
                        0,
                        0,
                        0,
                        4,
                        "one1",
                        "one0"),
                    Update(
                        "two.txt",
                        twoRevision,
                        0,
                        0,
                        0,
                        4,
                        "two1",
                        "two0"),
                ],
                true,
                CancellationToken.None);
        }
        catch (SourceEditSimulatedCrashException)
        {
            crashed = true;
        }

        Assert(crashed, "Failure injector did not simulate a crash.");
        AssertEqual(
            "one1\n",
            await File.ReadAllTextAsync(one),
            "First file should be in after-state before recovery.");
        AssertEqual(
            "two0\n",
            await File.ReadAllTextAsync(two),
            "Second file should remain in before-state before recovery.");

        var recoveryEngine = fixture.CreateEngine();
        await recoveryEngine.RecoverAllPendingAsync(
            CancellationToken.None);

        AssertEqual(
            "one0\n",
            await File.ReadAllTextAsync(one),
            "Crash recovery did not restore first file.");
        AssertEqual(
            "two0\n",
            await File.ReadAllTextAsync(two),
            "Crash recovery changed untouched second file.");
    }

    private static async Task LostResponseRetryAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("retry.txt", "before\n");
        var path = fixture.PathInWorkspace("retry.txt");
        var injector = new DelegateFaultInjector(
            after: (index, _, _) =>
            {
                if (index == 0)
                {
                    throw new SourceEditSimulatedCrashException(
                        "Simulated lost response after filesystem commit.");
                }

                return ValueTask.CompletedTask;
            });
        var crashingEngine = fixture.CreateEngine(injector);
        var revision = await RevisionAsync(crashingEngine, path);
        var transactionId = NewTransactionId();
        var patch = PatchUpdate(
            "retry.txt",
            revision,
            "-before",
            "+after");

        try
        {
            _ = await crashingEngine.ApplyPatchAsync(
                fixture.Root,
                transactionId,
                patch,
                true,
                CancellationToken.None);
            throw new InvalidOperationException(
                "Expected simulated crash was not raised.");
        }
        catch (SourceEditSimulatedCrashException)
        {
        }

        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(path),
            "Filesystem commit did not occur before simulated lost response.");

        var retryEngine = fixture.CreateEngine();
        var retry = await retryEngine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            patch,
            true,
            CancellationToken.None);

        Assert(retry.Success, retry.Error?.Message ?? "Retry failed.");
        Assert(retry.Replayed, "Retry did not return a replayed receipt.");
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(path),
            "Retry applied the mutation a second time or changed content.");
    }

    private static async Task TransactionIdReuseMismatchAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("reuse.txt", "before\n");
        var path = fixture.PathInWorkspace("reuse.txt");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var transactionId = NewTransactionId();

        var firstPatch = PatchUpdate(
            "reuse.txt",
            revision,
            "-before",
            "+after");
        var first = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            firstPatch,
            true,
            CancellationToken.None);
        Assert(first.Success, first.Error?.Message ?? "Initial commit failed.");

        var secondPatch =
            firstPatch.Replace(
                "+after",
                "+different",
                StringComparison.Ordinal);
        var second = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            secondPatch,
            true,
            CancellationToken.None);

        AssertError(
            second,
            SourceEditCodes.TransactionIdReuseMismatch);
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(path),
            "transactionId reuse modified committed content.");
    }

    private static async Task AddDeleteMoveAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("delete-me.txt", "delete\n");
        await fixture.WriteUtf8Async("move-me.txt", "move\n");
        var engine = fixture.CreateEngine();
        var deleteRevision =
            await RevisionAsync(
                engine,
                fixture.PathInWorkspace("delete-me.txt"));
        var moveRevision =
            await RevisionAsync(
                engine,
                fixture.PathInWorkspace("move-me.txt"));

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                new SourceEditChangeInput
                {
                    Operation = "add",
                    Path = "nested/new-file.txt",
                    Content = "new\n",
                    Encoding = "utf-8",
                    Newline = "lf",
                },
                new SourceEditChangeInput
                {
                    Operation = "delete",
                    Path = "delete-me.txt",
                    ExpectedRevision = deleteRevision,
                },
                new SourceEditChangeInput
                {
                    Operation = "move",
                    Path = "move-me.txt",
                    DestinationPath = "moved/move-me.txt",
                    ExpectedRevision = moveRevision,
                },
            ],
            true,
            CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Add/delete/move failed.");
        AssertEqual(
            "new\n",
            await File.ReadAllTextAsync(
                fixture.PathInWorkspace("nested/new-file.txt")),
            "Added file mismatch.");
        Assert(
            !File.Exists(
                fixture.PathInWorkspace("delete-me.txt")),
            "Deleted file still exists.");
        Assert(
            !File.Exists(
                fixture.PathInWorkspace("move-me.txt")),
            "Move source still exists.");
        AssertEqual(
            "move\n",
            await File.ReadAllTextAsync(
                fixture.PathInWorkspace("moved/move-me.txt")),
            "Move destination content mismatch.");
    }

    private static async Task CancellationBeforeCommitAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("cancel.txt", "before\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("cancel.txt");
        var revision = await RevisionAsync(engine, path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelled = false;
        try
        {
            _ = await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    Update(
                        "cancel.txt",
                        revision,
                        0,
                        0,
                        0,
                        6,
                        "after",
                        "before"),
                ],
                true,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert(cancelled, "Cancelled source edit did not observe cancellation.");
        AssertEqual(
            "before\n",
            await File.ReadAllTextAsync(path),
            "Cancellation before commit modified the file.");
    }
}
