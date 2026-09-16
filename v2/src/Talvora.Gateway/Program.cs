using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.Stateless;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    product = "Talvora",
    version = "2.0-dev",
    mcp = "/mcp",
    processId = Environment.ProcessId
}));

app.MapMcp("/mcp");

app.Run("http://127.0.0.1:7676");
