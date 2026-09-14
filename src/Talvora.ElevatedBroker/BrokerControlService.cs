using Grpc.Core;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.ElevatedBroker;

public sealed class BrokerControlService(ElevatedOperationExecutor operationExecutor)
    : BrokerControl.BrokerControlBase
{
    public override Task<BrokerHealthResponse> Health(BrokerHealthRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(BrokerHealthEvaluator.Evaluate(request.ProtocolVersion));
    }

    public override Task<ElevatedOperationResponse> Execute(
        ElevatedOperationRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return operationExecutor.ExecuteAsync(request, context.CancellationToken);
    }
}
