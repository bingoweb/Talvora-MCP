using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Talvora.Shared;

namespace Talvora.Tools;

internal static class ProtectedSecretPaths
{
    private const string DeniedMessage =
        "Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez.";

    private static readonly IReadOnlyList<string> DefaultEntries =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "Secrets"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "Context7", "api-key"),
    ];

    // Isolated regressions replace the set. Never use actual secret contents in tests.
    internal static IReadOnlyList<string>? EntriesOverrideForTests;

    // D89: lets a regression prove that an ordinary search does not open every entry for the guard.
    internal static int FinalPathResolutionsForTests;

    internal static void ThrowIfProtected(string path, IReadOnlyList<string>? protectedEntries = null)
    {
        if (IsProtected(path, protectedEntries))
        {
            throw new UnauthorizedAccessException(DeniedMessage);
        }
    }

    internal static bool IsProtected(string path, IReadOnlyList<string>? protectedEntries = null)
    {
        var fullPath = Path.GetFullPath(path);
        var entries = protectedEntries ?? EntriesOverrideForTests ?? DefaultEntries;
        var fullEntries = entries.Select(Path.GetFullPath).ToArray();

        if (fullEntries.Any(entry => IsSameOrBelow(fullPath, entry)))
        {
            return true;
        }

        var finalPath = ResolveFinalPathOrParent(fullPath);
        for (var i = 0; i < fullEntries.Length; i++)
        {
            if (IsSameOrBelow(finalPath, fullEntries[i]))
            {
                return true;
            }
        }
        return false;
    }

    internal static void ThrowIfDatabaseSourceProtected(string databaseSource)
    {
        if (databaseSource == ":memory:" ||
            databaseSource.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
            return;

        var path = databaseSource;
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            path = path[5..].Split('?', 2)[0];
            path = Uri.UnescapeDataString(path).Replace('/', '\\');
            var withoutLeadingSlashes = path.TrimStart('\\');
            if (withoutLeadingSlashes.Length >= 2 &&
                char.IsLetter(withoutLeadingSlashes[0]) &&
                withoutLeadingSlashes[1] == ':')
                path = withoutLeadingSlashes;
        }
        ThrowIfProtected(path);
    }

    internal static void ThrowIfContainsProtected(string directory)
    {
        if (ContainsProtected(directory))
        {
            throw new UnauthorizedAccessException(DeniedMessage);
        }
    }

    /// <summary>True when a protected entry lies inside <paramref name="directory"/> (given or final form).</summary>
    internal static bool ContainsProtected(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var finalDirectory = ResolveFinalPathOrParent(fullDirectory);
        foreach (var entry in EntriesOverrideForTests ?? DefaultEntries)
        {
            var fullEntry = Path.GetFullPath(entry);
            var finalEntry = ResolveFinalPathOrParent(fullEntry);
            if (IsSameOrBelow(fullEntry, fullDirectory) ||
                IsSameOrBelow(fullEntry, finalDirectory) ||
                IsSameOrBelow(finalEntry, fullDirectory) ||
                IsSameOrBelow(finalEntry, finalDirectory))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsSameOrBelow(string path, string entry)
    {
        var directory = Path.TrimEndingDirectorySeparator(entry);
        var prefix = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // For a missing child, resolve the nearest existing parent and append only its missing segments.
    private static string ResolveFinalPathOrParent(string path)
    {
        var candidate = path;
        var missing = new Stack<string>();
        while (true)
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                try
                {
                    var resolved = ResolveExistingFinalPath(candidate);
                    while (missing.Count > 0)
                        resolved = Path.Combine(resolved, missing.Pop());
                    return Path.GetFullPath(resolved);
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
                {
                    // Treat paths that cannot be opened as unresolved segments.
                }
            }
            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            missing.Push(Path.GetFileName(candidate));
            candidate = parent;
        }
    }

    private static string ResolveExistingFinalPath(string path)
    {
        Interlocked.Increment(ref FinalPathResolutionsForTests);
        using var handle = CreateFileW(
            Win32Path.Extended(path),
            desiredAccess: 0x00000080u,
            shareMode: 0x00000007u,
            securityAttributes: IntPtr.Zero,
            creationDisposition: 3u,
            flagsAndAttributes: 0x02000000u,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "The Windows filesystem could not resolve the final path.");
        }

        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            resolved = @"\\" + resolved[8..];
        }
        else if (resolved.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            resolved = resolved[4..];
        }
        return Path.GetFullPath(resolved);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, StringBuilder output, uint outputLength, uint flags);
}
