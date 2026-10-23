using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    internal static DirectorySecurity BuildProtectedRootAcl()
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None,
            AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None,
            AccessControlType.Allow));
        return sec;
    }

    internal static void ApplyProtectedProgramDataAcl(string root)
    {
        Directory.CreateDirectory(root);
        new DirectoryInfo(root).SetAccessControl(BuildProtectedRootAcl());

        // The tray must read Penpot's configuration and MCP files.
        var penpot = Path.Combine(root, "Penpot");
        if (!Directory.Exists(penpot)) { return; }
        var directory = new DirectoryInfo(penpot);
        var security = directory.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.PurgeAccessRules(users);
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
