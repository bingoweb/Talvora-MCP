using Talvora;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

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
    var runtime = TalvoraRuntimeMetadata.Load();
    return Results.Json(new
    {
        product = "Talvora",
        version = "3.0.0-dev",
        sourceCommit = runtime.SourceCommit,
        installedAtUtc = runtime.InstalledAtUtc,
        mcp = "/mcp",
        processId = Environment.ProcessId,
        user = Environment.UserName,
        sid = TalvoraRuntimeIdentity.Sid,
        isWindowsService = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService(),
    });
});

app.MapMcp("/mcp");
app.Run();