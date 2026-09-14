namespace Talvora.Modules.Registry;

public enum RegistryHiveId
{
    ClassesRoot,
    CurrentUser,
    LocalMachine,
    Users,
    CurrentConfig,
}

public enum RegistryViewId
{
    Default,
    Registry32,
    Registry64,
}

public enum RegistryValueType
{
    Unknown,
    None,
    String,
    ExpandString,
    Binary,
    DWord,
    MultiString,
    QWord,
}

public sealed record RegistryValueData(
    RegistryHiveId Hive,
    string SubKeyPath,
    string? ValueName,
    RegistryValueType Type,
    string? StringValue = null,
    int? DWordValue = null,
    long? QWordValue = null,
    IReadOnlyList<string>? MultiStringValue = null,
    byte[]? BinaryValue = null);
