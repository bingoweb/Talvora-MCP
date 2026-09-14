using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WinRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryKeyManagementTests
{
    [TestMethod]
    public async Task CreateAndDeleteKeySupportsNestedKeys()
    {
        var basePath = $"Software\\Talvora\\Tests\\KeyManagement\\{Guid.NewGuid():N}";
        var nestedPath = $"{basePath}\\Parent\\Child";

        try
        {
            var createMethod = typeof(IRegistryService).GetMethod(
                "CreateKeyAsync",
                [typeof(RegistryHiveId), typeof(string), typeof(RegistryViewId), typeof(CancellationToken)]);
            Assert.IsNotNull(
                createMethod,
                "IRegistryService must expose CreateKeyAsync with hive, path, view, and cancellation token.");

            var deleteMethod = typeof(IRegistryService).GetMethod(
                "DeleteKeyAsync",
                [typeof(RegistryHiveId), typeof(string), typeof(bool), typeof(RegistryViewId), typeof(CancellationToken)]);
            Assert.IsNotNull(
                deleteMethod,
                "IRegistryService must expose DeleteKeyAsync with hive, path, recursive flag, view, and cancellation token.");

            var service = new WindowsRegistryService();

            var pendingCreate = createMethod.Invoke(
                service,
                [RegistryHiveId.CurrentUser, nestedPath, RegistryViewId.Default, CancellationToken.None]);
            var create = Assert.IsInstanceOfType<ValueTask>(pendingCreate);
            await create.ConfigureAwait(false);

            using (var created = WinRegistry.CurrentUser.OpenSubKey(nestedPath, writable: false))
            {
                Assert.IsNotNull(created);
            }

            var pendingDelete = deleteMethod.Invoke(
                service,
                [RegistryHiveId.CurrentUser, basePath, true, RegistryViewId.Default, CancellationToken.None]);
            var delete = Assert.IsInstanceOfType<ValueTask>(pendingDelete);
            await delete.ConfigureAwait(false);

            using var deleted = WinRegistry.CurrentUser.OpenSubKey(basePath, writable: false);
            Assert.IsNull(deleted);
        }
        finally
        {
            WinRegistry.CurrentUser.DeleteSubKeyTree(basePath, throwOnMissingSubKey: false);
        }
    }
}
