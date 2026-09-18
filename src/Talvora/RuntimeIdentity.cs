using System.Security.Principal;

namespace Talvora;

public static class TalvoraRuntimeIdentity
{
    private static readonly Lazy<string?> CachedSid = new(
        LoadSid,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? Sid => CachedSid.Value;

    private static string? LoadSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }
}
