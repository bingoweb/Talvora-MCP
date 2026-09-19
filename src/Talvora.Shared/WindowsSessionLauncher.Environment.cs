using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Talvora.Shared;

public static partial class WindowsSessionLauncher
{
private static Dictionary<string, string> ReadEnvironmentBlock(
        IntPtr environment)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var cursor = environment;

        while (cursor != IntPtr.Zero)
        {
            var entry = Marshal.PtrToStringUni(cursor);
            if (string.IsNullOrEmpty(entry))
            {
                break;
            }

            var separator = entry[0] == '='
                ? entry.IndexOf('=', 1)
                : entry.IndexOf('=');

            if (separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }

            cursor = IntPtr.Add(
                cursor,
                checked((entry.Length + 1) * sizeof(char)));
        }

        return result;
    }

    private static void ApplyEnvironmentOverrides(
        Dictionary<string, string> values,
        IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (var pair in overrides)
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                throw new ArgumentException(
                    "Environment variable names cannot be empty.",
                    nameof(overrides));
            }

            if (pair.Value is null)
            {
                values.Remove(pair.Key);
            }
            else
            {
                values[pair.Key] = pair.Value;
            }
        }
    }

    private static IntPtr CreateEnvironmentBlockPointer(
        IReadOnlyDictionary<string, string> values)
    {
        var entries = values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}");

        return Marshal.StringToHGlobalUni(
            string.Join('\0', entries) + "\0");
    }

    private static string ResolveWorkingDirectory(
        string? requested,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException(
                    $"Working directory was not found: {full}");
            }

            return full;
        }

        if (environment.TryGetValue("USERPROFILE", out var profile) &&
            !string.IsNullOrWhiteSpace(profile) &&
            Directory.Exists(profile))
        {
            return Path.GetFullPath(profile);
        }

        return Environment.CurrentDirectory;
    }

    private static (string DisplayName, string? ApplicationName)
        ResolveExecutable(
            string executable,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment)
    {
        var containsSeparator =
            executable.Contains(Path.DirectorySeparatorChar) ||
            executable.Contains(Path.AltDirectorySeparatorChar);

        if (Path.IsPathFullyQualified(executable) || containsSeparator)
        {
            var candidate = Path.IsPathFullyQualified(executable)
                ? Path.GetFullPath(executable)
                : Path.GetFullPath(Path.Combine(workingDirectory, executable));

            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException(
                    "Executable was not found.",
                    candidate);
            }

            return (candidate, candidate);
        }

        var resolved = CommandResolver.Resolve(
            [executable],
            workingDirectory: workingDirectory,
            environment: environment);

        return resolved is null
            ? (executable, null)
            : (resolved, resolved);
    }

    private static string BuildCommandLine(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteWindowsArgument(executable));

        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteWindowsArgument(argument));
        }

        return builder.ToString();
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 &&
            !argument.Any(character =>
                char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(character);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
