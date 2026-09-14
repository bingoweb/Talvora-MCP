using Microsoft.Win32;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;
using Talvora.Platform.Windows;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class ElevatedRegistryExecutionTests
{
    [TestMethod]
    public async Task RegistryMutationsExecuteThroughElevatedOperationExecutor()
    {
        var subKeyPath = $"Software\\Talvora.Tests\\ElevatedRegistry\\{Guid.NewGuid():N}";
        using var currentUser = RegistryKey.OpenBaseKey(
            Microsoft.Win32.RegistryHive.CurrentUser,
            Microsoft.Win32.RegistryView.Default);

        try
        {
            var executor = new Talvora.ElevatedBroker.ElevatedOperationExecutor(new WindowsRegistryService());

            var createResponse = await executor.ExecuteAsync(
                CreateRequest(
                    "registry-create-key",
                    new RegistryMutationOperation
                    {
                        Kind = RegistryMutationKind.CreateKey,
                        Hive = Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentUser,
                        SubKeyPath = subKeyPath,
                        View = Talvora.Ipc.Contracts.Grpc.RegistryView.Default,
                    }),
                CancellationToken.None);

            Assert.IsTrue(createResponse.Success, createResponse.Error?.Message);
            Assert.IsNotNull(createResponse.Registry);
            Assert.IsTrue(createResponse.Registry.Completed);
            using (var createdKey = currentUser.OpenSubKey(subKeyPath, writable: false))
            {
                Assert.IsNotNull(createdKey, "Broker Registry create-key mutation must create the requested key.");
            }

            var writeResponse = await executor.ExecuteAsync(
                CreateRequest(
                    "registry-write-value",
                    new RegistryMutationOperation
                    {
                        Kind = RegistryMutationKind.WriteValue,
                        Hive = Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentUser,
                        SubKeyPath = subKeyPath,
                        ValueName = "Answer",
                        View = Talvora.Ipc.Contracts.Grpc.RegistryView.Default,
                        ValueType = Talvora.Ipc.Contracts.Grpc.RegistryValueType.Dword,
                        DwordValue = 42,
                    }),
                CancellationToken.None);

            Assert.IsTrue(writeResponse.Success, writeResponse.Error?.Message);
            Assert.IsNotNull(writeResponse.Registry);
            Assert.IsTrue(writeResponse.Registry.Completed);
            using (var writtenKey = currentUser.OpenSubKey(subKeyPath, writable: false))
            {
                Assert.IsNotNull(writtenKey);
                Assert.AreEqual(42, writtenKey.GetValue("Answer"));
            }

            var deleteValueResponse = await executor.ExecuteAsync(
                CreateRequest(
                    "registry-delete-value",
                    new RegistryMutationOperation
                    {
                        Kind = RegistryMutationKind.DeleteValue,
                        Hive = Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentUser,
                        SubKeyPath = subKeyPath,
                        ValueName = "Answer",
                        View = Talvora.Ipc.Contracts.Grpc.RegistryView.Default,
                    }),
                CancellationToken.None);

            Assert.IsTrue(deleteValueResponse.Success, deleteValueResponse.Error?.Message);
            Assert.IsNotNull(deleteValueResponse.Registry);
            Assert.IsTrue(deleteValueResponse.Registry.Completed);
            using (var valueDeletedKey = currentUser.OpenSubKey(subKeyPath, writable: false))
            {
                Assert.IsNotNull(valueDeletedKey);
                Assert.IsNull(valueDeletedKey.GetValue("Answer"));
            }

            var deleteKeyResponse = await executor.ExecuteAsync(
                CreateRequest(
                    "registry-delete-key",
                    new RegistryMutationOperation
                    {
                        Kind = RegistryMutationKind.DeleteKey,
                        Hive = Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentUser,
                        SubKeyPath = subKeyPath,
                        View = Talvora.Ipc.Contracts.Grpc.RegistryView.Default,
                        Recursive = true,
                    }),
                CancellationToken.None);

            Assert.IsTrue(deleteKeyResponse.Success, deleteKeyResponse.Error?.Message);
            Assert.IsNotNull(deleteKeyResponse.Registry);
            Assert.IsTrue(deleteKeyResponse.Registry.Completed);
            using var deletedKey = currentUser.OpenSubKey(subKeyPath, writable: false);
            Assert.IsNull(deletedKey, "Broker Registry delete-key mutation must remove the requested key.");
        }
        finally
        {
            currentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }

    private static ElevatedOperationRequest CreateRequest(
        string operationId,
        RegistryMutationOperation operation) =>
        new()
        {
            OperationId = operationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Registry = operation,
        };
}
