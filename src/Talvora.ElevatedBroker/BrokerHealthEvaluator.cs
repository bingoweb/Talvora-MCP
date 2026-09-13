using System.Security.Principal;
using Grpc.Core;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.ElevatedBroker;

internal static class BrokerHealthEvaluator
{
    public static BrokerHealthResponse Evaluate(int requestedProtocolVersion)
    {
        if (requestedProtocolVersion != BrokerProtocol.CurrentVersion)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Unsupported broker protocol version {requestedProtocolVersion}. Expected {BrokerProtocol.CurrentVersion}."));
        }

        return new BrokerHealthResponse
        {
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Ready = true,
            ServiceVersion = typeof(BrokerHealthEvaluator).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            IsElevated = IsProcessElevated(),
            ProcessId = Environment.ProcessId,
        };
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
