using Talvora;
using Talvora.SourceEditing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

if (SemanticWorkerHost.IsWorkerCommand(
        args))
{
    var responsePath =
        SemanticWorkerHost.GetResponsePath(
            args);
    await SemanticWorkerHost.RunAsync(
        Console.OpenStandardInput(),
        responsePath,
        CancellationToken.None);
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(options => options.ServiceName = "Talvora");
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7676));

builder.Services
    .AddMcpServer(options =>
        options.ServerInstructions =
            SourceEditRoutingContract.ServerInstructions)
    .WithHttpTransport(options =>
    {
        options.SessionMode =
            HttpServerSessionMode.Stateless;
        options.ConfigureSessionOptions = (
            httpContext,
            mcpServerOptions,
            cancellationToken) =>
        {
            McpToolSurfaceWirePolicy.Apply(
                mcpServerOptions,
                httpContext.Request.Path.Value);
            return Task.CompletedTask;
        };
    })
    .WithRequestFilters(filters =>
        filters.AddListToolsFilter(next => async (
            request,
            cancellationToken) =>
        {
            var result =
                await next(
                    request,
                    cancellationToken);
            McpToolMetadataWirePolicy.Apply(result);
            return result;
        }))
    .WithToolsFromAssembly();

var app = builder.Build();

try
{
    await SourceEditRuntime.Engine.RecoverAllPendingAsync(CancellationToken.None);
    foreach (var quarantine in SourceEditRuntime.Engine.LastRecoveryQuarantines)
    {
        app.Logger.LogError(
            "Source Edit journal quarantined. TransactionId={TransactionId} WorkspaceRoot={WorkspaceRoot} JournalPath={JournalPath} Reason={Reason}",
            quarantine.TransactionId ?? "unknown",
            quarantine.WorkspaceRoot ?? "unknown",
            quarantine.JournalPath,
            quarantine.Message);
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Source Edit recovery encountered unresolved state during startup. Source mutations in affected workspaces will remain blocked until recovery can prove a safe state.");
}

if (OperatingSystem.IsWindows() &&
    WindowsServiceHelpers.IsWindowsService() &&
    app.Services.GetService<IHostLifetime>() is WindowsServiceLifetime serviceLifetime)
{
    serviceLifetime.CanStop = true;
    serviceLifetime.CanPauseAndContinue = false;
    serviceLifetime.CanShutdown = true;
}

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
        mcpDev = "/mcp/dev",
        mcpAdmin = "/mcp/admin",
        processId = Environment.ProcessId,
        user = Environment.UserName,
        sid = TalvoraRuntimeIdentity.Sid,
        isWindowsService = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService(),
    });
});

PenpotAiPluginEndpoints.Map(app);
app.MapMcp("/mcp");
app.MapMcp("/mcp/dev");
app.MapMcp("/mcp/admin");
app.Run();