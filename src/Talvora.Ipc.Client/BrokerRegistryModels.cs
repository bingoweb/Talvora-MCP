namespace Talvora.Ipc.Client;

public enum BrokerRegistryMutationKind
{
    Unknown,
    WriteValue,
    DeleteValue,
    CreateKey,
    DeleteKey,
}

public enum BrokerRegistryHive
{
    ClassesRoot,
    CurrentUser,
    LocalMachine,
    Users,
    CurrentConfig,
}

public enum BrokerRegistryView
{
    Default,
    Registry32,
    Registry64,
}

public enum BrokerRegistryValueType
{
    Unknown,
    None,
    Text,
    ExpandableText,
    Binary,
    DWord,
    MultiText,
    QWord,
}

public sealed record BrokerRegistryMutationRequest(
    BrokerRegistryMutationKind Kind,
    BrokerRegistryHive Hive,
    string SubKeyPath,
    string? ValueName = null,
    BrokerRegistryView View = BrokerRegistryView.Default,
    bool Recursive = false,
    BrokerRegistryValueType ValueType = BrokerRegistryValueType.Unknown,
    string? StringValue = null,
    int? DWordValue = null,
    long? QWordValue = null,
    IReadOnlyList<string>? MultiStringValue = null,
    byte[]? BinaryValue = null,
    TimeSpan? Timeout = null,
    string? OperationId = null);

public sealed record BrokerRegistryMutationResult(
    string OperationId,
    bool Completed);
