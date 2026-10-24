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

    public static async Task<TalvoraToolInfoResponse> NinjaInfo(
        CancellationToken cancellationToken = default)
    {
        var executable = CommandResolver.Resolve(
            ["ninja.exe", "ninja"],
            [@"C:\ProgramData\chocolatey\bin\ninja.exe"]);

        return await ReadToolInfoAsync(executable, ["--version"], "ninja", cancellationToken);
    }

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
