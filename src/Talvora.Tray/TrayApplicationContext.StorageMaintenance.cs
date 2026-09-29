using Talvora.Shared;

namespace Talvora.Tray;

internal sealed partial class TrayApplicationContext
{
    private const int StorageMaintenanceIntervalMilliseconds =
        6 * 60 * 60 * 1000;

    private System.Windows.Forms.Timer? _storageMaintenanceTimer;

    private void InitializeStorageMaintenanceTimer()
    {
        _storageMaintenanceTimer =
            new System.Windows.Forms.Timer
            {
                Interval =
                    StorageMaintenanceIntervalMilliseconds,
                Enabled = false,
            };
        _storageMaintenanceTimer.Tick +=
            async (_, _) =>
                await RunStorageMaintenanceAsync(
                    GetStorageMaintenanceRegistrations(),
                    "periodic");
    }

    private void StartStorageMaintenanceTimer() =>
        _storageMaintenanceTimer?.Start();

    private void StopStorageMaintenanceTimer() =>
        _storageMaintenanceTimer?.Stop();

    private void DisposeStorageMaintenanceTimer()
    {
        var timer = Interlocked.Exchange(
            ref _storageMaintenanceTimer,
            null);
        if (timer is null)
        {
            return;
        }

        timer.Stop();
        timer.Dispose();
    }

    private IReadOnlyList<ManagedMcpRegistration>
        GetStorageMaintenanceRegistrations()
    {
        var registrations =
            new Dictionary<string, ManagedMcpRegistration>(
                StringComparer.OrdinalIgnoreCase);

        if (_talvoraRegistration is not null)
        {
            registrations[_talvoraRegistration.Id] =
                _talvoraRegistration;
        }

        if (_giteaRegistration is not null)
        {
            registrations[_giteaRegistration.Id] =
                _giteaRegistration;
        }

        foreach (var registration in _genericRegistrations)
        {
            registrations[registration.Id] =
                registration;
        }

        return registrations.Values.ToArray();
    }

    private async Task RunStorageMaintenanceAsync(
        IReadOnlyList<ManagedMcpRegistration> registrations,
        string trigger)
    {
        try
        {
            var result =
                await TalvoraStorageMaintenanceService.RunAsync(
                    registrations,
                    _lifetimeCts.Token);

            if (result.DeletedEntries == 0 &&
                result.RotatedTunnelLogs == 0)
            {
                return;
            }

            var reclaimedMiB =
                result.ReclaimedBytes /
                (1024d * 1024d);
            var detail =
                $"Tetikleyici={trigger}; " +
                $"Silinen={result.DeletedEntries}; " +
                $"Kazanılan={reclaimedMiB:0.##} MiB; " +
                $"Döndürülen tünel logu={result.RotatedTunnelLogs}.";

            TrayLog.Write(
                "Storage maintenance completed. " +
                detail);

            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "maintenance",
                "Talvora disk bakımı tamamlandı",
                detail,
                dedupKey:
                    "maintenance:storage:completed");
        }
        catch (OperationCanceledException)
            when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException ex)
        {
            TrayLog.Write(
                $"Storage maintenance timed out and was deferred. Trigger={trigger}",
                ex);
        }
        catch (Exception ex)
        {
            // This method is invoked by a WinForms Timer async event. No
            // maintenance-only fault is allowed to escape the async-void
            // event boundary and terminate the tray process.
            TrayLog.Write(
                $"Storage maintenance failed. Trigger={trigger}",
                ex);
        }
    }
}
