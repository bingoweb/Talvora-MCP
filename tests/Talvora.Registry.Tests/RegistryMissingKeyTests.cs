using Talvora.Adapter.Mcp;
using Talvora.Core;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryMissingKeyTests
{
    [TestMethod]
    public async Task ReadRegistryValueReturnsNotFoundWhenKeyDoesNotExist()
    {
        var tools = new RegistryTools(
            new WindowsRegistryService(),
            new OperationExecutor(new DefaultErrorMapper()));
        var missingSubKey = $"Software\\Talvora\\Tests\\Missing\\{Guid.NewGuid():N}";

        var result = await tools.ReadRegistryValue(
            RegistryHiveId.CurrentUser,
            missingSubKey,
            valueName: "AnyValue",
            RegistryViewId.Default,
            expandEnvironmentStrings: false,
            CancellationToken.None);

        Assert.IsFalse(result.Ok);
        Assert.IsNull(result.Data);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual("not_found", result.Error.Code);
        Assert.AreEqual("registry.read", result.Error.Operation);
    }
}
