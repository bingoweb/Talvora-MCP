namespace Talvora.Ipc.Client;

public sealed record BrokerHealthSnapshot(
    int ProtocolVersion,
    bool Ready,
    string ServiceVersion,
    bool IsElevated,
    int ProcessId);
