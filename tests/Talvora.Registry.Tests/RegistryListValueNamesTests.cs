using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WindowsRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryListValueNamesTests
{
    private static readonly string[] NamedValuesToCreate = ["Zulu", "alpha", "Beta"];
    private static readonly string[] ExpectedNames = ["", "Beta", "Zulu", "alpha"];

    [TestMethod]
    public async Task ListValueNamesReturnsOrdinalSortedNamesIncludingDefaultValue()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\ListValueNames\\{Guid.NewGuid():N}";

        try
        {
            using (var key = WindowsRegistry.CurrentUser.CreateSubKey(subKeyPath))
            {
                Assert.IsNotNull(key);
                key.SetValue(string.Empty, "default");

                foreach (var name in NamedValuesToCreate)
                {
                    key.SetValue(name, name);
                }
            }

            var interfaceMethod = typeof(IRegistryService).GetMethod(
                "ListValueNamesAsync",
                [typeof(RegistryHiveId), typeof(string), typeof(RegistryViewId), typeof(CancellationToken)]);

            Assert.IsNotNull(
                interfaceMethod,
                "IRegistryService must expose ListValueNamesAsync with hive, path, view, and cancellation token.");

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
