namespace Talvora.SourceEditing;

internal sealed record SourcePathClassification(
    string FullPath,
    string? WorkspaceRoot,
    bool IsDevelopmentWorkspace,
    bool IsGeneratedLocation,
    bool IsKnownSourcePath,
    bool IsLikelyText,
    string Classification)
{
    public bool ShouldGuardLegacyTextMutation =>
        IsDevelopmentWorkspace &&
        !IsGeneratedLocation &&
        (IsKnownSourcePath || IsLikelyText);
}

internal static class SourceWorkspaceClassifier
{
    private static readonly HashSet<string> StrongMarkerNames =
        new(
            [
                ".git",
                "package.json",
                "pyproject.toml",
                "requirements.txt",
                "pipfile",
                "cargo.toml",
                "go.mod",
                "pom.xml",
                "build.gradle",
                "build.gradle.kts",
                "settings.gradle",
                "settings.gradle.kts",
                "cmakelists.txt",
                "pubspec.yaml",
                "dockerfile",
                "docker-compose.yml",
                "docker-compose.yaml",
                "compose.yml",
                "compose.yaml",
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> GeneratedDirectories =
        new(
            [
                ".git",
                ".gradle",
                ".idea",
                ".vs",
                ".dart_tool",
                ".pub-cache",
                ".cache",
                ".next",
                ".nuxt",
                ".turbo",
                "node_modules",
                "bin",
                "obj",
                "target",
                "build",
                "dist",
                "out",
                "coverage",
                "artifacts",
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SourceFileNames =
        new(
            [
                "dockerfile",
                "makefile",
                "justfile",
                "readme",
                "license",
                "global.json",
                "nuget.config",
                "directory.build.props",
                "directory.build.targets",
                "directory.packages.props",
                ".editorconfig",
                ".gitattributes",
                ".gitignore",
                ".dockerignore",
                ".npmrc",
                ".yarnrc",
                ".yarnrc.yml",
                ".nvmrc",
                ".env",
                ".env.example",
                "package.json",
                "package-lock.json",
                "pnpm-lock.yaml",
                "yarn.lock",
                "bun.lock",
                "bun.lockb",
                "pyproject.toml",
                "requirements.txt",
                "cargo.toml",
                "cargo.lock",
                "go.mod",
                "go.sum",
                "pom.xml",
                "build.gradle",
                "build.gradle.kts",
                "settings.gradle",
                "settings.gradle.kts",
                "cmakelists.txt",
                "pubspec.yaml",
                "pubspec.lock",
                "docker-compose.yml",
                "docker-compose.yaml",
                "compose.yml",
                "compose.yaml",
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SourceExtensions =
        new(
            [
                ".c",
                ".cc",
                ".cpp",
                ".cxx",
                ".h",
                ".hh",
                ".hpp",
                ".hxx",
                ".cs",
                ".csx",
                ".fs",
                ".fsx",
                ".vb",
                ".java",
                ".kt",
                ".kts",
                ".groovy",
                ".gradle",
                ".go",
                ".rs",
                ".py",
                ".pyi",
                ".rb",
                ".php",
                ".swift",
                ".dart",
                ".js",
                ".jsx",
                ".mjs",
                ".cjs",
                ".ts",
                ".tsx",
                ".mts",
                ".cts",
                ".vue",
                ".svelte",
                ".html",
                ".htm",
                ".css",
                ".scss",
                ".sass",
                ".less",
                ".sql",
                ".graphql",
                ".gql",
                ".proto",
                ".sh",
                ".bash",
                ".zsh",
                ".fish",
                ".ps1",
                ".psm1",
                ".psd1",
                ".cmd",
                ".bat",
                ".json",
                ".jsonc",
                ".yaml",
                ".yml",
                ".toml",
                ".ini",
                ".xml",
                ".config",
                ".props",
                ".targets",
                ".csproj",
                ".fsproj",
                ".vbproj",
                ".vcxproj",
                ".sln",
                ".slnx",
                ".md",
                ".mdx",
                ".rst",
                ".txt",
                ".csv",
                ".tsv",
            ],
            StringComparer.OrdinalIgnoreCase);

    public static string ResolveExplicitWorkspaceRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (!Directory.Exists(fullRoot))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.WorkspaceNotFound,
                $"Development workspace root was not found: {fullRoot}",
                fullRoot);
        }

        EnsureNoReparsePoint(fullRoot, fullRoot);

        if (!IsDevelopmentWorkspaceRoot(fullRoot))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.WorkspaceNotFound,
                "The requested root is not a recognized development workspace. Use the repository/project root containing .git, a solution/project file, package manifest, or another supported development marker.",
                fullRoot);
        }

        return fullRoot;
    }

    public static string? FindWorkspaceRootForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var current = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : new FileInfo(fullPath).Directory;

        while (current is not null)
        {
            try
            {
                if (IsDevelopmentWorkspaceRoot(current.FullName))
                {
                    return Path.TrimEndingDirectorySeparator(current.FullName);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            current = current.Parent;
        }

        return null;
    }

    public static string ResolveWorkspacePath(
        string workspaceRoot,
        string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':', StringComparison.Ordinal) ||
            relativePath.IndexOf('\0') >= 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PathOutsideWorkspace,
                "Source-edit paths must be workspace-relative regular paths without drive/device/ADS syntax.",
                relativePath);
        }

        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(
            Path.Combine(workspaceRoot, normalizedRelative));

