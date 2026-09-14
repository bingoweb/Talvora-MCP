using Microsoft.Win32;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;
using WinRegistry = Microsoft.Win32.Registry;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryWriteValueTests
{
    private static readonly byte[] BinaryPayload = [1, 2, 3, 255];
    private static readonly string[] MultiTextPayload = ["one", "two"];
    private static readonly byte[] NonePayload = [9, 8, 7];

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

    [TestMethod]
    public async Task WriteValueAsyncSupportsRemainingWritableTypes()
    {
        var subKeyPath = $"Software\\Talvora\\Tests\\WriteValueTypes\\{Guid.NewGuid():N}";

        using (var key = WinRegistry.CurrentUser.CreateSubKey(subKeyPath, writable: true))
        {
            Assert.IsNotNull(key);
        }

        try
        {
            var service = new WindowsRegistryService();

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "Text",
                RegistryValueType.Text,
                StringValue: "plain text"));

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "Binary",
                RegistryValueType.Binary,
                BinaryValue: BinaryPayload));

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                valueName: null,
                RegistryValueType.DWord,
                DWordValue: 5280));

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "Multi",
                RegistryValueType.MultiText,
                MultiStringValue: MultiTextPayload));

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "QWord",
                RegistryValueType.QWord,
                QWordValue: 12345678901234L));

            await service.WriteValueAsync(new RegistryValueData(
                RegistryHiveId.CurrentUser,
                subKeyPath,
                "None",
                RegistryValueType.None,
                BinaryValue: NonePayload));

            using var key = WinRegistry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            Assert.IsNotNull(key);

            Assert.AreEqual(RegistryValueKind.String, key.GetValueKind("Text"));
            Assert.AreEqual("plain text", key.GetValue("Text"));

            Assert.AreEqual(RegistryValueKind.Binary, key.GetValueKind("Binary"));
            CollectionAssert.AreEqual(BinaryPayload, (byte[])key.GetValue("Binary")!);

            Assert.AreEqual(RegistryValueKind.DWord, key.GetValueKind(valueName: null));
            Assert.AreEqual(5280, key.GetValue(valueName: null));

            Assert.AreEqual(RegistryValueKind.MultiString, key.GetValueKind("Multi"));
            CollectionAssert.AreEqual(MultiTextPayload, (string[])key.GetValue("Multi")!);

            Assert.AreEqual(RegistryValueKind.QWord, key.GetValueKind("QWord"));
            Assert.AreEqual(12345678901234L, key.GetValue("QWord"));

            Assert.AreEqual(RegistryValueKind.None, key.GetValueKind("None"));
            CollectionAssert.AreEqual(NonePayload, (byte[])key.GetValue("None")!);
        }
        finally
        {
            WinRegistry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }
}
