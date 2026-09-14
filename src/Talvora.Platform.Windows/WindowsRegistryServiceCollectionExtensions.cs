using Microsoft.Extensions.DependencyInjection;
using Talvora.Modules.Registry;

namespace Talvora.Platform.Windows;

public static class WindowsRegistryServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRegistryService, WindowsRegistryService>();
        return services;
    }
}
