namespace Talvora.Ipc.Contracts;

public static class BrokerProtocol
{
    public const int CurrentVersion = 1;
    public const string DefaultPipeName = "Talvora.ElevatedBroker.v1";
    public const string PipeNameConfigurationKey = "Broker:PipeName";
    public const string AllowedUserSidConfigurationKey = "Broker:AllowedUserSid";
}
