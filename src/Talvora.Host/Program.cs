using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
using Talvora.Ipc.Client;
using Talvora.Ipc.Contracts;
using Talvora.Modules.FileSystem;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;
using Talvora.Platform.Windows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Talvora";
});

builder.Services.AddSingleton<IErrorMapper, DefaultErrorMapper>();
builder.Services.AddSingleton<IOperationExecutor, OperationExecutor>();
builder.Services.AddSingleton<IPlatformInfoProvider, WindowsPlatformInfoProvider>();
builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
builder.Services.AddSingleton<IShellService, ShellService>();
builder.Services.AddSingleton<IProcessService, ProcessService>();

var brokerPipeName = builder.Configuration[BrokerProtocol.PipeNameConfigurationKey]
    ?? BrokerProtocol.DefaultPipeName;
builder.Services.AddTalvoraBrokerClient(brokerPipeName);
builder.Services.AddSingleton<IExecutionRouter, ExecutionRouter>();
builder.Services.AddTalvoraMcp();

var app = builder.Build();

app.MapGet("/health", async (IPlatformInfoProvider platformInfo, CancellationToken cancellationToken) =>
{
    var platform = await platformInfo.GetAsync(cancellationToken);
    return Results.Ok(new
    {
        ok = true,
        service = "talvora",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
        platform.OperatingSystem,
        platform.IsElevated,
    });
});

app.MapGet("/health/broker", async (IBrokerClient brokerClient, CancellationToken cancellationToken) =>
{
    var probe = await brokerClient.ProbeAsync(TimeSpan.FromSeconds(2), cancellationToken);
    if (!probe.Connected || probe.Health is null)
    {
        return Results.Ok(new
        {
            ok = false,
            connected = false,
            pipeName = brokerPipeName,
            error = probe.Error,
        });
    }

    var health = probe.Health;
    return Results.Ok(new
    {
        ok = health.Ready && health.ProtocolVersion == BrokerProtocol.CurrentVersion,
        connected = true,
        protocolVersion = health.ProtocolVersion,
        health.Ready,
        health.ServiceVersion,
        health.IsElevated,
        health.ProcessId,
        pipeName = brokerPipeName,
    });
});

app.MapTalvoraMcp();

await app.RunAsync();
