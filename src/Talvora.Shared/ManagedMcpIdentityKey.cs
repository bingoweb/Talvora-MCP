using System.Security.Cryptography;
using System.Text;

namespace Talvora.Shared;

public static class ManagedMcpIdentityKey
{
    public static string Create(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var canonicalId =
            id.ToUpperInvariant();
        var bytes =
            Encoding.UTF8.GetBytes(
                canonicalId);
        var digest =
            SHA256.HashData(
                bytes);
        return
            "v2-" +
            Convert.ToHexString(digest)
                .ToLowerInvariant();
    }
}
