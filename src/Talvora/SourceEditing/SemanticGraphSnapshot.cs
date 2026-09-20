using Microsoft.CodeAnalysis;

namespace Talvora.SourceEditing;

internal sealed class SemanticGraphSnapshot
{
    private const int AbsoluteMaxMembershipPaths = 1000000;
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private static readonly string[] ConventionalControlNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "NuGet.Config",
    ];

    private readonly string workspaceRoot;
    private readonly IReadOnlyDictionary<string, SemanticGraphFileState> files;
    private readonly IReadOnlyList<string> sourceRoots;
    private readonly IReadOnlyList<string> sourceMembership;
    private HashSet<string> mutablePaths =
        new(PathComparer);

    private SemanticGraphSnapshot(
        string workspaceRoot,
        IReadOnlyDictionary<string, SemanticGraphFileState> files,
        IReadOnlyList<string> sourceRoots,
        IReadOnlyList<string> sourceMembership)
    {
        this.workspaceRoot = workspaceRoot;
        this.files = files;
        this.sourceRoots = sourceRoots;
        this.sourceMembership = sourceMembership;
    }

    public static async Task<SemanticGraphSnapshot> CaptureAsync(
        string workspaceRoot,
        string scopePath,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var states =
            new Dictionary<string, SemanticGraphFileState>(
                PathComparer);
        var sourceRootCandidates =
            new HashSet<string>(
                PathComparer);

        await AddFileStateAsync(
            states,
            scopePath,
            lockDuringCommit: true,
            requireExists: true,
            "scope",
            cancellationToken);

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(
                    project.FilePath))
            {
                var projectPath =
                    Path.GetFullPath(
                        project.FilePath);
                await AddFileStateAsync(
                    states,
                    projectPath,
                    lockDuringCommit: true,
                    requireExists: true,
                    "project",
                    cancellationToken);
                var projectDirectory =
                    Path.GetDirectoryName(
                        projectPath);
                if (!string.IsNullOrWhiteSpace(
                        projectDirectory) &&
                    SourceWorkspaceClassifier.IsContainedPath(
                        projectDirectory,
                        workspaceRoot))
                {
                    sourceRootCandidates.Add(
                        Path.GetFullPath(
                            projectDirectory));
                    await AddProjectControlStatesAsync(
                        states,
                        workspaceRoot,
                        projectPath,
                        cancellationToken);
                }
            }

            foreach (var document in project.Documents)
            {
                await AddTextDocumentStateAsync(
                    states,
                    document,
                    cancellationToken);
            }

            foreach (var document in project.AdditionalDocuments)
            {
                await AddTextDocumentStateAsync(
                    states,
                    document,
                    cancellationToken);
            }

            foreach (var document in project.AnalyzerConfigDocuments)
            {
                await AddTextDocumentStateAsync(
                    states,
                    document,
                    cancellationToken);
            }

            foreach (var reference in
                     project.MetadataReferences
                         .OfType<PortableExecutableReference>())
            {
                if (string.IsNullOrWhiteSpace(
                        reference.FilePath))
                {
                    continue;
                }

                var referencePath =
                    Path.GetFullPath(
                        reference.FilePath);
                if (!SourceWorkspaceClassifier.IsContainedPath(
                        referencePath,
                        workspaceRoot))
                {
                    continue;
                }

                await AddFileStateAsync(
                    states,
                    referencePath,
                    lockDuringCommit: true,
                    requireExists: true,
                    "workspace-metadata-reference",
                    cancellationToken);
            }

            foreach (var analyzerReference in
                     project.AnalyzerReferences)
            {
                await AddAnalyzerReferenceStateAsync(
                    states,
                    analyzerReference.FullPath,
                    "project-analyzer-reference",
                    cancellationToken);
            }
        }

        foreach (var analyzerReference in
                 solution.AnalyzerReferences)
        {
            await AddAnalyzerReferenceStateAsync(
                states,
                analyzerReference.FullPath,
                "solution-analyzer-reference",
                cancellationToken);
        }

        var roots =
            NormalizeSourceRoots(
                sourceRootCandidates);
        var membership =
            CaptureSourceMembership(
                roots,
                cancellationToken);
        return new SemanticGraphSnapshot(
            Path.GetFullPath(
                workspaceRoot),
            states,
            roots,
            membership);
    }

    public void BindMutablePaths(
        IEnumerable<string> paths)
    {
        mutablePaths =
            paths
                .Select(
                    Path.GetFullPath)
                .ToHashSet(
                    PathComparer);
    }

    public async Task VerifyAsync(
        CancellationToken cancellationToken)
    {
        foreach (var state in files.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutablePaths.Contains(
                    state.Path))
            {
                continue;
            }

            await VerifyFileStateAsync(
                state,
                cancellationToken);
        }

        var currentMembership =
            CaptureSourceMembership(
                sourceRoots,
                cancellationToken);
        if (!sourceMembership.SequenceEqual(
                currentMembership,
                PathComparer))
        {
            var expected =
                sourceMembership.ToHashSet(
                    PathComparer);
            var current =
                currentMembership.ToHashSet(
                    PathComparer);
            var added =
                current.Except(
                    expected,
                    PathComparer)
                    .Take(8)
                    .ToArray();
            var removed =
                expected.Except(
                    current,
                    PathComparer)
                    .Take(8)
                    .ToArray();
            throw Stale(
                "The physical C# source membership changed after Roslyn loaded the semantic graph.",
                workspaceRoot,
                new Dictionary<string, string>
                {
                    ["added"] =
                        string.Join(
                            "|",
                            added),
                    ["removed"] =
                        string.Join(
                            "|",
                            removed),
                });
        }
    }

    public async Task<ISourceEditCommitGuard?> AcquireCommitGuardAsync(
        CancellationToken cancellationToken)
    {
        var guard =
            new SemanticGraphCommitGuard(
                this);
        try
        {
            await guard.AcquireAsync(
                cancellationToken);
            return guard;
        }
        catch
        {
            await guard.DisposeAsync();
            throw;
        }
    }

    private async Task<IReadOnlyList<FileStream>> AcquireControlLocksAsync(
        CancellationToken cancellationToken)
    {
        var streams =
            new List<FileStream>();
        try
        {
            foreach (var state in files.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!state.LockDuringCommit ||
                    state.Revision is null ||
                    mutablePaths.Contains(
                        state.Path))
                {
                    continue;
                }

                FileStream stream;
                try
                {
                    stream =
                        new FileStream(
                            state.Path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 1,
                            FileOptions.SequentialScan);
                }
                catch (Exception ex) when (
                    ex is IOException or
                        UnauthorizedAccessException)
                {
                    throw Stale(
                        "A semantic graph control file could not be locked for the source-edit commit window.",
                        state.Path,
                        innerException: ex);
                }

                streams.Add(
                    stream);
                var revision =
                    await SourceTextCodec.ComputeRevisionAsync(
                        state.Path,
                        cancellationToken);
                if (!string.Equals(
                        revision,
                        state.Revision,
                        StringComparison.Ordinal))
                {
                    throw Stale(
                        "A semantic graph control file changed before the commit guard was acquired.",
                        state.Path,
                        new Dictionary<string, string>
                        {
                            ["expectedRevision"] =
                                state.Revision,
                            ["currentRevision"] =
                                revision,
                        });
                }
            }

            return streams;
        }
        catch
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    private static async Task AddProjectControlStatesAsync(
        IDictionary<string, SemanticGraphFileState> states,
        string workspaceRoot,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var projectDirectory =
            Path.GetDirectoryName(
                projectPath)!;
        var directory =
            projectDirectory;
        while (SourceWorkspaceClassifier.IsContainedPath(
                   directory,
                   workspaceRoot))
        {
            foreach (var name in ConventionalControlNames)
            {
                await AddFileStateAsync(
                    states,
                    Path.Combine(
                        directory,
                        name),
                    lockDuringCommit: true,
                    requireExists: false,
                    "msbuild-control",
                    cancellationToken);
            }

            if (PathsEqual(
                    directory,
                    workspaceRoot))
            {
                break;
            }

            var parent =
                Directory.GetParent(
                    directory);
            if (parent is null)
            {
                break;
            }

            directory =
                parent.FullName;
        }

        var objDirectory =
            Path.Combine(
                projectDirectory,
                "obj");
        var projectFileName =
            Path.GetFileName(
                projectPath);
        var generatedControlCandidates =
            new[]
            {
                Path.Combine(
                    objDirectory,
                    "project.assets.json"),
                Path.Combine(
                    objDirectory,
                    $"{projectFileName}.nuget.g.props"),
                Path.Combine(
                    objDirectory,
                    $"{projectFileName}.nuget.g.targets"),
            };
        foreach (var candidate in generatedControlCandidates)
        {
            await AddFileStateAsync(
                states,
                candidate,
                lockDuringCommit: true,
                requireExists: false,
                "restore-control",
                cancellationToken);
        }
    }

    private static async Task AddTextDocumentStateAsync(
        IDictionary<string, SemanticGraphFileState> states,
        TextDocument document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                document.FilePath))
        {
            return;
        }

        var path =
            Path.GetFullPath(
                document.FilePath);
        var snapshot =
            await SourceTextCodec.ReadSnapshotAsync(
                path,
                path,
                requireText: true,
                cancellationToken);
        if (!snapshot.Exists ||
            snapshot.Revision is null ||
            snapshot.Document is null)
        {
            throw Stale(
                "A Roslyn semantic document disappeared before the graph snapshot was captured.",
                path);
        }

        var roslynText =
            await document.GetTextAsync(
                cancellationToken);
        if (!string.Equals(
                roslynText.ToString(),
                snapshot.Document.Text,
                StringComparison.Ordinal))
        {
            throw Stale(
                "Roslyn's loaded semantic document no longer matches the physical source snapshot.",
                path,
                new Dictionary<string, string>
                {
                    ["diskRevision"] =
                        snapshot.Revision,
                });
        }

        AddKnownState(
            states,
            path,
            snapshot.Revision,
            lockDuringCommit: false,
            "semantic-document");
    }

    private static async Task AddFileStateAsync(
        IDictionary<string, SemanticGraphFileState> states,
        string path,
        bool lockDuringCommit,
        bool requireExists,
        string kind,
        CancellationToken cancellationToken)
    {
        var fullPath =
            Path.GetFullPath(
                path);
        if (!File.Exists(
                fullPath))
        {
            if (requireExists)
            {
                throw Stale(
                    "A semantic graph control file disappeared before the graph snapshot was captured.",
                    fullPath);
            }

            AddKnownState(
                states,
                fullPath,
                revision: null,
                lockDuringCommit,
                kind);
            return;
        }

        string revision;
        try
        {
            revision =
                await SourceTextCodec.ComputeRevisionAsync(
                    fullPath,
                    cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
            throw Stale(
                "A semantic graph file could not be revisioned.",
                fullPath,
                innerException: ex);
        }

        AddKnownState(
            states,
            fullPath,
            revision,
            lockDuringCommit,
            kind);
    }

    private static void AddKnownState(
        IDictionary<string, SemanticGraphFileState> states,
        string path,
        string? revision,
        bool lockDuringCommit,
        string kind)
    {
        var fullPath =
            Path.GetFullPath(
                path);
        if (states.TryGetValue(
                fullPath,
                out var existing))
        {
            if (!string.Equals(
                    existing.Revision,
                    revision,
                    StringComparison.Ordinal))
            {
                throw Stale(
                    "A semantic graph file changed while its snapshot was being captured.",
                    fullPath,
                    new Dictionary<string, string>
                    {
                        ["firstRevision"] =
                            existing.Revision ??
                            "<missing>",
                        ["currentRevision"] =
                            revision ??
                            "<missing>",
                    });
            }

            if (lockDuringCommit &&
                !existing.LockDuringCommit)
            {
                states[fullPath] =
                    existing with
                    {
                        LockDuringCommit = true,
                    };
            }

            return;
        }

        states[fullPath] =
            new SemanticGraphFileState(
                fullPath,
                revision,
                lockDuringCommit,
                kind);
    }

    private async Task VerifyFileStateAsync(
        SemanticGraphFileState state,
        CancellationToken cancellationToken)
    {
        var exists =
            File.Exists(
                state.Path);
        if (state.Revision is null)
        {
            if (exists)
            {
                throw Stale(
                    "A semantic graph control file appeared after Roslyn loaded the workspace.",
                    state.Path,
                    new Dictionary<string, string>
                    {
                        ["kind"] =
                            state.Kind,
                    });
            }

            return;
        }

        if (!exists)
        {
            throw Stale(
                "A semantic graph file disappeared after Roslyn loaded the workspace.",
                state.Path,
                new Dictionary<string, string>
                {
                    ["kind"] =
                        state.Kind,
                    ["expectedRevision"] =
                        state.Revision,
                });
        }

        string revision;
        try
        {
            revision =
                await SourceTextCodec.ComputeRevisionAsync(
                    state.Path,
                    cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
            throw Stale(
                "A semantic graph file could not be revalidated.",
                state.Path,
                innerException: ex);
        }

        if (!string.Equals(
                revision,
                state.Revision,
                StringComparison.Ordinal))
        {
            throw Stale(
                "The semantic graph changed after Roslyn produced the rename proposal.",
                state.Path,
                new Dictionary<string, string>
                {
                    ["kind"] =
                        state.Kind,
                    ["expectedRevision"] =
                        state.Revision,
                    ["currentRevision"] =
                        revision,
                });
        }
    }

    private static IReadOnlyList<string> NormalizeSourceRoots(
        IEnumerable<string> roots)
    {
        var normalized =
            roots
                .Select(
                    Path.GetFullPath)
                .Distinct(
                    PathComparer)
                .OrderBy(
                    path => path.Length)
                .ThenBy(
                    path => path,
                    PathComparer)
                .ToArray();
        var result =
            new List<string>();
        foreach (var candidate in normalized)
        {
            if (result.Any(
                    root =>
                        IsSameOrDescendant(
                            candidate,
                            root)))
            {
                continue;
            }

            result.Add(
                candidate);
        }

        return result;
    }

    private static IReadOnlyList<string> CaptureSourceMembership(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var paths =
            new HashSet<string>(
                PathComparer);
        var pending =
            new Stack<string>(
                roots.Reverse());
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory =
                pending.Pop();
            if (!Directory.Exists(
                    directory))
            {
                continue;
            }

            foreach (var entry in
                     Directory.EnumerateFileSystemEntries(
                         directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes =
                    File.GetAttributes(
                        entry);
                if ((attributes &
                     FileAttributes.Directory) != 0)
                {
                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0 ||
                        ShouldSkipDirectory(
                            Path.GetFileName(
                                entry)))
                    {
                        continue;
                    }

                    pending.Push(
                        entry);
                    continue;
                }

                if (!string.Equals(
                        Path.GetExtension(
                            entry),
                        ".cs",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                paths.Add(
                    Path.GetFullPath(
                        entry));
                if (paths.Count >
                    AbsoluteMaxMembershipPaths)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.ResourceLimit,
                        $"Semantic graph source-membership snapshot exceeded the absolute {AbsoluteMaxMembershipPaths} path ceiling.",
                        directory);
                }
            }
        }

        return paths
            .OrderBy(
                path => path,
                PathComparer)
            .ToArray();
    }

    private static bool ShouldSkipDirectory(
        string name) =>
        name.Equals(
            ".git",
            StringComparison.OrdinalIgnoreCase) ||
        name.Equals(
            ".vs",
            StringComparison.OrdinalIgnoreCase) ||
        name.Equals(
            "bin",
            StringComparison.OrdinalIgnoreCase) ||
        name.Equals(
            "obj",
            StringComparison.OrdinalIgnoreCase) ||
        name.Equals(
            "node_modules",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrDescendant(
        string candidate,
        string root)
    {
        if (PathsEqual(
                candidate,
                root))
        {
            return true;
        }

        var relative =
            Path.GetRelativePath(
                root,
                candidate);
        return !string.Equals(
                   relative,
                   "..",
                   PathComparison) &&
               !relative.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   PathComparison) &&
               !Path.IsPathFullyQualified(
                   relative);
    }

    private static bool PathsEqual(
        string left,
        string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    left)),
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    right)),
            PathComparison);

    private static SourceEditDomainException Stale(
        string message,
        string path,
        IReadOnlyDictionary<string, string>? details = null,
        Exception? innerException = null) =>
        new(
            SourceEditCodes.SemanticGraphStale,
            $"{SourceEditCodes.SemanticGraphStale}: {message}",
            path,
            details,
            innerException);

    private sealed record SemanticGraphFileState(
        string Path,
        string? Revision,
        bool LockDuringCommit,
        string Kind);

    private sealed class SemanticGraphCommitGuard(
        SemanticGraphSnapshot snapshot)
        : ISourceEditCommitGuard
    {
        private IReadOnlyList<FileStream> lockedStreams =
            [];

        public async Task AcquireAsync(
            CancellationToken cancellationToken)
        {
            await snapshot.VerifyAsync(
                cancellationToken);
            lockedStreams =
                await snapshot.AcquireControlLocksAsync(
                    cancellationToken);
            await snapshot.VerifyAsync(
                cancellationToken);
        }

        public async ValueTask VerifyAsync(
            CancellationToken cancellationToken)
        {
            await snapshot.VerifyAsync(
                cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var stream in lockedStreams)
            {
                stream.Dispose();
            }

            lockedStreams = [];
            return ValueTask.CompletedTask;
        }
    }
}
