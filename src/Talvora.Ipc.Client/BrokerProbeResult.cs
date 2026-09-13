namespace Talvora.Ipc.Client;

public sealed record BrokerProbeResult(
    bool Connected,
    BrokerHealthSnapshot? Health,
    string? Error)
{
    public static BrokerProbeResult Success(BrokerHealthSnapshot health)
    {
        ArgumentNullException.ThrowIfNull(health);
        return new BrokerProbeResult(true, health, Error: null);
    }

    public static BrokerProbeResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new BrokerProbeResult(false, Health: null, error);
    }
}
