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
        var entries = protectedEntries ?? DefaultEntries;
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

    private static bool IsSameOrBelow(string path, string entry)
    {
        var directory = Path.TrimEndingDirectorySeparator(entry);
        return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // For a missing child, resolve the nearest existing parent and append only its missing segments.
    private static string ResolveFinalPathOrParent(string path)
    {
        var candidate = path;
        var missing = new Stack<string>();
        while (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            missing.Push(Path.GetFileName(candidate));
            candidate = parent;
        }

        var finalPath = ResolveExistingFinalPath(candidate);
        while (missing.Count > 0)
        {
            finalPath = Path.Combine(finalPath, missing.Pop());
        }
        return Path.GetFullPath(finalPath);
    }

    private static string ResolveExistingFinalPath(string path)
    {
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
