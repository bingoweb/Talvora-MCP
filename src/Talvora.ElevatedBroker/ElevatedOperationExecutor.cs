using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.ElevatedBroker;

public sealed class ElevatedOperationExecutor
{
    public Task<ElevatedOperationResponse> ExecuteAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Elevated operation execution is not implemented yet.");
    }
}
