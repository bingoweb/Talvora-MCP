using Talvora.Abstractions;

namespace Talvora.Ipc.Client;

public interface IBrokerClient
{
    Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<BrokerProbeResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<TalvoraResult<BrokerExecutionResult>> ExecuteShellAsync(
        BrokerShellExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<TalvoraResult<BrokerExecutionResult>> ExecuteProcessAsync(
        BrokerProcessExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<TalvoraResult<BrokerRegistryMutationResult>> ExecuteRegistryAsync(
        BrokerRegistryMutationRequest request,
        CancellationToken cancellationToken = default);
}
