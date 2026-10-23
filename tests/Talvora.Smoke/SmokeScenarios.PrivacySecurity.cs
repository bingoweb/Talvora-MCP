using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static async Task RunPrivacySecurityAsync(string root)
    {
        var apiKey =
            string.Concat(
                "sk-",
                "proj-",
                "TalvoraRegressionSecret0123456789");
        const string bearerToken =
            "TalvoraBearerRegressionSecret0123456789";
        const string password =
            "TalvoraPasswordRegressionSecret";
        var githubToken =
            string.Concat(
                "gh",
                "p_",
                "0123456789abcdefghijklmnopqrstuv");
        var modalCredentials =
            new[]
            {
                string.Concat("ak-", "TalvoraModalTokenId0123456789"),
                string.Concat("as-", "TalvoraModalTokenSecret0123456789"),
                string.Concat("wk-", "TalvoraModalProxyKey0123456789"),
                string.Concat("ws-", "TalvoraModalProxySecret0123456789"),
                string.Concat("oc-", "TalvoraModalOauthClient0123456789"),
                string.Concat("ov-", "TalvoraModalOauthSecret0123456789"),
            };
        var jwt =
            string.Join(
                ".",
                "ey" + "JTalvoraHeader123",
                "ey" + "JTalvoraPayload456",
                "TalvoraSignature789");

        var diagnosticText =
            $"Authorization: Bearer {bearerToken}; " +
            $"api_key={apiKey}; password=\"{password}\"; " +
            $"{githubToken}; {string.Join("; ", modalCredentials)}; " +
            $"{jwt}; ExitCode=17";
        var redactedDiagnostic =
            FileLog.RedactSensitiveData(diagnosticText);

        if (redactedDiagnostic.Contains(
                bearerToken,
                StringComparison.Ordinal) ||
            redactedDiagnostic.Contains(
                apiKey,
                StringComparison.Ordinal) ||
            redactedDiagnostic.Contains(
                password,
                StringComparison.Ordinal) ||
            redactedDiagnostic.Contains(
                githubToken,
                StringComparison.Ordinal) ||
            modalCredentials.Any(credential =>
                redactedDiagnostic.Contains(
                    credential,
                    StringComparison.Ordinal)) ||
            redactedDiagnostic.Contains(
                jwt,
                StringComparison.Ordinal) ||
            !redactedDiagnostic.Contains(
                "ExitCode=17",
                StringComparison.Ordinal) ||
            !redactedDiagnostic.Contains(
                "[REDACTED]",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "FileLog sensitive-data redaction contract failed.");
        }

        var privacyLogPath =
            Path.Combine(root, "privacy-redaction.log");
        FileLog.Write(
            privacyLogPath,
            $"runtime api_key={apiKey} normal-marker",
            new InvalidOperationException(
                $"password={password}; Authorization=Bearer {bearerToken}"));
        var persistedLog =
            await File.ReadAllTextAsync(privacyLogPath);
        if (persistedLog.Contains(apiKey, StringComparison.Ordinal) ||
            persistedLog.Contains(password, StringComparison.Ordinal) ||
            persistedLog.Contains(bearerToken, StringComparison.Ordinal) ||
            !persistedLog.Contains(
                "normal-marker",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "FileLog persisted secret-redaction contract failed.");
        }

        var runnerType = typeof(InteractiveUserProcessRunner);
        var cleanupMethod = runnerType.GetMethod(
            "CleanupStaleRunDirectories",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "Interactive run stale-cleanup helper was not found.");
        var runnerScriptField = runnerType.GetField(
            "RunnerScript",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "Interactive runner script field was not found.");
        var runnerScript = runnerScriptField.GetRawConstantValue() as string
            ?? throw new InvalidOperationException(
                "Interactive runner script is unavailable.");

        var requestReadIndex = runnerScript.IndexOf(
            "Get-Content -Raw -LiteralPath $RequestPath",
            StringComparison.Ordinal);
        var requestDeleteIndex = runnerScript.IndexOf(
            "[IO.File]::Delete($RequestPath)",
            StringComparison.Ordinal);
        var helperLoadIndex = runnerScript.IndexOf(
            "[void][Reflection.Assembly]::LoadFrom",
            StringComparison.Ordinal);
        if (requestReadIndex < 0 ||
            requestDeleteIndex <= requestReadIndex ||
            helperLoadIndex <= requestDeleteIndex)
        {
            throw new InvalidOperationException(
                "Interactive runner does not erase request.json immediately after reading it.");
        }

        var runsRoot = Path.Combine(root, "interactive-runs");
        Directory.CreateDirectory(runsRoot);
        var staleWithoutLease = Path.Combine(runsRoot, Guid.NewGuid().ToString("N"));
        var staleWithLease = Path.Combine(runsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleWithoutLease);
        Directory.CreateDirectory(staleWithLease);

        var activeLeasePath = Path.Combine(staleWithLease, "active.lock");
        await File.WriteAllTextAsync(activeLeasePath, string.Empty);
        File.SetLastWriteTimeUtc(activeLeasePath, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(
            staleWithoutLease,
            DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(
            staleWithLease,
            DateTime.UtcNow.AddDays(-2));

        using (var activeLease = new FileStream(
                   activeLeasePath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.Read))
        {
            cleanupMethod.Invoke(null, [runsRoot]);
            if (Directory.Exists(staleWithoutLease) ||
                !Directory.Exists(staleWithLease))
            {
                throw new InvalidOperationException(
                    "Interactive run stale-cleanup lease contract failed.");
            }
        }

        cleanupMethod.Invoke(null, [runsRoot]);
        if (Directory.Exists(staleWithLease))
        {
            throw new InvalidOperationException(
                "Released stale interactive run directory was not cleaned.");
        }

        var resolveRunsRoot = runnerType.GetMethod("GetSecureRunsRoot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("S3 secure run root resolver is missing.");
        var expectedRunsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "InteractiveRuns");
        if (!string.Equals((string?)resolveRunsRoot.Invoke(null, null), expectedRunsRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("S3 runner still uses a user-controlled path.");

        var createRun = runnerType.GetMethod("CreateProtectedRunDirectory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("S3 per-user run ACL helper is missing.");
        var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var sandbox = Path.Combine(root, "s3-secure-runs");
        Directory.CreateDirectory(sandbox);
        var run = (string)createRun.Invoke(null, [sandbox, userSid.Value])!;
        var runAcl = new DirectoryInfo(run).GetAccessControl();
        if (!runAcl.AreAccessRulesProtected ||
            !HasAccess(runAcl, userSid, System.Security.AccessControl.FileSystemRights.Modify) ||
            !HasAccess(runAcl,
                new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                System.Security.AccessControl.FileSystemRights.FullControl))
            throw new InvalidOperationException("S3 protected run ACL is missing.");

        var outside = Path.Combine(root, "s3-outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "keep.txt"), "PRESERVE");
        var junction = Path.Combine(run, "junction");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                   "cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
               { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        {
            if (mklink is null) throw new InvalidOperationException("mklink did not start.");
            await mklink.WaitForExitAsync();
            if (mklink.ExitCode != 0) throw new InvalidOperationException("Junction fixture could not be created.");
        }
        TalvoraOwnedTempCleanup.TryDeleteStaleEntry(sandbox, run,
            DateTimeOffset.MaxValue, out _);
        if (!File.Exists(Path.Combine(outside, "keep.txt")))
            throw new InvalidOperationException("S3 cleanup followed a junction outside the runs root.");
        var redirectedRoot = Path.Combine(root, "s3-linked-runs-root");
        using (var rootLink = System.Diagnostics.Process.Start(
                   new System.Diagnostics.ProcessStartInfo(
                       "cmd.exe", $"/c mklink /J \"{redirectedRoot}\" \"{outside}\"")
                   { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        {
            if (rootLink is null) throw new InvalidOperationException("S3 root junction fixture could not start.");
            await rootLink.WaitForExitAsync();
            if (rootLink.ExitCode != 0) throw new InvalidOperationException("S3 root junction fixture failed.");
        }
        var rootRejected = false;
        try { createRun.Invoke(null, [redirectedRoot, userSid.Value]); }
        catch (System.Reflection.TargetInvocationException error)
            when (error.InnerException is InvalidOperationException)
        {
            rootRejected = true;
        }
        if (!rootRejected)
            throw new InvalidOperationException("S3 reparse-point run root was not rejected.");
        Directory.Delete(redirectedRoot);
        var normalRun = (string)createRun.Invoke(null, [sandbox, userSid.Value])!;
        await File.WriteAllTextAsync(Path.Combine(normalRun, "output.txt"), "clean");
        if (!TalvoraOwnedTempCleanup.TryDeleteStaleEntry(
                sandbox, normalRun, DateTimeOffset.MaxValue, out _) ||
            Directory.Exists(normalRun))
            throw new InvalidOperationException("S3 safe per-run deletion failed.");

        var hardened = Talvora.Installer.InstallerEngine.BuildProtectedRootAcl();
        if (!hardened.AreAccessRulesProtected ||
            !HasAccess(hardened,
                new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                System.Security.AccessControl.FileSystemRights.FullControl))
            throw new InvalidOperationException("S3 installer root ACL is not protected.");
        if (HasAccess(hardened,
                new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                System.Security.AccessControl.FileSystemRights.Write))
            throw new InvalidOperationException("S3 installer root still allows Users write.");

        var aclFixtureRoot = Path.Combine(root, "s3-acl-fixture");
        Directory.CreateDirectory(aclFixtureRoot);
        var explicitFilePath = Path.Combine(aclFixtureRoot, "explicit-file-ace.txt");
        await File.WriteAllTextAsync(explicitFilePath, "preserve");
        var fileInfo = new FileInfo(explicitFilePath);
        var childAcl = fileInfo.GetAccessControl();
        childAcl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            userSid, System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.AccessControlType.Allow));
        fileInfo.SetAccessControl(childAcl);
        // This fixture retains tester FullControl on its parent solely for CI cleanup.
        hardened.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            userSid, System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(aclFixtureRoot).SetAccessControl(hardened);
        var retained = fileInfo.GetAccessControl().GetAccessRules(true, false,
            typeof(System.Security.Principal.SecurityIdentifier))
            .OfType<System.Security.AccessControl.FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Equals(userSid) && !rule.IsInherited &&
                (rule.FileSystemRights & System.Security.AccessControl.FileSystemRights.Read) != 0);
        if (!retained)
            throw new InvalidOperationException("S3 root ACL update erased an explicit file ACE.");
    }

    private static bool HasAccess(
        System.Security.AccessControl.DirectorySecurity security,
        System.Security.Principal.SecurityIdentifier sid,
        System.Security.AccessControl.FileSystemRights required)
    {
        return security.GetAccessRules(true, true,
                typeof(System.Security.Principal.SecurityIdentifier))
            .OfType<System.Security.AccessControl.FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Equals(sid) &&
                rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow &&
                (rule.FileSystemRights & required) == required);
    }
}
