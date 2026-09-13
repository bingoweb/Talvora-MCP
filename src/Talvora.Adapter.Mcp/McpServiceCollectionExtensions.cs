using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Talvora.Adapter.Mcp;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddTalvoraMcp(this IServiceCollection services)
    {
        services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools<FileSystemTools>()
            .WithTools<ShellTools>()
            .WithTools<ProcessTools>()
            .WithTools<SystemTools>();

        return services;
    }

    public static IEndpointRouteBuilder MapTalvoraMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp");
        return endpoints;
    }
}
