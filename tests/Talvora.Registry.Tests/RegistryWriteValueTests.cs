using Microsoft.Win32;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WinRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryWriteValueTests
{
    [TestMethod]
    public async Task WriteValueAsyncUpdatesExpandableStringWithoutExpanding()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\WriteValue\\{Guid.NewGuid():N}";

        using (var key = WinRegistry.CurrentUser.CreateSubKey(subKeyPath, writable: true))
        {
            Assert.IsNotNull(key);
            key.SetValue("Greeting", "old value", RegistryValueKind.String);
        }

        try
        {
            var value = new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "Greeting",
                RegistryValueType.ExpandableText,
                StringValue: "hello %USERNAME%");

            var interfaceMethod = typeof(IRegistryService).GetMethod(
                "WriteValueAsync",
                [typeof(RegistryValueData), typeof(RegistryViewId), typeof(CancellationToken)]);

            Assert.IsNotNull(
                interfaceMethod,
                "IRegistryService must expose WriteValueAsync with value data, view, and cancellation token.");

            var service = new WindowsRegistryService();
            var pending = interfaceMethod.Invoke(
                service,
                [value, RegistryViewId.Default, CancellationToken.None]);

            var pendingWrite = Assert.IsInstanceOfType<ValueTask>(pending);
            await pendingWrite.ConfigureAwait(false);

            using var updatedKey = WinRegistry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            Assert.IsNotNull(updatedKey);
            Assert.AreEqual(RegistryValueKind.ExpandString, updatedKey.GetValueKind("Greeting"));

            var rawValue = updatedKey.GetValue(
                "Greeting",
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);

            Assert.AreEqual("hello %USERNAME%", rawValue);
        }
        finally
        {
            WinRegistry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }
}
