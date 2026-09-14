using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedRegistryProtocolContractTests
{
    private static readonly string[] RegistryMutationFieldNames =
    [
        "kind",
        "hive",
        "sub_key_path",
        "value_name",
        "view",
        "recursive",
        "value_type",
        "string_value",
        "dword_value",
        "qword_value",
        "multi_string_value",
        "binary_value",
    ];

    [TestMethod]
    public void ProtocolV2CarriesRegistryMutationAndRegistryResult()
    {
        var registryField = ElevatedOperationRequest.Descriptor.FindFieldByName("registry")
            ?? throw new AssertFailedException("ElevatedOperationRequest must expose the registry field.");
        Assert.AreEqual(12, registryField.FieldNumber);
        Assert.AreEqual("RegistryMutationOperation", registryField.MessageType.Name);

        var operationOneof = registryField.ContainingOneof
            ?? throw new AssertFailedException("Registry field must belong to the operation oneof.");
        Assert.AreEqual("operation", operationOneof.Name);

        foreach (var fieldName in RegistryMutationFieldNames)
        {
            Assert.IsNotNull(
                RegistryMutationOperation.Descriptor.FindFieldByName(fieldName),
                $"RegistryMutationOperation must expose proto field '{fieldName}'.");
        }

        var responseRegistryField = ElevatedOperationResponse.Descriptor.FindFieldByName("registry")
            ?? throw new AssertFailedException("ElevatedOperationResponse must expose the registry field.");
        Assert.AreEqual(6, responseRegistryField.FieldNumber);
        Assert.AreEqual("RegistryOperationResult", responseRegistryField.MessageType.Name);
    }
}