        if (!IsContainedPath(fullPath, workspaceRoot) ||
            PathsEqual(fullPath, workspaceRoot))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PathOutsideWorkspace,
                "Source-edit path escapes the declared workspace.",
                relativePath);
        }

        EnsureNoReparsePoint(workspaceRoot, fullPath);
        return fullPath;
    }

    public static string GetRelativePath(
        string workspaceRoot,
        string fullPath) =>
        Path.GetRelativePath(workspaceRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    public static SourcePathClassification Classify(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var workspaceRoot = FindWorkspaceRootForPath(fullPath);
        if (workspaceRoot is null)
        {
            return new SourcePathClassification(
                fullPath,
                null,
                false,
                false,
                IsKnownSourcePath(fullPath),
                File.Exists(fullPath) && IsLikelyTextFile(fullPath),
                "ordinary-file");
        }

        var relative = Path.GetRelativePath(workspaceRoot, fullPath);
        var generated = IsGeneratedRelativePath(relative);
        var known = IsKnownSourcePath(fullPath);
        var likelyText = File.Exists(fullPath) && IsLikelyTextFile(fullPath);
        var classification = generated
            ? "workspace-generated"
            : known
                ? "workspace-source"
                : likelyText
                    ? "workspace-text"
                    : "workspace-other";

        return new SourcePathClassification(
            fullPath,
            workspaceRoot,
            true,
            generated,
            known,
            likelyText,
            classification);
    }

    public static bool IsKnownSourcePath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (SourceFileNames.Contains(fileName))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return SourceExtensions.Contains(extension);
    }

    public static bool IsDevelopmentWorkspaceRoot(string root)
    {
        if (File.Exists(Path.Combine(root, ".git")) ||
            Directory.Exists(Path.Combine(root, ".git")))
        {
            return true;
        }

        foreach (var marker in StrongMarkerNames)
        {
            if (string.Equals(marker, ".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(Path.Combine(root, marker)))
            {
                return true;
            }
        }

        try
        {
            return Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(root, "*.fsproj", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(root, "*.vbproj", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(root, "*.vcxproj", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void EnsureNoReparsePoint(
        string workspaceRoot,
        string targetPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var target = Path.GetFullPath(targetPath);

        if (!IsContainedPath(target, root) && !PathsEqual(target, root))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PathOutsideWorkspace,
                "Path is outside the source-edit workspace.",
                target);
        }

        var current = root;
        CheckReparse(current);

        if (PathsEqual(current, target))
        {
            return;
        }

        var relative = Path.GetRelativePath(root, target);
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            CheckReparse(current);
        }
    }

    public static bool IsContainedPath(string candidate, string parent)
    {
        var normalizedCandidate =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));

        return PathsEqual(normalizedCandidate, normalizedParent) ||
               normalizedCandidate.StartsWith(
                   normalizedParent + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedRelativePath(string relativePath)
    {
        if (relativePath == ".")
        {
            return false;
        }

        return relativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(GeneratedDirectories.Contains);
    }

    private static bool IsLikelyTextFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, 64 * 1024);
            if (length == 0)
            {
                return true;
            }

            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, buffer.Length);
            return SourceTextCodec.IsLikelyTextPayload(
                buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CheckReparse(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.ReparsePointUnsupported,
                    "Source Edit transactions do not traverse symlinks, junctions, or other reparse points. Use a regular workspace path or an unrestricted general filesystem/process tool when that behavior is intentional.",
                    path);
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
