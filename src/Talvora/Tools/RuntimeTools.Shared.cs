using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class RuntimeTools
{
private static string? ResolveExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(command) ||
            command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                var full = Path.GetFullPath(command);
                return File.Exists(full) ? full : null;
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException or
                IOException or
                UnauthorizedAccessException)
            {
                return null;
            }
        }

        return CommandResolver.Resolve([command]);
    }

    private static async Task<TalvoraRuntimeCommandResponse>
        RunCommandAsync(
            string executable,
            IEnumerable<string> arguments,
            string? workingDirectory,
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

        return new TalvoraRuntimeCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments);
    }
}
