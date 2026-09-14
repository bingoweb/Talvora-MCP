using Talvora.Abstractions;
using Talvora.Ipc.Client;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerRegistryClientContractTests
{
    private static readonly string[] MutationKindNames =
    [
        "Unknown",
        "WriteValue",
        "DeleteValue",
        "CreateKey",
        "DeleteKey",
    ];

    private static readonly string[] HiveNames =
    [
        "ClassesRoot",
        "CurrentUser",
        "LocalMachine",
        "Users",
        "CurrentConfig",
    ];

    private static readonly string[] ViewNames =
    [
        "Default",
        "Registry32",
        "Registry64",
    ];

    private static readonly string[] ValueTypeNames =
    [
        "Unknown",
        "None",
        "Text",
        "ExpandableText",
        "Binary",
        "DWord",
        "MultiText",
        "QWord",
    ];

    [TestMethod]
    public void BrokerClientExposesTransportNeutralRegistryMutationContract()
    {
        var assembly = typeof(IBrokerClient).Assembly;
        var requestType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryMutationRequest");
        var resultType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryMutationResult");
        var mutationKindType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryMutationKind");
        var hiveType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryHive");
        var viewType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryView");
        var valueType = GetRequiredType(assembly, "Talvora.Ipc.Client.BrokerRegistryValueType");

        CollectionAssert.AreEqual(MutationKindNames, Enum.GetNames(mutationKindType));
        CollectionAssert.AreEqual(HiveNames, Enum.GetNames(hiveType));
        CollectionAssert.AreEqual(ViewNames, Enum.GetNames(viewType));
        CollectionAssert.AreEqual(ValueTypeNames, Enum.GetNames(valueType));

        AssertProperty(requestType, "Kind", mutationKindType);
        AssertProperty(requestType, "Hive", hiveType);
        AssertProperty(requestType, "SubKeyPath", typeof(string));
        AssertProperty(requestType, "ValueName", typeof(string));
        AssertProperty(requestType, "View", viewType);
        AssertProperty(requestType, "Recursive", typeof(bool));
        AssertProperty(requestType, "ValueType", valueType);
        AssertProperty(requestType, "StringValue", typeof(string));
        AssertProperty(requestType, "DWordValue", typeof(int?));
        AssertProperty(requestType, "QWordValue", typeof(long?));
        AssertProperty(requestType, "MultiStringValue", typeof(IReadOnlyList<string>));
        AssertProperty(requestType, "BinaryValue", typeof(byte[]));
        AssertProperty(requestType, "Timeout", typeof(TimeSpan?));
        AssertProperty(requestType, "OperationId", typeof(string));

        AssertProperty(resultType, "OperationId", typeof(string));
        AssertProperty(resultType, "Completed", typeof(bool));

        var executeMethod = typeof(IBrokerClient).GetMethod(
            "ExecuteRegistryAsync",
            [requestType, typeof(CancellationToken)]);
        Assert.IsNotNull(executeMethod, "IBrokerClient must expose ExecuteRegistryAsync.");

        var expectedResultEnvelope = typeof(TalvoraResult<>).MakeGenericType(resultType);
        var expectedReturnType = typeof(Task<>).MakeGenericType(expectedResultEnvelope);
        Assert.AreEqual(expectedReturnType, executeMethod.ReturnType);
    }

    private static Type GetRequiredType(System.Reflection.Assembly assembly, string fullName) =>
        assembly.GetType(fullName)
        ?? throw new AssertFailedException($"Required transport-neutral broker type '{fullName}' was not found.");

    private static void AssertProperty(Type declaringType, string propertyName, Type expectedType)
    {
        var property = declaringType.GetProperty(propertyName)
            ?? throw new AssertFailedException($"{declaringType.Name} must expose property '{propertyName}'.");
        Assert.AreEqual(expectedType, property.PropertyType, $"Unexpected type for {declaringType.Name}.{propertyName}.");
    }
}
