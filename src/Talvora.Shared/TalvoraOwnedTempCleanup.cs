using System.IO;

namespace Talvora.Shared;

public sealed record TalvoraOwnedTempCleanupResult(
    int DeletedEntries,
    long ReclaimedBytes,
    bool ScanLimitReached = false);

public static class TalvoraOwnedTempCleanup
{
    private const int MaximumScannedEntriesPerCandidate = 100_000;
    private const int MaximumCleanupCandidatesPerRun = 512;
    private const int MaximumDirectChildEntriesPerRun = 10_000;

    public static IReadOnlyList<string> DefaultPrefixes { get; } =
    [
        "Talvora-Deploy-",
        "Talvora-Setup-",
        "Talvora-ManagedMcpRegistry-",
        "Talvora-ManagedMcpOwnership-",
        "Talvora-ManagedMcpPrimary-",
        "Talvora-Archive-",
        "Talvora-Archive-Stage-",
        "Talvora-JobStorage-",
        "Talvora-JobStop-",
        "TalvoraDeploy-",
        "Talvora-159-live-",
        "Talvora-MCP-live-",
        "TalvoraReparse",
        "TalvoraGiteaLifecycle",
        "TalvoraBuildFingerprintAudit",
        "TalvoraHttpResumeAudit",
        "TalvoraArchiveJunctionAudit",
        "Talvora.AtomicFileRegression.",
        "Talvora-business-selftest-",
        "Talvora-tunnel-client-",
        "Talvora-reset-",
        "Talvora-Host-Swap-",
        "Talvora-Broker-",
        "TalvoraBug",
        "TalvoraSourceAudit",
        "TalvoraIdempotencyRetentionAudit",
        "TalvoraWalTail",
        "TalvoraRecoveryOwnershipAudit",
        "Talvora-Audit-",
        "TalvoraDependencyProvenanceAudit",
        "Talvora-Patches",
        "Talvora-Structural-",
        "Talvora Runner Test",
        "talvora-audit-",
        "talvora-host-",
        "talvora-analyzers",
        "talvora-latest",
        "talvora-shared",
        "talvora-tray",
        "talvora-pipe-test",
        "talvora-args",
        "talvora-wrapper",
        "talvora-processrunner",
        "talvora-onearg",
        "talvora-git-apply-probe",
        "talvora-run-",
        "talvora-vsdev-",
    ];

    public static IReadOnlyList<string> DefaultExactNames { get; } =
    [
        "Talvora-Reset-And-Install.ps1",
        "talvora-npm-user.json",
    ];

    public static IReadOnlyList<string> TestPrefixes { get; } =
    [
        "Talvora-ManagedMcpRegistry-",
        "Talvora-ManagedMcpOwnership-",
        "Talvora-ManagedMcpPrimary-",
        "Talvora-JobStorage-",
        "Talvora-JobStop-",
        "TalvoraReparse",
        "TalvoraGiteaLifecycle",
        "TalvoraBuildFingerprintAudit",
        "TalvoraHttpResumeAudit",
        "TalvoraArchiveJunctionAudit",
        "Talvora.AtomicFileRegression.",
        "Talvora-business-selftest-",
        "TalvoraBug",
        "TalvoraSourceAudit",
        "TalvoraIdempotencyRetentionAudit",
        "TalvoraWalTail",
        "TalvoraRecoveryOwnershipAudit",
        "Talvora-Audit-",
        "TalvoraDependencyProvenanceAudit",
        "Talvora-Structural-",
        "Talvora Runner Test",
        "talvora-audit-",
        "talvora-analyzers",
        "talvora-shared",
        "talvora-tray",
        "talvora-pipe-test",
        "talvora-args",
        "talvora-wrapper",
        "talvora-processrunner",
        "talvora-onearg",
        "talvora-git-apply-probe",
    ];

