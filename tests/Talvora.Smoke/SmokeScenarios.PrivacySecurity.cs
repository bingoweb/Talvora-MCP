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
            "Remove-Item -LiteralPath $RequestPath",
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
        var staleWithoutLease = Path.Combine(runsRoot, "stale-no-lease");
        var staleWithLease = Path.Combine(runsRoot, "stale-with-lease");
        Directory.CreateDirectory(staleWithoutLease);
        Directory.CreateDirectory(staleWithLease);

        var activeLeasePath = Path.Combine(staleWithLease, "active.lock");
        await File.WriteAllTextAsync(activeLeasePath, string.Empty);
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
    }
}
