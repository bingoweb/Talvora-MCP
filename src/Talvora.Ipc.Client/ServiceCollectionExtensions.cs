using Microsoft.Extensions.DependencyInjection;

namespace Talvora.Ipc.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTalvoraBrokerClient(this IServiceCollection services, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        services.AddSingleton<IBrokerClient>(_ => new BrokerClient(pipeName));
        return services;
    }
}
