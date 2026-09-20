using System.Text;
using System.Text.Json.Nodes;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task CrashRecoveryPreservesUnappliedExternalAddAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("first.txt", "first0\n");
        await fixture.WriteUtf8Async("third.txt", "third0\n");
        var first = fixture.PathInWorkspace("first.txt");
        var second = fixture.PathInWorkspace("second.txt");
        var third = fixture.PathInWorkspace("third.txt");

        var injector = new DelegateFaultInjector(
            after: (index, _, _) =>
            {
                if (index == 0)
                {
                    throw new SourceEditSimulatedCrashException(
                        "Simulated crash after the first filesystem commit.");
                }

                return ValueTask.CompletedTask;
            });
        var crashingEngine = fixture.CreateEngine(injector);
        var firstRevision = await RevisionAsync(crashingEngine, first);
        var thirdRevision = await RevisionAsync(crashingEngine, third);

        var crashed = false;
        try
        {
            _ = await crashingEngine.ApplyEditsAsync(
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
                    new SourceEditChangeInput
                    {
                        Operation = "add",
                        Path = "second.txt",
                        Content = "second1\n",
                        Encoding = "utf-8",
                        Newline = "lf",
                    },
                    Update(
                        "third.txt",
                        thirdRevision,
                        0,
                        0,
                        0,
                        6,
                        "third1",
                        "third0"),
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
            "first1\n",
            await File.ReadAllTextAsync(first),
            "First step was not committed before the simulated crash.");
        Assert(!File.Exists(second), "Second add unexpectedly ran before the crash.");

        await File.WriteAllTextAsync(
            second,
            "second1\n",
            new UTF8Encoding(false));

        var recoveryEngine = fixture.CreateEngine();
        await recoveryEngine.RecoverAllPendingAsync(
            CancellationToken.None);

        AssertEqual(
            "first0\n",
            await File.ReadAllTextAsync(first),
            "Recovery did not restore the proven-applied first step.");
        AssertEqual(
            "second1\n",
            await File.ReadAllTextAsync(second),
            "Recovery removed an externally created file for an unapplied add step.");
        AssertEqual(
            "third0\n",
            await File.ReadAllTextAsync(third),
            "Recovery changed an unattempted third step.");
    }

    private static async Task CompleteInvalidWalTailFailsClosedAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("wal.txt", "before\n");
        var path = fixture.PathInWorkspace("wal.txt");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var transactionId = NewTransactionId();
        var patch = PatchUpdate(
            "wal.txt",
            revision,
            "-before",
            "+after");

        var committed = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            patch,
            true,
            CancellationToken.None);
        Assert(committed.Success, committed.Error?.Message ?? "Commit failed.");

        var store = new SourceEditTransactionStore(fixture.StateRoot);
        var journalPath = store.GetJournalPath(transactionId);
        var lines = await File.ReadAllLinesAsync(journalPath);
        var envelope =
            JsonNode.Parse(lines[^1])?.AsObject()
            ?? throw new InvalidOperationException("WAL envelope could not be parsed.");
        envelope["checksum"] =
            "sha256:" + new string('0', 64);
        lines[^1] = envelope.ToJsonString();
        await File.WriteAllTextAsync(
            journalPath,
            string.Join(Environment.NewLine, lines) +
            Environment.NewLine,
            new UTF8Encoding(false));

        var failedClosed = false;
        try
        {
            _ = await store.ReadStateAsync(
                transactionId,
                CancellationToken.None);
        }
        catch (SourceEditDomainException ex)
            when (ex.Code ==
                  SourceEditCodes.TransactionRecoveryRequired)
        {
            failedClosed = true;
        }

        Assert(
            failedClosed,
            "A complete integrity-invalid final WAL record was treated as an incomplete tail.");
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(path),
            "WAL integrity validation unexpectedly changed live source content.");
    }


    private static async Task CorruptWalIsolatedToRelatedWorkspaceAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("isolated.txt", "before\n");
        var firstPath = fixture.PathInWorkspace("isolated.txt");
        var engine = fixture.CreateEngine();
        var firstRevision = await RevisionAsync(engine, firstPath);
        var firstTransactionId = NewTransactionId();

        var firstCommit = await engine.ApplyPatchAsync(
            fixture.Root,
            firstTransactionId,
            PatchUpdate(
                "isolated.txt",
                firstRevision,
                "-before",
                "+after"),
            true,
            CancellationToken.None);
        Assert(firstCommit.Success, firstCommit.Error?.Message ?? "First workspace commit failed.");

        var store = new SourceEditTransactionStore(fixture.StateRoot);
        await File.AppendAllTextAsync(
            store.GetJournalPath(firstTransactionId),
            "{\"payload\":\"not-valid-base64\",\"checksum\":\"sha256:" +
            new string('0', 64) +
            "\"}" +
            Environment.NewLine,
            new UTF8Encoding(false));

        var secondRoot = Path.Combine(
            fixture.OrdinaryRoot,
            "independent-workspace");
        Directory.CreateDirectory(secondRoot);
        Directory.CreateDirectory(Path.Combine(secondRoot, ".git"));
        var secondPath = Path.Combine(secondRoot, "healthy.txt");
        await File.WriteAllTextAsync(
            secondPath,
            "healthy-before\n",
            new UTF8Encoding(false));

        var secondRevision = await RevisionAsync(engine, secondPath);
        var secondCommit = await engine.ApplyPatchAsync(
            secondRoot,
            NewTransactionId(),
            PatchUpdate(
                "healthy.txt",
                secondRevision,
                "-healthy-before",
                "+healthy-after"),
            true,
            CancellationToken.None);
        Assert(
            secondCommit.Success,
            secondCommit.Error?.Message ??
            "A quarantined journal in another workspace blocked a healthy workspace.");
        AssertEqual(
            "healthy-after\n",
            await File.ReadAllTextAsync(secondPath),
            "Healthy workspace mutation was not committed.");

        var relatedRevision = await RevisionAsync(engine, firstPath);
        var relatedAttempt = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            PatchUpdate(
                "isolated.txt",
                relatedRevision,
                "-after",
                "+again"),
            true,
            CancellationToken.None);
        AssertError(
            relatedAttempt,
            SourceEditCodes.TransactionRecoveryRequired);
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(firstPath),
            "Related workspace was mutated despite its quarantined journal.");

        await engine.RecoverAllPendingAsync(
            CancellationToken.None);
        Assert(
            engine.LastRecoveryQuarantines.Any(
                item =>
                    string.Equals(
                        item.TransactionId,
                        firstTransactionId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        item.WorkspaceRoot,
                        fixture.Root,
                        StringComparison.OrdinalIgnoreCase)),
            "Recovery sweep did not retain a workspace-identifiable quarantine descriptor.");
        Assert(
            File.Exists(
                Path.Combine(
                    store.GetTransactionDirectory(firstTransactionId),
                    "quarantine.json")),
            "Quarantined journal did not persist its diagnostic descriptor.");
    }

    private static async Task UnterminatedWalTailIsIgnoredAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("tail.txt", "before\n");
        var path = fixture.PathInWorkspace("tail.txt");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var transactionId = NewTransactionId();

        var committed = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            PatchUpdate(
                "tail.txt",
                revision,
                "-before",
                "+after"),
            true,
            CancellationToken.None);
        Assert(committed.Success, committed.Error?.Message ?? "Commit failed.");

        var store = new SourceEditTransactionStore(fixture.StateRoot);
        await File.AppendAllTextAsync(
            store.GetJournalPath(transactionId),
            "{\"payload\":\"partial\"",
            new UTF8Encoding(false));

        var state = await store.ReadStateAsync(
            transactionId,
            CancellationToken.None);
        if (state is null)
        {
            throw new InvalidOperationException(
                "Journal state disappeared.");
        }
        AssertEqual(
            "Committed",
            state.LastEvent,
            "An unterminated final WAL fragment replaced the last complete state.");
    }

    private static async Task ExpiredTransactionIdFailsClosedAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("expiry.txt", "before\n");
        var path = fixture.PathInWorkspace("expiry.txt");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var transactionId = NewTransactionId();
        var patch = PatchUpdate(
            "expiry.txt",
            revision,
            "-before",
            "+after");

        var committed = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            patch,
            true,
            CancellationToken.None);
        Assert(committed.Success, committed.Error?.Message ?? "Commit failed.");

        var store = new SourceEditTransactionStore(fixture.StateRoot);
        var receiptPath = store.GetReceiptPath(transactionId);
        File.SetLastWriteTimeUtc(
            receiptPath,
            DateTime.UtcNow.AddDays(-31));
        await store.CleanupCompletedAsync(
            CancellationToken.None);

        Assert(!File.Exists(receiptPath), "Aged receipt was not retired.");
        Assert(
            !Directory.Exists(store.GetTransactionDirectory(transactionId)),
            "Aged transaction journal was not retired.");
        Assert(
            File.Exists(store.GetTombstonePath(transactionId)),
            "Retired transaction identity tombstone was not persisted.");

        await File.WriteAllTextAsync(
            path,
            "before\n",
            new UTF8Encoding(false));

        var retry = await engine.ApplyPatchAsync(
            fixture.Root,
            transactionId,
            patch,
            true,
            CancellationToken.None);

        AssertError(
            retry,
            SourceEditCodes.TransactionIdExpired);
        AssertEqual(
            "before\n",
            await File.ReadAllTextAsync(path),
            "An expired transactionId was allowed to execute again.");
    }
}
