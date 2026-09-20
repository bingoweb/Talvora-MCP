using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Talvora.SourceEditing;

internal static class RoslynSemanticEditEngine
{
    public static async Task<SemanticEditTransactionResult> ApplyRenameAsync(
        string workspaceRoot,
        string transactionId,
        string solutionOrProjectPath,
        string documentPath,
        int line,
        int character,
        string expectedRevision,
        string newName,
        string? projectPath,
        string? expectedSymbolName,
        bool renameOverloads,
        bool renameInStrings,
        bool renameInComments,
        int maxProjects,
        int maxDocuments,
        int maxChangedDocuments,
        long maxTotalChangedCharacters,
        int maxDiagnostics,
        int timeoutSeconds,
        bool validateSyntax,
        CancellationToken cancellationToken)
    {
        string root;
        SemanticRenameRequest request;
        try
        {
            root =
                SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                    workspaceRoot);
            request =
                SemanticRenameRequest.Normalize(
                    root,
                    solutionOrProjectPath,
                    documentPath,
                    line,
                    character,
                    expectedRevision,
                    newName,
                    projectPath,
                    expectedSymbolName,
                    renameOverloads,
                    renameInStrings,
                    renameInComments,
                    maxProjects,
                    maxDocuments,
                    maxChangedDocuments,
                    maxTotalChangedCharacters,
                    maxDiagnostics,
                    timeoutSeconds,
                    validateSyntax);
        }
        catch (SourceEditDomainException ex)
        {
            var rejected =
                SourceEditTransactionResult.Rejected(
                    transactionId ?? string.Empty,
                    null,
                    null,
                    ex);
            return new SemanticEditTransactionResult(
                false,
                "rename",
                false,
                null,
                null,
                [],
                rejected);
        }

        var context =
            new SemanticExecutionContext(
                request.MaxDiagnostics);
        var result =
            await SourceEditRuntime.Engine.ApplyGeneratedEditsAsync(
                root,
                transactionId,
                request.ComputeHash(),
                validateSyntax,
                (canonicalRoot, token) =>
                    GenerateRenameChangesAsync(
                        canonicalRoot,
                        request,
                        context,
                        token),
                cancellationToken,
                context.ToDurableReceiptJson);

        var durableReceipt =
            SemanticDurableReceipt.Parse(
                result.AdapterReceiptJson);

