using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class BuildRunnerTools
{
private static Task<TalvoraCliCommandResponse> RunDotnetAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var dotnet = ResolveDotnet()
            ?? throw new FileNotFoundException(
                "dotnet CLI was not found. Install the .NET SDK/runtime or provide it on the machine PATH.");

        return RunCliAsync(
            dotnet,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static Task<TalvoraCliCommandResponse> RunNpmAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var npm = ResolveNpm()
            ?? throw new FileNotFoundException(
                "npm was not found. Install Node.js/npm (for example through Chocolatey) and make it available on the machine.");

        return RunCliAsync(
            npm,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static void AddTarget(List<string> arguments, string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            arguments.Add(target);
        }
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("workingDirectory is required.", nameof(workingDirectory));
        }

        var full = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {full}");
        }

        return full;
    }

    private static async Task<TalvoraCliCommandResponse> RunCliCheckedAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunCheckedAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments,
            result.ElapsedMilliseconds);
    }

    private static async Task<TalvoraCliCommandResponse> RunCliAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments,
            result.ElapsedMilliseconds);
    }
    private static string? ResolveDotnet()
    {
        var candidates = new List<string>();

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            candidates.Add(Path.Combine(dotnetRoot, "dotnet.exe"));
            candidates.Add(Path.Combine(dotnetRoot, "dotnet"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "dotnet", "dotnet.exe"));
        }

        return ResolveFromCandidatesAndPath(candidates, "dotnet.exe", "dotnet");
    }

    private static string? ResolveNode()
    {
        var candidates = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "nodejs", "node.exe"));
        }

        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "node.exe"));
        }

        return ResolveFromCandidatesAndPath(candidates, "node.exe", "node");
    }

    private static string? ResolveNpm()
    {
        var candidates = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "nodejs", "npm.cmd"));
            candidates.Add(Path.Combine(programFiles, "nodejs", "npm.exe"));
        }

        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "npm.exe"));
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "npm.cmd"));
        }

        return ResolveFromCandidatesAndPath(
            candidates,
            "npm.exe",
            "npm.cmd",
            "npm");
    }

    private static string? ResolveFromCandidatesAndPath(
        IEnumerable<string> initialCandidates,
        params string[] commandNames) =>
        CommandResolver.Resolve(commandNames, initialCandidates);
}
