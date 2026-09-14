using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Talvora.Modules.Registry;
using Talvora.Platform.Windows;

namespace Talvora.Registry.Tests;

[TestClass]
public sealed class RegistryDependencyInjectionTests
{
    [TestMethod]
    public void AddWindowsRegistryRegistersWindowsRegistryServiceAsSingleton()
    {
        var extensionType = typeof(WindowsPlatformInfoProvider).Assembly.GetType(
            "Talvora.Platform.Windows.WindowsRegistryServiceCollectionExtensions",
            throwOnError: false);

        Assert.IsNotNull(
            extensionType,
            "Talvora.Platform.Windows must expose WindowsRegistryServiceCollectionExtensions.");

        var addWindowsRegistry = extensionType.GetMethod(
            "AddWindowsRegistry",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(IServiceCollection)],
            modifiers: null);

        Assert.IsNotNull(addWindowsRegistry, "AddWindowsRegistry(IServiceCollection) must be public and static.");

        var services = new ServiceCollection();
        var result = addWindowsRegistry.Invoke(null, [services]);

        Assert.AreSame(services, result);

        var descriptor = services.Single(service => service.ServiceType == typeof(IRegistryService));
        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.AreEqual("WindowsRegistryService", descriptor.ImplementationType?.Name);
    }
}
