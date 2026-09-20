using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal static partial class ManagedMcpTunnelProvisioningService
{
    private const int ClientUpdateJournalSchemaVersion = 1;
    private const string ClientUpdatePhasePrepared = "prepared";
    private const string ClientUpdatePhaseCandidateConfigured =
        "candidate-configured";
    private const string ClientUpdatePhaseCommitted = "committed";
    private const string ClientUpdatePhaseRolledBack = "rolled-back";

    private sealed record TunnelClientUpdateJournal(
        int SchemaVersion,
        string RegistrationId,
        string ConfigPath,
        BusinessConfig PreviousConfig,
        BusinessConfig CandidateConfig,
        string Phase,
        DateTimeOffset UpdatedAtUtc);

    private readonly record struct ClientUpdateRecoveryResult(
        bool Handled,
        bool Updated);

    private static async Task ExecuteClientUpdateTransactionAsync(
        ManagedMcpRegistration registration,
        string configPath,
        BusinessConfig previousConfig,
        BusinessConfig candidateConfig,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var journal = new TunnelClientUpdateJournal(
            SchemaVersion: ClientUpdateJournalSchemaVersion,
            RegistrationId: registration.Id,
            ConfigPath: Path.GetFullPath(configPath),
            PreviousConfig: previousConfig,
            CandidateConfig: candidateConfig,
            Phase: ClientUpdatePhasePrepared,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await WriteClientUpdateJournalAsync(
            journal,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await DisconnectExistingAsync(
                registration,
                cancellationToken).ConfigureAwait(false);

            WriteBusinessConfig(
                configPath,
                candidateConfig);

            journal = journal with
            {
                Phase = ClientUpdatePhaseCandidateConfigured,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await WriteClientUpdateJournalAsync(
                journal,
                CancellationToken.None).ConfigureAwait(false);

            await ConnectExistingWithConfigAsync(
                registration,
                configPath,
                candidateConfig,
                cancellationToken).ConfigureAwait(false);

            journal = journal with
            {
                Phase = ClientUpdatePhaseCommitted,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await WriteClientUpdateJournalAsync(
                journal,
                CancellationToken.None).ConfigureAwait(false);

            TryDeleteClientUpdateJournal(
                configPath,
                "committed update");

            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "tunnel",
                $"{registration.DisplayName} tunnel-client güncellendi",
                $"OpenAI tunnel-client {candidateConfig.TunnelClientVersion} sürümüne readiness doğrulamasından sonra atomik olarak geçirildi.",
                registration.Id,
                $"tunnel:{registration.Id}:client-updated");
        }
        catch (Exception updateException)
        {
            var rollbackException =
                await TryRollbackClientUpdateAsync(
                    registration,
                    configPath,
                    previousConfig,
                    journal,
                    CancellationToken.None).ConfigureAwait(false);

            if (rollbackException is null)
            {
                throw new InvalidOperationException(
                    "tunnel-client update readiness failed; the previous client configuration and runtime were restored.",
                    updateException);
            }

            throw new AggregateException(
                "tunnel-client update failed and automatic rollback could not restore the previous runtime. The durable recovery journal was retained.",
                updateException,
                rollbackException);
        }
    }

    private static async Task<ClientUpdateRecoveryResult>
        RecoverInterruptedClientUpdateAsync(
            ManagedMcpRegistration registration,
            CancellationToken cancellationToken)
    {
        var configPath = ResolveConfigPath(registration);
        var journalPath =
            GetClientUpdateJournalPath(configPath);
        if (!File.Exists(journalPath))
        {
            return default;
        }

        TunnelClientUpdateJournal journal;
        try
        {
            journal =
                await JsonFileStore.ReadAsync<TunnelClientUpdateJournal>(
                    journalPath,
                    ConfigJsonOptions,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            InvalidDataException)
        {
            throw new InvalidOperationException(
                $"tunnel-client update recovery journal could not be read: {journalPath}",
                ex);
        }

        ValidateClientUpdateJournal(
            registration,
            configPath,
            journal);

        if (string.Equals(
                journal.Phase,
                ClientUpdatePhaseCommitted,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                journal.Phase,
                ClientUpdatePhaseRolledBack,
                StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteClientUpdateJournal(
                configPath,
                journal.Phase);
            return default;
        }

        BusinessConfig? currentConfig = null;
        if (File.Exists(configPath))
        {
            currentConfig = LoadBusinessConfig(configPath);
        }

        if (currentConfig == journal.CandidateConfig)
        {
            try
            {
                await ConnectExistingWithConfigAsync(
                    registration,
                    configPath,
                    journal.CandidateConfig,
                    cancellationToken).ConfigureAwait(false);

                journal = journal with
                {
                    Phase = ClientUpdatePhaseCommitted,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                await WriteClientUpdateJournalAsync(
                    journal,
                    CancellationToken.None).ConfigureAwait(false);
                TryDeleteClientUpdateJournal(
                    configPath,
                    "recovered candidate");

                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "tunnel",
                    $"{registration.DisplayName} tunnel-client update recovered",
                    $"Interrupted update recovered with {journal.CandidateConfig.TunnelClientVersion}; runtime readiness was verified before commit.",
                    registration.Id,
                    $"tunnel:{registration.Id}:client-update-recovered");

                return new ClientUpdateRecoveryResult(
                    Handled: true,
                    Updated: true);
            }
            catch (Exception candidateException)
            {
                var rollbackException =
                    await TryRollbackClientUpdateAsync(
                        registration,
                        configPath,
                        journal.PreviousConfig,
                        journal,
                        CancellationToken.None).ConfigureAwait(false);

                if (rollbackException is not null)
                {
                    throw new AggregateException(
                        "Interrupted tunnel-client update candidate was not ready and recovery rollback failed. The durable recovery journal was retained.",
                        candidateException,
                        rollbackException);
                }

                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Warning,
                    "tunnel",
                    $"{registration.DisplayName} tunnel-client update geri alındı",
                    $"Interrupted candidate {journal.CandidateConfig.TunnelClientVersion} readiness doğrulamasını geçemedi; previous client {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.",
                    registration.Id,
                    $"tunnel:{registration.Id}:client-update-rolled-back");

                return new ClientUpdateRecoveryResult(
                    Handled: true,
                    Updated: false);
            }
        }

        var preparedRollbackException =
            await TryRollbackClientUpdateAsync(
                registration,
                configPath,
                journal.PreviousConfig,
                journal,
                CancellationToken.None).ConfigureAwait(false);
        if (preparedRollbackException is not null)
        {
            throw new InvalidOperationException(
                "Interrupted tunnel-client update could not restore the previous runtime. The durable recovery journal was retained.",
                preparedRollbackException);
        }

        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Warning,
            "tunnel",
            $"{registration.DisplayName} tunnel-client update toparlandı",
            $"Incomplete update journal was rolled back to {journal.PreviousConfig.TunnelClientVersion}.",
            registration.Id,
            $"tunnel:{registration.Id}:client-update-recovered-rollback");

        return new ClientUpdateRecoveryResult(
            Handled: true,
            Updated: false);
    }

    private static async Task<Exception?> TryRollbackClientUpdateAsync(
        ManagedMcpRegistration registration,
        string configPath,
        BusinessConfig previousConfig,
        TunnelClientUpdateJournal journal,
        CancellationToken cancellationToken)
    {
        try
        {
            WriteBusinessConfig(
                configPath,
                previousConfig);

            try
            {
                await DisconnectExistingAsync(
                    registration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception stopException)
            {
                TrayLog.Write(
                    $"tunnel-client update rollback stop was not clean. MCP={registration.Id}",
                    stopException);
            }

            await ConnectExistingWithConfigAsync(
                registration,
                configPath,
                previousConfig,
                cancellationToken).ConfigureAwait(false);

            var rolledBack = journal with
            {
                Phase = ClientUpdatePhaseRolledBack,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await WriteClientUpdateJournalAsync(
                rolledBack,
                CancellationToken.None).ConfigureAwait(false);
            TryDeleteClientUpdateJournal(
                configPath,
                "rollback");

            return null;
        }
        catch (Exception rollbackException)
        {
            TrayLog.Write(
                $"tunnel-client update rollback failed. MCP={registration.Id}; Journal={GetClientUpdateJournalPath(configPath)}",
                rollbackException);
            return rollbackException;
        }
    }

    private static Task WriteClientUpdateJournalAsync(
        TunnelClientUpdateJournal journal,
        CancellationToken cancellationToken) =>
        JsonFileStore.WriteAsync(
            GetClientUpdateJournalPath(
                journal.ConfigPath),
            journal,
            ConfigJsonOptions,
            createBackup: false,
            cancellationToken);

    private static string GetClientUpdateJournalPath(
        string configPath) =>
        Path.GetFullPath(configPath) +
        ".client-update.pending.json";

    private static void TryDeleteClientUpdateJournal(
        string configPath,
        string reason)
    {
        var journalPath =
            GetClientUpdateJournalPath(configPath);
        try
        {
            File.Delete(journalPath);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                $"tunnel-client update journal cleanup deferred. Reason={reason}; Path={journalPath}",
                ex);
        }
    }

    private static void ValidateClientUpdateJournal(
        ManagedMcpRegistration registration,
        string configPath,
        TunnelClientUpdateJournal journal)
    {
        if (journal.SchemaVersion !=
                ClientUpdateJournalSchemaVersion ||
            !string.Equals(
                journal.RegistrationId,
                registration.Id,
                StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetFullPath(journal.ConfigPath),
                Path.GetFullPath(configPath),
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(journal.Phase) ||
            string.IsNullOrWhiteSpace(
                journal.PreviousConfig.TunnelClient) ||
            string.IsNullOrWhiteSpace(
                journal.CandidateConfig.TunnelClient))
        {
            throw new InvalidDataException(
                $"Invalid tunnel-client update recovery journal: {GetClientUpdateJournalPath(configPath)}");
        }
    }
}
