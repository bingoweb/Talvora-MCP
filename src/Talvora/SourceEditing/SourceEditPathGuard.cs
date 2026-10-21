using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Talvora.SourceEditing;

internal sealed class SourceEditPathGuard : IDisposable
{
    private const uint FileReadAttributes = 0x00000080;
    private const uint DeleteAccess = 0x00010000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileIdInfoClass = 18;
    private const int SharingViolation = 32;

    private readonly List<SafeFileHandle> handles = [];
    private readonly Dictionary<string, SafeFileHandle> directoryHandles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SourceEditDirectoryIdentity> identities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> createdDirectories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> unprotectedDirectories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> ownerNotPreserved =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> UnprotectedDirectories => unprotectedDirectories;
    public IReadOnlyCollection<string> OwnerNotPreserved => ownerNotPreserved;
    public System.Security.Principal.SecurityIdentifier? WorkspaceRootOwner { get; private set; }
    public void RecordOwnerNotPreserved(string path) => ownerNotPreserved.Add(path);

    private SourceEditPathGuard()
    {
    }

    public IReadOnlyList<SourceEditDirectoryIdentity> DirectoryIdentities =>
        identities.Values
            .OrderBy(identity => identity.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<SourceEditDirectoryIdentity> CapturePreparedIdentities(
        string workspaceRoot,
        IReadOnlyList<SourceEditPreparedFile> files)
    {
        var captured =
            new Dictionary<string, SourceEditDirectoryIdentity>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EnumerateRelevantDirectories(
                     workspaceRoot,
                     files.SelectMany(GetPreparedPaths)))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            using var handle =
                OpenDirectory(
                    directory,
                    denyDeleteShare: false,
                    out _);
            captured[directory] =
                ReadAndValidateIdentity(
                    handle,
                    directory,
                    expected: null);
        }

