using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedRegistryProtocolContractTests
{
    [TestMethod]
    public void BrokerProtocolCarriesTypedRegistryMutationWithoutVersionBump()
    {
        Assert.AreEqual(2, BrokerProtocol.CurrentVersion);
        Assert.AreEqual("Talvora.ElevatedBroker.v2", BrokerProtocol.DefaultPipeName);

        var requestType = typeof(ElevatedOperationRequest);
        var registryProperty = requestType.GetProperty("Registry");
        Assert.IsNotNull(registryProperty, "ElevatedOperationRequest must expose a Registry oneof member.");

        var operationCases = Enum.GetNames(typeof(ElevatedOperationRequest.OperationOneofCase));
        CollectionAssert.Contains(operationCases, "Registry");

        var contractAssembly = requestType.Assembly;
        var registryOperationType = contractAssembly.GetType(
            "Talvora.Ipc.Contracts.Grpc.RegistryMutationOperation",
            throwOnError: false);
        Assert.IsNotNull(registryOperationType, "Broker protocol must define RegistryMutationOperation.");

        foreach (var propertyName in new[]
        {
            "Kind",
            "Hive",
            "SubKeyPath",
            "ValueName",
            "View",
            "Recursive",
            "ValueType",
            "StringValue",
            "DwordValue",
            "QwordValue",
            "MultiStringValue",
            "BinaryValue",
        })
        {
            Assert.IsNotNull(
                registryOperationType.GetProperty(propertyName),
                $"RegistryMutationOperation must expose {propertyName}.");
        }

        var responseRegistryProperty = typeof(ElevatedOperationResponse).GetProperty("Registry");
        Assert.IsNotNull(
            responseRegistryProperty,
            "ElevatedOperationResponse must expose a Registry completion result without replacing the existing execution result.");
    }
}
