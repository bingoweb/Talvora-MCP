using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class RuntimeTools
{
[McpServerTool(
        Name = "talvora_python_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPythonInfoResponse)),
     Description("Discover Python for the signed-in user or inspect an explicitly supplied Python/py launcher. Returns interpreter identity, version, virtual-environment state, and uv availability. Missing Python is reported structurally rather than treated as an error.")]
    public static async Task<TalvoraPythonInfoResponse> PythonInfo(
        string? pythonExecutable = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var python = ResolvePython(pythonExecutable);
        if (python is null)
        {
            return new TalvoraPythonInfoResponse(
                false,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                null);
        }

        var probe = """
            import json,sys
            print(json.dumps({
                "executable": sys.executable,
                "version": sys.version.split()[0],
                "prefix": sys.prefix,
                "base_prefix": getattr(sys, "base_prefix", sys.prefix),
                "venv": sys.prefix != getattr(sys, "base_prefix", sys.prefix)
            }, separators=(",",":")))
            """;

        var probeArguments = new List<string>();
        probeArguments.AddRange(python.PrefixArguments);
        probeArguments.Add("-c");
        probeArguments.Add(probe);

        var probeResult = await RunCommandAsync(
            python.Executable,
            probeArguments,
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        if (probeResult.ExitCode != 0 || probeResult.TimedOut)
        {
            throw new InvalidOperationException(
                $"Python probe failed. ExitCode={probeResult.ExitCode}, TimedOut={probeResult.TimedOut}, stderr={probeResult.StandardError}");
        }

        using var document = JsonDocument.Parse(
            probeResult.StandardOutput.Trim());
        var root = document.RootElement;

        // UR4: Python packages go through uv (talvora_uv_run), so the probe reports uv instead of pip.
        var uv = SignedInUserToolchain.Resolve(["uv.exe", "uv"]);
        var uvResult = uv is null
            ? null
            : await RunCommandAsync(
                uv,
                ["--version"],
                workingDirectory,
                environment: null,
                timeoutSeconds: 30,
                cancellationToken);

        return new TalvoraPythonInfoResponse(
            true,
            python.Executable,
            root.GetProperty("executable").GetString(),
            root.GetProperty("version").GetString(),
            root.GetProperty("prefix").GetString(),
            root.GetProperty("base_prefix").GetString(),
            root.GetProperty("venv").GetBoolean(),
            uvResult is { ExitCode: 0, TimedOut: false },
            uvResult is { ExitCode: 0, TimedOut: false }
                ? uvResult.StandardOutput.Trim()
                : null);
    }

    [McpServerTool(
        Name = "talvora_python_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run Python with an arbitrary argument vector, working directory, environment overrides, and timeout. If pythonExecutable is omitted Talvora resolves python/python3/py. No Python option/module/script/path allowlist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PythonRun(
        string[] arguments,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var python = RequirePython(pythonExecutable);
        return RunCommandAsync(
            python.Executable,
            python.PrefixArguments.Concat(arguments),
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static PythonCommand RequirePython(
        string? requested)
    {
        return ResolvePython(requested)
            ?? throw new FileNotFoundException(
                "Python was not found. Install Python through Talvora Chocolatey tools or provide pythonExecutable explicitly.");
    }

    private static PythonCommand? ResolvePython(
        string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var explicitPath =
                ResolveExecutable(requested);
            if (explicitPath is null)
            {
                throw new FileNotFoundException(
                    "Requested Python executable was not found.",
                    requested);
            }

            return IsPythonLauncher(explicitPath)
                ? new PythonCommand(
                    explicitPath,
                    ["-3"])
                : new PythonCommand(
                    explicitPath,
                    []);
        }

        foreach (var name in new[]
                 {
                     "python.exe",
                     "python3.exe",
                     "python",
                     "python3",
                     "py.exe",
                     "py",
                 })
        {
            var candidate = ResolveExecutable(name);
            if (candidate is null)
            {
                continue;
            }

            return IsPythonLauncher(candidate)
                ? new PythonCommand(
                    candidate,
                    ["-3"])
                : new PythonCommand(
                    candidate,
                    []);
        }

        return null;
    }

    private static bool IsPythonLauncher(
        string path) =>
        string.Equals(
            Path.GetFileName(path),
            "py.exe",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.GetFileName(path),
            "py",
            StringComparison.OrdinalIgnoreCase);
}
