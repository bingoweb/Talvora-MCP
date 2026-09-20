using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Talvora.SourceEditing;

internal interface ISourceEditFaultInjector
{
    ValueTask BeforeCommitStepAsync(
        int index,
        SourceEditPreparedFile file,
        CancellationToken cancellationToken);

    ValueTask AfterCommitStepAsync(
        int index,
        SourceEditPreparedFile file,
        CancellationToken cancellationToken);
}

internal sealed class NullSourceEditFaultInjector : ISourceEditFaultInjector
{
    public static NullSourceEditFaultInjector Instance { get; } = new();

    public ValueTask BeforeCommitStepAsync(
        int index,
        SourceEditPreparedFile file,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask AfterCommitStepAsync(
        int index,
        SourceEditPreparedFile file,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

internal sealed class SourceEditSimulatedCrashException(string message)
    : Exception(message);

internal enum SourceEditRecoveryStepState
{
    NotApplied,
    Applied,
    Indeterminate,
}

internal sealed class SourceEditCommitter
{
    public async Task<SourceEditPreparedTransaction> PrepareAsync(
        NormalizedSourceEditChangeSet changeSet,
        IReadOnlyList<SourceEditValidationResult> validation,
        CancellationToken cancellationToken)
    {
        var prepared = new List<SourceEditPreparedFile>(
            changeSet.Changes.Count);
        var transactionKey = ShortTransactionKey(
            changeSet.TransactionId);

        try
        {
            for (var index = 0; index < changeSet.Changes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var change = changeSet.Changes[index];
                var finalPath =
                    change.DestinationFullPath ??
                    change.FullPath;
                var createdDirectories =
                    GetMissingDirectories(
                        Path.GetDirectoryName(finalPath));
                string? stagePath = null;
                string? backupPath = null;
                string? afterRevision = null;
                long afterBytes = 0;

                if (change.ProposedDocument is not null)
                {
                    var bytes =
                        SourceTextCodec.Encode(
                            change.ProposedDocument);
                    afterRevision =
                        SourceEditRevision.Format(
                            SHA256.HashData(bytes));
                    afterBytes = bytes.LongLength;

                    var needsStage =
                        change.Operation != SourceEditOperationKind.Move ||
                        !string.Equals(
                            afterRevision,
                            change.Before?.Revision,
                            StringComparison.Ordinal);

                    if (needsStage)
                    {
                        var stageDirectory =
                            FindNearestExistingDirectory(
                                Path.GetDirectoryName(finalPath)
                                ?? changeSet.WorkspaceRoot);
                        stagePath = Path.Combine(
                            stageDirectory,
                            $".talvora-stage-{transactionKey}-{index:D4}.tmp");
                    }
                }

                if (change.Operation is
                        SourceEditOperationKind.Update or
                        SourceEditOperationKind.Delete ||
                    (change.Operation == SourceEditOperationKind.Move &&
                     stagePath is not null))
                {
                    var backupDirectory =
                        change.Operation == SourceEditOperationKind.Move
                            ? Path.GetDirectoryName(finalPath)!
                            : Path.GetDirectoryName(change.FullPath)!;
                    backupPath = Path.Combine(
                        backupDirectory,
                        $".talvora-backup-{transactionKey}-{index:D4}.tmp");
                }

                prepared.Add(
                    new SourceEditPreparedFile(
                        index,
                        change,
                        stagePath,
                        backupPath,
                        afterRevision,
                        afterBytes,
                        createdDirectories));
            }

            var directoryIdentities =
                SourceEditPathGuard.CapturePreparedIdentities(
                    changeSet.WorkspaceRoot,
                    prepared);
            return new SourceEditPreparedTransaction(
                changeSet,
                prepared,
                validation,
                directoryIdentities);
        }
        catch
        {
            CleanupPreparedArtifacts(prepared);
            throw;
        }
    }

    public async Task MaterializeStagesAsync(
        SourceEditPreparedTransaction prepared,
        CancellationToken cancellationToken)
    {
        foreach (var file in prepared.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.StagePath is null ||
                file.Change.ProposedDocument is null)
            {
                continue;
            }

            if (File.Exists(file.StagePath))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    "A deterministic source-edit stage path already exists. Recovery must resolve the previous transaction state before continuing.",
                    file.StagePath);
            }

            var bytes =
                SourceTextCodec.Encode(
                    file.Change.ProposedDocument);
            await WriteDurableFileAsync(
                file.StagePath,
                bytes,
                cancellationToken);
        }
    }

