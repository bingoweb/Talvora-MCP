using System.IO;
namespace Talvora.Tray;

internal static class ControlCenterAdminCredentialStore
{
    public static string PathName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "openai-admin-key.dpapi");

    internal static char[] CopyTransientKey(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var trimmed = password.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return Array.Empty<char>();
        }

        if (trimmed.Length < 20)
        {
            throw new ArgumentException("OpenAI Admin API key biçimi geçersiz.");
        }

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                throw new ArgumentException("OpenAI Admin API key biçimi geçersiz.");
            }
        }

        return trimmed.ToArray();
    }

    internal static void ClearTransientKey(char[]? buffer)
    {
        if (buffer is not null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan()));
        }
    }

    public static bool DeleteLegacyIfPresent(string? testPath = null)
    {
        var path = testPath ?? PathName;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Kaydedilmiş OpenAI Admin API key silinemedi.", ex);
        }
    }

    internal static void AssertNoPersistenceContract()
    {
        var fixture = Path.Combine(
            Path.GetTempPath(), "Talvora-Admin-Upgrade-" +
            Guid.NewGuid().ToString("N") + ".dpapi");
        try
        {
            File.WriteAllBytes(fixture, [0x01, 0x02, 0x03]);
            if (!DeleteLegacyIfPresent(fixture) ||
                File.Exists(fixture) ||
                DeleteLegacyIfPresent(fixture))
            {
                throw new InvalidOperationException(
                    "Legacy Admin credential upgrade cleanup failed.");
            }

            var buffer = "temporary-fixture-secret-value".ToCharArray();
            ClearTransientKey(buffer);
            if (buffer.Any(character => character != '\0'))
            {
                throw new InvalidOperationException(
                    "Transient Admin credential buffer was not cleared.");
            }
        }
        finally
        {
            if (File.Exists(fixture))
            {
                File.Delete(fixture);
            }
        }
    }

    internal static void AssertSeparationContract()
    {
        if (string.Equals(
                Path.GetFullPath(PathName),
                Path.GetFullPath(BusinessTunnelClient.RuntimeCredentialPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Admin ve Runtime credential aynı DPAPI dosyasını kullanamaz.");
        }

        if (!Path.GetFileName(PathName).Contains(
                "admin",
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(BusinessTunnelClient.RuntimeCredentialPath).Contains(
                "runtime",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Admin/Runtime credential path contract failed.");
        }
    }
}