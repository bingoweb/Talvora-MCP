namespace Talvora.Ipc.Client;

public interface IBrokerClient
{
    Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<BrokerProbeResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
