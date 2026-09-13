using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Talvora.ElevatedBroker;
using Talvora.Ipc.Contracts;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Talvora Elevated Broker";
});
builder.Services.AddGrpc();
builder.Services.AddSingleton<ElevatedOperationExecutor>();

var pipeName = builder.Configuration[BrokerProtocol.PipeNameConfigurationKey]
    ?? BrokerProtocol.DefaultPipeName;
var allowedUserSid = BrokerPipeFactory.ResolveAllowedUserSid(
    builder.Configuration[BrokerProtocol.AllowedUserSidConfigurationKey]);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenNamedPipe(pipeName, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.WebHost.UseNamedPipes(options =>
{
    // Talvora.Host is intentionally non-elevated while this broker can be elevated.
    // CurrentUserOnly also checks elevation level, so the broker uses an explicit
    // SID ACL instead of that built-in mode to preserve cross-elevation IPC.
    options.CurrentUserOnly = false;
    options.CreateNamedPipeServerStream = context => BrokerPipeFactory.Create(context, allowedUserSid);
});

var app = builder.Build();
app.MapGrpcService<BrokerControlService>();

await app.RunAsync();
