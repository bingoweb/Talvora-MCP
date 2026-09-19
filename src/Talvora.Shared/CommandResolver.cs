namespace Talvora.Shared;

public static class CommandResolver
{
    public static string? Resolve(
        IEnumerable<string> commandNames,
        IEnumerable<string>? explicitCandidates = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null) =>
        ResolveAll(
            commandNames,
            explicitCandidates,
            workingDirectory,
            environment).FirstOrDefault();

    public static IReadOnlyList<string> ResolveAll(
        IEnumerable<string> commandNames,
        IEnumerable<string>? explicitCandidates = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(commandNames);

        var names = commandNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidates = new List<string>();
        if (explicitCandidates is not null)
        {
            candidates.AddRange(
                explicitCandidates.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        foreach (var name in names)
        {
            if (Path.IsPathFullyQualified(name))
            {
                candidates.Add(name);
                continue;
            }

            if (name.Contains(Path.DirectorySeparatorChar) ||
                name.Contains(Path.AltDirectorySeparatorChar))
            {
                candidates.Add(Path.Combine(cwd, name));
                continue;
            }

            candidates.Add(Path.Combine(cwd, name));
        }

        var path = GetEnvironmentValue(environment, "PATH");
        foreach (var directory in (path ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                if (!Path.IsPathFullyQualified(name) &&
                    !name.Contains(Path.DirectorySeparatorChar) &&
                    !name.Contains(Path.AltDirectorySeparatorChar))
                {
                    candidates.Add(Path.Combine(directory, name));
                }
            }
        }

        var extensions = GetExecutableExtensions(environment);
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            foreach (var expanded in ExpandCandidate(candidate, extensions))
            {
                try
                {
                    if (!File.Exists(expanded))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(expanded);
                    if (seen.Add(fullPath))
                    {
                        results.Add(fullPath);
                    }
                }
                catch (Exception ex) when (
                    ex is ArgumentException or
                    NotSupportedException or
                    PathTooLongException or
                    IOException or
                    UnauthorizedAccessException)
                {
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetExecutableExtensions(
        IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [string.Empty];
        }

        var raw = GetEnvironmentValue(environment, "PATHEXT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = ".COM;.EXE;.BAT;.CMD";
        }

        return raw.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(extension =>
                extension.StartsWith('.') ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExpandCandidate(
        string candidate,
        IReadOnlyList<string> extensions)
    {
        if (Path.HasExtension(candidate))
        {
            yield return candidate;
            yield break;
        }

        yield return candidate;
        foreach (var extension in extensions)
        {
            if (extension.Length > 0)
            {
                yield return candidate + extension;
            }
        }
    }

    private static string? GetEnvironmentValue(
        IReadOnlyDictionary<string, string>? environment,
        string name)
    {
        if (environment is not null &&
            environment.TryGetValue(name, out var value))
        {
            return value;
        }

        return Environment.GetEnvironmentVariable(name);
    }
}
