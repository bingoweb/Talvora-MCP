using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Talvora.Tray;

internal sealed record WindowsProcessIdentity(
    int ProcessId,
    int ParentProcessId,
    string ExecutableName,
    long StartTimeUtcTicks);

internal static class WindowsProcessTree
{
    private const uint Th32CsSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static WindowsProcessIdentity? FindClosestDescendant(
        int ancestorProcessId,
        params string[] executableNames)
    {
        if (ancestorProcessId <= 0 || executableNames.Length == 0)
        {
            return null;
        }

        var entries = Snapshot();
        var byPid = entries.ToDictionary(entry => entry.ProcessId);
        var names = executableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return entries
            .Where(entry => names.Contains(entry.ExecutableName))
            .Select(entry => new
            {
                Entry = entry,
                Depth = GetDepthToAncestor(
                    entry.ProcessId,
                    ancestorProcessId,
                    byPid),
            })
            .Where(item => item.Depth > 0)
            .OrderBy(item => item.Depth)
            .ThenBy(item => item.Entry.StartTimeUtcTicks)
            .Select(item => item.Entry)
            .FirstOrDefault();
    }

    public static bool IsSameLiveProcess(WindowsProcessIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
            {
                return false;
            }

            return process.StartTime
                .ToUniversalTime()
                .Ticks == identity.StartTimeUtcTicks;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            InvalidOperationException or
            Win32Exception)
        {
            return false;
        }
    }

    private static IReadOnlyList<WindowsProcessIdentity> Snapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var handle = CreateToolhelp32Snapshot(
            Th32CsSnapProcess,
            0);
        if (handle == InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows process snapshot alınamadı.");
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };

            var result = new List<WindowsProcessIdentity>();
            if (!Process32First(handle, ref entry))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 18)
                {
                    return result;
                }

                throw new Win32Exception(
                    error,
                    "Windows process snapshot başlatılamadı.");
            }

            do
            {
                var processId = unchecked((int)entry.ProcessId);
                if (processId > 0 &&
                    TryGetStartTimeUtcTicks(
                        processId,
                        out var startTimeUtcTicks))
                {
                    result.Add(
                        new WindowsProcessIdentity(
                            processId,
                            unchecked((int)entry.ParentProcessId),
                            entry.ExecutableFile ?? string.Empty,
                            startTimeUtcTicks));
                }

                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(handle, ref entry));

            return result;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static int GetDepthToAncestor(
        int processId,
        int ancestorProcessId,
        IReadOnlyDictionary<int, WindowsProcessIdentity> byPid)
    {
        var current = processId;
        var visited = new HashSet<int>();

        for (var depth = 0; depth < 64; depth++)
        {
            if (!visited.Add(current))
            {
                return -1;
            }

            if (!byPid.TryGetValue(current, out var entry))
            {
                return -1;
            }

            if (entry.ParentProcessId == ancestorProcessId)
            {
                return depth + 1;
            }

            if (entry.ParentProcessId <= 0 ||
                entry.ParentProcessId == current)
            {
                return -1;
            }

            current = entry.ParentProcessId;
        }

        return -1;
    }

    private static bool TryGetStartTimeUtcTicks(
        int processId,
        out long startTimeUtcTicks)
    {
        startTimeUtcTicks = 0;

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            startTimeUtcTicks = process.StartTime
                .ToUniversalTime()
                .Ticks;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            InvalidOperationException or
            Win32Exception)
        {
            return false;
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFile;
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags,
        uint processId);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
