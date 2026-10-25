using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Talvora.Tray;
using Talvora.Tray.ViewModels;

internal static partial class Program
{
    /// <summary>
    /// UI6b: the folder-permission notice appears only when a broad group may write to the Talvora ProgramData
    /// root; protected, unreadable or missing roots show nothing; the view model carries the approved P-12 text.
    /// </summary>
    private static void AssertUi6bAclNotice()
    {
        var root = Path.Combine(Path.GetTempPath(), "TalvoraUi6b-" + Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(root);
        try
        {
            var self = WindowsIdentity.GetCurrent().User!;
            var protectedAcl = new DirectorySecurity();
            protectedAcl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            protectedAcl.AddAccessRule(new FileSystemAccessRule(self, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            protectedAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(protectedAcl);
            if (ProgramDataAclCheck.IsStale(root))
                throw new InvalidOperationException("UI6b: a protected root (Users read only) must not show the notice.");

            var stale = directory.GetAccessControl();
            stale.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(stale);
            if (!ProgramDataAclCheck.IsStale(root))
                throw new InvalidOperationException("UI6b: Users with write access must show the notice.");

            if (ProgramDataAclCheck.IsStale(Path.Combine(root, "missing")))
                throw new InvalidOperationException("UI6b: a missing root must not show the notice.");

            var model = new SettingsViewModel();
            if (model.ShowAclNotice || model.AclNoticeText != "Talvora klasör izinleri güncel değil. Kurulumu yeniden çalıştırın.")
                throw new InvalidOperationException("UI6b: the notice must default to hidden and carry the approved P-12 text.");
            model.ShowAclNotice = true;
            if (!model.ShowAclNotice)
                throw new InvalidOperationException("UI6b: the notice visibility did not change.");
        }
        finally
        {
            var cleanup = new DirectorySecurity();
            cleanup.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            try { directory.SetAccessControl(cleanup); } catch (UnauthorizedAccessException) { }
            Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("UI6B_ACL_NOTICE_GREEN");
    }
}
