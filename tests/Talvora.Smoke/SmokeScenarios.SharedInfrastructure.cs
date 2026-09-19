using Talvora.Shared;

internal static partial class SmokeScenarios
{
    internal static async Task RunSharedInfrastructureAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Shared-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var batchPath = Path.Combine(root, "args.cmd");
            await File.WriteAllTextAsync(
                batchPath,
                "@echo off\r\n" +
                "setlocal DisableDelayedExpansion\r\n" +
                "set \"ARG1=%~1\"\r\n" +
                "set \"ARG2=%~2\"\r\n" +
                "set \"ARG3=%~3\"\r\n" +
                "set \"ARG4=%~4\"\r\n" +
                "set \"ARG5=%~5\"\r\n" +
                "set \"ARG6=%~6\"\r\n" +
                "set \"ARG7=%~7\"\r\n" +
                "set \"ARG8=%~8\"\r\n" +
                "set ARG1\r\n" +
                "set ARG2\r\n" +
                "set ARG3\r\n" +
                "set ARG4\r\n" +
                "set ARG5\r\n" +
                "set ARG7\r\n" +
                "set ARG8\r\n");

            var batch = await ProcessRunner.RunAsync(
                batchPath,
                root,
                ["alpha beta", "a&b", "%PATH%", "x!y", "(z)", "", @"C:\tail\", "a|b"],
                timeoutSeconds: 30);

            if (batch.ExitCode != 0 ||
                !batch.StandardOutput.Contains("ARG1=alpha beta", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains("ARG2=a&b", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains("ARG3=%PATH%", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains("ARG4=x!y", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains("ARG5=(z)", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains(@"ARG7=C:\tail\", StringComparison.Ordinal) ||
                !batch.StandardOutput.Contains("ARG8=a|b", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "ProcessRunner did not preserve batch-file arguments.");
            }

            var embeddedQuoteBatch = await ProcessRunner.RunAsync(
                batchPath,
                root,
                ["a\"b", "normal", "a<b", "a>b", "a&&b", "six", "seven", "eight"],
                timeoutSeconds: 30);

            if (embeddedQuoteBatch.ExitCode != 0 ||
                !embeddedQuoteBatch.StandardOutput.Contains("ARG1=a\"\"b", StringComparison.Ordinal) ||
                !embeddedQuoteBatch.StandardOutput.Contains("ARG2=normal", StringComparison.Ordinal) ||
                !embeddedQuoteBatch.StandardOutput.Contains("ARG3=a<b", StringComparison.Ordinal) ||
                !embeddedQuoteBatch.StandardOutput.Contains("ARG4=a>b", StringComparison.Ordinal) ||
                !embeddedQuoteBatch.StandardOutput.Contains("ARG5=a&&b", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "ProcessRunner did not preserve batch argument boundaries around an embedded quote. " +
                    $"ExitCode={embeddedQuoteBatch.ExitCode}; " +
                    $"stdout={embeddedQuoteBatch.StandardOutput}; " +
                    $"stderr={embeddedQuoteBatch.StandardError}");
            }

            var powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");

            var timeout = await ProcessRunner.RunAsync(
                powershell,
                root,
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "Start-Sleep -Seconds 30",
                ],
                timeoutSeconds: 1);

            if (!timeout.TimedOut || timeout.ElapsedMilliseconds >= 15_000)
            {
                throw new InvalidOperationException(
                    "ProcessRunner timeout did not terminate the process promptly.");
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cancellationStarted = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _ = await ProcessRunner.RunAsync(
                    powershell,
                    root,
                    [
                        "-NoLogo",
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        "Start-Sleep -Seconds 30",
                    ],
                    cancellationToken: cancellation.Token);
                throw new InvalidOperationException(
                    "ProcessRunner cancellation did not propagate.");
            }
            catch (OperationCanceledException)
            {
                cancellationStarted.Stop();
                if (cancellationStarted.Elapsed >= TimeSpan.FromSeconds(15))
                {
                    throw new InvalidOperationException(
                        "ProcessRunner cancellation cleanup took too long.");
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