    public static bool IsOwnedTempName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return MatchesOwnedName(
            name,
            DefaultPrefixes,
            DefaultExactNames);
    }

    public static TalvoraOwnedTempCleanupResult CleanupTopLevel(
        string root,
        DateTimeOffset cutoffUtc,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> exactNames,
        CancellationToken cancellationToken,
        Func<string, bool>? candidateFilter = null,
        Func<string, bool>? treeEntryFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(exactNames);

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

        var deleted = 0;
        long reclaimedBytes = 0;
        var inspectedCandidates = 0;
        var scanLimitReached = false;

        foreach (var entry in EnumerateMatchingTopLevelCandidates(
                     fullRoot,
                     prefixes,
                     exactNames,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++inspectedCandidates > MaximumCleanupCandidatesPerRun)
            {
                scanLimitReached = true;
                break;
            }

            if (candidateFilter is not null &&
                !candidateFilter(entry))
            {
                continue;
            }

            if (TryDeleteStaleEntry(
                    fullRoot,
                    entry,
                    cutoffUtc,
                    out var bytes,
                    cancellationToken,
                    treeEntryFilter))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new TalvoraOwnedTempCleanupResult(
            deleted,
            reclaimedBytes,
            scanLimitReached);
    }

    public static TalvoraOwnedTempCleanupResult CleanupGuidDirectories(
        string root,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken,
        Func<string, bool>? candidateFilter = null,
        Func<string, bool>? treeEntryFilter = null) =>
        CleanupDirectChildren(
            root,
            "*",
            directories: true,
            static name => Guid.TryParseExact(
                name,
                "N",
                out _),
            cutoffUtc,
            cancellationToken,
            candidateFilter,
            treeEntryFilter);

    public static TalvoraOwnedTempCleanupResult CleanupGuidJsonFiles(
        string root,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken,
        Func<string, bool>? candidateFilter = null,
        Func<string, bool>? treeEntryFilter = null) =>
        CleanupDirectChildren(
            root,
            "*.json",
            directories: false,
            static name =>
                name.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(
                    Path.GetFileNameWithoutExtension(name),
                    "N",
                    out _),
            cutoffUtc,
            cancellationToken,
            candidateFilter,
            treeEntryFilter);

    private static TalvoraOwnedTempCleanupResult CleanupDirectChildren(
        string root,
        string searchPattern,
        bool directories,
        Func<string, bool> managedNameFilter,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken,
        Func<string, bool>? candidateFilter,
        Func<string, bool>? treeEntryFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        ArgumentNullException.ThrowIfNull(managedNameFilter);

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            return new TalvoraOwnedTempCleanupResult(0, 0);
        }

            var rootInfo = new DirectoryInfo(fullRoot);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new TalvoraOwnedTempCleanupResult(0, 0);
            }

            var deleted = 0;
            long reclaimedBytes = 0;
            var enumeratedEntries = 0;
            var cleanupCandidates = 0;
            var scanLimitReached = false;
            var entries = directories
                ? Directory.EnumerateDirectories(
                    fullRoot,
                    searchPattern,
                    SearchOption.TopDirectoryOnly)
                : Directory.EnumerateFiles(
                    fullRoot,
                    searchPattern,
                    SearchOption.TopDirectoryOnly);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++enumeratedEntries > MaximumDirectChildEntriesPerRun)
                {
                    scanLimitReached = true;
                    break;
                }

                if (!managedNameFilter(Path.GetFileName(entry)) ||
                    (candidateFilter is not null &&
                     !candidateFilter(entry)))
                {
                    continue;
                }

                if (++cleanupCandidates > MaximumCleanupCandidatesPerRun)
                {
                    scanLimitReached = true;
                    break;
                }

                if (TryDeleteStaleEntry(
                        fullRoot,
                        entry,
                        cutoffUtc,
                        out var bytes,
                        cancellationToken,
                        treeEntryFilter))
                {
                    deleted++;
                    reclaimedBytes += bytes;
                }
            }

            return new TalvoraOwnedTempCleanupResult(
                deleted,
                reclaimedBytes,
                scanLimitReached);
    }

    public static bool TryDeleteStaleEntry(
        string allowedRoot,
        string path,
        DateTimeOffset cutoffUtc,
        out long reclaimedBytes,
        CancellationToken cancellationToken = default,
        Func<string, bool>? treeEntryFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        reclaimedBytes = 0;
        try
        {
            if (!File.Exists(path) &&
                !Directory.Exists(path))
            {
                return false;
            }

            if (!IsPathUnderRoot(path, allowedRoot))
            {
                return false;
            }

            if (treeEntryFilter is not null &&
                !treeEntryFilter(path))
            {
                return false;
            }

            var scan = InspectTreeWithoutFollowingReparsePoints(
                path,
                cancellationToken,
                treeEntryFilter);
            if (!scan.Complete ||
                scan.NewestWriteUtc >= cutoffUtc)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Once deletion begins, finish the already-proven stale owned tree
            // instead of leaving a partially deleted tree whose directory
            // timestamps would postpone the remaining cleanup.
            DeleteTreeWithoutFollowingReparsePoints(
                path,
                treeEntryFilter);
            reclaimedBytes = scan.Bytes;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    public static bool IsPathUnderRoot(
        string path,
        string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            fullRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOwnedName(
        string name,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> exactNames)
    {
        if (exactNames.Any(candidate =>
                string.Equals(
                    candidate,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return prefixes.Any(prefix =>
            name.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateMatchingTopLevelCandidates(
        string root,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> exactNames,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var exactName in exactNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(exactName))
            {
                continue;
            }

            var path = Path.Combine(
                root,
                exactName);
            if ((File.Exists(path) ||
                 Directory.Exists(path)) &&
                seen.Add(Path.GetFullPath(path)))
            {
                yield return path;
            }
        }

        foreach (var prefix in prefixes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(
                         root,
                         prefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath) &&
                    MatchesOwnedName(
                        Path.GetFileName(fullPath),
                        prefixes,
                        exactNames))
                {
                    yield return fullPath;
                }
            }
        }
    }

    private static TreeScanResult InspectTreeWithoutFollowingReparsePoints(
        string path,
        CancellationToken cancellationToken,
        Func<string, bool>? treeEntryFilter)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return new TreeScanResult(
                true,
                file.Length,
                new DateTimeOffset(
                    file.LastWriteTimeUtc,
                    TimeSpan.Zero));
        }

        var root = new DirectoryInfo(path);
        if (!root.Exists)
        {
            return new TreeScanResult(
                false,
                0,
                DateTimeOffset.MaxValue);
        }

        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new TreeScanResult(
                true,
                0,
                new DateTimeOffset(
                    root.LastWriteTimeUtc,
                    TimeSpan.Zero));
        }

        long bytes = 0;
        var newest = new DateTimeOffset(
            root.LastWriteTimeUtc,
            TimeSpan.Zero);
        var scanned = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (treeEntryFilter is not null &&
                    !treeEntryFilter(entry.FullName))
                {
                    return new TreeScanResult(
                        false,
                        bytes,
                        newest);
                }

                if (++scanned > MaximumScannedEntriesPerCandidate)
                {
                    return new TreeScanResult(
                        false,
                        bytes,
                        newest);
                }

                var writeTime = new DateTimeOffset(
                    entry.LastWriteTimeUtc,
                    TimeSpan.Zero);
                if (writeTime > newest)
                {
                    newest = writeTime;
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is FileInfo file)
                {
                    bytes += file.Length;
                }
                else if (entry is DirectoryInfo child)
                {
                    stack.Push(child);
                }
            }
        }

        return new TreeScanResult(
            true,
            bytes,
            newest);
    }

    private static void DeleteTreeWithoutFollowingReparsePoints(
        string path,
        Func<string, bool>? treeEntryFilter)
    {
        EnsureDeletionEntryAllowed(
            path,
            treeEntryFilter);

        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return;
        }

        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            EnsureDeletionEntryAllowed(
                entry.FullName,
                treeEntryFilter);

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (entry is DirectoryInfo reparseDirectory)
                {
                    reparseDirectory.Delete();
                }
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }

                continue;
            }

            if (entry is DirectoryInfo childDirectory)
            {
                DeleteTreeWithoutFollowingReparsePoints(
                    childDirectory.FullName,
                    treeEntryFilter);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        directory.Delete();
    }

    private static void EnsureDeletionEntryAllowed(
        string path,
        Func<string, bool>? treeEntryFilter)
    {
        if (treeEntryFilter is not null &&
            !treeEntryFilter(path))
        {
            throw new IOException(
                "Cleanup safety filter rejected an entry during deletion.");
        }
    }

    private sealed record TreeScanResult(
        bool Complete,
        long Bytes,
        DateTimeOffset NewestWriteUtc);
}
