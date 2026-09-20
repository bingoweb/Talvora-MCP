using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Talvora.SourceEditing;

internal sealed record SourceEditJournalFilePlan(
    int Index,
    string Operation,
    string SourcePath,
    string FinalPath,
    string? DestinationPath,
    string? BeforeRevision,
    string? AfterRevision,
    long BeforeBytes,
    long AfterBytes,
    string? StagePath,
    string? BackupPath,
    IReadOnlyList<string> CreatedDirectories);

internal sealed record SourceEditJournalPlan(
    int SchemaVersion,
    string TransactionId,
    string RequestHash,
    string WorkspaceRoot,
    string InputKind,
    bool ValidateSyntax,
    IReadOnlyList<SourceEditJournalFilePlan> Files,
    IReadOnlyList<SourceEditValidationResult> Validation,
    IReadOnlyList<SourceEditDirectoryIdentity>? DirectoryIdentities);

internal sealed record SourceEditJournalState(
    SourceEditJournalPlan Plan,
    string LastEvent,
    long LastSequence,
    IReadOnlySet<int> AttemptedFileIndices,
    IReadOnlySet<int> AppliedFileIndices,
    IReadOnlySet<int> RolledBackFileIndices);

internal sealed record SourceEditTransactionTombstone(
    int SchemaVersion,
    string RequestHash,
    string WorkspaceRoot,
    string TerminalStatus,
    DateTimeOffset RetiredUtc);

internal sealed record SourceEditJournalPayload(
    int SchemaVersion,
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Event,
    string TransactionId,
    string RequestHash,
    string WorkspaceRoot,
    string? DataJson);

internal sealed record SourceEditJournalEnvelope(
    string Payload,
    string Checksum);

internal sealed record SourceEditJournalIdentity(
    int SchemaVersion,
    string TransactionId,
    string RequestHash,
    string WorkspaceRoot);

internal sealed record SourceEditJournalQuarantine(
    int SchemaVersion,
    string? TransactionId,
    string? RequestHash,
    string? WorkspaceRoot,
    string JournalPath,
    string Message,
    DateTimeOffset DetectedUtc);

internal sealed record SourceEditJournalEnumeration(
    IReadOnlyList<SourceEditJournalState> States,
    IReadOnlyList<SourceEditJournalQuarantine> QuarantinedJournals);