        return captured.Values
            .OrderBy(identity => identity.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SourceEditPathGuard AcquirePrepared(
        SourceEditPreparedTransaction prepared)
    {
        var guard = new SourceEditPathGuard();
        guard.WorkspaceRootOwner = FileOwnership.TryGetOwner(prepared.ChangeSet.WorkspaceRoot);
        try
        {
            foreach (var expected in prepared.DirectoryIdentities
                         .OrderBy(identity => identity.Path.Length))
            {
                guard.HoldExisting(
                    expected.Path,
                    expected);
            }

            var missingDirectories =
                prepared.Files
                    .SelectMany(file => file.CreatedDirectories)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path.Length)
                    .ToArray();
            foreach (var directory in missingDirectories)
            {
                if (guard.identities.ContainsKey(directory))
                {
                    continue;
                }

                guard.CreateAndHoldDirectory(directory);
            }

            return guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    public static SourceEditPathGuard AcquireJournal(
        SourceEditJournalPlan plan)
    {
        if (plan.DirectoryIdentities is null ||
            plan.DirectoryIdentities.Count == 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "The durable transaction journal does not contain directory identity anchors required for path-safe recovery.",
                plan.WorkspaceRoot);
        }

        var guard = new SourceEditPathGuard();
        guard.WorkspaceRootOwner = FileOwnership.TryGetOwner(plan.WorkspaceRoot);
        try
        {
            foreach (var expected in plan.DirectoryIdentities
                         .OrderBy(identity => identity.Path.Length))
            {
                guard.HoldExisting(
                    expected.Path,
                    expected);
            }

            foreach (var directory in plan.Files
                         .SelectMany(file => file.CreatedDirectories)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (guard.identities.ContainsKey(directory))
                {
                    guard.createdDirectories.Add(directory);
                }
            }

            return guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    public static SourceEditPathGuard AcquireFileSystemTargets(
        string existingRoot,
        IEnumerable<string> targetPaths)
    {
        var guard = new SourceEditPathGuard();
        try
        {
            foreach (var directory in EnumerateRelevantDirectories(
                         existingRoot,
                         targetPaths))
            {
                if (Directory.Exists(directory))
                {
                    guard.HoldExisting(
                        directory,
                        expected: null);
                }
                else
                {
                    guard.CreateAndHoldDirectory(
                        directory);
                }
            }

            return guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    public void CommitCreatedDirectories() =>
        createdDirectories.Clear();

    public void Dispose()
    {
        var cleanupDirectories =
            createdDirectories
                .OrderByDescending(path => path.Length)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        for (var index = handles.Count - 1; index >= 0; index--)
        {
            handles[index].Dispose();
        }

        handles.Clear();
        directoryHandles.Clear();
        identities.Clear();
        createdDirectories.Clear();

        foreach (var directory in cleanupDirectories)
        {
            TryDeleteEmptyDirectory(directory);
        }
    }

    public void VerifyAnchors()
    {
        foreach (var expected in identities.Values
                     .OrderBy(identity => identity.Path.Length))
        {
            if (!Directory.Exists(expected.Path))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.Conflict,
                    "A source-edit directory identity anchor moved or disappeared before the file operation.",
                    expected.Path);
            }

            using var current =
                OpenDirectory(
                    expected.Path,
                    denyDeleteShare: false,
                    out _);
            _ = ReadAndValidateIdentity(
                current,
                expected.Path,
                expected);
        }
    }

    public void MoveFileByHandle(
        string sourcePath,
        string destinationPath,
        bool replaceExisting)
    {
        using var source =
            OpenMutationFile(sourcePath);
        VerifyAnchors();
        RenameFileHandle(
            source,
            destinationPath,
            replaceExisting);
    }

    public void DeleteFileByHandle(string path)
    {
        using var file =
            OpenMutationFile(path);
        VerifyAnchors();
        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 0, 1);
            if (!SetFileInformationByHandle(
                    file,
                    4,
                    buffer,
                    1))
            {
                var error = Marshal.GetLastWin32Error();
                throw new SourceEditDomainException(
                    SourceEditCodes.IoFailure,
                    $"Source-edit handle-bound delete failed with Win32 error {error}.",
                    path,
                    innerException:
                        new Win32Exception(error));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HoldExisting(
        string path,
        SourceEditDirectoryIdentity? expected)
    {
        if (identities.ContainsKey(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "A source-edit directory identity anchor disappeared before mutation.",
                path);
        }

        var handle =
            OpenDirectory(
                path,
                denyDeleteShare: true,
                out var renameProtected);
        if (!renameProtected)
        {
            unprotectedDirectories.Add(path);
        }
        try
        {
            var actual =
                ReadAndValidateIdentity(
                    handle,
                    path,
                    expected);
            handles.Add(handle);
            directoryHandles[path] = handle;
            identities[path] = actual;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private void CreateAndHoldDirectory(string path)
    {
        if (Directory.Exists(path) ||
            File.Exists(path))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "A source-edit destination directory appeared after preflight.",
                path);
        }

        var parent =
            Directory.GetParent(path)?.FullName
            ?? throw new SourceEditDomainException(
                SourceEditCodes.IoFailure,
                "A source-edit destination directory has no parent.",
                path);
        if (!identities.ContainsKey(parent))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "A source-edit destination parent is not identity-anchored.",
                parent);
        }

        if (!CreateDirectoryW(path, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            throw new SourceEditDomainException(
                error is 80 or 183
                    ? SourceEditCodes.Conflict
                    : SourceEditCodes.IoFailure,
                $"Source-edit destination directory creation failed with Win32 error {error}.",
                path,
                innerException:
                    new Win32Exception(error));
        }

        if (WorkspaceRootOwner is { } rootOwner &&
            !FileOwnership.TrySetOwner(path, rootOwner))
        {
            RecordOwnerNotPreserved(path);
        }

        SafeFileHandle? handle = null;
        try
        {
            handle =
                OpenDirectory(
                    path,
                    denyDeleteShare: true,
                    out var renameProtected);
            if (!renameProtected)
            {
                unprotectedDirectories.Add(path);
            }
            var actual =
                ReadAndValidateIdentity(
                    handle,
                    path,
                    expected: null);
            handles.Add(handle);
            directoryHandles[path] = handle;
            identities[path] = actual;
            createdDirectories.Add(path);
            handle = null;
        }
        catch
        {
            handle?.Dispose();
            TryDeleteEmptyDirectory(path);
            throw;
        }
    }

    private static IEnumerable<string> GetPreparedPaths(
        SourceEditPreparedFile file)
    {
        yield return file.Change.FullPath;
        if (file.Change.DestinationFullPath is not null)
        {
            yield return file.Change.DestinationFullPath;
        }

        if (file.StagePath is not null)
        {
            yield return file.StagePath;
        }

        if (file.BackupPath is not null)
        {
            yield return file.BackupPath;
        }
    }

    private static IEnumerable<string> EnumerateRelevantDirectories(
        string workspaceRoot,
        IEnumerable<string> paths)
    {
        var root =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspaceRoot));
        var directories =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                root,
            };

        foreach (var path in paths)
        {
            var directory =
                Path.GetDirectoryName(
                    Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var current =
                Path.TrimEndingDirectorySeparator(directory);
            while (true)
            {
                if (!IsSameOrDescendant(root, current))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.PathOutsideWorkspace,
                        "Source-edit directory identity path is outside the workspace.",
                        current);
                }

                directories.Add(current);
                if (string.Equals(
                        current,
                        root,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current =
                    Directory.GetParent(current)?.FullName
                    ?? throw new SourceEditDomainException(
                        SourceEditCodes.PathOutsideWorkspace,
                        "Source-edit directory identity path could not be traced to the workspace root.",
                        current);
            }
        }

        return directories
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSameOrDescendant(
        string root,
        string candidate)
    {
        if (string.Equals(
                root,
                candidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix =
            root.EndsWith(
                Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private SafeFileHandle OpenMutationFile(string path)
    {
        var handle =
            CreateFileW(
                path,
                DeleteAccess | FileReadAttributes,
                FileShare.Read |
                FileShare.Write |
                FileShare.Delete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint,
                IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                $"Source-edit file identity could not be opened for handle-bound mutation. Win32 error {error}.",
                path,
                innerException:
                    new Win32Exception(error));
        }

        if (!GetFileInformationByHandle(
                handle,
                out var info))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new SourceEditDomainException(
                SourceEditCodes.IoFailure,
                $"Source-edit file identity query failed with Win32 error {error}.",
                path,
                innerException:
                    new Win32Exception(error));
        }

        var attributes =
            (FileAttributes)info.FileAttributes;
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            handle.Dispose();
            throw new SourceEditDomainException(
                SourceEditCodes.ReparsePointUnsupported,
                "Source-edit handle-bound file mutation requires a regular non-reparse file.",
                path);
        }

        return handle;
    }

    private void RenameFileHandle(
        SafeFileHandle file,
        string destinationPath,
        bool replaceExisting)
    {
        var destinationDirectory =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    Path.GetDirectoryName(destinationPath)
                    ?? throw new SourceEditDomainException(
                        SourceEditCodes.IoFailure,
                        "Source-edit rename destination has no parent directory.",
                        destinationPath)));
        if (!directoryHandles.TryGetValue(
                destinationDirectory,
                out var parent))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionRecoveryRequired,
                "Source-edit rename destination parent is not identity-anchored.",
                destinationDirectory);
        }

