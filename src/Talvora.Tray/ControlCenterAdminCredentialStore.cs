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

    public static bool Exists() =>
        DpapiSecretStore.Exists(PathName);

    public static void Save(string adminKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminKey);

        var trimmed = adminKey.Trim();
        if (trimmed.Length < 20 ||
            trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "OpenAI Admin API key biçimi geçersiz.",
                nameof(adminKey));
        }

        DpapiSecretStore.WriteString(PathName, trimmed);
    }

    public static string Read() =>
        DpapiSecretStore.ReadString(
            PathName,
            "OpenAI Admin API key");

    public static void Delete()
    {
        try
        {
            if (File.Exists(PathName))
            {
                File.Delete(PathName);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Kaydedilmiş OpenAI Admin API key silinemedi.",
                ex);
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