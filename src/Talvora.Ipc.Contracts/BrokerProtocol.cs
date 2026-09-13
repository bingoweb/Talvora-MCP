namespace Talvora.Ipc.Contracts;

public static class BrokerProtocol
{
    public const int CurrentVersion = 2;
    public const string DefaultPipeName = "Talvora.ElevatedBroker.v2";
    public const string PipeNameConfigurationKey = "Broker:PipeName";
    public const string AllowedUserSidConfigurationKey = "Broker:AllowedUserSid";
}
