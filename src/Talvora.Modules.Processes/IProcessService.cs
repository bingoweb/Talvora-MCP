namespace Talvora.Modules.Processes;

public interface IProcessService
{
    ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ProcessStartResult> StartAsync(
        StartProcessRequest request,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(
        int processId,
        bool entireProcessTree = true,
        CancellationToken cancellationToken = default);
}
