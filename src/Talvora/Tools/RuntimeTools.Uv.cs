using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class RuntimeTools
{
    // UV1: Python work runs through uv as the signed-in user (user rule 2026-11-01: uv for everything Python,
    // never pip or venv); the same identity, output bounds and redaction as talvora_user_run.
    [McpServerTool(
        Name = "talvora_uv_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraUserRunResponse)),
     Description("Run uv as the signed-in Windows user for Python packages, environments, scripts, tools and Python versions (sync, run, add, lock, venv, pip, tool run, python install). Use instead of pip or venv.")]
    public static Task<TalvoraUserRunResponse> UvRun(
        string[] arguments,
        string workingDirectory,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 600,
        int maxOutputCharacters = ToolOutputCompactor.DefaultMaximumOutputCharacters,
        CancellationToken cancellationToken = default) =>
        UserRunTools.Run(
            "uv",
            arguments,
            workingDirectory,
            environment,
            timeoutSeconds,
            maxOutputCharacters,
            cancellationToken);
}
