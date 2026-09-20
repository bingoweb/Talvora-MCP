namespace Talvora.SourceEditing;

internal sealed class SourceEditEngineOptions
{
    public string? StateRoot { get; init; }
}

internal sealed class SourceEditEngine
{
    private readonly SourceEditTransactionStore store;
    private readonly SourceEditCommitter committer;
    private readonly ISourceEditFaultInjector faultInjector;
    private readonly SourceEditAsyncKeyedLock workspaceLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SourceEditAsyncKeyedLock transactionLocks =
        new(StringComparer.Ordinal);

    public IReadOnlyList<SourceEditJournalQuarantine> LastRecoveryQuarantines
    {
        get;
        private set;
    } = [];

    public SourceEditEngine(
        SourceEditEngineOptions? options = null,
        ISourceEditFaultInjector? faultInjector = null)
    {
        store = new SourceEditTransactionStore(
            options?.StateRoot);
        committer = new SourceEditCommitter();
        this.faultInjector =
            faultInjector ??
            NullSourceEditFaultInjector.Instance;
    }

    public async Task<TalvoraSourceReadResponse> ReadSourceAsync(
        string path,
        int startLine,
        int lineCount,
        CancellationToken cancellationToken)
    {
        return await ReadSourceAsync(
            path,
            startLine,
            startCharacter: 0,
            lineCount,
            SourceTextStreamingReader.DefaultReadMaxCharacters,
            cancellationToken);
    }