        return new SemanticEditTransactionResult(
            result.Success,
            "rename",
            result.Replayed,
            durableReceipt?.Workspace ??
                context.Workspace,
            durableReceipt?.Symbol ??
                context.Symbol,
            durableReceipt?.Diagnostics ??
                context.Diagnostics,
            result);
    }

    private static async Task<IReadOnlyList<SourceEditChangeInput>> GenerateRenameChangesAsync(
        string workspaceRoot,
        SemanticRenameRequest request,
        SemanticExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                request.TimeoutSeconds));

        try
        {
            return await GenerateRenameChangesCoreAsync(
                workspaceRoot,
                request,
                context,
                timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeout.IsCancellationRequested)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.SemanticTimeout,
                $"{SourceEditCodes.SemanticTimeout}: Roslyn semantic rename exceeded timeoutSeconds={request.TimeoutSeconds}.");
        }
    }

    private static async Task<IReadOnlyList<SourceEditChangeInput>> GenerateRenameChangesCoreAsync(
        string workspaceRoot,
        SemanticRenameRequest request,
        SemanticExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration =
            RoslynMsBuildBootstrap.EnsureRegistered();

        using var workspace =
            CreateWorkspace();
        workspace.SkipUnrecognizedProjects = false;
        workspace.LoadMetadataForReferencedProjects = false;

        var workspaceDiagnostics =
            new ConcurrentQueue<WorkspaceDiagnostic>();
        using var workspaceFailureRegistration =
            workspace.RegisterWorkspaceFailedHandler(
                args =>
                    workspaceDiagnostics.Enqueue(
                        args.Diagnostic));

        SemanticLoadScope loadScope;
        try
        {
            loadScope =
                await ResolveLoadScopeAsync(
                    workspaceRoot,
                    request,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SourceEditDomainException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                System.Xml.XmlException)
        {
            throw LoadFailure(
                "Semantic project-scope discovery could not establish a complete workspace scope.",
                request.SolutionOrProjectFullPath,
                context,
                ex);
        }

        Solution solution;
        try
        {
            solution =
                await OpenSolutionOrProjectAsync(
                    workspace,
                    loadScope.Path,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            DrainWorkspaceDiagnostics(
                workspace,
                workspaceDiagnostics,
                context);
            throw LoadFailure(
                "Roslyn/MSBuild could not load the requested solution/project.",
                loadScope.Path,
                context,
                ex);
        }

        DrainWorkspaceDiagnostics(
            workspace,
            workspaceDiagnostics,
            context);
        RejectWorkspaceFailures(
            request,
            context);

        var projects =
            solution.Projects
                .OrderBy(
                    project => project.FilePath ?? project.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var documentCount =
            projects.Sum(
                project => project.DocumentIds.Count);
        if (projects.Length > request.MaxProjects)
        {
            throw ResourceLimit(
                $"Loaded project count {projects.Length} exceeds maxProjects={request.MaxProjects}.");
        }

        if (documentCount > request.MaxDocuments)
        {
            throw ResourceLimit(
                $"Loaded document count {documentCount} exceeds maxDocuments={request.MaxDocuments}.");
        }

        context.Workspace =
            new SemanticWorkspaceReceipt(
                SourceWorkspaceClassifier.GetRelativePath(
                    workspaceRoot,
                    loadScope.Path),
                loadScope.Kind,
                registration.Version,
                registration.MsBuildPath,
                projects.Length,
                documentCount);

        await EnsureCompilationHealthyAsync(
            solution,
            context,
            SourceEditCodes.SemanticCompilationInvalid,
            "The loaded semantic workspace contains compiler errors before rename. No mutation was attempted.",
            cancellationToken);

        var anchorRelative =
            SourceWorkspaceClassifier.GetRelativePath(
                workspaceRoot,
                request.DocumentFullPath);
        var anchorSnapshot =
            await SourceTextCodec.ReadSnapshotAsync(
                request.DocumentFullPath,
                anchorRelative,
                requireText: true,
                cancellationToken);
        if (!anchorSnapshot.Exists ||
            anchorSnapshot.Document is null ||
            anchorSnapshot.Revision is null)
        {
            throw Domain(
                SourceEditCodes.SemanticDocumentNotFound,
                "The semantic anchor source document was not found or is not readable text.",
                request.DocumentFullPath);
        }

        if (!string.Equals(
                SourceEditRevision.Normalize(
                    anchorSnapshot.Revision),
                request.ExpectedRevision,
                StringComparison.Ordinal))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ExpectedRevisionMismatch,
                $"{SourceEditCodes.ExpectedRevisionMismatch}: semantic anchor revision is stale.",
                request.DocumentFullPath,
                new Dictionary<string, string>
                {
                    ["expectedRevision"] =
                        request.ExpectedRevision,
                    ["currentRevision"] =
                        anchorSnapshot.Revision,
                });
        }

        var anchorDocuments =
            FindAnchorDocuments(
                projects,
                request);
        if (anchorDocuments.Length == 0)
        {
            throw Domain(
                SourceEditCodes.SemanticDocumentNotFound,
                "Roslyn loaded the workspace, but the requested C# source document was not part of the selected solution/project context.",
                request.DocumentFullPath);
        }

        var resolved =
            new List<ResolvedSemanticSymbol>();
        foreach (var document in anchorDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldText =
                await document.GetTextAsync(
                    cancellationToken);
            EnsureRoslynTextMatchesSnapshot(
                oldText,
                anchorSnapshot,
                request.DocumentFullPath);
            var position =
                GetUtf16Position(
                    oldText,
                    request.Line,
                    request.Character,
                    request.DocumentFullPath);
            var semanticModel =
                await document.GetSemanticModelAsync(
                    cancellationToken);
            if (semanticModel is null)
            {
                throw Domain(
                    SourceEditCodes.SemanticWorkspaceLoadFailed,
                    "Roslyn did not produce a semantic model for the anchor document.",
                    document.FilePath);
            }

            var symbol =
                await SymbolFinder.FindSymbolAtPositionAsync(
                    semanticModel,
                    position,
                    workspace,
                    cancellationToken);
            if (symbol is null)
            {
                continue;
            }

            var canonicalSymbol =
                symbol.OriginalDefinition;
            if (!string.IsNullOrWhiteSpace(
                    request.ExpectedSymbolName) &&
                !string.Equals(
                    canonicalSymbol.Name,
                    request.ExpectedSymbolName,
                    StringComparison.Ordinal))
            {
                throw Domain(
                    SourceEditCodes.SemanticSymbolNotFound,
                    $"Resolved semantic symbol '{canonicalSymbol.Name}' does not match expectedSymbolName '{request.ExpectedSymbolName}'.",
                    document.FilePath);
            }

            resolved.Add(
                ResolvedSemanticSymbol.Create(
                    canonicalSymbol,
                    document.Project.FilePath));
        }

        if (resolved.Count == 0)
        {
            throw Domain(
                SourceEditCodes.SemanticSymbolNotFound,
                "No renameable semantic symbol exists at the requested UTF-16 source position.",
                request.DocumentFullPath);
        }

        var identityGroups =
            resolved
                .GroupBy(
                    item => item.IdentityCanonical,
                    StringComparer.Ordinal)
                .ToArray();
        if (identityGroups.Length != 1)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.SemanticSymbolAmbiguous,
                $"{SourceEditCodes.SemanticSymbolAmbiguous}: the same physical source position resolves to different semantic symbols across loaded project contexts. Supply projectPath to disambiguate.",
                request.DocumentFullPath,
                new Dictionary<string, string>
                {
                    ["identityCount"] =
                        identityGroups.Length.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    ["projectCount"] =
                        resolved.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                });
        }

        var selected =
            identityGroups[0]
                .OrderBy(
                    item => item.ProjectPath ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .First();
        context.Symbol =
            selected.ToReceipt(
                request.NewName,
                resolved);

        Solution changedSolution;
        try
        {
            var options =
                new SymbolRenameOptions(
                    request.RenameOverloads,
                    request.RenameInStrings,
                    request.RenameInComments,
                    RenameFile: false);
            changedSolution =
                await Renamer.RenameSymbolAsync(
                    solution,
                    selected.Symbol,
                    options,
                    request.NewName,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
                ArgumentException or
                NotSupportedException)
        {
            throw Domain(
                SourceEditCodes.SemanticProposalInvalid,
                "Roslyn could not produce a semantic rename proposal.",
                request.DocumentFullPath,
                ex);
        }

        await EnsureCompilationHealthyAsync(
            changedSolution,
            context,
            SourceEditCodes.SemanticConflict,
            "The semantic rename proposal introduces compiler errors. No mutation was attempted.",
            cancellationToken);

        return await BuildStructuredChangesAsync(
            workspaceRoot,
            solution,
            changedSolution,
            request,
            cancellationToken);
    }

    private static MSBuildWorkspace CreateWorkspace() =>
        MSBuildWorkspace.Create();

    private static async Task<SemanticLoadScope> ResolveLoadScopeAsync(
        string workspaceRoot,
        SemanticRenameRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                request.InputKind,
                "project",
                StringComparison.Ordinal))
        {
            return new SemanticLoadScope(
                request.SolutionOrProjectFullPath,
                "solution");
        }

        var discovered =
            DiscoverSemanticScopeFiles(
                workspaceRoot,
                request.MaxProjects,
                cancellationToken);
        var containingSolutions =
            new List<string>();
        foreach (var solutionPath in discovered.SolutionPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await SolutionContainsProjectAsync(
                    solutionPath,
                    request.SolutionOrProjectFullPath,
                    cancellationToken))
            {
                containingSolutions.Add(solutionPath);
            }
        }

        if (containingSolutions.Count == 1)
        {
            return new SemanticLoadScope(
                containingSolutions[0],
                "solution");
        }

        if (containingSolutions.Count > 1)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.SemanticScopeAmbiguous,
                $"{SourceEditCodes.SemanticScopeAmbiguous}: project-mode semantic rename found multiple containing solution files. Supply the intended .sln/.slnx as solutionOrProjectPath so the semantic scope is explicit.",
                request.SolutionOrProjectFullPath,
                new Dictionary<string, string>
                {
                    ["solutionCount"] =
                        containingSolutions.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    ["solutions"] =
                        string.Join(
                            "|",
                            containingSolutions.Select(
                                path =>
                                    SourceWorkspaceClassifier.GetRelativePath(
                                        workspaceRoot,
                                        path))),
                });
        }

        if (discovered.ProjectPaths.Count > 1)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.SemanticScopeIncomplete,
                $"{SourceEditCodes.SemanticScopeIncomplete}: project-mode semantic rename cannot prove reverse-dependent project completeness in this multi-project workspace because no unique containing solution was found. Supply a .sln/.slnx as solutionOrProjectPath.",
                request.SolutionOrProjectFullPath,
                new Dictionary<string, string>
                {
                    ["projectCount"] =
                        discovered.ProjectPaths.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                });
        }

        return new SemanticLoadScope(
            request.SolutionOrProjectFullPath,
            "project");
    }

    private static SemanticScopeDiscovery DiscoverSemanticScopeFiles(
        string workspaceRoot,
        int maxProjects,
        CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        var solutions = new List<string>();
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);
        var maxSolutions = Math.Max(32, maxProjects);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                        ShouldSkipSemanticDiscoveryDirectory(
                            Path.GetFileName(entry)))
                    {
                        continue;
                    }

                    pending.Push(entry);
                    continue;
                }

                var extension =
                    Path.GetExtension(entry);
                if (string.Equals(
                        extension,
                        ".csproj",
                        StringComparison.OrdinalIgnoreCase))
                {
                    projects.Add(
                        Path.GetFullPath(entry));
                    if (projects.Count > maxProjects)
                    {
                        throw ResourceLimit(
                            $"Project-scope discovery found more than maxProjects={maxProjects} C# projects before semantic loading.");
                    }

                    continue;
                }

                if (!string.Equals(
                        extension,
                        ".sln",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        extension,
                        ".slnx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                solutions.Add(
                    Path.GetFullPath(entry));
                if (solutions.Count > maxSolutions)
                {
                    throw ResourceLimit(
                        $"Project-scope discovery found more than {maxSolutions} solution files.");
                }
            }
        }

        return new SemanticScopeDiscovery(
            projects
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            solutions
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool ShouldSkipSemanticDiscoveryDirectory(
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

    private static async Task<bool> SolutionContainsProjectAsync(
        string solutionPath,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var extension =
            Path.GetExtension(solutionPath);
        var solutionDirectory =
            Path.GetDirectoryName(solutionPath)!;

        if (string.Equals(
                extension,
                ".slnx",
                StringComparison.OrdinalIgnoreCase))
        {
            await using var stream =
                new FileStream(
                    solutionPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 16 * 1024,
                    options: FileOptions.Asynchronous |
                             FileOptions.SequentialScan);
            var document =
                await XDocument.LoadAsync(
                    stream,
                    LoadOptions.None,
                    cancellationToken);
            foreach (var projectElement in
                     document.Descendants()
                         .Where(
                             element =>
                                 string.Equals(
                                     element.Name.LocalName,
                                     "Project",
                                     StringComparison.OrdinalIgnoreCase)))
            {
                var pathValue =
                    projectElement.Attributes()
                        .FirstOrDefault(
                            attribute =>
                                string.Equals(
                                    attribute.Name.LocalName,
                                    "Path",
                                    StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                if (ProjectReferenceMatches(
                        solutionDirectory,
                        pathValue,
                        projectPath))
                {
                    return true;
                }
            }

            return false;
        }

        using var reader =
            new StreamReader(
                solutionPath,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(
                   cancellationToken) is { } line)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(
                    "Project(",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var equalsIndex =
                trimmed.IndexOf('=');
            if (equalsIndex < 0)
            {
                continue;
            }

            var fields =
                ExtractQuotedFields(
                    trimmed[(equalsIndex + 1)..]);
            if (fields.Count < 2)
            {
                continue;
            }

            if (ProjectReferenceMatches(
                    solutionDirectory,
                    fields[1],
                    projectPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ProjectReferenceMatches(
        string solutionDirectory,
        string? referencedPath,
        string projectPath)
    {
        if (string.IsNullOrWhiteSpace(referencedPath) ||
            !string.Equals(
                Path.GetExtension(referencedPath),
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate =
            Path.IsPathFullyQualified(referencedPath)
                ? Path.GetFullPath(referencedPath)
                : Path.GetFullPath(
                    Path.Combine(
                        solutionDirectory,
                        referencedPath));
        return PathsEqual(
            candidate,
            projectPath);
    }

    private static IReadOnlyList<string> ExtractQuotedFields(
        string value)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var character in value)
        {
            if (character == '"')
            {
                if (inQuotes)
                {
                    fields.Add(
                        current.ToString());
                    current.Clear();
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                current.Append(character);
            }
        }

        return fields;
    }

    private static async Task<Solution> OpenSolutionOrProjectAsync(
        MSBuildWorkspace workspace,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var extension =
            Path.GetExtension(
                inputPath);
        if (string.Equals(
                extension,
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            var project =
                await workspace.OpenProjectAsync(
                    inputPath,
                    progress: null,
                    cancellationToken);
            return project.Solution;
        }

        return await workspace.OpenSolutionAsync(
            inputPath,
            progress: null,
            cancellationToken);
    }

    private static Document[] FindAnchorDocuments(
        IReadOnlyList<Project> projects,
        SemanticRenameRequest request)
    {
        var candidates =
            projects
                .Where(
                    project =>
                        string.Equals(
                            project.Language,
                            LanguageNames.CSharp,
                            StringComparison.Ordinal) &&
                        (request.ProjectFullPath is null ||
                         PathsEqual(
                             project.FilePath,
                             request.ProjectFullPath)))
                .SelectMany(
                    project => project.Documents)
                .Where(
                    document =>
                        PathsEqual(
                            document.FilePath,
                            request.DocumentFullPath))
                .OrderBy(
                    document =>
                        document.Project.FilePath ??
                        document.Project.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (request.ProjectFullPath is not null &&
            candidates.Length > 1)
        {
            var distinctProjects =
                candidates
                    .Select(
                        document =>
                            document.Project.FilePath ??
                            document.Project.Name)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .Count();
            if (distinctProjects > 1)
            {
                throw Domain(
                    SourceEditCodes.SemanticDocumentAmbiguous,
                    "projectPath matched multiple loaded project contexts.",
                    request.ProjectFullPath);
            }
        }

        return candidates;
    }

    private static async Task<IReadOnlyList<SourceEditChangeInput>> BuildStructuredChangesAsync(
        string workspaceRoot,
        Solution originalSolution,
        Solution changedSolution,
        SemanticRenameRequest request,
        CancellationToken cancellationToken)
    {
        var solutionChanges =
            changedSolution.GetChanges(
                originalSolution);
        if (solutionChanges.GetAddedProjects().Any() ||
            solutionChanges.GetRemovedProjects().Any())
        {
            throw Domain(
                SourceEditCodes.SemanticProposalInvalid,
                "Semantic rename attempted to add or remove projects, which is outside the rename transaction contract.");
        }

        var proposals =
            new Dictionary<string, SemanticFileProposal>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projectChange.GetAddedDocuments().Any() ||
                projectChange.GetRemovedDocuments().Any())
            {
                throw Domain(
                    SourceEditCodes.SemanticProposalInvalid,
                    "Semantic rename attempted to add or remove documents. RenameFile is intentionally disabled for the initial semantic capability.");
            }

            foreach (var documentId in
                     projectChange.GetChangedDocuments(
                         onlyGetDocumentsWithTextChanges: true))
            {
                var oldDocument =
                    originalSolution.GetDocument(
                        documentId);
                var newDocument =
                    changedSolution.GetDocument(
                        documentId);
                if (oldDocument is null ||
                    newDocument is null ||
                    string.IsNullOrWhiteSpace(
                        oldDocument.FilePath) ||
                    string.IsNullOrWhiteSpace(
                        newDocument.FilePath))
                {
                    throw Domain(
                        SourceEditCodes.SemanticProposalInvalid,
                        "Roslyn returned a changed document without a stable physical source path.");
                }

                var oldFullPath =
                    Path.GetFullPath(
                        oldDocument.FilePath);
                var newFullPath =
                    Path.GetFullPath(
                        newDocument.FilePath);
                if (!PathsEqual(
                        oldFullPath,
                        newFullPath))
                {
                    throw Domain(
                        SourceEditCodes.SemanticProposalInvalid,
                        "Roslyn returned a file-path rename even though RenameFile is disabled.",
                        oldFullPath);
                }

                if (!SourceWorkspaceClassifier.IsContainedPath(
                        oldFullPath,
                        workspaceRoot))
                {
                    throw Domain(
                        SourceEditCodes.SemanticProposalInvalid,
                        "Roslyn returned a changed document outside the declared workspace.",
                        oldFullPath);
                }

                SourceWorkspaceClassifier.EnsureNoReparsePoint(
                    workspaceRoot,
                    oldFullPath);
                var relative =
                    SourceWorkspaceClassifier.GetRelativePath(
                        workspaceRoot,
                        oldFullPath);
                var snapshot =
                    await SourceTextCodec.ReadSnapshotAsync(
                        oldFullPath,
                        relative,
                        requireText: true,
                        cancellationToken);
                if (!snapshot.Exists ||
                    snapshot.Document is null ||
                    snapshot.Revision is null)
                {
                    throw Domain(
                        SourceEditCodes.SemanticSnapshotMismatch,
                        "A Roslyn-changed document is missing from the live source snapshot.",
                        oldFullPath);
                }

                var oldText =
                    await oldDocument.GetTextAsync(
                        cancellationToken);
                EnsureRoslynTextMatchesSnapshot(
                    oldText,
                    snapshot,
                    oldFullPath);
                var newText =
                    await newDocument.GetTextAsync(
                        cancellationToken);
                var textChanges =
                    newText
                        .GetTextChanges(
                            oldText)
                        .ToArray();
                if (textChanges.Length == 0)
                {
                    continue;
                }

                var edits =
                    textChanges
                        .Select(
                            change =>
                                ToSourceEditRange(
                                    oldText,
                                    change))
                        .ToArray();
                var proposal =
                    new SemanticFileProposal(
                        relative,
                        snapshot.Revision,
                        newText.ToString(),
                        edits);
                if (proposals.TryGetValue(
                        relative,
                        out var existing))
                {
                    if (!string.Equals(
                            existing.ProposedText,
                            proposal.ProposedText,
                            StringComparison.Ordinal))
                    {
                        throw Domain(
                            SourceEditCodes.SemanticSymbolAmbiguous,
                            "Linked project contexts produced contradictory rename proposals for the same physical source file.",
                            oldFullPath);
                    }

                    continue;
                }

                proposals[relative] =
                    proposal;
            }
        }

        if (proposals.Count == 0)
        {
            throw Domain(
                SourceEditCodes.SemanticNoChanges,
                "Roslyn resolved the symbol but the requested rename produced no source changes.");
        }

        if (proposals.Count >
            request.MaxChangedDocuments)
        {
            throw ResourceLimit(
                $"Semantic rename changed {proposals.Count} physical documents, exceeding maxChangedDocuments={request.MaxChangedDocuments}.");
        }

        long totalChangedCharacters = 0;
        foreach (var proposal in proposals.Values)
        {
            foreach (var edit in proposal.Edits)
            {
                totalChangedCharacters =
                    checked(
                        totalChangedCharacters +
                        (edit.ExpectedText?.Length ?? 0) +
                        edit.NewText.Length);
            }
        }

        if (totalChangedCharacters >
            request.MaxTotalChangedCharacters)
        {
            throw ResourceLimit(
                $"Semantic rename proposal contains {totalChangedCharacters} changed characters, exceeding maxTotalChangedCharacters={request.MaxTotalChangedCharacters}.");
        }

        return proposals
            .Values
            .OrderBy(
                proposal => proposal.RelativePath,
                StringComparer.Ordinal)
            .Select(
                proposal =>
                    new SourceEditChangeInput
                    {
                        Operation = "update",
                        Path =
                            proposal.RelativePath,
                        ExpectedRevision =
                            proposal.ExpectedRevision,
                        Edits =
                            proposal.Edits,
                    })
            .ToArray();
    }

    private static SourceEditTextRangeInput ToSourceEditRange(
        SourceText oldText,
        TextChange change)
    {
        var start =
            oldText.Lines.GetLinePosition(
                change.Span.Start);
        var end =
            oldText.Lines.GetLinePosition(
                change.Span.End);
        return new SourceEditTextRangeInput
        {
            StartLine = start.Line,
            StartCharacter = start.Character,
            EndLine = end.Line,
            EndCharacter = end.Character,
            ExpectedText =
                oldText.ToString(
                    change.Span),
            NewText =
                change.NewText ??
                string.Empty,
        };
    }

    private static int GetUtf16Position(
        SourceText text,
        int line,
        int character,
        string path)
    {
        if (line < 0 ||
            line >= text.Lines.Count)
        {
            throw Domain(
                SourceEditCodes.SemanticInputInvalid,
                $"line={line} is outside the source document.",
                path);
        }

        var sourceLine =
            text.Lines[line];
        if (character < 0 ||
            character >
                sourceLine.Span.Length)
        {
            throw Domain(
                SourceEditCodes.SemanticInputInvalid,
                $"character={character} is outside line={line}. Character offsets are zero-based UTF-16 code units.",
                path);
        }

        return sourceLine.Start + character;
    }

    private static void EnsureRoslynTextMatchesSnapshot(
        SourceText roslynText,
        SourceFileSnapshot snapshot,
        string path)
    {
        if (snapshot.Document is null ||
            !string.Equals(
                roslynText.ToString(),
                snapshot.Document.Text,
                StringComparison.Ordinal))
        {
            throw Domain(
                SourceEditCodes.SemanticSnapshotMismatch,
                "Roslyn's in-memory source text does not exactly match Talvora's revisioned disk snapshot. No mutation was attempted.",
                path);
        }
    }

    private static void DrainWorkspaceDiagnostics(
        MSBuildWorkspace workspace,
        ConcurrentQueue<WorkspaceDiagnostic> queued,
        SemanticExecutionContext context)
    {
        foreach (var diagnostic in queued)
        {
            context.AddWorkspaceDiagnostic(
                diagnostic);
        }

        foreach (var diagnostic in workspace.Diagnostics)
        {
            context.AddWorkspaceDiagnostic(
                diagnostic);
        }
    }

    private static void RejectWorkspaceFailures(
        SemanticRenameRequest request,
        SemanticExecutionContext context)
    {
        if (context.WorkspaceFailureCount == 0)
        {
            return;
        }

        throw new SourceEditDomainException(
            SourceEditCodes.SemanticWorkspaceLoadFailed,
            $"{SourceEditCodes.SemanticWorkspaceLoadFailed}: Roslyn/MSBuild reported {context.WorkspaceFailureCount} workspace load failure event(s). No mutation was attempted.",
            request.SolutionOrProjectFullPath,
            new Dictionary<string, string>
            {
                ["failureCount"] =
                    context.WorkspaceFailureCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                ["firstFailure"] =
                    context.FirstWorkspaceFailure ??
                    "Roslyn/MSBuild workspace failure.",
            });
    }

    private static async Task EnsureCompilationHealthyAsync(
        Solution solution,
        SemanticExecutionContext context,
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        var errorCount = 0;
        SemanticEditDiagnostic? first = null;
        foreach (var project in solution.Projects
                     .Where(
                         project =>
                             string.Equals(
                                 project.Language,
                                 LanguageNames.CSharp,
                                 StringComparison.Ordinal))
                     .OrderBy(
                         project =>
                             project.FilePath ??
                             project.Name,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation =
                await project.GetCompilationAsync(
                    cancellationToken)
                ?? throw Domain(
                    SourceEditCodes.SemanticWorkspaceLoadFailed,
                    "Roslyn did not produce a C# compilation for a loaded project.",
                    project.FilePath);
            foreach (var diagnostic in
                     compilation.GetDiagnostics(
                         cancellationToken))
            {
                if (diagnostic.IsSuppressed ||
                    diagnostic.Severity !=
                    DiagnosticSeverity.Error)
                {
                    continue;
                }

                errorCount++;
                var semanticDiagnostic =
                    context.AddCompilerDiagnostic(
                        diagnostic,
                        project.FilePath);
                first ??=
                    semanticDiagnostic;
            }
        }

        if (errorCount == 0)
        {
            return;
        }

        var details =
            new Dictionary<string, string>
            {
                ["compilerErrorCount"] =
                    errorCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
            };
        if (first is not null)
        {
            details["firstCompilerError"] =
                $"{first.Code}: {first.Message}";
            if (!string.IsNullOrWhiteSpace(
                    first.ProjectPath))
            {
                details["firstProject"] =
                    first.ProjectPath!;
            }
        }

        throw new SourceEditDomainException(
            errorCode,
            $"{errorCode}: {message} Compiler error count={errorCount}.",
            first?.DocumentPath ??
            first?.ProjectPath,
            details);
    }

    private static SourceEditDomainException LoadFailure(
        string message,
        string? path,
        SemanticExecutionContext context,
        Exception innerException)
    {
        var details =
            new Dictionary<string, string>
            {
                ["diagnosticCount"] =
                    context.Diagnostics.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
            };
        if (context.Diagnostics.Count > 0)
        {
            details["firstDiagnostic"] =
                context.Diagnostics[0].Message;
        }

        return new SourceEditDomainException(
            SourceEditCodes.SemanticWorkspaceLoadFailed,
            $"{SourceEditCodes.SemanticWorkspaceLoadFailed}: {message}",
            path,
            details,
            innerException);
    }

    private static SourceEditDomainException Domain(
        string code,
        string message,
        string? path = null,
        Exception? innerException = null) =>
        new(
            code,
            $"{code}: {message}",
            path,
            innerException: innerException);

    private static SourceEditDomainException ResourceLimit(
        string message) =>
        Domain(
            SourceEditCodes.ResourceLimit,
            message);

    private static bool PathsEqual(
        string? left,
        string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    left)),
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private sealed record SemanticFileProposal(
        string RelativePath,
        string ExpectedRevision,
        string ProposedText,
        IReadOnlyList<SourceEditTextRangeInput> Edits);

    private sealed record SemanticLoadScope(
        string Path,
        string Kind);

    private sealed record SemanticScopeDiscovery(
        IReadOnlyList<string> ProjectPaths,
        IReadOnlyList<string> SolutionPaths);

    private sealed record SemanticDurableReceipt(
        int SchemaVersion,
        string Operation,
        SemanticWorkspaceReceipt? Workspace,
        SemanticSymbolReceipt? Symbol,
        IReadOnlyList<SemanticEditDiagnostic> Diagnostics)
    {
        public static SemanticDurableReceipt? Parse(
            string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            SemanticDurableReceipt? receipt;
            try
            {
                receipt =
                    JsonSerializer.Deserialize<SemanticDurableReceipt>(
                        json);
            }
            catch (JsonException ex)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    "Durable semantic receipt metadata is unreadable.",
                    innerException: ex);
            }

            if (receipt is null ||
                receipt.SchemaVersion != 1 ||
                !string.Equals(
                    receipt.Operation,
                    "rename",
                    StringComparison.Ordinal) ||
                receipt.Diagnostics is null)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.TransactionRecoveryRequired,
                    "Durable semantic receipt metadata has an unsupported or incomplete schema.");
            }

            return receipt;
        }
    }

    private sealed record ResolvedSemanticSymbol(
        ISymbol Symbol,
        string IdentityCanonical,
        string IdentityHash,
        string Display,
        string? ProjectPath,
        IReadOnlyList<string> DeclarationPaths)
    {
        public static ResolvedSemanticSymbol Create(
            ISymbol symbol,
            string? projectPath)
        {
            var declarations =
                symbol
                    .Locations
                    .Where(
                        location =>
                            location.IsInSource &&
                            location.SourceTree?.FilePath is not null)
                    .Select(
                        location =>
                            $"{Path.GetFullPath(location.SourceTree!.FilePath)}:{location.SourceSpan.Start}:{location.SourceSpan.Length}")
                    .OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            var display =
                symbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat);
            var containing =
                symbol.ContainingSymbol?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) ??
                string.Empty;
            var assembly =
                symbol.ContainingAssembly?.Identity.ToString() ??
                string.Empty;
            var canonical =
                string.Join(
                    "\n",
                    symbol.Kind.ToString(),
                    symbol.Name,
                    symbol.MetadataName,
                    display,
                    containing,
                    assembly,
                    string.Join(
                        "|",
                        declarations));
            var identityHash =
                SourceEditRevision.Format(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            canonical)));

            return new ResolvedSemanticSymbol(
                symbol,
                canonical,
                identityHash,
                display,
                projectPath,
                declarations);
        }

        public SemanticSymbolReceipt ToReceipt(
            string newName,
            IReadOnlyList<ResolvedSemanticSymbol> contexts) =>
            new(
                Symbol.Name,
                newName,
                Symbol.Kind.ToString(),
                Display,
                IdentityHash,
                contexts
                    .Select(
                        context => context.ProjectPath)
                    .Where(
                        path =>
                            !string.IsNullOrWhiteSpace(
                                path))
                    .Select(
                        path =>
                            path!)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                contexts
                    .SelectMany(
                        context =>
                            context.DeclarationPaths)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private sealed class SemanticExecutionContext
    {
        private readonly int maxDiagnostics;
        private readonly List<SemanticEditDiagnostic> diagnostics = [];
        private readonly HashSet<string> diagnosticKeys =
            new(
                StringComparer.Ordinal);
        private bool diagnosticsTruncated;

        public SemanticExecutionContext(
            int maxDiagnostics)
        {
            this.maxDiagnostics =
                maxDiagnostics;
        }

        public SemanticWorkspaceReceipt? Workspace { get; set; }
        public SemanticSymbolReceipt? Symbol { get; set; }
        public int WorkspaceFailureCount { get; private set; }
        public string? FirstWorkspaceFailure { get; private set; }
        public IReadOnlyList<SemanticEditDiagnostic> Diagnostics =>
            diagnostics;

        public string ToDurableReceiptJson() =>
            JsonSerializer.Serialize(
                new SemanticDurableReceipt(
                    1,
                    "rename",
                    Workspace,
                    Symbol,
                    diagnostics.ToArray()));

        public void AddWorkspaceDiagnostic(
            WorkspaceDiagnostic diagnostic)
        {
            if (diagnostics.Count >=
                maxDiagnostics)
            {
                if (diagnostic.Kind ==
                    WorkspaceDiagnosticKind.Failure)
                {
                    WorkspaceFailureCount++;
                    FirstWorkspaceFailure ??=
                        diagnostic.Message;
                }

                if (!diagnosticsTruncated &&
                    diagnostics.Count > 0)
                {
                    diagnosticsTruncated = true;
                    diagnostics[^1] =
                        new SemanticEditDiagnostic(
                            "talvora-semantic",
                            "warning",
                            "SEMANTIC_DIAGNOSTICS_TRUNCATED",
                            $"Semantic diagnostics exceeded maxDiagnostics={maxDiagnostics}; additional diagnostics were omitted.",
                            null,
                            null);
                }

                return;
            }

            var severity =
                diagnostic.Kind ==
                WorkspaceDiagnosticKind.Failure
                    ? "failure"
                    : "warning";
            var key =
                $"{severity}\n{diagnostic.Message}";
            if (diagnosticKeys.Count <
                    maxDiagnostics &&
                !diagnosticKeys.Add(
                    key))
            {
                return;
            }

            if (diagnostic.Kind ==
                WorkspaceDiagnosticKind.Failure)
            {
                WorkspaceFailureCount++;
                FirstWorkspaceFailure ??=
                    diagnostic.Message;
            }

            if (diagnostics.Count >=
                maxDiagnostics)
            {
                if (!diagnosticsTruncated &&
                    diagnostics.Count > 0)
                {
                    diagnosticsTruncated = true;
                    diagnostics[^1] =
                        new SemanticEditDiagnostic(
                            "talvora-semantic",
                            "warning",
                            "SEMANTIC_DIAGNOSTICS_TRUNCATED",
                            $"Semantic diagnostics exceeded maxDiagnostics={maxDiagnostics}; additional diagnostics were omitted.",
                            null,
                            null);
                }

                return;
            }

            diagnostics.Add(
                new SemanticEditDiagnostic(
                    "msbuild-workspace",
                    severity,
                    diagnostic.Kind.ToString(),
                    diagnostic.Message,
                    null,
                    null));
        }

        public SemanticEditDiagnostic AddCompilerDiagnostic(
            Diagnostic diagnostic,
            string? projectPath)
        {
            var message =
                diagnostic.GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture);
            var documentPath =
                diagnostic.Location.IsInSource
                    ? diagnostic.Location.SourceTree?.FilePath
                    : null;
            var item =
                new SemanticEditDiagnostic(
                    "csharp-compiler",
                    "error",
                    diagnostic.Id,
                    message,
                    projectPath,
                    documentPath);
            var key =
                string.Join(
                    "\n",
                    item.Source,
                    item.Code,
                    item.Message,
                    item.ProjectPath ?? string.Empty,
                    item.DocumentPath ?? string.Empty,
                    diagnostic.Location.IsInSource
                        ? diagnostic.Location.SourceSpan.Start.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty,
                    diagnostic.Location.IsInSource
                        ? diagnostic.Location.SourceSpan.Length.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty);
            if (diagnosticKeys.Count <
                    maxDiagnostics &&
                !diagnosticKeys.Add(
                    key))
            {
                return item;
            }

            if (diagnostics.Count >=
                maxDiagnostics)
            {
                if (!diagnosticsTruncated &&
                    diagnostics.Count > 0)
                {
                    diagnosticsTruncated = true;
                    diagnostics[^1] =
                        new SemanticEditDiagnostic(
                            "talvora-semantic",
                            "warning",
                            "SEMANTIC_DIAGNOSTICS_TRUNCATED",
                            $"Semantic diagnostics exceeded maxDiagnostics={maxDiagnostics}; additional diagnostics were omitted.",
                            null,
                            null);
                }

                return item;
            }

            diagnostics.Add(item);
            return item;
        }
    }

    private sealed record SemanticRenameRequest(
        string SolutionOrProjectFullPath,
        string InputKind,
        string DocumentFullPath,
        int Line,
        int Character,
        string ExpectedRevision,
        string NewName,
        string? ProjectFullPath,
        string? ExpectedSymbolName,
        bool RenameOverloads,
        bool RenameInStrings,
        bool RenameInComments,
        int MaxProjects,
        int MaxDocuments,
        int MaxChangedDocuments,
        long MaxTotalChangedCharacters,
        int MaxDiagnostics,
        int TimeoutSeconds,
        bool ValidateSyntax)
    {
        public static SemanticRenameRequest Normalize(
            string workspaceRoot,
            string solutionOrProjectPath,
            string documentPath,
            int line,
            int character,
            string expectedRevision,
            string newName,
            string? projectPath,
            string? expectedSymbolName,
            bool renameOverloads,
            bool renameInStrings,
            bool renameInComments,
            int maxProjects,
            int maxDocuments,
            int maxChangedDocuments,
            long maxTotalChangedCharacters,
            int maxDiagnostics,
            int timeoutSeconds,
            bool validateSyntax)
        {
            var inputPath =
                SourceWorkspaceClassifier.ResolveWorkspacePath(
                    workspaceRoot,
                    solutionOrProjectPath);
            if (!File.Exists(
                    inputPath))
            {
                throw Domain(
                    SourceEditCodes.SemanticInputInvalid,
                    "solutionOrProjectPath was not found.",
                    inputPath);
            }

            var extension =
                Path.GetExtension(
                    inputPath);
            var inputKind =
                extension.ToLowerInvariant() switch
                {
                    ".sln" => "solution",
                    ".slnx" => "solution",
                    ".csproj" => "project",
                    _ => throw Domain(
                        SourceEditCodes.SemanticInputInvalid,
                        "solutionOrProjectPath must point to a .sln, .slnx, or .csproj file.",
                        inputPath),
                };

            var sourcePath =
                SourceWorkspaceClassifier.ResolveWorkspacePath(
                    workspaceRoot,
                    documentPath);
            if (!File.Exists(
                    sourcePath) ||
                !string.Equals(
                    Path.GetExtension(
                        sourcePath),
                    ".cs",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Domain(
                    SourceEditCodes.SemanticInputInvalid,
                    "documentPath must point to an existing C# source file.",
                    sourcePath);
            }

            string? projectFullPath = null;
            if (!string.IsNullOrWhiteSpace(
                    projectPath))
            {
                projectFullPath =
                    SourceWorkspaceClassifier.ResolveWorkspacePath(
                        workspaceRoot,
                        projectPath);
                if (!File.Exists(
                        projectFullPath) ||
                    !string.Equals(
                        Path.GetExtension(
                            projectFullPath),
                        ".csproj",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Domain(
                        SourceEditCodes.SemanticInputInvalid,
                        "projectPath must point to an existing .csproj when supplied.",
                        projectFullPath);
                }
            }

            if (line < 0 ||
                character < 0)
            {
                throw Domain(
                    SourceEditCodes.SemanticInputInvalid,
                    "line and character must be zero-based non-negative UTF-16 coordinates.",
                    sourcePath);
            }

            if (string.IsNullOrWhiteSpace(
                    newName) ||
                !string.Equals(
                    newName,
                    newName.Trim(),
                    StringComparison.Ordinal) ||
                !SyntaxFacts.IsValidIdentifier(
                    newName))
            {
                throw Domain(
                    SourceEditCodes.SemanticInputInvalid,
                    $"newName '{newName}' is not a valid C# identifier.");
            }

            if (maxProjects <= 0 ||
                maxDocuments <= 0 ||
                maxChangedDocuments <= 0 ||
                maxTotalChangedCharacters <= 0 ||
                maxDiagnostics <= 0 ||
                timeoutSeconds <= 0 ||
                timeoutSeconds > 86400)
            {
                throw Domain(
                    SourceEditCodes.SemanticInputInvalid,
                    "Resource limits must be positive and timeoutSeconds must be in the range 1..86400.");
            }

            return new SemanticRenameRequest(
                inputPath,
                inputKind,
                sourcePath,
                line,
                character,
                SourceEditRevision.Normalize(
                    expectedRevision),
                newName,
                projectFullPath,
                string.IsNullOrWhiteSpace(
                    expectedSymbolName)
                    ? null
                    : expectedSymbolName,
                renameOverloads,
                renameInStrings,
                renameInComments,
                maxProjects,
                maxDocuments,
                maxChangedDocuments,
                maxTotalChangedCharacters,
                maxDiagnostics,
                timeoutSeconds,
                validateSyntax);
        }

        public string ComputeHash()
        {
            var payload =
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schema = 1,
                        operation = "rename",
                        solutionOrProjectPath =
                            SolutionOrProjectFullPath,
                        documentPath =
                            DocumentFullPath,
                        Line,
                        Character,
                        ExpectedRevision,
                        NewName,
                        projectPath =
                            ProjectFullPath,
                        ExpectedSymbolName,
                        RenameOverloads,
                        RenameInStrings,
                        RenameInComments,
                        MaxProjects,
                        MaxDocuments,
                        MaxChangedDocuments,
                        MaxTotalChangedCharacters,
                        MaxDiagnostics,
                        TimeoutSeconds,
                        ValidateSyntax,
                    });
            return SourceEditRevision.Format(
                SHA256.HashData(
                    payload));
        }
    }
}
