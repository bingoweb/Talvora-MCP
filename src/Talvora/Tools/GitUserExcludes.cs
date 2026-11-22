using System.ComponentModel;
using Talvora.Shared;

namespace Talvora.Tools;

internal static class GitUserExcludes
{
    private static readonly Lock Gate = new();
    private static (string Home, long Stamp, string? Path)? _cached;

    internal static string? ResolvePath(
        string home, string? xdgConfigHome, string? configuredValue)
    {
        string path;
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            var value = configuredValue.StartsWith(":(optional)", StringComparison.Ordinal)
                ? configuredValue[11..] : configuredValue;
            if (value is ['~', '/' or '\\', .. var rest])
            {
                path = Path.Join(home, rest);
            }
            else if (Path.IsPathFullyQualified(value))
            {
                path = value;
            }
            else
            {
                return null;
            }
        }
        else
        {
            var configHome = string.IsNullOrWhiteSpace(xdgConfigHome)
                ? Path.Combine(home, ".config") : xdgConfigHome;
            path = Path.Combine(configHome, "git", "ignore");
        }

        var absolutePath = Path.GetFullPath(path);
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    internal static string[] AddArgument(
        IReadOnlyList<string> arguments, string? excludesPath)
    {
        if (excludesPath is null ||
            arguments.Any(argument =>
                argument.StartsWith("core.excludesFile=", StringComparison.OrdinalIgnoreCase)))
        {
            return [.. arguments];
        }

        var i = 0;
        while (i + 1 < arguments.Count && arguments[i] == "-c")
        {
            i += 2;
        }

        return
        [
            .. arguments.Take(i),
            "-c",
            "core.excludesFile=" + excludesPath.Replace('\\', '/'),
            .. arguments.Skip(i),
        ];
    }

    internal static async Task<string?> ResolveForSignedInUserAsync(
        string git, CancellationToken cancellationToken)
    {
        InteractiveUserContext user;
        try
        {
            user = WindowsSessionLauncher.GetActiveInteractiveUser();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        var home = user.Environment.TryGetValue("HOME", out var configuredHome) &&
            !string.IsNullOrWhiteSpace(configuredHome)
                ? configuredHome : user.UserProfile;
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        lock (Gate)
        {
            if (_cached is { } cached &&
                string.Equals(cached.Home, home, StringComparison.OrdinalIgnoreCase) &&
                TimeProvider.System.GetElapsedTime(cached.Stamp) < TimeSpan.FromSeconds(60))
            {
                return cached.Path;
            }
        }

        var xdgConfigHome = user.Environment.TryGetValue("XDG_CONFIG_HOME", out var configuredXdg)
            ? configuredXdg : null;
        var gitConfigHome = string.IsNullOrWhiteSpace(xdgConfigHome)
            ? Path.Combine(home, ".config") : xdgConfigHome;
        string? excludesSetting = null;

        foreach (var configurationFile in new[]
        {
            Path.Combine(home, ".gitconfig"),
            Path.Combine(gitConfigHome, "git", "config"),
        })
        {
            if (!File.Exists(configurationFile))
            {
                continue;
            }

            var result = await ProcessRunner.RunAsync(
                git, workingDirectory: null,
                arguments: ["config", "--file", configurationFile, "--get", "core.excludesFile"],
                timeoutSeconds: 10,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                continue;
            }

            excludesSetting = result.StandardOutput.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(excludesSetting))
            {
                break;
            }
        }

        var resolved = ResolvePath(home, xdgConfigHome, excludesSetting);
        lock (Gate)
        {
            _cached = (home, TimeProvider.System.GetTimestamp(), resolved);
        }

        return resolved;
    }
}