    public async Task<TalvoraSourceReadResponse> ReadSourceAsync(
        string path,
        int startLine,
        int startCharacter,
        int lineCount,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }

        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        if (startCharacter < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startCharacter));
        }

        var fullPath = Path.GetFullPath(path);
        var workspaceRoot =
            SourceWorkspaceClassifier.FindWorkspaceRootForPath(
                fullPath)
            ?? throw new SourceEditDomainException(
                SourceEditCodes.WorkspaceNotFound,
                "talvora_read_source requires a file inside a recognized development workspace.",
                fullPath);

        SourceWorkspaceClassifier.EnsureNoReparsePoint(
            workspaceRoot,
            fullPath);

        var slice =
            await SourceTextStreamingReader.ReadSliceAsync(
                fullPath,
                startLine,
                startCharacter,
                lineCount,
                maxCharacters,
                cancellationToken);
        var classification =
            SourceWorkspaceClassifier.Classify(fullPath);

        return new TalvoraSourceReadResponse(
            fullPath,
            workspaceRoot,
            classification.Classification,
            slice.Length,
            slice.Revision,
            slice.Encoding.Name,
            slice.Newline,
            slice.HasFinalNewline,
            startLine,
            slice.LinesRead,
            slice.EndReached,
            slice.Text,
            startCharacter,
            slice.NextStartLine,
            slice.NextStartCharacter,
            slice.ResponseLimited,
            slice.ResponseLimitCharacters);
    }

    public Task<SourceEditTransactionResult> ApplyPatchAsync(
        string workspaceRoot,
        string transactionId,
        string patch,
        bool validateSyntax,
        CancellationToken cancellationToken) =>
        ApplyPatchAsync(
            workspaceRoot,
            transactionId,
            patch,
            "talvora",
            expectedRevisions: null,
            validateSyntax,
            cancellationToken);

    public async Task<SourceEditTransactionResult> ApplyPatchAsync(
        string workspaceRoot,
        string transactionId,
        string patch,
        string inputFormat,
        IReadOnlyDictionary<string, string>? expectedRevisions,
        bool validateSyntax,
        CancellationToken cancellationToken)
    {
        string tx;
        string root;
        string format;
        string requestHash;

        try
        {
            tx =
                SourceEditNormalizer.NormalizeTransactionId(
                    transactionId);
            root =
                SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                    workspaceRoot);
            format = NormalizePatchInputFormat(
                inputFormat);
            requestHash =
                format == "unified-diff"
                    ? SourceEditNormalizer.ComputeUnifiedDiffRequestHash(
                        root,
                        patch,
                        expectedRevisions,
                        validateSyntax)
                    : SourceEditNormalizer.ComputePatchRequestHash(
                        root,
                        patch,
                        validateSyntax);
        }
        catch (SourceEditDomainException ex)
        {
            return SourceEditTransactionResult.Rejected(
                transactionId ?? string.Empty,
                null,
                null,
                ex);
        }

        using var workspaceLease =
            await workspaceLocks.AcquireAsync(
                root,
                cancellationToken);

        try
        {
            await RecoverWorkspaceLockedAsync(
                root,
                cancellationToken);
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }

        using var transactionLease =
            await transactionLocks.AcquireAsync(
                tx,
                cancellationToken);

        try
        {
            return await ExecuteLockedAsync(
                tx,
                root,
                requestHash,
                cancellationToken,
                async token =>
                    format == "unified-diff"
                        ? await SourceEditNormalizer.FromUnifiedDiffAsync(
                            root,
                            tx,
                            patch,
                            expectedRevisions,
                            validateSyntax,
                            requestHash,
                            token)
                        : await SourceEditNormalizer.FromPatchAsync(
                            root,
                            tx,
                            patch,
                            validateSyntax,
                            requestHash,
                            token));
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }
    }

    public async Task<SourceEditTransactionResult> ApplyEditsAsync(
        string workspaceRoot,
        string transactionId,
        IReadOnlyList<SourceEditChangeInput> changes,
        bool validateSyntax,
        CancellationToken cancellationToken)
    {
        string tx;
        string root;
        string requestHash;

        try
        {
            tx =
                SourceEditNormalizer.NormalizeTransactionId(
                    transactionId);
            root =
                SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                    workspaceRoot);
            requestHash =
                SourceEditNormalizer.ComputeStructuredRequestHash(
                    root,
                    changes,
                    validateSyntax);
        }
        catch (SourceEditDomainException ex)
        {
            return SourceEditTransactionResult.Rejected(
                transactionId ?? string.Empty,
                null,
                null,
                ex);
        }

        using var workspaceLease =
            await workspaceLocks.AcquireAsync(
                root,
                cancellationToken);

        try
        {
            await RecoverWorkspaceLockedAsync(
                root,
                cancellationToken);
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }

        using var transactionLease =
            await transactionLocks.AcquireAsync(
                tx,
                cancellationToken);

        try
        {
            return await ExecuteLockedAsync(
                tx,
                root,
                requestHash,
                cancellationToken,
                async token =>
                    await SourceEditNormalizer.FromStructuredAsync(
                        root,
                        tx,
                        changes,
                        validateSyntax,
                        requestHash,
                        token));
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }
    }

    internal async Task<SourceEditTransactionResult> ApplyGeneratedEditsAsync(
        string workspaceRoot,
        string transactionId,
        string requestHash,
        bool validateSyntax,
        Func<string, CancellationToken, Task<IReadOnlyList<SourceEditChangeInput>>> generateChanges,
        CancellationToken cancellationToken,
        Func<string?>? adapterReceiptProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(generateChanges);

        string tx;
        string root;

        try
        {
            tx =
                SourceEditNormalizer.NormalizeTransactionId(
                    transactionId);
            root =
                SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                    workspaceRoot);
        }
        catch (SourceEditDomainException ex)
        {
            return SourceEditTransactionResult.Rejected(
                transactionId ?? string.Empty,
                null,
                null,
                ex);
        }

        using var workspaceLease =
            await workspaceLocks.AcquireAsync(
                root,
                cancellationToken);

        try
        {
            await RecoverWorkspaceLockedAsync(
                root,
                cancellationToken);
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }

        using var transactionLease =
            await transactionLocks.AcquireAsync(
                tx,
                cancellationToken);

        try
        {
            return await ExecuteLockedAsync(
                tx,
                root,
                requestHash,
                cancellationToken,
                async token =>
                {
                    var changes =
                        await generateChanges(
                            root,
                            token);
                    return await SourceEditNormalizer.FromStructuredAsync(
                        root,
                        tx,
                        changes,
                        validateSyntax,
                        requestHash,
                        token);
                },
                adapterReceiptProvider);
        }
        catch (SourceEditDomainException ex)
        {
            return BuildDomainFailure(
                tx,
                root,
                requestHash,
                ex);
        }
    }

    public async Task RecoverAllPendingAsync(
        CancellationToken cancellationToken)
    {
        var enumeration =
            await store.EnumerateStatesAsync(
                cancellationToken);
        LastRecoveryQuarantines =
            enumeration.QuarantinedJournals;

        foreach (var state in enumeration.States)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workspaceLease =
                await workspaceLocks.AcquireAsync(
                    state.Plan.WorkspaceRoot,
                    cancellationToken);
            using var transactionLease =
                await transactionLocks.AcquireAsync(
                    state.Plan.TransactionId,
                    cancellationToken);

            var current =
                await store.ReadStateAsync(
                    state.Plan.TransactionId,
                    cancellationToken);
            if (current is null)
            {
                continue;
            }

            if (string.Equals(
                    current.LastEvent,
                    "RecoveryRequired",
                    StringComparison.Ordinal))
            {
                continue;
            }

            await RecoverStateLockedAsync(
                current,
                cancellationToken);
        }

        await store.CleanupCompletedAsync(
            cancellationToken);
    }

    private async Task<SourceEditTransactionResult> ExecuteLockedAsync(
        string transactionId,
        string workspaceRoot,
        string requestHash,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<NormalizedSourceEditChangeSet>> normalize,
        Func<string?>? adapterReceiptProvider = null)
    {
        var receipt =
            await store.TryReadReceiptAsync(
                transactionId,
                cancellationToken);
        if (receipt is not null)
        {
            EnsureSameIdentity(
                transactionId,
                workspaceRoot,
                requestHash,
                receipt.RequestHash,
                receipt.WorkspaceRoot);
            return receipt with { Replayed = true };
        }

        var existingState =
            await store.ReadStateAsync(
                transactionId,
                cancellationToken);
        if (existingState is not null)
        {
            EnsureSameIdentity(
                transactionId,
                workspaceRoot,
                requestHash,
                existingState.Plan.RequestHash,
                existingState.Plan.WorkspaceRoot);

            var recovered =
                await RecoverStateLockedAsync(
                    existingState,
                    cancellationToken);
            return recovered with { Replayed = true };
        }

        var tombstone =
            await store.TryReadTombstoneAsync(
                transactionId,
                cancellationToken);
        if (tombstone is not null)
        {
            EnsureSameIdentity(
                transactionId,
                workspaceRoot,
                requestHash,
                tombstone.RequestHash,
                tombstone.WorkspaceRoot);
            return SourceEditTransactionResult.Rejected(
                transactionId,
                workspaceRoot,
                requestHash,
                new SourceEditDomainException(
                    SourceEditCodes.TransactionIdExpired,
                    "This transactionId has already reached a terminal state whose detailed receipt was retired. Reusing it is not allowed.",
                    details:
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["terminalStatus"] =
                                tombstone.TerminalStatus,
                        }));
        }

        NormalizedSourceEditChangeSet changeSet;
        IReadOnlyList<SourceEditValidationResult> validation;
        SourceEditPreparedTransaction prepared;
        string? adapterReceiptJson = null;

        try
        {
            changeSet = await normalize(cancellationToken);
            adapterReceiptJson =
                adapterReceiptProvider?.Invoke();
            validation =
                SourceEditSyntaxValidator.Validate(changeSet);
            prepared =
                await committer.PrepareAsync(
                    changeSet,
                    validation,
                    cancellationToken);
        }
        catch (SourceEditDomainException ex)
        {
            adapterReceiptJson ??=
                adapterReceiptProvider?.Invoke();
            var rejected =
                SourceEditTransactionResult.Rejected(
                    transactionId,
                    workspaceRoot,
                    requestHash,
                    ex) with
                {
                    AdapterReceiptJson =
                        adapterReceiptJson,
                };
            await TryPersistRejectedAsync(
                rejected,
                cancellationToken);
            return rejected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            adapterReceiptJson ??=
                adapterReceiptProvider?.Invoke();
            var domain =
                new SourceEditDomainException(
                    SourceEditCodes.IoFailure,
                    $"Source edit preflight failed: {ex.Message}",
                    innerException: ex);
            var rejected =
                SourceEditTransactionResult.Rejected(
                    transactionId,
                    workspaceRoot,
                    requestHash,
                    domain) with
                {
                    AdapterReceiptJson =
                        adapterReceiptJson,
                };
            await TryPersistRejectedAsync(
                rejected,
                cancellationToken);
            return rejected;
        }

        using var pathGuard =
            committer.AcquirePreparedPathGuard(
                prepared);
        var plan =
            store.CreatePlan(
                prepared,
                pathGuard.DirectoryIdentities,
                adapterReceiptJson);
        var journalPrepared = false;
        var attemptedIndices = new HashSet<int>();
        var appliedIndices = new HashSet<int>();
        var commitDecisionDurable = false;

        try
        {
            await store.AppendPreparedAsync(
                plan,
                cancellationToken);
            journalPrepared = true;

            await committer.MaterializeStagesAsync(
                prepared,
                cancellationToken);
            await store.AppendEventAsync(
                plan,
                "StagesDurable",
                new
                {
                    staged = prepared.Files.Count(
                        file => file.StagePath is not null),
                },
                cancellationToken);

            await committer.VerifyCommitBarrierAsync(
                prepared,
                cancellationToken);

            for (var index = 0; index < prepared.Files.Count; index++)
            {
                var file = prepared.Files[index];
                await store.AppendEventAsync(
                    plan,
                    "CommitStepStarting",
                    new
                    {
                        index = file.Index,
                        path =
                            file.Change.DestinationFullPath ??
                            file.Change.FullPath,
                    },
                    cancellationToken);

                attemptedIndices.Add(file.Index);
                await faultInjector.BeforeCommitStepAsync(
                    index,
                    file,
                    cancellationToken);
                pathGuard.VerifyAnchors();
                await committer.CommitFileAsync(
                    file,
                    pathGuard,
                    cancellationToken);
                appliedIndices.Add(file.Index);
                await faultInjector.AfterCommitStepAsync(
                    index,
                    file,
                    cancellationToken);

                await store.AppendEventAsync(
                    plan,
                    "CommitStepApplied",
                    new
                    {
                        index = file.Index,
                        afterRevision = file.AfterRevision,
                    },
                    cancellationToken);
            }

            var committed =
                BuildPlanResult(
                    plan,
                    success: true,
                    status: "committed",
                    replayed: false,
                    error: null);

            await store.AppendEventAsync(
                plan,
                "Committed",
                new
                {
                    fileCount = plan.Files.Count,
                },
                cancellationToken);
            commitDecisionDurable = true;

            try
            {
                await store.PersistReceiptAsync(
                    committed,
                    cancellationToken);
                committer.CleanupTerminalArtifacts(plan);
                await store.CleanupCompletedAsync(
                    cancellationToken);
                return committed;
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException or
                    SourceEditDomainException)
            {
                return committed with
                {
                    Warnings =
                    [
                        new SourceEditWarning(
                            "SOURCE_EDIT_RECEIPT_PERSISTENCE_PENDING",
                            $"Commit is durable in the WAL, but receipt persistence/cleanup needs recovery: {ex.Message}",
                            null),
                    ],
                };
            }
        }
        catch (SourceEditSimulatedCrashException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            if (!commitDecisionDurable)
            {
                if (journalPrepared)
                {
                    _ = await RollbackAfterFailureAsync(
                        plan,
                        new SourceEditDomainException(
                            SourceEditCodes.TransactionRolledBack,
                            "Transaction was cancelled and rolled back."),
                        pathGuard,
                        attemptedIndices,
                        appliedIndices,
                        CancellationToken.None);
                }
                else
                {
                    committer.CleanupPreparedArtifacts(
                        prepared.Files);
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            if (commitDecisionDurable)
            {
                var committed =
                    BuildPlanResult(
                        plan,
                        success: true,
                        status: "committed",
                        replayed: false,
                        error: null);
                return committed with
                {
                    Warnings =
                    [
                        new SourceEditWarning(
                            "SOURCE_EDIT_POST_COMMIT_MAINTENANCE_FAILED",
                            ex.Message,
                            null),
                    ],
                };
            }

            if (!journalPrepared)
            {
                committer.CleanupPreparedArtifacts(
                    prepared.Files);
                var domain = ToDomainException(ex);
                var rejected =
                    SourceEditTransactionResult.Rejected(
                        transactionId,
                        workspaceRoot,
                        requestHash,
                        domain);
                await TryPersistRejectedAsync(
                    rejected,
                    CancellationToken.None);
                return rejected;
            }

            var failure = ToDomainException(ex);
            return await RollbackAfterFailureAsync(
                plan,
                failure,
                pathGuard,
                attemptedIndices,
                appliedIndices,
                CancellationToken.None);
        }
    }

    private async Task RecoverWorkspaceLockedAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var enumeration =
            await store.EnumerateStatesAsync(
                cancellationToken);

        var relatedQuarantine =
            enumeration.QuarantinedJournals
                .FirstOrDefault(
                    quarantine =>
                        !string.IsNullOrWhiteSpace(
                            quarantine.WorkspaceRoot) &&
                        string.Equals(
                            quarantine.WorkspaceRoot,
                            workspaceRoot,
                            StringComparison.OrdinalIgnoreCase));
        if (relatedQuarantine is not null)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                $"Workspace has a quarantined source-edit transaction journal: {relatedQuarantine.TransactionId ?? "unknown"}",
                relatedQuarantine.JournalPath,
                new Dictionary<string, string>
                {
                    ["workspaceRoot"] =
                        workspaceRoot,
                    ["journalPath"] =
                        relatedQuarantine.JournalPath,
                    ["reason"] =
                        relatedQuarantine.Message,
                });
        }

        foreach (var state in enumeration.States.Where(
                     state =>
                         string.Equals(
                             state.Plan.WorkspaceRoot,
                             workspaceRoot,
                             StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(
                    state.LastEvent,
                    "RecoveryRequired",
                    StringComparison.Ordinal))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    $"Workspace has an unresolved source-edit transaction: {state.Plan.TransactionId}",
                    workspaceRoot);
            }

            if (string.Equals(
                    state.LastEvent,
                    "Committed",
                    StringComparison.Ordinal) ||
                string.Equals(
                    state.LastEvent,
                    "RolledBack",
                    StringComparison.Ordinal))
            {
                continue;
            }

            using var transactionLease =
                await transactionLocks.AcquireAsync(
                    state.Plan.TransactionId,
                    cancellationToken);

            var current =
                await store.ReadStateAsync(
                    state.Plan.TransactionId,
                    cancellationToken);
            if (current is null)
            {
                continue;
            }

            var result =
                await RecoverStateLockedAsync(
                    current,
                    cancellationToken);
            if (string.Equals(
                    result.Status,
                    "recovery-required",
                    StringComparison.Ordinal))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    result.Error?.Message ??
                    "Source-edit recovery requires manual intervention.",
                    workspaceRoot);
            }
        }
    }

    private async Task<SourceEditTransactionResult> RecoverStateLockedAsync(
        SourceEditJournalState state,
        CancellationToken cancellationToken)
    {
        var plan = state.Plan;
        var existingReceipt =
            await store.TryReadReceiptAsync(
                plan.TransactionId,
                cancellationToken);

        if (existingReceipt is not null)
        {
            EnsureSameIdentity(
                plan.TransactionId,
                plan.WorkspaceRoot,
                plan.RequestHash,
                existingReceipt.RequestHash,
                existingReceipt.WorkspaceRoot);

            if (string.Equals(
                    state.LastEvent,
                    "Committed",
                    StringComparison.Ordinal) ||
                string.Equals(
                    state.LastEvent,
                    "RolledBack",
                    StringComparison.Ordinal) ||
                string.Equals(
                    state.LastEvent,
                    "RecoveryRequired",
                    StringComparison.Ordinal))
            {
                if (string.Equals(
                        state.LastEvent,
                        "Committed",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        state.LastEvent,
                        "RolledBack",
                        StringComparison.Ordinal))
                {
                    committer.CleanupTerminalArtifacts(plan);
                }

                return existingReceipt;
            }
        }

        if (string.Equals(
                state.LastEvent,
                "Committed",
                StringComparison.Ordinal))
        {
            var committed =
                BuildPlanResult(
                    plan,
                    success: true,
                    status: "committed",
                    replayed: false,
                    error: null);
            await store.PersistReceiptAsync(
                committed,
                cancellationToken);
            committer.CleanupTerminalArtifacts(plan);
            return committed;
        }

        if (string.Equals(
                state.LastEvent,
                "RolledBack",
                StringComparison.Ordinal))
        {
            var rolledBack =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "rolled-back",
                    replayed: false,
                    new SourceEditError(
                        SourceEditCodes.TransactionRolledBack,
                        "Transaction was rolled back.",
                        null,
                        null));
            await store.PersistReceiptAsync(
                rolledBack,
                cancellationToken);
            committer.CleanupTerminalArtifacts(plan);
            return rolledBack;
        }

        if (string.Equals(
                state.LastEvent,
                "RecoveryRequired",
                StringComparison.Ordinal))
        {
            var recoveryRequired =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "recovery-required",
                    replayed: false,
                    new SourceEditError(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Transaction is marked RecoveryRequired. Automatic mutation is blocked for this workspace until recovery can prove a safe state.",
                        null,
                        null));
            await store.PersistReceiptAsync(
                recoveryRequired,
                cancellationToken);
            return recoveryRequired;
        }

        try
        {
            using var recoveryPathGuard =
                committer.AcquireJournalPathGuard(
                    plan);
            var ownedApplied =
                new HashSet<int>(
                    state.AppliedFileIndices);
            var alreadyRolledBack =
                new HashSet<int>(
                    state.RolledBackFileIndices);
            var needsRollback =
                new HashSet<int>(
                    ownedApplied);
            needsRollback.ExceptWith(
                alreadyRolledBack);

            foreach (var file in plan.Files)
            {
                if (ownedApplied.Contains(file.Index) ||
                    alreadyRolledBack.Contains(file.Index) ||
                    !state.AttemptedFileIndices.Contains(file.Index))
                {
                    continue;
                }

                var stepState =
                    await committer.GetRecoveryStepStateAsync(
                        file,
                        cancellationToken);
                if (stepState ==
                    SourceEditRecoveryStepState.Applied)
                {
                    ownedApplied.Add(file.Index);
                    needsRollback.Add(file.Index);
                    continue;
                }

                if (stepState ==
                    SourceEditRecoveryStepState.Indeterminate)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Recovery cannot prove whether a started source-edit file step reached the filesystem. No automatic rollback was attempted for the indeterminate step.",
                        file.SourcePath);
                }
            }

            var allAfter =
                alreadyRolledBack.Count == 0 &&
                ownedApplied.Count == plan.Files.Count;
            if (allAfter)
            {
                foreach (var file in plan.Files)
                {
                    if (!await committer.IsAfterStateAsync(
                            file,
                            cancellationToken))
                    {
                        allAfter = false;
                        break;
                    }
                }
            }

            if (allAfter)
            {
                var committed =
                    BuildPlanResult(
                        plan,
                        success: true,
                        status: "committed",
                        replayed: false,
                        error: null);
                await store.AppendEventAsync(
                    plan,
                    "Committed",
                    new
                    {
                        recovered = true,
                        reason = "all-paths-match-after-state",
                    },
                    cancellationToken);
                await store.PersistReceiptAsync(
                    committed,
                    cancellationToken);
                committer.CleanupTerminalArtifacts(plan);
                return committed;
            }

            await store.AppendEventAsync(
                plan,
                "RollingBack",
                new
                {
                    recovered = true,
                },
                cancellationToken);

            for (var index = plan.Files.Count - 1;
                 index >= 0;
                 index--)
            {
                var file = plan.Files[index];
                if (!needsRollback.Contains(file.Index))
                {
                    continue;
                }

                await committer.RollbackJournalFileAsync(
                    file,
                    recoveryPathGuard,
                    cancellationToken);
                await store.AppendEventAsync(
                    plan,
                    "RollbackStepApplied",
                    new
                    {
                        index = file.Index,
                        recovered = true,
                    },
                    cancellationToken);
            }

            foreach (var file in plan.Files)
            {
                if (!ownedApplied.Contains(file.Index) &&
                    !alreadyRolledBack.Contains(file.Index))
                {
                    continue;
                }

                if (!await committer.IsBeforeStateAsync(
                        file,
                        cancellationToken))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Recovery rollback completed operations but could not prove the complete before-state.",
                        file.SourcePath);
                }
            }

            var rolledBack =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "rolled-back",
                    replayed: false,
                    new SourceEditError(
                        SourceEditCodes.TransactionRolledBack,
                        "Incomplete transaction was recovered by deterministic rollback.",
                        null,
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["recovered"] = "true",
                        }));

            await store.AppendEventAsync(
                plan,
                "RolledBack",
                new
                {
                    recovered = true,
                },
                cancellationToken);
            await store.PersistReceiptAsync(
                rolledBack,
                cancellationToken);
            committer.CleanupTerminalArtifacts(plan);
            return rolledBack;
        }
        catch (Exception ex)
        {
            var message =
                $"Source-edit recovery could not prove a safe terminal state: {ex.Message}";
            try
            {
                await store.AppendEventAsync(
                    plan,
                    "RecoveryRequired",
                    new
                    {
                        message,
                    },
                    CancellationToken.None);
            }
            catch
            {
            }

            var result =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "recovery-required",
                    replayed: false,
                    new SourceEditError(
                        SourceEditCodes.TransactionRecoveryRequired,
                        message,
                        ex is SourceEditDomainException domain
                            ? domain.Path
                            : null,
                        null));
            try
            {
                await store.PersistReceiptAsync(
                    result,
                    CancellationToken.None);
            }
            catch
            {
            }

            return result;
        }
    }

    private async Task<SourceEditTransactionResult> RollbackAfterFailureAsync(
        SourceEditJournalPlan plan,
        SourceEditDomainException failure,
        SourceEditPathGuard pathGuard,
        IReadOnlySet<int> attemptedIndices,
        IReadOnlySet<int> appliedIndices,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendEventAsync(
                plan,
                "RollingBack",
                new
                {
                    causeCode = failure.Code,
                    causeMessage = failure.Message,
                },
                cancellationToken);

            for (var index = plan.Files.Count - 1;
                 index >= 0;
                 index--)
            {
                var file = plan.Files[index];
                if (!appliedIndices.Contains(file.Index))
                {
                    if (attemptedIndices.Contains(file.Index))
                    {
                        committer.CleanupUncommittedJournalFile(file);
                    }
                    continue;
                }
                await committer.RollbackJournalFileAsync(
                    file,
                    pathGuard,
                    cancellationToken);
                await store.AppendEventAsync(
                    plan,
                    "RollbackStepApplied",
                    new
                    {
                        index = file.Index,
                    },
                    cancellationToken);
            }

            foreach (var file in plan.Files)
            {
                if (!appliedIndices.Contains(file.Index))
                {
                    continue;
                }
                if (!await committer.IsBeforeStateAsync(
                        file,
                        cancellationToken))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Rollback could not prove restoration of every before-state.",
                        file.SourcePath);
                }
            }

            var rolledBack =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "rolled-back",
                    replayed: false,
                    new SourceEditError(
                        failure.Code,
                        failure.Message,
                        failure.Path,
                        MergeFailureDetails(failure)));

            await store.AppendEventAsync(
                plan,
                "RolledBack",
                new
                {
                    causeCode = failure.Code,
                },
                cancellationToken);
            await store.PersistReceiptAsync(
                rolledBack,
                cancellationToken);
            committer.CleanupTerminalArtifacts(plan);
            return rolledBack;
        }
        catch (Exception rollbackException)
        {
            var message =
                $"Transaction failed with {failure.Code}; rollback/recovery also failed: {rollbackException.Message}";
            try
            {
                await store.AppendEventAsync(
                    plan,
                    "RecoveryRequired",
                    new
                    {
                        causeCode = failure.Code,
                        rollbackError =
                            rollbackException.Message,
                    },
                    CancellationToken.None);
            }
            catch
            {
            }

            var recoveryRequired =
                BuildPlanResult(
                    plan,
                    success: false,
                    status: "recovery-required",
                    replayed: false,
                    new SourceEditError(
                        SourceEditCodes.TransactionRecoveryRequired,
                        message,
                        rollbackException is SourceEditDomainException domain
                            ? domain.Path
                            : failure.Path,
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["causeCode"] = failure.Code,
                        }));
            try
            {
                await store.PersistReceiptAsync(
                    recoveryRequired,
                    CancellationToken.None);
            }
            catch
            {
            }

            return recoveryRequired;
        }
    }

    private async Task TryPersistRejectedAsync(
        SourceEditTransactionResult rejected,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing =
                await store.TryReadReceiptAsync(
                    rejected.TransactionId,
                    cancellationToken);
            if (existing is null)
            {
                await store.PersistReceiptAsync(
                    rejected,
                    cancellationToken);
            }
        }
        catch
        {
        }
    }

    private static SourceEditTransactionResult BuildPlanResult(
        SourceEditJournalPlan plan,
        bool success,
        string status,
        bool replayed,
        SourceEditError? error)
    {
        var files = plan.Files.Select(file =>
        {
            string? oldPath = null;
            string? newPath = null;

            switch (file.Operation)
            {
                case "update":
                    oldPath = file.SourcePath;
                    newPath = file.FinalPath;
                    break;
                case "add":
                    newPath = file.FinalPath;
                    break;
                case "delete":
                    oldPath = file.SourcePath;
                    break;
                case "move":
                    oldPath = file.SourcePath;
                    newPath = file.FinalPath;
                    break;
            }

            return new SourceEditFileReceipt(
                file.Operation,
                oldPath,
                newPath,
                file.BeforeRevision,
                file.AfterRevision,
                file.BeforeBytes,
                file.AfterBytes);
        }).ToArray();

        return new SourceEditTransactionResult(
            success,
            plan.TransactionId,
            status,
            replayed,
            plan.WorkspaceRoot,
            plan.RequestHash,
            files,
            plan.Validation,
            [],
            error)
        {
            AdapterReceiptJson =
                plan.AdapterReceiptJson,
        };
    }

    private static SourceEditTransactionResult BuildDomainFailure(
        string transactionId,
        string workspaceRoot,
        string requestHash,
        SourceEditDomainException exception)
    {
        var status =
            string.Equals(
                exception.Code,
                SourceEditCodes.TransactionRecoveryRequired,
                StringComparison.Ordinal)
                ? "recovery-required"
                : "rejected";

        return new SourceEditTransactionResult(
            false,
            transactionId,
            status,
            false,
            workspaceRoot,
            requestHash,
            [],
            [],
            [],
            new SourceEditError(
                exception.Code,
                exception.Message,
                exception.Path,
                exception.Details));
    }

    private static SourceEditDomainException ToDomainException(
        Exception exception) =>
        exception as SourceEditDomainException ??
        new SourceEditDomainException(
            SourceEditCodes.IoFailure,
            exception.Message,
            innerException: exception);

    private static IReadOnlyDictionary<string, string>? MergeFailureDetails(
        SourceEditDomainException failure)
    {
        var result =
            failure.Details is null
                ? new Dictionary<string, string>(
                    StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    failure.Details,
                    StringComparer.Ordinal);
        result["rolledBack"] = "true";
        return result;
    }

    private static void EnsureSameIdentity(
        string transactionId,
        string workspaceRoot,
        string requestHash,
        string? storedRequestHash,
        string? storedWorkspaceRoot)
    {
        if (!string.Equals(
                requestHash,
                storedRequestHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                workspaceRoot,
                storedWorkspaceRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionIdReuseMismatch,
                $"transactionId '{transactionId}' was already used for a different source-edit request/workspace. Use a new transactionId.");
        }
    }

    private static string NormalizePatchInputFormat(
        string? inputFormat)
    {
        var normalized =
            string.IsNullOrWhiteSpace(inputFormat)
                ? "talvora"
                : inputFormat.Trim().ToLowerInvariant();

        return normalized switch
        {
            "talvora" or "talvora-patch" => "talvora",
            "unified-diff" or "git-unified-diff" => "unified-diff",
            _ => throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                $"Unsupported patch inputFormat '{inputFormat}'. Use 'talvora' (default) or 'unified-diff'."),
        };
    }
}

internal static class SourceEditRuntime
{
    public static SourceEditEngine Engine { get; } = new();
}
