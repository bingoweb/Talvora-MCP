using Talvora;
using Talvora.Tools;
using Talvora.SourceEditing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.AccessControl;
using System.Security.Principal;

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
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7676));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TalvoraDesktopProgressNotifier>();
builder.Services.AddHostedService<TalvoraSystemStorageMaintenanceService>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Talvora",
            Version = TalvoraRuntimeMetadata.InformationalVersion,
        };
        options.ServerInstructions =
            SourceEditRoutingContract.ServerInstructions + " " +
            RemotionTools.ServerInstructions + " " +
            "When a Talvora tool call fails, its result ends with 'How to call <tool>' and example arguments; correct the next call exactly as shown.";
    })
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
            if (ToolArgumentValidator.TryCreateError(request) is { } invalidArguments)
            {
                return ToolFailureGuidance.Apply(request.Params?.Name, invalidArguments);
            }

            var notifier =
                request.Services?.GetService<TalvoraDesktopProgressNotifier>();
            Func<CancellationToken, ValueTask<CallToolResult>> operation =
                token => next(request, token);
            if (notifier is not null)
            {
                var inner = operation;
                operation = token => notifier.RunToolCallAsync(
                    request.Params.Name,
                    request.Params.Arguments,
                    inner,
                    token,
                    request.Services?.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                        ?.HttpContext?.Request.Path.Value,
                    request.Server.ClientInfo?.Name);
            }

            return ToolFailureGuidance.Apply(request.Params?.Name, ToolResultTextPolicy.Apply(await operation(cancellationToken)));
        });

        filters.AddListToolsFilter(next => async (
            request,
            cancellationToken) =>
        {
            var result =
                await next(
                    request,
                    cancellationToken);
            McpToolMetadataWirePolicy.Apply(
                result,
                Talvora.Shared.TalvoraMcpToolSurfacePolicy.ResolvePath(
                    request.Services?.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                        ?.HttpContext?.Request.Path.Value));
            return result;
        });
    })
    .WithToolsFromAssembly();

var app = builder.Build();
app.UseLoopbackOriginGuard();
ClaudeBackgroundJobTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
CodexBackgroundJobTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
RemotionTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
var isWindowsService =
    OperatingSystem.IsWindows() &&
    WindowsServiceHelpers.IsWindowsService();

if (isWindowsService)
{
    try
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora");
        if (Directory.Exists(root))
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var unsafeRights = FileSystemRights.WriteData |
                FileSystemRights.AppendData |
                FileSystemRights.WriteExtendedAttributes |
                FileSystemRights.WriteAttributes;
            var allowsWrite = new DirectoryInfo(root).GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .OfType<FileSystemAccessRule>()
                .Any(rule => rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference.Equals(users) &&
                    (rule.FileSystemRights & unsafeRights) != 0);
            if (allowsWrite)
            {
                app.Logger.LogWarning(
                    "Talvora klasör izinleri güncel değil. Kurulumu yeniden çalıştırın.");
            }
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
        System.Security.SecurityException)
    {
        app.Logger.LogWarning(ex, "Talvora ProgramData ACL inspection could not complete.");
    }
}

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

try
{
    await AgentJobRegistry.Shared.RecoverAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Agent job recovery could not complete at startup.");
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
        version = TalvoraRuntimeMetadata.InformationalVersion,
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
EmbeddedMcpIntegrationHealth.Map(app);
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
