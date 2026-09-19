using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class WindowsToolchainTools
{
[McpServerTool(
        Name = "talvora_msbuild_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve MSBuild. Prefers Visual Studio MSBuild.exe when installed and otherwise falls back to dotnet msbuild, which keeps MSBuild available on machines with only the .NET SDK.")]
    public static async Task<TalvoraToolInfoResponse> MsBuildInfo(
        CancellationToken cancellationToken = default)
    {
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        var msbuild = ResolveMsBuild(instances);

        if (!string.IsNullOrWhiteSpace(msbuild) && File.Exists(msbuild))
        {
            var nativeVersion = await ReadFirstLineAsync(
                msbuild,
                ["-version", "-nologo"],
                cancellationToken);

            return new TalvoraToolInfoResponse(
                true,
                msbuild,
                nativeVersion,
                "MSBuild.exe");
        }

        var dotnet = CommandResolver.Resolve(
            ["dotnet.exe", "dotnet"],
            [@"C:\Program Files\dotnet\dotnet.exe"]);

        if (dotnet is null)
        {
            return new TalvoraToolInfoResponse(false, null, null, null);
        }

        var result = await RunCliAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["msbuild", "-version", "-nologo"],
            null,
            60,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return new TalvoraToolInfoResponse(false, dotnet, null, "dotnet msbuild");
        }

        var fallbackVersion = TextLines.Split(result.StandardOutput)
            .LastOrDefault(line => Version.TryParse(line.Trim(), out _))
            ?? TextLines.Split(result.StandardOutput).LastOrDefault();

        return new TalvoraToolInfoResponse(
            true,
            dotnet,
            fallbackVersion?.Trim(),
            "dotnet msbuild");
    }

    [McpServerTool(
        Name = "talvora_msbuild_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run MSBuild.exe with an arbitrary argument vector, working directory, and environment overrides. No target/property/project/option allowlist or denylist is applied.")]
    public static async Task<TalvoraCliCommandResponse> MsBuildRun(
        string workingDirectory,
        string[] arguments,
        string? msBuildExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = string.IsNullOrWhiteSpace(msBuildExecutable)
            ? ResolveMsBuild(await DiscoverVisualStudioInstancesAsync(cancellationToken))
            : Path.GetFullPath(msBuildExecutable);

        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            return await RunCliAsync(
                executable,
                workingDirectory,
                arguments,
                environment,
                timeoutSeconds,
                cancellationToken);
        }

        var dotnet = CommandResolver.Resolve(
            ["dotnet.exe", "dotnet"],
            [@"C:\Program Files\dotnet\dotnet.exe"])
            ?? throw new FileNotFoundException("Neither MSBuild.exe nor dotnet msbuild was found.");

        var fallbackArguments = new List<string> { "msbuild" };
        fallbackArguments.AddRange(arguments);

        return await RunCliAsync(
            dotnet,
            workingDirectory,
            fallbackArguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static string? ResolveMsBuild(
        IReadOnlyList<TalvoraVisualStudioInstance> instances)
    {
        foreach (var instance in instances
                     .OrderByDescending(item => item.InstallationVersion, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                Path.Combine(instance.InstallationPath, "MSBuild", "Current", "Bin", "MSBuild.exe"),
                Path.Combine(instance.InstallationPath, "MSBuild", "15.0", "Bin", "MSBuild.exe"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var knownRoots = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio",
            @"C:\Program Files (x86)\Microsoft Visual Studio",
        };

        foreach (var root in knownRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var match = Directory.EnumerateFiles(
                        root,
                        "MSBuild.exe",
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (match is not null)
                {
                    return match;
                }
            }
            catch
            {
            }
        }

        return CommandResolver.Resolve(["MSBuild.exe", "msbuild"], []);
    }
}
