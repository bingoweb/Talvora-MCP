using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunWorkContractAsync()
    {
        var cases = new (string Name, Func<string, Task> Run)[]
        {
            ("valid", ValidAsync),
            ("missing-trailer", MissingTrailerAsync),
            ("two-trailers", TwoTrailersAsync),
            ("status-not-staged", StatusNotStagedAsync),
            ("duplicate-after-audit", DuplicateAfterAuditAsync),
            ("ci-follow-up", CiFollowUpAsync),
            ("amend", AmendAsync),
            ("no-contract", NoContractAsync),
            ("global-options", GlobalOptionsAsync),
            ("file-message", FileMessageAsync),
        };

        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-WorkContract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (name, run) in cases)
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                await CreateRepositoryAsync(directory);
                await run(directory);
                Console.WriteLine($"PASS work-contract-{name}");
            }
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateRepositoryAsync(string directory)
    {
        await RequireGitSuccessAsync(directory, ["init", "-q"]);
        Directory.CreateDirectory(Path.Combine(directory, "docs", "codex"));
        await File.WriteAllTextAsync(
            Path.Combine(directory, "docs", "codex", "STATUS.md"), "initial");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "README.md"), "initial");
        await RequireGitSuccessAsync(directory, ["add", "-A"]);
        await RequireGitSuccessAsync(directory, CommitArgs("initial"));
        await RequireGitSuccessAsync(
            directory, ["tag", "audit/2026-10-19-01"]);
    }

    private static async Task EnableContractAsync(
        string directory, bool includeStatus = true)
    {
        Directory.CreateDirectory(Path.Combine(directory, ".talvora"));
        await File.WriteAllTextAsync(
            Path.Combine(directory, ".talvora", "work-contract.json"),
            """{"schemaVersion":1,"statusFile":"docs/codex/STATUS.md","cardTrailer":"Card","legacyCardTrailers":["Codex-Card"],"auditTagPrefix":"audit/"}""");
        await RequireGitSuccessAsync(
            directory, ["add", "--", ".talvora/work-contract.json"]);
        if (includeStatus)
        {
            await StageStatusAsync(directory, "stage-one");
        }
    }

    private static async Task StageStatusAsync(string directory, string text)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, "docs", "codex", "STATUS.md"), text);
        await RequireGitSuccessAsync(
            directory, ["add", "--", "docs/codex/STATUS.md"]);
    }

    private static string[] CommitArgs(string message) =>
    [
        "-c", "user.name=Talvora Regression",
        "-c", "user.email=talvora@example.invalid",
        "commit", "-q", "-m", message,
    ];

    private static async Task<TalvoraGitRunResponse> RunGitAsync(
        string directory, IReadOnlyList<string> arguments) =>
        await GitTools.Run(
            directory, arguments.ToArray(), explicitAdmin: true);

    private static async Task RequireGitSuccessAsync(
        string directory, IReadOnlyList<string> arguments)
    {
        var result = await RunGitAsync(directory, arguments);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"Fixture git command failed: {result.StandardError}");
        }
    }

    private static async Task<string> HeadAsync(string directory)
    {
        var result = await RunGitAsync(directory, ["rev-parse", "HEAD"]);
        return result.StandardOutput.Trim();
    }

    private static async Task MustRejectAsync(
        string directory, IReadOnlyList<string> args)
    {
        var before = await HeadAsync(directory);
        var response = await RunGitAsync(directory, args);
        if (response.ExitCode == 0 ||
            !response.StandardError.Contains(
                "WorkContract", StringComparison.Ordinal) ||
            await HeadAsync(directory) != before)
        {
            throw new InvalidOperationException(
                "Work contract did not reject the commit without mutation.");
        }
    }

    private static async Task ValidAsync(string dir)
    {
        await EnableContractAsync(dir);
        await RequireGitSuccessAsync(
            dir, CommitArgs("feat: allowed\n\nCard: A3"));
    }

    private static async Task MissingTrailerAsync(string dir)
    {
        await EnableContractAsync(dir);
        await MustRejectAsync(dir, CommitArgs("feat: no trailer"));
    }

    private static async Task TwoTrailersAsync(string dir)
    {
        await EnableContractAsync(dir);
        await MustRejectAsync(
            dir, CommitArgs("feat: repeated\n\nCard: X1\nCard: X2"));
    }

    private static async Task StatusNotStagedAsync(string dir)
    {
        await EnableContractAsync(dir, includeStatus: false);
        await MustRejectAsync(dir, CommitArgs("feat: no status\n\nCard: X1"));
    }

    private static async Task DuplicateAfterAuditAsync(string dir)
    {
        await EnableContractAsync(dir);
        await RequireGitSuccessAsync(
            dir, CommitArgs("feat: first\n\nCard: X1"));
        await StageStatusAsync(dir, "stage-two");
        await MustRejectAsync(
            dir, CommitArgs("feat: duplicate\n\nCard: X1"));
    }

    private static async Task CiFollowUpAsync(string dir)
    {
        await EnableContractAsync(dir);
        await RequireGitSuccessAsync(
            dir, CommitArgs("feat: first\n\nCard: X1"));
        await StageStatusAsync(dir, "stage-two");
        await RequireGitSuccessAsync(
            dir, CommitArgs("fix: ci\n\nCI follow-up: run-123\n\nCard: X1"));
    }

    private static async Task AmendAsync(string dir)
    {
        await EnableContractAsync(dir);
        await RequireGitSuccessAsync(
            dir, CommitArgs("feat: first\n\nCard: X1"));
        await StageStatusAsync(dir, "stage-two");
        await MustRejectAsync(dir,
        [
            "-c", "user.name=Talvora Regression",
            "-c", "user.email=talvora@example.invalid",
            "commit", "--amend", "-m", "feat: amend\n\nCard: X2",
        ]);
    }

    private static async Task NoContractAsync(string dir)
    {
        await StageStatusAsync(dir, "stage-two");
        await RequireGitSuccessAsync(
            dir, CommitArgs("feat: no contract needed"));
    }

    private static async Task GlobalOptionsAsync(string dir)
    {
        await EnableContractAsync(dir);
        await MustRejectAsync(dir,
        [
            "-C", dir,
            "-c", "user.name=Talvora Regression",
            "-c", "user.email=talvora@example.invalid",
            "commit", "-m", "feat: no trailer",
        ]);
    }

    private static async Task FileMessageAsync(string dir)
    {
        await EnableContractAsync(dir);
        var message = Path.Combine(dir, "message.txt");
        await File.WriteAllTextAsync(
            message, "feat: read file\n\nCodex-Card: X3\n");
        await RequireGitSuccessAsync(dir,
        [
            "-c", "user.name=Talvora Regression",
            "-c", "user.email=talvora@example.invalid",
            "commit", "--file", message,
        ]);
    }
}
