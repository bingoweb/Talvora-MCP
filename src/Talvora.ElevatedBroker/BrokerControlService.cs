using Grpc.Core;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.ElevatedBroker;

public sealed class BrokerControlService : BrokerControl.BrokerControlBase
{
    public override Task<BrokerHealthResponse> Health(BrokerHealthRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(BrokerHealthEvaluator.Evaluate(request.ProtocolVersion));
    }
}