internal sealed class SourceEditTransactionStore
{
    private const long MaximumCompletedStateBytes = 512L * 1024 * 1024;
    private const int MaximumCompletedTransactions = 10_000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };

    private static readonly JsonSerializerOptions ReceiptJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private readonly string stateRoot;
    private readonly string transactionsRoot;
    private readonly string receiptsRoot;
    private readonly string tombstonesRoot;

    public SourceEditTransactionStore(string? stateRoot = null)
    {
        this.stateRoot = Path.GetFullPath(
            stateRoot ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Talvora",
                "SourceEdit"));
        transactionsRoot = Path.Combine(this.stateRoot, "transactions");
        receiptsRoot = Path.Combine(this.stateRoot, "receipts");
        tombstonesRoot = Path.Combine(this.stateRoot, "tombstones");
    }

    public string StateRoot => stateRoot;

    public SourceEditJournalPlan CreatePlan(
        SourceEditPreparedTransaction prepared,
        IReadOnlyList<SourceEditDirectoryIdentity> directoryIdentities)
    {
        var files = prepared.Files
            .Select(file =>
            {
                var finalPath =
                    file.Change.DestinationFullPath ??
                    file.Change.FullPath;
                return new SourceEditJournalFilePlan(
                    file.Index,
                    file.Change.Operation.ToString().ToLowerInvariant(),
                    file.Change.FullPath,
                    finalPath,
                    file.Change.DestinationFullPath,
                    file.Change.Before?.Revision,
                    file.AfterRevision,
                    file.Change.Before?.Length ?? 0,
                    file.AfterBytes,
                    file.StagePath,
                    file.BackupPath,
                    file.CreatedDirectories);
            })
            .ToArray();

        return new SourceEditJournalPlan(
            prepared.ChangeSet.SchemaVersion,
            prepared.ChangeSet.TransactionId,
            prepared.ChangeSet.RequestHash,
            prepared.ChangeSet.WorkspaceRoot,
            prepared.ChangeSet.InputKind,
            prepared.ChangeSet.ValidateSyntax,
            files,
            prepared.Validation,
            directoryIdentities);
    }

    public async Task AppendPreparedAsync(
        SourceEditJournalPlan plan,
        CancellationToken cancellationToken)
    {
        var existing = await ReadStateAsync(
            plan.TransactionId,
            cancellationToken);
        if (existing is not null)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionIdReuseMismatch,
                "A transaction journal already exists for this transactionId.");
        }

        await PersistJournalIdentityAsync(
            plan,
            cancellationToken);
        var dataJson = JsonSerializer.Serialize(plan, JsonOptions);
        await AppendAsync(
            plan.TransactionId,
            plan.RequestHash,
            plan.WorkspaceRoot,
            "Prepared",
            dataJson,
            sequence: 1,
            cancellationToken);
    }

    public async Task AppendEventAsync(
        SourceEditJournalPlan plan,
        string eventName,
        object? data,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(
            plan.TransactionId,
            cancellationToken)
            ?? throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Transaction journal disappeared after preparation.");

        if (!string.Equals(
                state.Plan.RequestHash,
                plan.RequestHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                state.Plan.WorkspaceRoot,
                plan.WorkspaceRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionIdReuseMismatch,
                "Transaction journal identity does not match the active request.");
        }

        var dataJson = data is null
            ? null
            : JsonSerializer.Serialize(data, JsonOptions);

        await AppendAsync(
            plan.TransactionId,
            plan.RequestHash,
            plan.WorkspaceRoot,
            eventName,
            dataJson,
            checked(state.LastSequence + 1),
            cancellationToken);
    }

    public async Task<SourceEditJournalState?> ReadStateAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var journalPath = GetJournalPath(transactionId);
        if (!File.Exists(journalPath))
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(
                journalPath,
                cancellationToken);
        }
        catch (IOException ex)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Unable to read the durable source-edit journal.",
                journalPath,
                innerException: ex);
        }

        SourceEditJournalPlan? plan = null;
        SourceEditJournalPayload? last = null;
        var attemptedFileIndices = new HashSet<int>();
        var appliedFileIndices = new HashSet<int>();
        var rolledBackFileIndices = new HashSet<int>();
        var tailTerminated =
            await HasTerminatedTailAsync(
                journalPath,
                cancellationToken);

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            if (index == lines.Length - 1 &&
                !tailTerminated)
            {
                break;
            }

            SourceEditJournalPayload payload;
            try
            {
                payload = DecodeLine(lines[index]);
            }
            catch (Exception ex) when (
                ex is JsonException or
                    FormatException or
                    CryptographicException or
                    InvalidDataException)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    "A complete source-edit WAL record is corrupt.",
                    journalPath,
                    innerException: ex);
            }

            if (last is not null &&
                payload.Sequence != last.Sequence + 1)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    "Source-edit WAL sequence is not monotonic.",
                    journalPath);
            }

            if (plan is null &&
                string.Equals(
                    payload.Event,
                    "Prepared",
                    StringComparison.Ordinal))
            {
                if (payload.DataJson is null)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Prepared WAL record does not contain a transaction plan.",
                        journalPath);
                }

                plan = JsonSerializer.Deserialize<SourceEditJournalPlan>(
                    payload.DataJson,
                    JsonOptions)
                    ?? throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Prepared transaction plan could not be deserialized.",
                        journalPath);
            }

            TrackStepOwnership(
                payload,
                plan,
                attemptedFileIndices,
                appliedFileIndices,
                rolledBackFileIndices,
                journalPath);

            last = payload;
        }

        if (plan is null || last is null)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit WAL contains no complete Prepared record.",
                journalPath);
        }

        return new SourceEditJournalState(
            plan,
            last.Event,
            last.Sequence,
            attemptedFileIndices,
            appliedFileIndices,
            rolledBackFileIndices);
    }

    public async Task<SourceEditTransactionResult?> TryReadReceiptAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var receiptPath = GetReceiptPath(transactionId);
        if (!File.Exists(receiptPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                receiptPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SourceEditTransactionResult>(
                stream,
                ReceiptJsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Committed source-edit receipt is unreadable.",
                receiptPath,
                innerException: ex);
        }
    }

    public async Task<SourceEditTransactionTombstone?> TryReadTombstoneAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var tombstonePath = GetTombstonePath(transactionId);
        if (!File.Exists(tombstonePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                tombstonePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SourceEditTransactionTombstone>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit transaction tombstone is unreadable.",
                tombstonePath,
                innerException: ex);
        }
    }

    public async Task PersistReceiptAsync(
        SourceEditTransactionResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(receiptsRoot);
        var receiptPath = GetReceiptPath(result.TransactionId);
        var tempPath =
            receiptPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                result,
                ReceiptJsonOptions);
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             options: FileOptions.Asynchronous |
                                      FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, receiptPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task PersistJournalIdentityAsync(
        SourceEditJournalPlan plan,
        CancellationToken cancellationToken)
    {
        var directory =
            GetTransactionDirectory(
                plan.TransactionId);
        Directory.CreateDirectory(
            directory);
        var path =
            Path.Combine(
                directory,
                "identity.json");
        var temp =
            path +
            ".tmp." +
            Guid.NewGuid().ToString("N");
        try
        {
            var identity =
                new SourceEditJournalIdentity(
                    1,
                    plan.TransactionId,
                    plan.RequestHash,
                    plan.WorkspaceRoot);
            var bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                    identity,
                    JsonOptions);
            await using (var stream = new FileStream(
                             temp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options:
                                 FileOptions.Asynchronous |
                                 FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                    bytes,
                    cancellationToken);
                await stream.FlushAsync(
                    cancellationToken);
                stream.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temp,
                path,
                overwrite: true);
        }
        finally
        {
            TryDeleteFile(
                temp);
        }
    }

    private async Task<SourceEditJournalIdentity?> TryReadJournalIdentityAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path =
            Path.Combine(
                directory,
                "identity.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 16 * 1024,
                    options:
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SourceEditJournalIdentity>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or
                JsonException or
                UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<SourceEditJournalQuarantine?> TryReadJournalQuarantineAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path =
            Path.Combine(
                directory,
                "quarantine.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 16 * 1024,
                    options:
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SourceEditJournalQuarantine>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or
                JsonException or
                UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task PersistJournalQuarantineBestEffortAsync(
        string directory,
        SourceEditJournalQuarantine quarantine,
        CancellationToken cancellationToken)
    {
        var path =
            Path.Combine(
                directory,
                "quarantine.json");
        var temp =
            path +
            ".tmp." +
            Guid.NewGuid().ToString("N");
        try
        {
            var bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                    quarantine,
                    ReceiptJsonOptions);
            await using (var stream =
                         new FileStream(
                             temp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options:
                                 FileOptions.Asynchronous |
                                 FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                    bytes,
                    cancellationToken);
                await stream.FlushAsync(
                    cancellationToken);
                stream.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temp,
                path,
                overwrite: true);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
        }
        finally
        {
            TryDeleteFile(
                temp);
        }
    }

    public async Task CleanupCompletedAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(receiptsRoot))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var completed = new List<CompletedEntry>();

        foreach (var receiptPath in Directory.EnumerateFiles(
                     receiptsRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceEditTransactionResult? receipt;
            try
            {
                await using var stream = File.OpenRead(receiptPath);
                receipt =
                    await JsonSerializer.DeserializeAsync<SourceEditTransactionResult>(
                        stream,
                        ReceiptJsonOptions,
                        cancellationToken);
            }
            catch (Exception ex) when (
                ex is IOException or JsonException)
            {
                continue;
            }

            if (receipt is null ||
                string.Equals(
                    receipt.Status,
                    "recovery-required",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(receiptPath);
            var retention =
                string.Equals(
                    receipt.Status,
                    "rolled-back",
                    StringComparison.OrdinalIgnoreCase)
                    ? TimeSpan.FromDays(7)
                    : TimeSpan.FromDays(30);

            if (now - info.LastWriteTimeUtc > retention)
            {
                await PersistTombstoneAsync(
                    receipt,
                    cancellationToken);
                DeleteCompletedEntry(receiptPath);
                continue;
            }

            var transactionDirectory =
                Path.Combine(
                    transactionsRoot,
                    Path.GetFileNameWithoutExtension(receiptPath));
            var size = info.Length + GetDirectorySize(transactionDirectory);
            completed.Add(
                new CompletedEntry(
                    receipt,
                    receiptPath,
                    transactionDirectory,
                    info.LastWriteTimeUtc,
                    size));
        }

        completed.Sort(
            (left, right) =>
                left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));

        var totalBytes = completed.Sum(entry => entry.Size);
        var index = 0;
        while ((completed.Count - index > MaximumCompletedTransactions ||
                totalBytes > MaximumCompletedStateBytes) &&
               index < completed.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = completed[index++];
            totalBytes -= entry.Size;
            await PersistTombstoneAsync(
                entry.Receipt,
                cancellationToken);
            TryDeleteFile(entry.ReceiptPath);
            TryDeleteDirectory(entry.TransactionDirectory);
        }
    }

    public async Task<SourceEditJournalEnumeration> EnumerateStatesAsync(
        CancellationToken cancellationToken)
    {
        var states =
            new List<SourceEditJournalState>();
        var quarantined =
            new List<SourceEditJournalQuarantine>();
        if (!Directory.Exists(
                transactionsRoot))
        {
            return new SourceEditJournalEnumeration(
                states,
                quarantined);
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     transactionsRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journalPath =
                Path.Combine(
                    directory,
                    "journal.ndjson");
            if (!File.Exists(journalPath))
            {
                continue;
            }

            var identity =
                await TryReadJournalIdentityAsync(
                    directory,
                    cancellationToken);
            var existingQuarantine =
                await TryReadJournalQuarantineAsync(
                    directory,
                    cancellationToken);
            if (existingQuarantine is not null)
            {
                if (existingQuarantine.WorkspaceRoot is null &&
                    identity is not null)
                {
                    existingQuarantine =
                        existingQuarantine with
                        {
                            TransactionId =
                                identity.TransactionId,
                            RequestHash =
                                identity.RequestHash,
                            WorkspaceRoot =
                                identity.WorkspaceRoot,
                        };
                    await PersistJournalQuarantineBestEffortAsync(
                        directory,
                        existingQuarantine,
                        cancellationToken);
                }

                quarantined.Add(
                    existingQuarantine);
                continue;
            }

            try
            {
                string? firstLine = null;
                foreach (var line in File.ReadLines(
                             journalPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(
                            line))
                    {
                        firstLine = line;
                        break;
                    }
                }

                if (firstLine is null)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Source-edit transaction journal contains no complete WAL record.",
                        journalPath);
                }

                var first =
                    DecodeLine(
                        firstLine);
                identity ??=
                    new SourceEditJournalIdentity(
                        1,
                        first.TransactionId,
                        first.RequestHash,
                        first.WorkspaceRoot);
                if (!string.Equals(
                        identity.TransactionId,
                        first.TransactionId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        identity.RequestHash,
                        first.RequestHash,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        identity.WorkspaceRoot,
                        first.WorkspaceRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Source-edit transaction identity sidecar does not match the first WAL record.",
                        journalPath);
                }

                var expectedDirectory =
                    GetTransactionDirectory(
                        first.TransactionId);
                if (!string.Equals(
                        Path.GetFullPath(
                            expectedDirectory),
                        Path.GetFullPath(
                            directory),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.TransactionRecoveryRequired,
                        "Source-edit WAL transactionId does not match its transaction directory.",
                        journalPath);
                }

                var state =
                    await ReadStateAsync(
                        first.TransactionId,
                        cancellationToken);
                if (state is not null)
                {
                    states.Add(
                        state);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is SourceEditDomainException or
                    IOException or
                    UnauthorizedAccessException or
                    JsonException or
                    FormatException or
                    CryptographicException or
                    InvalidDataException)
            {
                var quarantine =
                    new SourceEditJournalQuarantine(
                        1,
                        identity?.TransactionId,
                        identity?.RequestHash,
                        identity?.WorkspaceRoot,
                        journalPath,
                        ex.Message,
                        DateTimeOffset.UtcNow);
                await PersistJournalQuarantineBestEffortAsync(
                    directory,
                    quarantine,
                    cancellationToken);
                quarantined.Add(
                    quarantine);
            }
        }

        return new SourceEditJournalEnumeration(
            states,
            quarantined);
    }

    public string GetTransactionDirectory(string transactionId) =>
        Path.Combine(transactionsRoot, GetTransactionKey(transactionId));

    public string GetJournalPath(string transactionId) =>
        Path.Combine(
            GetTransactionDirectory(transactionId),
            "journal.ndjson");

    public string GetReceiptPath(string transactionId) =>
        Path.Combine(
            receiptsRoot,
            GetTransactionKey(transactionId) + ".json");

    public string GetTombstonePath(string transactionId) =>
        Path.Combine(
            tombstonesRoot,
            GetTransactionKey(transactionId) + ".json");

    public static bool IsTerminalEvent(string eventName) =>
        string.Equals(eventName, "Committed", StringComparison.Ordinal) ||
        string.Equals(eventName, "RolledBack", StringComparison.Ordinal) ||
        string.Equals(eventName, "RecoveryRequired", StringComparison.Ordinal);

    private async Task AppendAsync(
        string transactionId,
        string requestHash,
        string workspaceRoot,
        string eventName,
        string? dataJson,
        long sequence,
        CancellationToken cancellationToken)
    {
        var transactionDirectory =
            GetTransactionDirectory(transactionId);
        Directory.CreateDirectory(transactionDirectory);
        var journalPath = Path.Combine(
            transactionDirectory,
            "journal.ndjson");

        var payload = new SourceEditJournalPayload(
            1,
            sequence,
            DateTimeOffset.UtcNow,
            eventName,
            transactionId,
            requestHash,
            workspaceRoot,
            dataJson);
        var payloadBytes =
            JsonSerializer.SerializeToUtf8Bytes(
                payload,
                JsonOptions);
        var envelope =
            new SourceEditJournalEnvelope(
                Convert.ToBase64String(payloadBytes),
                SourceEditRevision.Format(
                    SHA256.HashData(payloadBytes)));
        var line =
            JsonSerializer.Serialize(envelope, JsonOptions) +
            Environment.NewLine;
        var lineBytes = Encoding.UTF8.GetBytes(line);

        await using var stream = new FileStream(
            journalPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous |
                     FileOptions.WriteThrough);
        await stream.WriteAsync(lineBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static SourceEditJournalPayload DecodeLine(string line)
    {
        var envelope =
            JsonSerializer.Deserialize<SourceEditJournalEnvelope>(
                line,
                JsonOptions)
            ?? throw new InvalidDataException(
                "WAL envelope is empty.");
        var payloadBytes =
            Convert.FromBase64String(envelope.Payload);
        var actual =
            SourceEditRevision.Format(
                SHA256.HashData(payloadBytes));
        if (!string.Equals(
                actual,
                envelope.Checksum,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "WAL checksum mismatch.");
        }

        return JsonSerializer.Deserialize<SourceEditJournalPayload>(
                   payloadBytes,
                   JsonOptions)
               ?? throw new InvalidDataException(
                   "WAL payload is empty.");
    }

    private static void TrackStepOwnership(
        SourceEditJournalPayload payload,
        SourceEditJournalPlan? plan,
        ISet<int> attemptedFileIndices,
        ISet<int> appliedFileIndices,
        ISet<int> rolledBackFileIndices,
        string journalPath)
    {
        var isStarting = string.Equals(
            payload.Event,
            "CommitStepStarting",
            StringComparison.Ordinal);
        var isApplied = string.Equals(
            payload.Event,
            "CommitStepApplied",
            StringComparison.Ordinal);
        var isRolledBack = string.Equals(
            payload.Event,
            "RollbackStepApplied",
            StringComparison.Ordinal);

        if (!isStarting && !isApplied && !isRolledBack)
        {
            return;
        }

        if (plan is null ||
            string.IsNullOrWhiteSpace(payload.DataJson))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit WAL step record is missing its durable transaction plan or file index.",
                journalPath);
        }

        int fileIndex;
        try
        {
            using var document =
                JsonDocument.Parse(payload.DataJson);
            if (!document.RootElement.TryGetProperty(
                    "index",
                    out var indexElement) ||
                !indexElement.TryGetInt32(out fileIndex))
            {
                throw new InvalidDataException(
                    "Source-edit WAL step record does not contain a valid file index.");
            }
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidDataException)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit WAL step record metadata is corrupt.",
                journalPath,
                innerException: ex);
        }

        if (!plan.Files.Any(file => file.Index == fileIndex))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit WAL step record references a file index outside the prepared transaction plan.",
                journalPath);
        }

        if (isStarting)
        {
            attemptedFileIndices.Add(fileIndex);
            return;
        }

        if (isApplied)
        {
            attemptedFileIndices.Add(fileIndex);
            appliedFileIndices.Add(fileIndex);
            return;
        }

        rolledBackFileIndices.Add(fileIndex);
    }

    private async Task PersistTombstoneAsync(
        SourceEditTransactionResult receipt,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tombstonesRoot);
        var tombstonePath =
            GetTombstonePath(receipt.TransactionId);
        var tempPath =
            tombstonePath + ".tmp." + Guid.NewGuid().ToString("N");
        var tombstone =
            new SourceEditTransactionTombstone(
                1,
                receipt.RequestHash ?? string.Empty,
                receipt.WorkspaceRoot ?? string.Empty,
                receipt.Status,
                DateTimeOffset.UtcNow);

        try
        {
            var bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                    tombstone,
                    JsonOptions);
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous |
                                      FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                    bytes,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                tempPath,
                tombstonePath,
                overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static async Task<bool> HasTerminatedTailAsync(
        string journalPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 1,
            options: FileOptions.Asynchronous |
                     FileOptions.RandomAccess);
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Seek(-1, SeekOrigin.End);
        var buffer = new byte[1];
        var read = await stream.ReadAsync(
            buffer,
            cancellationToken);
        return read == 1 &&
               (buffer[0] == (byte)'\n' ||
                buffer[0] == (byte)'\r');
    }

    private static string GetTransactionKey(string transactionId) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(transactionId)))
            .ToLowerInvariant();

    private void DeleteCompletedEntry(string receiptPath)
    {
        var key = Path.GetFileNameWithoutExtension(receiptPath);
        TryDeleteFile(receiptPath);
        TryDeleteDirectory(Path.Combine(transactionsRoot, key));
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(
                    path,
                    "*",
                    SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        return 0L;
                    }
                });
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDeleteFile(string path)
    {
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record CompletedEntry(
        SourceEditTransactionResult Receipt,
        string ReceiptPath,
        string TransactionDirectory,
        DateTime LastWriteTimeUtc,
        long Size);
}
