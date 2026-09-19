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
     Description("Discover Python for the Talvora service or inspect an explicitly supplied Python/py launcher. Returns interpreter identity, version, virtual-environment state, and pip availability. Missing Python is reported structurally rather than treated as an error.")]
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

        var pipArguments = new List<string>();
        pipArguments.AddRange(python.PrefixArguments);
        pipArguments.Add("-m");
        pipArguments.Add("pip");
        pipArguments.Add("--version");

        var pipResult = await RunCommandAsync(
            python.Executable,
            pipArguments,
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
            pipResult.ExitCode == 0 && !pipResult.TimedOut,
            pipResult.ExitCode == 0
                ? pipResult.StandardOutput.Trim()
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

    [McpServerTool(
        Name = "talvora_python_venv_create",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPythonVenvResponse)),
     Description("Create or recreate a Python virtual environment using python -m venv. Supports system-site-packages, clear, upgrade-deps, without-pip, arbitrary extra venv arguments, and an explicit Python executable. No target-path restriction is applied.")]
    public static async Task<TalvoraPythonVenvResponse> PythonVenvCreate(
        string path,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        bool systemSitePackages = false,
        bool clear = false,
        bool upgradeDeps = false,
        bool withoutPip = false,
        string[]? additionalArguments = null,
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Virtual environment path is required.", nameof(path));
        }

        var python = RequirePython(pythonExecutable);
        var target = Path.GetFullPath(
            string.IsNullOrWhiteSpace(workingDirectory)
                ? path
                : Path.Combine(workingDirectory, path));

        var arguments = new List<string>();
        arguments.AddRange(python.PrefixArguments);
        arguments.Add("-m");
        arguments.Add("venv");

        if (systemSitePackages)
        {
            arguments.Add("--system-site-packages");
        }
        if (clear)
        {
            arguments.Add("--clear");
        }
        if (upgradeDeps)
        {
            arguments.Add("--upgrade-deps");
        }
        if (withoutPip)
        {
            arguments.Add("--without-pip");
        }

        arguments.AddRange(additionalArguments ?? []);
        arguments.Add(target);

        var result = await RunCommandAsync(
            python.Executable,
            arguments,
            workingDirectory,
            environment: null,
            timeoutSeconds,
            cancellationToken);

        var venvPython = OperatingSystem.IsWindows()
            ? Path.Combine(target, "Scripts", "python.exe")
            : Path.Combine(target, "bin", "python");

        return new TalvoraPythonVenvResponse(
            target,
            File.Exists(venvPython) ? venvPython : null,
            result);
    }

    [McpServerTool(
        Name = "talvora_pip_install",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Install arbitrary Python packages using python -m pip install. Supports upgrade/pre/index/extra-index/target plus arbitrary additional pip arguments. No package/index/path/option allowlist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PipInstall(
        string[] packages,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool upgrade = false,
        bool prerelease = false,
        string? indexUrl = null,
        string? extraIndexUrl = null,
        string? target = null,
        string[]? additionalArguments = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Length == 0)
        {
            throw new ArgumentException(
                "At least one pip package or requirement specifier is required.",
                nameof(packages));
        }

        var arguments = new List<string> { "-m", "pip", "install" };

        if (upgrade)
        {
            arguments.Add("--upgrade");
        }
        if (prerelease)
        {
            arguments.Add("--pre");
        }
        if (!string.IsNullOrWhiteSpace(indexUrl))
        {
            arguments.Add("--index-url");
            arguments.Add(indexUrl);
        }
        if (!string.IsNullOrWhiteSpace(extraIndexUrl))
        {
            arguments.Add("--extra-index-url");
            arguments.Add(extraIndexUrl);
        }
        if (!string.IsNullOrWhiteSpace(target))
        {
            arguments.Add("--target");
            arguments.Add(target);
        }

        arguments.AddRange(additionalArguments ?? []);
        arguments.AddRange(packages);

        return PythonRun(
            arguments.ToArray(),
            pythonExecutable,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_pip_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run python -m pip with an arbitrary pip argument vector. No pip subcommand, package, index, target, or option allowlist/denylist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PipRun(
        string[] arguments,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return PythonRun(
            new[] { "-m", "pip" }.Concat(arguments).ToArray(),
            pythonExecutable,
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