    public async Task VerifyCommitBarrierAsync(
        SourceEditPreparedTransaction prepared,
        CancellationToken cancellationToken)
    {
        foreach (var file in prepared.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = file.Change;

            if (change.Before?.Exists == true)
            {
                if (!File.Exists(change.FullPath))
                {
                    throw Conflict(
                        "Source disappeared after preflight.",
                        change.FullPath);
                }

                var revision =
                    await SourceTextCodec.ComputeRevisionAsync(
                        change.FullPath,
                        cancellationToken);
                if (!string.Equals(
                        revision,
                        change.Before.Revision,
                        StringComparison.Ordinal))
                {
                    throw RevisionConflict(
                        change.FullPath,
                        change.Before.Revision!,
                        revision);
                }
            }

            if (change.Operation == SourceEditOperationKind.Add &&
                File.Exists(change.FullPath))
            {
                throw Conflict(
                    "Add destination appeared after preflight.",
                    change.FullPath);
            }

            if (change.Operation == SourceEditOperationKind.Move &&
                change.DestinationFullPath is not null &&
                (File.Exists(change.DestinationFullPath) ||
                 Directory.Exists(change.DestinationFullPath)))
            {
                throw Conflict(
                    "Move destination appeared after preflight.",
                    change.DestinationFullPath);
            }

            SourceWorkspaceClassifier.EnsureNoReparsePoint(
                prepared.ChangeSet.WorkspaceRoot,
                change.FullPath);
            if (change.DestinationFullPath is not null)
            {
                SourceWorkspaceClassifier.EnsureNoReparsePoint(
                    prepared.ChangeSet.WorkspaceRoot,
                    change.DestinationFullPath);
            }
        }
    }

    public async Task CommitFileAsync(
        SourceEditPreparedFile prepared,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var change = prepared.Change;

        switch (change.Operation)
        {
            case SourceEditOperationKind.Update:
                await CommitUpdateAsync(
                    prepared,
                    pathGuard,
                    cancellationToken);
                break;
            case SourceEditOperationKind.Add:
                await CommitAddAsync(
                    prepared,
                    pathGuard,
                    cancellationToken);
                break;
            case SourceEditOperationKind.Delete:
                await CommitDeleteAsync(
                    prepared,
                    pathGuard,
                    cancellationToken);
                break;
            case SourceEditOperationKind.Move:
                await CommitMoveAsync(
                    prepared,
                    pathGuard,
                    cancellationToken);
                break;
            default:
                throw new UnreachableException();
        }
    }

    public SourceEditPathGuard AcquirePreparedPathGuard(
        SourceEditPreparedTransaction prepared) =>
        SourceEditPathGuard.AcquirePrepared(prepared);

    public SourceEditPathGuard AcquireJournalPathGuard(
        SourceEditJournalPlan plan) =>
        SourceEditPathGuard.AcquireJournal(plan);

    public async Task RollbackJournalFileAsync(
        SourceEditJournalFilePlan plan,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (plan.Operation)
        {
            case "update":
                await RollbackUpdateAsync(
                    plan,
                    pathGuard,
                    cancellationToken);
                break;
            case "add":
                await RollbackAddAsync(
                    plan,
                    pathGuard,
                    cancellationToken);
                break;
            case "delete":
                await RollbackDeleteAsync(
                    plan,
                    pathGuard,
                    cancellationToken);
                break;
            case "move":
                await RollbackMoveAsync(
                    plan,
                    pathGuard,
                    cancellationToken);
                break;
            default:
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    $"Unknown operation '{plan.Operation}' in recovery journal.",
                    plan.SourcePath);
        }

