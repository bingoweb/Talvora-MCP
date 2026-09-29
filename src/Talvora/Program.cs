using Talvora;
using Talvora.SourceEditing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
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
builder.Services.AddSingleton<TalvoraDesktopProgressNotifier>();
builder.Services.AddSingleton<TalvoraAutomaticLearningObserver>();
builder.Services.AddHostedService<TalvoraSystemStorageMaintenanceService>();

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
    {
        filters.AddCallToolFilter(next => async (
            request,
            cancellationToken) =>
        {
            var notifier =
                request.Services?.GetService<TalvoraDesktopProgressNotifier>();
            var learner =
                request.Services?.GetService<TalvoraAutomaticLearningObserver>();

            Func<CancellationToken, ValueTask<CallToolResult>> operation =
                token => next(request, token);
            if (notifier is not null)
            {
                var inner = operation;
                operation = token => notifier.RunToolCallAsync(
                    request.Params.Name,
                    request.Params.Arguments,
                    inner,
                    token);
            }

            return learner is null
                ? await operation(cancellationToken)
                : await learner.RunToolCallAsync(
                    request.Params.Name,
                    request.Params.Arguments,
                    operation,
                    cancellationToken);
        });

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
        });
    })
    .WithToolsFromAssembly();

var app = builder.Build();
var isWindowsService =
    OperatingSystem.IsWindows() &&
    WindowsServiceHelpers.IsWindowsService();

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

if (isWindowsService &&
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
        isWindowsService,
    });
});

PenpotAiPluginEndpoints.Map(app);
app.MapMcp("/mcp");
app.MapMcp("/mcp/dev");
app.MapMcp("/mcp/admin");
try
{
    app.Run();
}
catch (OperationCanceledException)
    when (isWindowsService)
{
    // Windows service shutdown may cancel the host stop token while SCM is
    // waiting for a clean STOPPED transition. Treat that cancellation as the
    // expected service-stop path instead of crashing the process.
}