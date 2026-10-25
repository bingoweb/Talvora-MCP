using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Talvora.Tray;

/// <summary>
/// UI6b (P-12, P-37): the signed-in user's view of <c>C:\ProgramData\Talvora</c>. S3's protected ACL gives broad
/// groups no write access; if one of them may write, the Control Center shows the approved notice. A root the
/// user cannot read (the protected case) or that does not exist needs no notice.
/// </summary>
internal static class ProgramDataAclCheck
{
    internal const string NoticeText = "Talvora klasör izinleri güncel değil. Kurulumu yeniden çalıştırın.";

    private static readonly SecurityIdentifier[] BroadPrincipals =
    [
        new(WellKnownSidType.BuiltinUsersSid, null),
        new(WellKnownSidType.AuthenticatedUserSid, null),
        new(WellKnownSidType.WorldSid, null),
        new(WellKnownSidType.InteractiveSid, null),
    ];

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes |
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    internal static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora");

    internal static bool IsStale(string root)
    {
        try
        {
            var directory = new DirectoryInfo(root);
            return directory.Exists && GrantsBroadWrite(directory.GetAccessControl());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    internal static bool GrantsBroadWrite(DirectorySecurity security)
    {
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier sid &&
                BroadPrincipals.Contains(sid) &&
                (rule.FileSystemRights & WriteRights) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
