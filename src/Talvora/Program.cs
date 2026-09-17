using System.Security.Principal;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(options => options.ServiceName = "Talvora");
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7676));

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/healthz", () =>
{
    var identity = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent() : null;
    return Results.Json(new
    {
        product = "Talvora",
        version = "3.0.0-dev",
        mcp = "/mcp",
        processId = Environment.ProcessId,
        user = Environment.UserName,
        sid = identity?.User?.Value,
        isWindowsService = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService(),
    });
});

app.MapMcp("/mcp");
app.Run();
