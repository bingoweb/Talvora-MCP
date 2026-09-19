using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace Talvora.Tray;

internal static class ManagedMcpSessionState
{
    private const string MutexName =
        @"Local\Talvora.ManagedMcpSessionState";
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private static string StatePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "session-state.json");

    public static bool IsManuallyStopped(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);

        lock (Sync)
        {
            using var lease = AcquireCrossProcessLease();
            var state = LoadCurrentScope();
            return state.ManualStops.Contains(
                mcpId,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void MarkManuallyStopped(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);

        lock (Sync)
        {
            using var lease = AcquireCrossProcessLease();
            var state = LoadCurrentScope();
            if (state.ManualStops.Contains(
                    mcpId,
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            WriteState(
                state with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    ManualStops =
                    [
                        .. state.ManualStops,
                        mcpId,
                    ],
                });
        }
    }

    public static void ClearManualStop(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);

        lock (Sync)
        {
            using var lease = AcquireCrossProcessLease();
            var state = LoadCurrentScope();
            var updated = state.ManualStops
                .Where(item => !string.Equals(
                    item,
                    mcpId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (updated.Length == state.ManualStops.Count)
            {
                return;
            }

            WriteState(
                state with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    ManualStops = [.. updated],
                });
        }
    }

    internal static void AssertContract()
    {
        var scope = GetCurrentScope();
        if (scope.SessionId !=
            Process.GetCurrentProcess().SessionId)
        {
            throw new InvalidOperationException(
                "Manual-stop session scope SessionId ile eşleşmiyor.");
        }

        if (string.IsNullOrWhiteSpace(scope.AuthenticationId))
        {
            throw new InvalidOperationException(
                "Manual-stop session scope AuthenticationId çözümlenemedi.");
        }

        var probeId = "self-test-" + Guid.NewGuid().ToString("N");
        try
        {
            MarkManuallyStopped(probeId);
            if (!IsManuallyStopped(probeId))
            {
                throw new InvalidOperationException(
                    "Manual-stop session state round-trip kaydı okunamadı.");
            }
        }
        finally
        {
            ClearManualStop(probeId);
        }

        if (IsManuallyStopped(probeId))
        {
            throw new InvalidOperationException(
                "Manual-stop session state round-trip temizliği başarısız.");
        }
    }

    private static SessionStateDocument LoadCurrentScope()
    {
        var scope = GetCurrentScope();

        if (!File.Exists(StatePath))
        {
            return CreateEmpty(scope);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SessionStateDocument>(
                File.ReadAllText(StatePath),
                JsonOptions);

            if (parsed is null ||
                parsed.SchemaVersion != 1 ||
                parsed.SessionId != scope.SessionId ||
                !string.Equals(
                    parsed.UserSid,
                    scope.UserSid,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    parsed.AuthenticationId,
                    scope.AuthenticationId,
                    StringComparison.Ordinal))
            {
                var fresh = CreateEmpty(scope);
                WriteState(fresh);
                return fresh;
            }

            return parsed;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                "Manual-stop session state could not be read; resetting current logon-session state.",
                ex);
            var fresh = CreateEmpty(scope);
            WriteState(fresh);
            return fresh;
        }
    }

    private static SessionStateDocument CreateEmpty(
        SessionScope scope) =>
        new(
            SchemaVersion: 1,
            SessionId: scope.SessionId,
            UserSid: scope.UserSid,
            AuthenticationId: scope.AuthenticationId,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ManualStops: []);

    private static void WriteState(
        SessionStateDocument state)
    {
        var directory = Path.GetDirectoryName(StatePath)
            ?? throw new InvalidOperationException(
                "Manual-stop session-state directory çözümlenemedi.");
        Directory.CreateDirectory(directory);

        var temp = StatePath + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, StatePath, overwrite: true);
    }

    private static SessionScope GetCurrentScope()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException(
                "Current Windows user SID çözümlenemedi.");

        var statistics = GetTokenStatistics(
            identity.Token);

        return new SessionScope(
            Process.GetCurrentProcess().SessionId,
            sid,
            $"{statistics.AuthenticationId.HighPart:X8}:{statistics.AuthenticationId.LowPart:X8}");
    }

    private static TokenStatistics GetTokenStatistics(
        IntPtr token)
    {
        if (!GetTokenInformation(
                token,
                TokenInformationClass.TokenStatistics,
                IntPtr.Zero,
                0,
                out var required) &&
            Marshal.GetLastWin32Error() != 122)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Token statistics boyutu okunamadı.");
        }

        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            if (!GetTokenInformation(
                    token,
                    TokenInformationClass.TokenStatistics,
                    buffer,
                    required,
                    out _))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Token statistics okunamadı.");
            }

            return Marshal.PtrToStructure<TokenStatistics>(
                buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IDisposable AcquireCrossProcessLease()
    {
        var mutex = new Mutex(
            initiallyOwned: false,
            name: MutexName);

        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Manual-stop session-state kilidi alınamadı.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Previous Tray died while holding the lock; ownership is
                // granted to the current process and the atomic file remains
                // the source of truth.
            }

            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class MutexLease : IDisposable
    {
        private Mutex? _mutex;

        public MutexLease(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            var mutex = Interlocked.Exchange(
                ref _mutex,
                null);
            if (mutex is null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    private sealed record SessionStateDocument(
        int SchemaVersion,
        int SessionId,
        string UserSid,
        string AuthenticationId,
        DateTimeOffset UpdatedAtUtc,
        List<string> ManualStops);

    private sealed record SessionScope(
        int SessionId,
        string UserSid,
        string AuthenticationId);

    private enum TokenInformationClass
    {
        TokenStatistics = 10,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId;
        public Luid AuthenticationId;
        public long ExpirationTime;
        public uint TokenType;
        public uint ImpersonationLevel;
        public uint DynamicCharged;
        public uint DynamicAvailable;
        public uint GroupCount;
        public uint PrivilegeCount;
        public Luid ModifiedId;
    }

    [DllImport(
        "advapi32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