        CleanupCreatedDirectories(plan.CreatedDirectories);
    }

    public async Task<bool> IsAfterStateAsync(
        SourceEditJournalFilePlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return plan.Operation switch
        {
            "update" or "add" =>
                await PathHasRevisionAsync(
                    plan.FinalPath,
                    plan.AfterRevision,
                    cancellationToken),
            "delete" =>
                !File.Exists(plan.SourcePath) &&
                !Directory.Exists(plan.SourcePath),
            "move" =>
                !File.Exists(plan.SourcePath) &&
                await PathHasRevisionAsync(
                    plan.FinalPath,
                    plan.AfterRevision,
                    cancellationToken),
            _ => false,
        };
    }

    public async Task<bool> IsBeforeStateAsync(
        SourceEditJournalFilePlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return plan.Operation switch
        {
            "update" or "delete" =>
                await PathHasRevisionAsync(
                    plan.SourcePath,
                    plan.BeforeRevision,
                    cancellationToken),
            "add" =>
                !File.Exists(plan.FinalPath) &&
                !Directory.Exists(plan.FinalPath),
            "move" =>
                await PathHasRevisionAsync(
                    plan.SourcePath,
                    plan.BeforeRevision,
                    cancellationToken) &&
                !File.Exists(plan.FinalPath) &&
                !Directory.Exists(plan.FinalPath),
            _ => false,
        };
    }

    public async Task<SourceEditRecoveryStepState> GetRecoveryStepStateAsync(
        SourceEditJournalFilePlan plan,
        CancellationToken cancellationToken)
    {
        if (await IsBeforeStateAsync(
                plan,
                cancellationToken))
        {
            return SourceEditRecoveryStepState.NotApplied;
        }

        if (!await IsAfterStateAsync(
                plan,
                cancellationToken))
        {
            return SourceEditRecoveryStepState.Indeterminate;
        }

        return plan.Operation switch
        {
            "update" =>
                plan.BackupPath is not null &&
                await PathHasRevisionAsync(
                    plan.BackupPath,
                    plan.BeforeRevision,
                    cancellationToken) &&
                (plan.StagePath is null ||
                 !File.Exists(plan.StagePath))
                    ? SourceEditRecoveryStepState.Applied
                    : SourceEditRecoveryStepState.Indeterminate,
            "add" =>
                plan.StagePath is not null &&
                !File.Exists(plan.StagePath)
                    ? SourceEditRecoveryStepState.Applied
                    : SourceEditRecoveryStepState.Indeterminate,
            "delete" =>
                plan.BackupPath is not null &&
                await PathHasRevisionAsync(
                    plan.BackupPath,
                    plan.BeforeRevision,
                    cancellationToken)
                    ? SourceEditRecoveryStepState.Applied
                    : SourceEditRecoveryStepState.Indeterminate,
            "move" =>
                plan.StagePath is not null &&
                !File.Exists(plan.StagePath) &&
                plan.BackupPath is not null &&
                await PathHasRevisionAsync(
                    plan.BackupPath,
                    plan.BeforeRevision,
                    cancellationToken)
                    ? SourceEditRecoveryStepState.Applied
                    : SourceEditRecoveryStepState.Indeterminate,
            _ => SourceEditRecoveryStepState.Indeterminate,
        };
    }

    public void CleanupUncommittedPreparedFile(
        SourceEditPreparedFile file)
    {
        CleanupCreatedDirectories(
            file.CreatedDirectories);
    }

    public void CleanupUncommittedJournalFile(
        SourceEditJournalFilePlan file)
    {
        CleanupCreatedDirectories(
            file.CreatedDirectories);
    }

    public void CleanupTerminalArtifacts(
        SourceEditJournalPlan plan)
    {
        foreach (var file in plan.Files)
        {
            TryDeleteFile(file.StagePath);
            TryDeleteFile(file.BackupPath);
        }
    }

    public void CleanupPreparedArtifacts(
        IReadOnlyList<SourceEditPreparedFile> files)
    {
        foreach (var file in files)
        {
            TryDeleteFile(file.StagePath);
            TryDeleteFile(file.BackupPath);
            CleanupCreatedDirectories(
                file.CreatedDirectories);
        }
    }

    private static async Task CommitUpdateAsync(
        SourceEditPreparedFile prepared,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        var change = prepared.Change;
        var stage = RequirePath(
            prepared.StagePath,
            "Update stage path is missing.");
        var backup = RequirePath(
            prepared.BackupPath,
            "Update backup path is missing.");
        var expectedBefore =
            change.Before?.Revision ??
            throw new InvalidOperationException(
                "Update before revision is missing.");

        await VerifyStillExpectedAsync(
            change.FullPath,
            expectedBefore,
            cancellationToken);
        pathGuard.VerifyAnchors();

        try
        {
            File.Replace(
                stage,
                change.FullPath,
                backup,
                ignoreMetadataErrors: false);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            throw IoFailure(
                "Atomic file replacement failed.",
                change.FullPath,
                ex);
        }

        var displacedRevision =
            await SourceTextCodec.ComputeRevisionAsync(
                backup,
                cancellationToken);
        if (!string.Equals(
                displacedRevision,
                expectedBefore,
                StringComparison.Ordinal))
        {
            await RestoreReplacementAsync(
                change.FullPath,
                backup,
                pathGuard,
                cancellationToken);
            throw RevisionConflict(
                change.FullPath,
                expectedBefore,
                displacedRevision);
        }

        await VerifyAfterRevisionAsync(
            change.FullPath,
            prepared.AfterRevision,
            cancellationToken);
        FlushPathToDisk(change.FullPath);
    }

    private static async Task CommitAddAsync(
        SourceEditPreparedFile prepared,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        var target = prepared.Change.FullPath;
        var stage = RequirePath(
            prepared.StagePath,
            "Add stage path is missing.");

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw Conflict(
                "Add destination appeared during commit.",
                target);
        }

        pathGuard.MoveFileByHandle(
            stage,
            target,
            replaceExisting: false);
        await VerifyAfterRevisionAsync(
            target,
            prepared.AfterRevision,
            cancellationToken);
        FlushPathToDisk(target);
    }

    private static async Task CommitDeleteAsync(
        SourceEditPreparedFile prepared,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        var source = prepared.Change.FullPath;
        var backup = RequirePath(
            prepared.BackupPath,
            "Delete backup path is missing.");
        var expectedBefore =
            prepared.Change.Before?.Revision ??
            throw new InvalidOperationException(
                "Delete before revision is missing.");

        await VerifyStillExpectedAsync(
            source,
            expectedBefore,
            cancellationToken);
        pathGuard.MoveFileByHandle(
            source,
            backup,
            replaceExisting: false);

        var displacedRevision =
            await SourceTextCodec.ComputeRevisionAsync(
                backup,
                cancellationToken);
        if (!string.Equals(
                displacedRevision,
                expectedBefore,
                StringComparison.Ordinal))
        {
            pathGuard.MoveFileByHandle(
                backup,
                source,
                replaceExisting: false);
            throw RevisionConflict(
                source,
                expectedBefore,
                displacedRevision);
        }
    }

    private static async Task CommitMoveAsync(
        SourceEditPreparedFile prepared,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        var change = prepared.Change;
        var source = change.FullPath;
        var destination =
            change.DestinationFullPath ??
            throw new InvalidOperationException(
                "Move destination path is missing.");
        var expectedBefore =
            change.Before?.Revision ??
            throw new InvalidOperationException(
                "Move before revision is missing.");

        if (File.Exists(destination) ||
            Directory.Exists(destination))
        {
            throw Conflict(
                "Move destination appeared during commit.",
                destination);
        }

        await VerifyStillExpectedAsync(
            source,
            expectedBefore,
            cancellationToken);
        pathGuard.MoveFileByHandle(
            source,
            destination,
            replaceExisting: false);

        var movedRevision =
            await SourceTextCodec.ComputeRevisionAsync(
                destination,
                cancellationToken);
        if (!string.Equals(
                movedRevision,
                expectedBefore,
                StringComparison.Ordinal))
        {
            pathGuard.MoveFileByHandle(
                destination,
                source,
                replaceExisting: false);
            throw RevisionConflict(
                source,
                expectedBefore,
                movedRevision);
        }

        if (prepared.StagePath is not null)
        {
            var backup = RequirePath(
                prepared.BackupPath,
                "Edited move backup path is missing.");
            try
            {
                pathGuard.VerifyAnchors();
                File.Replace(
                    prepared.StagePath,
                    destination,
                    backup,
                    ignoreMetadataErrors: false);
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException or
                    PlatformNotSupportedException)
            {
                if (File.Exists(destination) &&
                    !File.Exists(source))
                {
                    pathGuard.MoveFileByHandle(
                        destination,
                        source,
                        replaceExisting: false);
                }

                throw IoFailure(
                    "Atomic edited-move replacement failed.",
                    destination,
                    ex);
            }

            var backupRevision =
                await SourceTextCodec.ComputeRevisionAsync(
                    backup,
                    cancellationToken);
            if (!string.Equals(
                    backupRevision,
                    expectedBefore,
                    StringComparison.Ordinal))
            {
                await RestoreEditedMoveAsync(
                    source,
                    destination,
                    backup,
                    pathGuard,
                    cancellationToken);
                throw RevisionConflict(
                    source,
                    expectedBefore,
                    backupRevision);
            }
        }

        await VerifyAfterRevisionAsync(
            destination,
            prepared.AfterRevision,
            cancellationToken);
        FlushPathToDisk(destination);
    }

    private static async Task RollbackUpdateAsync(
        SourceEditJournalFilePlan plan,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        if (await PathHasRevisionAsync(
                plan.SourcePath,
                plan.BeforeRevision,
                cancellationToken))
        {
            TryDeleteFile(plan.BackupPath);
            return;
        }

        if (plan.BackupPath is not null &&
            File.Exists(plan.BackupPath) &&
            await PathHasRevisionAsync(
                plan.BackupPath,
                plan.BeforeRevision,
                cancellationToken))
        {
            if (File.Exists(plan.SourcePath))
            {
                if (!await PathHasRevisionAsync(
                        plan.SourcePath,
                        plan.AfterRevision,
                        cancellationToken))
                {
                    throw RecoveryFailure(
                        "Updated file changed externally after the transaction step; automatic rollback would destroy external data.",
                        plan.SourcePath);
                }

                await RestoreReplacementAsync(
                    plan.SourcePath,
                    plan.BackupPath,
                    pathGuard,
                    cancellationToken);
            }
            else
            {
                pathGuard.MoveFileByHandle(
                    plan.BackupPath,
                    plan.SourcePath,
                    replaceExisting: false);
            }

            return;
        }

        throw RecoveryFailure(
            "Update rollback cannot prove or restore the before-state.",
            plan.SourcePath);
    }

    private static async Task RollbackAddAsync(
        SourceEditJournalFilePlan plan,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(plan.FinalPath))
        {
            return;
        }

        if (!await PathHasRevisionAsync(
                plan.FinalPath,
                plan.AfterRevision,
                cancellationToken))
        {
            throw RecoveryFailure(
                "Added file changed after transaction commit; automatic rollback would destroy external data.",
                plan.FinalPath);
        }

        pathGuard.DeleteFileByHandle(
            plan.FinalPath);
    }

    private static async Task RollbackDeleteAsync(
        SourceEditJournalFilePlan plan,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        if (await PathHasRevisionAsync(
                plan.SourcePath,
                plan.BeforeRevision,
                cancellationToken))
        {
            TryDeleteFile(plan.BackupPath);
            return;
        }

        if (File.Exists(plan.SourcePath))
        {
            throw RecoveryFailure(
                "Deleted source path was recreated with unexpected content; automatic rollback would overwrite external data.",
                plan.SourcePath);
        }

        if (plan.BackupPath is null ||
            !await PathHasRevisionAsync(
                plan.BackupPath,
                plan.BeforeRevision,
                cancellationToken))
        {
            throw RecoveryFailure(
                "Delete backup is missing or does not match the before-state.",
                plan.SourcePath);
        }

        pathGuard.MoveFileByHandle(
            plan.BackupPath,
            plan.SourcePath,
            replaceExisting: false);
    }

    private static async Task RollbackMoveAsync(
        SourceEditJournalFilePlan plan,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        if (await PathHasRevisionAsync(
                plan.SourcePath,
                plan.BeforeRevision,
                cancellationToken) &&
            !File.Exists(plan.FinalPath))
        {
            TryDeleteFile(plan.BackupPath);
            return;
        }

        if (File.Exists(plan.SourcePath))
        {
            throw RecoveryFailure(
                "Move source path contains unexpected content; automatic rollback would overwrite external data.",
                plan.SourcePath);
        }

        if (plan.BackupPath is not null &&
            File.Exists(plan.BackupPath) &&
            await PathHasRevisionAsync(
                plan.BackupPath,
                plan.BeforeRevision,
                cancellationToken))
        {
            if (File.Exists(plan.FinalPath))
            {
                if (!await PathHasRevisionAsync(
                        plan.FinalPath,
                        plan.AfterRevision,
                        cancellationToken))
                {
                    throw RecoveryFailure(
                        "Edited move destination changed externally; automatic rollback is unsafe.",
                        plan.FinalPath);
                }

                await RestoreReplacementAsync(
                    plan.FinalPath,
                    plan.BackupPath,
                    pathGuard,
                    cancellationToken);
                pathGuard.MoveFileByHandle(
                    plan.FinalPath,
                    plan.SourcePath,
                    replaceExisting: false);
                return;
            }

            pathGuard.MoveFileByHandle(
                plan.BackupPath,
                plan.SourcePath,
                replaceExisting: false);
            return;
        }

        if (File.Exists(plan.FinalPath) &&
            await PathHasRevisionAsync(
                plan.FinalPath,
                plan.BeforeRevision,
                cancellationToken))
        {
            pathGuard.MoveFileByHandle(
                plan.FinalPath,
                plan.SourcePath,
                replaceExisting: false);
            return;
        }

        throw RecoveryFailure(
            "Move rollback cannot prove or restore the before-state.",
            plan.SourcePath);
    }

    private static async Task RestoreReplacementAsync(
        string target,
        string backup,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(backup))
        {
            throw RecoveryFailure(
                "Replacement backup is missing.",
                target);
        }

        if (File.Exists(target))
        {
            pathGuard.VerifyAnchors();
            File.Replace(
                backup,
                target,
                destinationBackupFileName: null,
                ignoreMetadataErrors: false);
        }
        else
        {
            pathGuard.MoveFileByHandle(
                backup,
                target,
                replaceExisting: false);
        }

        FlushPathToDisk(target);
        await Task.CompletedTask;
    }

    private static async Task RestoreEditedMoveAsync(
        string source,
        string destination,
        string backup,
        SourceEditPathGuard pathGuard,
        CancellationToken cancellationToken)
    {
        await RestoreReplacementAsync(
            destination,
            backup,
            pathGuard,
            cancellationToken);
        if (File.Exists(source))
        {
            throw RecoveryFailure(
                "Move source unexpectedly exists during rollback.",
                source);
        }

        pathGuard.MoveFileByHandle(
            destination,
            source,
            replaceExisting: false);
    }

    private static async Task VerifyAfterRevisionAsync(
        string path,
        string? expected,
        CancellationToken cancellationToken)
    {
        if (expected is null)
        {
            return;
        }

        var actual =
            await SourceTextCodec.ComputeRevisionAsync(
                path,
                cancellationToken);
        if (!string.Equals(
                expected,
                actual,
                StringComparison.Ordinal))
        {
            throw RecoveryFailure(
                "Committed file bytes do not match the prepared after-revision.",
                path);
        }
    }

    private static async Task VerifyStillExpectedAsync(
        string path,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            throw Conflict(
                "Source disappeared during commit.",
                path);
        }

        var actual =
            await SourceTextCodec.ComputeRevisionAsync(
                path,
                cancellationToken);
        if (!string.Equals(
                actual,
                expectedRevision,
                StringComparison.Ordinal))
        {
            throw RevisionConflict(
                path,
                expectedRevision,
                actual);
        }
    }

    private static async Task<bool> PathHasRevisionAsync(
        string path,
        string? revision,
        CancellationToken cancellationToken)
    {
        if (revision is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var actual =
                await SourceTextCodec.ComputeRevisionAsync(
                    path,
                    cancellationToken);
            return string.Equals(
                actual,
                revision,
                StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static SourceEditJournalFilePlan ToJournalFilePlan(
        SourceEditPreparedFile prepared)
    {
        var change = prepared.Change;
        var finalPath =
            change.DestinationFullPath ??
            change.FullPath;
        return new SourceEditJournalFilePlan(
            prepared.Index,
            change.Operation.ToString().ToLowerInvariant(),
            change.FullPath,
            finalPath,
            change.DestinationFullPath,
            change.Before?.Revision,
            prepared.AfterRevision,
            change.Before?.Length ?? 0,
            prepared.AfterBytes,
            prepared.StagePath,
            prepared.BackupPath,
            prepared.CreatedDirectories);
    }

    private static IReadOnlyList<string> GetMissingDirectories(
        string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return [];
        }

        var missing = new Stack<string>();
        var current =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(targetDirectory));

        while (!Directory.Exists(current))
        {
            missing.Push(current);
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return missing.ToArray();
    }

    private static string FindNearestExistingDirectory(
        string start)
    {
        var current =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(start));
        while (!Directory.Exists(current))
        {
            var parent = Directory.GetParent(current)
                ?? throw new DirectoryNotFoundException(
                    $"No existing ancestor directory was found for '{start}'.");
            current = parent.FullName;
        }

        return current;
    }

    private static void CreateMissingDirectories(
        IReadOnlyList<string> directories)
    {
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void CleanupCreatedDirectories(
        IReadOnlyList<string> directories)
    {
        for (var index = directories.Count - 1; index >= 0; index--)
        {
            var directory = directories[index];
            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task WriteDurableFileAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous |
                     FileOptions.SequentialScan |
                     FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void FlushPathToDisk(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete,
                bufferSize: 1,
                options: FileOptions.WriteThrough);
            stream.Flush(flushToDisk: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw IoFailure(
                "Committed file could not be durably flushed.",
                path,
                ex);
        }
        catch (IOException ex)
        {
            throw IoFailure(
                "Committed file could not be durably flushed.",
                path,
                ex);
        }
    }

    private static void MoveDurable(
        string source,
        string destination,
        bool replaceExisting)
    {
        var flags = MoveFileFlags.WriteThrough;
        if (replaceExisting)
        {
            flags |= MoveFileFlags.ReplaceExisting;
        }

        if (!MoveFileExW(
                source,
                destination,
                flags))
        {
            throw new IOException(
                $"MoveFileExW failed moving '{source}' to '{destination}'.",
                new Win32Exception(
                    Marshal.GetLastWin32Error()));
        }
    }

    private static string RequirePath(
        string? path,
        string message) =>
        path ??
        throw new InvalidOperationException(message);

    private static string ShortTransactionKey(string transactionId) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(transactionId)))
            .ToLowerInvariant();

    private static SourceEditDomainException Conflict(
        string message,
        string path) =>
        new(
            SourceEditCodes.Conflict,
            message,
            path);

    private static SourceEditDomainException RevisionConflict(
        string path,
        string expected,
        string actual) =>
        new(
            SourceEditCodes.Conflict,
            "Source content changed during the transaction. The operation was not allowed to overwrite the concurrent change.",
            path,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["expectedRevision"] = expected,
                ["actualRevision"] = actual,
            });

    private static SourceEditDomainException IoFailure(
        string message,
        string path,
        Exception innerException) =>
        new(
            SourceEditCodes.IoFailure,
            message,
            path,
            innerException: innerException);

    private static SourceEditDomainException RecoveryFailure(
        string message,
        string path) =>
        new(
            SourceEditCodes.TransactionRecoveryRequired,
            message,
            path);

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Flags]
    private enum MoveFileFlags : uint
    {
        ReplaceExisting = 0x1,
        WriteThrough = 0x8,
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "MoveFileExW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(
        string existingFileName,
        string newFileName,
        MoveFileFlags flags);
}
