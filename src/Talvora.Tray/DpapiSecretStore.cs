using System.IO;
using System.Text;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class DpapiSecretStore
{
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    public static bool Exists(string path) =>
        File.Exists(Path.GetFullPath(path));

    public static void WriteString(string path, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var plaintext = Encoding.Unicode.GetBytes(secret);
        byte[]? encrypted = null;

        try
        {
            encrypted = NativeDpapi.Protect(plaintext);
            var hex = Convert.ToHexString(encrypted);

            AtomicFile.WriteAllTextAsync(
                    path,
                    hex,
                    Utf8NoBom,
                    createBackup: false,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Array.Clear(plaintext);
            if (encrypted is not null)
            {
                Array.Clear(encrypted);
            }
        }
    }

    internal static void AssertRoundTripContract()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            ".dpapi-self-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        var expected = "talvora-dpapi-self-test-" + Guid.NewGuid().ToString("N");

        try
        {
            WriteString(path, expected);
            var actual = ReadString(path, "DPAPI self-test credential");
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Current-user DPAPI round-trip contract failed.");
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
            }
        }
    }

    public static string ReadString(string path, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Kaydedilmiş {displayName} bulunamadı.",
                fullPath);
        }

        var hex = File.ReadAllText(fullPath).Trim();
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            throw new InvalidOperationException(
                $"Kaydedilmiş {displayName} biçimi geçersiz.");
        }

        byte[] encrypted;
        try
        {
            encrypted = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Kaydedilmiş {displayName} DPAPI hex biçiminde değil.",
                ex);
        }

        byte[]? decrypted = null;
        try
        {
            decrypted = NativeDpapi.Unprotect(encrypted);
            var value = Encoding.Unicode.GetString(decrypted).TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Kaydedilmiş {displayName} boş.");
            }

            return value;
        }
        finally
        {
            Array.Clear(encrypted);
            if (decrypted is not null)
            {
                Array.Clear(decrypted);
            }
        }
    }
}