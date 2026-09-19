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
        Name = "talvora_cmake_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve CMake from PATH or common install locations and report version. Missing CMake is reported structurally.")]
    public static async Task<TalvoraToolInfoResponse> CMakeInfo(
        CancellationToken cancellationToken = default)
    {
        var executable = CommandResolver.Resolve(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]);

        return await ReadToolInfoAsync(executable, ["--version"], "cmake", cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_ninja_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve Ninja from PATH or common Chocolatey locations and report version. Missing Ninja is reported structurally.")]
    public static async Task<TalvoraToolInfoResponse> NinjaInfo(
        CancellationToken cancellationToken = default)
    {
        var executable = CommandResolver.Resolve(
            ["ninja.exe", "ninja"],
            [@"C:\ProgramData\chocolatey\bin\ninja.exe"]);

        return await ReadToolInfoAsync(executable, ["--version"], "ninja", cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_cmake_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run CMake with an arbitrary argument vector, working directory, and environment overrides. No generator/preset/source/build-directory/option allowlist or denylist is applied.")]
    public static Task<TalvoraCliCommandResponse> CMakeRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = CommandResolver.Resolve(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]) ?? throw new FileNotFoundException("CMake was not found.");

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_ninja_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Ninja with an arbitrary argument vector, working directory, and environment overrides. No target/build-directory/option allowlist or denylist is applied.")]
    public static Task<TalvoraCliCommandResponse> NinjaRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = CommandResolver.Resolve(
            ["ninja.exe", "ninja"],
            [
                @"C:\ProgramData\chocolatey\bin\ninja.exe",
            ]) ?? throw new FileNotFoundException("Ninja was not found.");

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<TalvoraToolInfoResponse> ReadToolInfoAsync(
        string? executable,
        IReadOnlyList<string> arguments,
        string invocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new TalvoraToolInfoResponse(false, null, null, null);
        }

        var result = await RunCliAsync(
            executable,
            Environment.CurrentDirectory,
            arguments,
            null,
            30,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return new TalvoraToolInfoResponse(false, executable, null, invocation);
        }

        var firstLine = TextLines.Split(result.StandardOutput).FirstOrDefault()?.Trim();
        var version = firstLine;
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            version = parts.LastOrDefault() ?? firstLine;
        }

        return new TalvoraToolInfoResponse(true, executable, version, invocation);
    }

    private static async Task<string?> ReadFirstLineAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunCliAsync(
            executable,
            Environment.CurrentDirectory,
            arguments,
            null,
            30,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return null;
        }

        return TextLines.Split(result.StandardOutput).FirstOrDefault();
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
}
