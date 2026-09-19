using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraChocolateyInfoResponse(
    string Executable,
    string Version);

public sealed record TalvoraChocolateyPackage(
    string Name,
    string Version);

public sealed record TalvoraChocolateyQueryResponse(
    string Command,
    int ExitCode,
    bool TimedOut,
    IReadOnlyList<TalvoraChocolateyPackage> Packages,
    string StandardOutput,
    string StandardError);

public sealed record TalvoraChocolateyCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

[McpServerToolType]
public static class ChocolateyTools
{
    [McpServerTool(
        Name = "talvora_choco_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyInfoResponse)),
     Description("Return the Chocolatey CLI executable used by Talvora and its installed version.")]
    public static async Task<TalvoraChocolateyInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        var result = await RunChocolateyAsync(
            ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Chocolatey version query failed. ExitCode={result.ExitCode}, TimedOut={result.TimedOut}, stderr={result.StandardError}");
        }

        return new TalvoraChocolateyInfoResponse(
            result.Executable,
            result.StandardOutput.Trim());
    }

    [McpServerTool(
        Name = "talvora_choco_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyQueryResponse)),
     Description("List locally installed Chocolatey packages using --limit-output. Optional query filtering is applied to the structured package result without reducing Chocolatey's local package visibility.")]
    public static async Task<TalvoraChocolateyQueryResponse> List(
        string? query = null,
        bool exact = false,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        var result = await RunChocolateyAsync(
            ["list", "--limit-output", "--no-color"],
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);

        var packages = ParsePackages(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(query))
        {
            packages = packages
                .Where(package => exact
                    ? string.Equals(package.Name, query, StringComparison.OrdinalIgnoreCase)
                    : package.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return new TalvoraChocolateyQueryResponse(
            "list",
            result.ExitCode,
            result.TimedOut,
            packages,
            result.StandardOutput,
            result.StandardError);
    }

    [McpServerTool(
        Name = "talvora_choco_search",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyQueryResponse)),
     Description("Search Chocolatey package sources and return parsed name/version rows from --limit-output. Supports exact/all-versions/source/page controls without a package or source allowlist.")]
    public static async Task<TalvoraChocolateyQueryResponse> Search(
        string query,
        bool exact = false,
        bool allVersions = false,
        string? source = null,
        int? page = null,
        int? pageSize = null,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Chocolatey search query is required.", nameof(query));
        }
        if (page is < 0 || pageSize is < 1)
        {
            throw new ArgumentOutOfRangeException("page cannot be negative and pageSize must be positive.");
        }

        var arguments = new List<string>
        {
            "search",
            query,
            "--limit-output",
            "--no-color",
        };

        if (exact)
        {
            arguments.Add("--exact");
        }
        if (allVersions)
        {
            arguments.Add("--all-versions");
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            arguments.Add($"--source={source}");
        }
        if (page is int pageValue)
        {
            arguments.Add($"--page={pageValue}");
        }
        if (pageSize is int pageSizeValue)
        {
            arguments.Add($"--page-size={pageSizeValue}");
        }

        var result = await RunChocolateyAsync(
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);

        return new TalvoraChocolateyQueryResponse(
            "search",
            result.ExitCode,
            result.TimedOut,
            ParsePackages(result.StandardOutput),
            result.StandardOutput,
            result.StandardError);
    }

    [McpServerTool(
        Name = "talvora_choco_install",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyCommandResponse)),
     Description("Install any Chocolatey package with optional version/source/package parameters/install arguments and arbitrary additional Chocolatey arguments. No package/source/option allowlist is applied.")]
    public static Task<TalvoraChocolateyCommandResponse> Install(
        string package,
        string? version = null,
        string? source = null,
        string? packageParameters = null,
        string? installArguments = null,
        bool prerelease = false,
        bool force = false,
        bool yes = true,
        bool noProgress = true,
        string[]? additionalArguments = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default) =>
        RunPackageCommand(
            "install",
            package,
            version,
            source,
            packageParameters,
            installArguments,
            prerelease,
            force,
            yes,
            noProgress,
            additionalArguments,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_choco_upgrade",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyCommandResponse)),
     Description("Upgrade or install any Chocolatey package with optional version/source/package parameters/install arguments and arbitrary additional Chocolatey arguments. No package/source/option allowlist is applied.")]
    public static Task<TalvoraChocolateyCommandResponse> Upgrade(
        string package,
        string? version = null,
        string? source = null,
        string? packageParameters = null,
        string? installArguments = null,
        bool prerelease = false,
        bool force = false,
        bool yes = true,
        bool noProgress = true,
        string[]? additionalArguments = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default) =>
        RunPackageCommand(
            "upgrade",
            package,
            version,
            source,
            packageParameters,
            installArguments,
            prerelease,
            force,
            yes,
            noProgress,
            additionalArguments,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_choco_uninstall",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyCommandResponse)),
     Description("Uninstall any Chocolatey package with optional version and arbitrary additional Chocolatey arguments. No package or option allowlist is applied.")]
    public static async Task<TalvoraChocolateyCommandResponse> Uninstall(
        string package,
        string? version = null,
        bool force = false,
        bool yes = true,
        string[]? additionalArguments = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);

