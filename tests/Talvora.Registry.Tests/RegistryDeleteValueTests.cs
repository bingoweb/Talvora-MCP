using Microsoft.Win32;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WinRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryDeleteValueTests
{
    [TestMethod]
    public async Task DeleteValueAsyncDeletesNamedAndDefaultValues()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\DeleteValue\\{Guid.NewGuid():N}";

        using (var key = WinRegistry.CurrentUser.CreateSubKey(subKeyPath, writable: true))
        {
            Assert.IsNotNull(key);
            key.SetValue("Greeting", "hello", RegistryValueKind.String);
            key.SetValue(null, 5280, RegistryValueKind.DWord);
        }

        try
        {
            var interfaceMethod = typeof(IRegistryService).GetMethod(
                "DeleteValueAsync",
                [
                    typeof(RegistryHiveId),
                    typeof(string),
                    typeof(string),
                    typeof(RegistryViewId),
                    typeof(CancellationToken),
                ]);

            Assert.IsNotNull(
                interfaceMethod,
                "IRegistryService must expose DeleteValueAsync with hive, path, value name, view, and cancellation token.");

            var service = new WindowsRegistryService();

            var pendingNamed = interfaceMethod.Invoke(
                service,
                [
                    RegistryHiveId.CurrentUser,
                    subKeyPath,
                    "Greeting",
                    RegistryViewId.Default,
                    CancellationToken.None,
                ]);
            await Assert.IsInstanceOfType<ValueTask>(pendingNamed).ConfigureAwait(false);

            var pendingDefault = interfaceMethod.Invoke(
                service,
                [
                    RegistryHiveId.CurrentUser,
                    subKeyPath,
                    null,
                    RegistryViewId.Default,
                    CancellationToken.None,
                ]);
            await Assert.IsInstanceOfType<ValueTask>(pendingDefault).ConfigureAwait(false);

            using var key = WinRegistry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            Assert.IsNotNull(key);
            var valueNames = key.GetValueNames();
            CollectionAssert.DoesNotContain(valueNames, "Greeting");
            CollectionAssert.DoesNotContain(valueNames, string.Empty);
        }
        finally
        {
            WinRegistry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }
}