        var fileName =
            Path.GetFileName(destinationPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.IoFailure,
                "Source-edit rename destination filename is empty.",
                destinationPath);
        }

        var fileNameBytes =
            Encoding.Unicode.GetBytes(fileName);
        var rootOffset =
            IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset =
            rootOffset + IntPtr.Size;
        var nameOffset =
            lengthOffset + sizeof(uint);
        var structureSize =
            IntPtr.Size == 8 ? 24 : 16;
        var bufferSize =
            structureSize + fileNameBytes.Length;
        var buffer =
            Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var index = 0; index < bufferSize; index++)
            {
                Marshal.WriteByte(
                    buffer,
                    index,
                    0);
            }

            Marshal.WriteByte(
                buffer,
                0,
                replaceExisting ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(
                buffer,
                rootOffset,
                parent.DangerousGetHandle());
            Marshal.WriteInt32(
                buffer,
                lengthOffset,
                fileNameBytes.Length);
            Marshal.Copy(
                fileNameBytes,
                0,
                IntPtr.Add(
                    buffer,
                    nameOffset),
                fileNameBytes.Length);

            var status =
                NtSetInformationFile(
                    file,
                    out _,
                    buffer,
                    (uint)bufferSize,
                    10);
            if (status < 0)
            {
                var error =
                    RtlNtStatusToDosError(status);
                throw new SourceEditDomainException(
                    error is 80 or 183
                        ? SourceEditCodes.Conflict
                        : SourceEditCodes.IoFailure,
                    $"Source-edit handle-bound rename failed with NTSTATUS 0x{status:X8} / Win32 error {error}.",
                    destinationPath,
                    innerException:
                        new Win32Exception(
                            unchecked((int)error)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle OpenDirectory(
        string path,
        bool denyDeleteShare,
        out bool renameProtected)
    {
        // Identity probes never request content access and share all operations.
        // The commit guard still blocks external parent renames if possible.
        var desiredAccess =
            denyDeleteShare ? FileReadAttributes | DeleteAccess : 0u;
        var share =
            FileShare.Read |
            FileShare.Write |
            (denyDeleteShare ? FileShare.None : FileShare.Delete);

        for (var attempt = 0; attempt <= 3; attempt++)
        {
            var handle =
                CreateFileW(
                    path,
                    desiredAccess,
                    share,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics |
                    FileFlagOpenReparsePoint,
                    IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                renameProtected = denyDeleteShare;
                return handle;
            }

            var error = Marshal.GetLastWin32Error();
            handle.Dispose();

            if (error == SharingViolation && attempt < 3)
            {
                Thread.Sleep(50 << attempt);
                continue;
            }

            if (error == SharingViolation && denyDeleteShare)
            {
                // A concurrent actor already holding DELETE access must not
                // prevent identity verification. A shared metadata-only
                // handle still confirms volume and 128-bit identity.
                var shared = OpenDirectory(path, denyDeleteShare: false, out _);
                renameProtected = false;
                return shared;
            }

            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                $"Klasörün kimliği doğrulanamadı. Başka bir uygulama klasörü geçici olarak kilitlemiş olabilir.",
                path,
                innerException:
                    new Win32Exception(error));
        }

        throw new InvalidOperationException("Unreachable source-edit directory open.");
    }

    private static SourceEditDirectoryIdentity ReadAndValidateIdentity(
        SafeFileHandle handle,
        string path,
        SourceEditDirectoryIdentity? expected)
    {
        if (!GetFileInformationByHandle(
                handle,
                out var info))
        {
            var error = Marshal.GetLastWin32Error();
            throw new SourceEditDomainException(
                SourceEditCodes.IoFailure,
                $"Source-edit directory identity query failed with Win32 error {error}.",
                path,
                innerException:
                    new Win32Exception(error));
        }

        var attributes =
            (FileAttributes)info.FileAttributes;
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "A source-edit directory identity path no longer refers to a directory.",
                path);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ReparsePointUnsupported,
                "Source-edit transactions do not follow reparse-point directory components.",
                path);
        }

        if (!GetFileInformationByHandleEx(
                handle,
                FileIdInfoClass,
                out var fileIdInfo,
                (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            var error = Marshal.GetLastWin32Error();
            throw new SourceEditDomainException(
                SourceEditCodes.IoFailure,
                $"Kaynak düzenleme klasörünün kimlik bilgileri okunamadı.",
                path,
                innerException: new Win32Exception(error));
        }

        var actual =
            new SourceEditDirectoryIdentity(
                Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(path)),
                fileIdInfo.VolumeSerialNumber,
                ((ulong)info.FileIndexHigh << 32) |
                info.FileIndexLow,
                new Guid(fileIdInfo.FileId),
                info.VolumeSerialNumber);
        if (expected is not null &&
            (expected.FileId128 is { } expected128
                ? actual.VolumeSerialNumber != expected.VolumeSerialNumber ||
                  actual.FileId128 != expected128
                : actual.LegacyVolumeSerialNumber != expected.VolumeSerialNumber ||
                  actual.FileId != expected.FileId))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "A source-edit directory identity changed after preflight.",
                path);
        }

        return actual;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle file,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(
        int status);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateDirectoryW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(
        string pathName,
        IntPtr securityAttributes);
}