        var arguments = new List<string>
        {
            "uninstall",
            package,
            "--no-color",
        };

        if (yes)
        {
            arguments.Add("--yes");
        }
        if (!string.IsNullOrWhiteSpace(version))
        {
            arguments.Add($"--version={version}");
        }
        if (force)
        {
            arguments.Add("--force");
        }

        arguments.AddRange(additionalArguments ?? []);

        return await RunChocolateyAsync(
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_choco_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraChocolateyCommandResponse)),
     Description("Run Chocolatey with an arbitrary argument vector, optional working directory, and optional environment overrides. This preserves the complete Chocolatey CLI surface with no subcommand/package/source/option allowlist or denylist.")]
    public static Task<TalvoraChocolateyCommandResponse> Run(
        string[] arguments,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunChocolateyAsync(
            arguments,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static Task<TalvoraChocolateyCommandResponse> RunPackageCommand(
        string command,
        string package,
        string? version,
        string? source,
        string? packageParameters,
        string? installArguments,
        bool prerelease,
        bool force,
        bool yes,
        bool noProgress,
        string[]? additionalArguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        ValidatePackage(package);

        var arguments = new List<string>
        {
            command,
            package,
            "--no-color",
        };

        if (yes)
        {
            arguments.Add("--yes");
        }
        if (noProgress)
        {
            arguments.Add("--no-progress");
        }
        if (!string.IsNullOrWhiteSpace(version))
        {
            arguments.Add($"--version={version}");
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            arguments.Add($"--source={source}");
        }
        if (!string.IsNullOrWhiteSpace(packageParameters))
        {
            arguments.Add($"--package-parameters={packageParameters}");
        }
        if (!string.IsNullOrWhiteSpace(installArguments))
        {
            arguments.Add($"--install-arguments={installArguments}");
        }
        if (prerelease)
        {
            arguments.Add("--pre");
        }
        if (force)
        {
            arguments.Add("--force");
        }

        arguments.AddRange(additionalArguments ?? []);

        return RunChocolateyAsync(
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);
    }

    private static void ValidatePackage(string package)
    {
        if (string.IsNullOrWhiteSpace(package))
        {
            throw new ArgumentException("Chocolatey package name is required.", nameof(package));
        }
    }

    private static TalvoraChocolateyPackage[] ParsePackages(string output)
    {
        var packages = new List<TalvoraChocolateyPackage>();

        foreach (var raw in output.Split(
                     new[] { "\r\n", "\n", "\r" },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('|');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var version = line[(separator + 1)..].Trim();

            if (name.Length == 0 || version.Length == 0)
            {
                continue;
            }

            packages.Add(new TalvoraChocolateyPackage(name, version));
        }

        return packages.ToArray();
    }

    private static async Task<TalvoraChocolateyCommandResponse> RunChocolateyAsync(
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveChocolateyExecutable();
        var result = await ProcessRunner.RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraChocolateyCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments);
    }

    private static string ResolveChocolateyExecutable()
    {
        var candidates = new List<string>();
        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "choco.exe"));
            candidates.Add(Path.Combine(chocolateyInstall, "choco.exe"));
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        candidates.Add(Path.Combine(programData, "chocolatey", "bin", "choco.exe"));

        return CommandResolver.Resolve(["choco.exe", "choco"], candidates)
            ?? "choco.exe";
    }
}
