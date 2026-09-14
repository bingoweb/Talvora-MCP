using Microsoft.Win32;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WinRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryReadTests
{
    [TestMethod]
    public async Task ReadValueAsyncReadsExpandStringWithoutExpandingByDefault()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\{Guid.NewGuid():N}";

        using (var key = WinRegistry.CurrentUser.CreateSubKey(subKeyPath, writable: true))
        {
            Assert.IsNotNull(key);
            key.SetValue("Greeting", "hello %USERNAME%", RegistryValueKind.ExpandString);
        }

        try
        {
            var implementationType = typeof(WindowsPlatformInfoProvider).Assembly
                .GetType("Talvora.Platform.Windows.WindowsRegistryService", throwOnError: false);

            Assert.IsNotNull(
                implementationType,
                "Talvora.Platform.Windows must expose WindowsRegistryService for registry operations.");
            Assert.IsTrue(typeof(IRegistryService).IsAssignableFrom(implementationType));

            var service = (IRegistryService)Activator.CreateInstance(implementationType)!;
            var value = await service.ReadValueAsync(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "Greeting",
                RegistryViewId.Default,
                expandEnvironmentStrings: false,
                CancellationToken.None);

            Assert.AreEqual(RegistryValueType.ExpandableText, value.Type);
            Assert.AreEqual("hello %USERNAME%", value.StringValue);
            Assert.AreEqual(subKeyPath, value.SubKeyPath);
            Assert.AreEqual("Greeting", value.ValueName);
        }
        finally
        {
            WinRegistry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }
}
