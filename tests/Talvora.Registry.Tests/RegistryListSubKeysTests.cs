using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WindowsRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryListSubKeysTests
{
    private static readonly string[] ExpectedNames = ["Beta", "Zulu", "alpha"];

    [TestMethod]
    public async Task ListSubKeyNamesReturnsOrdinalSortedNames()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\ListSubKeys\\{Guid.NewGuid():N}";

        try
        {
            using (var parent = WindowsRegistry.CurrentUser.CreateSubKey(subKeyPath))
            {
                Assert.IsNotNull(parent);

                foreach (var name in new[] { "Zulu", "alpha", "Beta" })
                {
                    using var child = parent.CreateSubKey(name);
                    Assert.IsNotNull(child);
                }
            }

            var interfaceMethod = typeof(IRegistryService).GetMethod(
                "ListSubKeyNamesAsync",
                [typeof(RegistryHiveId), typeof(string), typeof(RegistryViewId), typeof(CancellationToken)]);

            Assert.IsNotNull(
                interfaceMethod,
                "IRegistryService must expose ListSubKeyNamesAsync with hive, path, view, and cancellation token.");

            var service = new WindowsRegistryService();
            var pending = interfaceMethod.Invoke(
                service,
                [RegistryHiveId.CurrentUser, subKeyPath, RegistryViewId.Default, CancellationToken.None]);

            var pendingNames = Assert.IsInstanceOfType<ValueTask<IReadOnlyList<string>>>(pending);
            var names = await pendingNames.ConfigureAwait(false);

            CollectionAssert.AreEqual(ExpectedNames, names.ToArray());
        }
        finally
        {
            WindowsRegistry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }
}
