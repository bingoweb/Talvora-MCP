using System.IO;

namespace Talvora.Shared;

public sealed record TalvoraOwnedTempCleanupResult(
    int DeletedEntries,
    long ReclaimedBytes);

public static class TalvoraOwnedTempCleanup
{
    private const int MaximumScannedEntriesPerCandidate = 50_000;
    private const int MaximumCleanupCandidatesPerRun = 512;

    public static IReadOnlyList<string> DefaultPrefixes { get; } =
    [
        "Talvora-Deploy-",
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
    ];

    public static IReadOnlyList<string> DefaultExactNames { get; } =
    [
        "Talvora-Reset-And-Install.ps1",
        "talvora-npm-user.json",
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
        CancellationToken cancellationToken)
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
        var inspected = 0;

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     fullRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++inspected > MaximumCleanupCandidatesPerRun)
            {
                break;
            }

            var name = Path.GetFileName(entry);
            if (!MatchesOwnedName(name, prefixes, exactNames))
            {
                continue;
            }

            if (TryDeleteStaleEntry(
                    fullRoot,
                    entry,
                    cutoffUtc,
                    out var bytes))
            {
                deleted++;
                reclaimedBytes += bytes;
            }
        }

        return new TalvoraOwnedTempCleanupResult(
            deleted,
            reclaimedBytes);
    }

    public static bool TryDeleteStaleEntry(
        string allowedRoot,
        string path,
        DateTimeOffset cutoffUtc,
        out long reclaimedBytes)
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

            var scan = InspectTreeWithoutFollowingReparsePoints(path);
            if (!scan.Complete ||
                scan.NewestWriteUtc >= cutoffUtc)
            {
                return false;
            }

            DeleteTreeWithoutFollowingReparsePoints(path);
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

    private static TreeScanResult InspectTreeWithoutFollowingReparsePoints(
        string path)
    {
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
            var directory = stack.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
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
        string path)
    {
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
                    childDirectory.FullName);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        directory.Delete();
    }

    private sealed record TreeScanResult(
        bool Complete,
        long Bytes,
        DateTimeOffset NewestWriteUtc);
}
