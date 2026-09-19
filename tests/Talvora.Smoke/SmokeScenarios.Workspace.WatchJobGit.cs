using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceWatchJobGitAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var smokeId = context.SmokeId;
        var repositoryPath = context.RepositoryPath;
        var root = context.Root;
        var file = context.File;
        var developerWatchFile = context.DeveloperWatchFile;
        var watchStartResult = await EnsureSuccess(byName["talvora_watch_start"], new()
                {
                    ["path"] = root,
                    ["filter"] = "watch-*.txt",
                    ["includeSubdirectories"] = true,
                    ["internalBufferSize"] = 32768,
                    ["maxQueuedEvents"] = 0,
                });
                if (watchStartResult.StructuredContent is not { } watchStartJson ||
                    !watchStartJson.TryGetProperty("watchId", out var watchIdJson) ||
                    string.IsNullOrWhiteSpace(watchIdJson.GetString()))
                {
                    throw new InvalidOperationException("watch_start did not return a watch ID.");
                }
                
                var watchId = watchIdJson.GetString()!;
                try
                {
                    var watchListResult = await EnsureSuccess(byName["talvora_watch_list"], new());
                    if (watchListResult.StructuredContent is not { } watchListJson ||
                        !watchListJson.GetProperty("watches").EnumerateArray().Any(watch =>
                            watch.TryGetProperty("watchId", out var listedWatchId) &&
                            string.Equals(listedWatchId.GetString(), watchId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("watch_list did not include the active watcher.");
                    }
                
                    var watchGetResult = await EnsureSuccess(byName["talvora_watch_get"], new()
                    {
                        ["watchId"] = watchId,
                    });
                    if (watchGetResult.StructuredContent is not { } watchGetJson ||
                        !string.Equals(
                            watchGetJson.GetProperty("watchId").GetString(),
                            watchId,
                            StringComparison.OrdinalIgnoreCase) ||
                        !watchGetJson.GetProperty("enabled").GetBoolean() ||
                        !string.Equals(
                            Path.GetFullPath(watchGetJson.GetProperty("path").GetString() ?? string.Empty),
                            Path.GetFullPath(root),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("watch_get did not report the active watcher.");
                    }
                
                    await EnsureSuccess(byName["talvora_write_text"], new()
                    {
                        ["path"] = developerWatchFile,
                        ["content"] = "watch-payload-" + smokeId,
                    });
                
                    var watchWaitResult = await EnsureSuccess(byName["talvora_watch_wait"], new()
                    {
                        ["watchId"] = watchId,
                        ["afterSequence"] = 0L,
                        ["timeoutSeconds"] = 10,
                        ["pollIntervalMilliseconds"] = 50,
                    });
                    if (watchWaitResult.StructuredContent is not { } watchWaitJson ||
                        !watchWaitJson.GetProperty("signaled").GetBoolean() ||
                        !watchWaitJson.TryGetProperty("event", out var waitedEvent) ||
                        waitedEvent.ValueKind != System.Text.Json.JsonValueKind.Object ||
                        !string.Equals(
                            waitedEvent.GetProperty("fullPath").GetString(),
                            Path.GetFullPath(developerWatchFile),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("watch_wait did not observe the smoke file change.");
                    }
                
                    var watchReadResult = await EnsureSuccess(byName["talvora_watch_read"], new()
                    {
                        ["watchId"] = watchId,
                        ["afterSequence"] = 0L,
                        ["maxEvents"] = 0,
                        ["consume"] = true,
                    });
                    if (watchReadResult.StructuredContent is not { } watchReadJson ||
                        watchReadJson.GetProperty("count").GetInt32() < 1 ||
                        !watchReadJson.GetProperty("events").EnumerateArray().Any(change =>
                            change.TryGetProperty("fullPath", out var changedPath) &&
                            string.Equals(
                                changedPath.GetString(),
                                Path.GetFullPath(developerWatchFile),
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("watch_read did not return the smoke file change.");
                    }
                }
                finally
                {
                    var watchStopResult = await EnsureSuccess(byName["talvora_watch_stop"], new()
                    {
                        ["watchId"] = watchId,
                    });
                    if (watchStopResult.StructuredContent is not { } watchStopJson ||
                        !watchStopJson.GetProperty("found").GetBoolean() ||
                        !watchStopJson.GetProperty("stopped").GetBoolean())
                    {
                        throw new InvalidOperationException("watch_stop did not stop the smoke watcher.");
                    }
                }
                
                var jobToken = "TALVORA_JOB_SMOKE_" + smokeId;
                var cmdPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cmd.exe");
                var jobStartResult = await EnsureSuccess(byName["talvora_job_start"], new()
                {
                    ["executable"] = cmdPath,
                    ["arguments"] = new[] { "/d", "/q" },
                    ["workingDirectory"] = root,
                });
                if (jobStartResult.StructuredContent is not { } jobStartJson ||
                    !jobStartJson.TryGetProperty("jobId", out var jobIdJson) ||
                    string.IsNullOrWhiteSpace(jobIdJson.GetString()) ||
                    !jobStartJson.TryGetProperty("processId", out var jobPidJson) ||
                    jobPidJson.GetInt32() <= 0)
                {
                    throw new InvalidOperationException("job_start did not return a live job identity.");
                }
                
                var jobId = jobIdJson.GetString()!;
                var jobPid = jobPidJson.GetInt32();
                
                try
                {
                    var jobGetResult = await EnsureSuccess(byName["talvora_job_get"], new()
                    {
                        ["jobId"] = jobId,
                    });
                    if (jobGetResult.StructuredContent is not { } jobGetJson ||
                        jobGetJson.GetProperty("processId").GetInt32() != jobPid ||
                        !string.Equals(jobGetJson.GetProperty("state").GetString(), "Running", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("job_get did not report the started job as running.");
                    }
                
                    var jobListResult = await EnsureSuccess(byName["talvora_job_list"], new()
                    {
                        ["includeExited"] = true,
                        ["maxResults"] = 0,
                    });
                    if (jobListResult.StructuredContent is not { } jobListJson ||
                        !jobListJson.GetProperty("jobs").EnumerateArray().Any(job =>
                            job.TryGetProperty("jobId", out var listedJobId) &&
                            string.Equals(listedJobId.GetString(), jobId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("job_list did not include the started job.");
                    }
                
                    var stdinResult = await EnsureSuccess(byName["talvora_job_write_stdin"], new()
                    {
                        ["jobId"] = jobId,
                        ["text"] = "echo " + jobToken,
                        ["appendNewLine"] = true,
                    });
                    if (stdinResult.StructuredContent is not { } stdinJson ||
                        stdinJson.GetProperty("processId").GetInt32() != jobPid)
                    {
                        throw new InvalidOperationException("job_write_stdin did not target the expected process.");
                    }
                
                    var observedOutput = false;
                    for (var attempt = 0; attempt < 20 && !observedOutput; attempt++)
                    {
                        await Task.Delay(100);
                        var outputResult = await EnsureSuccess(byName["talvora_job_read_output"], new()
                        {
                            ["jobId"] = jobId,
                            ["stream"] = "stdout",
                            ["offset"] = 0L,
                            ["maxBytes"] = 0,
                        });
                        if (outputResult.StructuredContent is { } outputJson &&
                            (outputJson.GetProperty("text").GetString() ?? string.Empty)
                                .Contains(jobToken, StringComparison.Ordinal))
                        {
                            observedOutput = true;
                        }
                    }
                
                    if (!observedOutput)
                    {
                        throw new InvalidOperationException("job_read_output did not observe stdin-triggered stdout.");
                    }
                }
                finally
                {
                    var stopResult = await EnsureSuccess(byName["talvora_job_stop"], new()
                    {
                        ["jobId"] = jobId,
                        ["entireProcessTree"] = true,
                        ["timeoutSeconds"] = 15,
                    });
                    if (stopResult.StructuredContent is not { } stopJson ||
                        !stopJson.GetProperty("found").GetBoolean() ||
                        !stopJson.GetProperty("exited").GetBoolean())
                    {
                        throw new InvalidOperationException("job_stop did not terminate the smoke job.");
                    }
                
                    var deleteJobResult = await EnsureSuccess(byName["talvora_job_delete"], new()
                    {
                        ["jobId"] = jobId,
                        ["stopIfRunning"] = false,
                    });
                    if (deleteJobResult.StructuredContent is not { } deleteJobJson ||
                        !deleteJobJson.GetProperty("found").GetBoolean() ||
                        !deleteJobJson.GetProperty("deleted").GetBoolean())
                    {
                        throw new InvalidOperationException("job_delete did not remove the smoke job metadata.");
                    }
                }
                
                await EnsureError(byName["talvora_job_get"], new()
                {
                    ["jobId"] = jobId,
                });
                
                var gitInfoResult = await EnsureSuccess(byName["talvora_git_info"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                });
                if (gitInfoResult.StructuredContent is not { } gitInfoJson ||
                    !gitInfoJson.TryGetProperty("root", out var gitRootJson) ||
                    string.IsNullOrWhiteSpace(gitRootJson.GetString()) ||
                    !gitInfoJson.TryGetProperty("head", out var gitHeadJson) ||
                    string.IsNullOrWhiteSpace(gitHeadJson.GetString()))
                {
                    throw new InvalidOperationException("git_info did not return repository root and HEAD.");
                }
                
                var gitRoot = gitRootJson.GetString()!;
                var gitHead = gitHeadJson.GetString()!;
                
                var gitStatusResult = await EnsureSuccess(byName["talvora_git_status"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                    ["includeUntracked"] = true,
                });
                if (gitStatusResult.StructuredContent is not { } gitStatusJson ||
                    !string.Equals(
                        Path.GetFullPath(gitStatusJson.GetProperty("root").GetString() ?? string.Empty),
                        Path.GetFullPath(gitRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("git_status returned an unexpected repository root.");
                }
                
                var gitDiffResult = await EnsureSuccess(byName["talvora_git_diff"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                    ["revisionRange"] = "HEAD~1..HEAD",
                    ["contextLines"] = 2,
                });
                if (gitDiffResult.StructuredContent is not { } gitDiffJson ||
                    string.IsNullOrWhiteSpace(gitDiffJson.GetProperty("diff").GetString()) ||
                    !(gitDiffJson.GetProperty("diff").GetString() ?? string.Empty)
                        .Contains("diff --git ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("git_diff did not return the committed HEAD~1..HEAD change.");
                }
                
                var gitLogResult = await EnsureSuccess(byName["talvora_git_log"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                    ["maxCount"] = 5,
                });
                if (gitLogResult.StructuredContent is not { } gitLogJson ||
                    gitLogJson.GetProperty("count").GetInt32() < 1 ||
                    !gitLogJson.GetProperty("commits").EnumerateArray().Any(commit =>
                        commit.TryGetProperty("commit", out var commitId) &&
                        string.Equals(commitId.GetString(), gitHead, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("git_log did not include HEAD.");
                }
                
                var gitBranchesResult = await EnsureSuccess(byName["talvora_git_branches"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                    ["includeRemote"] = true,
                });
                if (gitBranchesResult.StructuredContent is not { } gitBranchesJson ||
                    gitBranchesJson.GetProperty("count").GetInt32() < 1 ||
                    !gitBranchesJson.GetProperty("branches").EnumerateArray().Any(branch =>
                        branch.TryGetProperty("current", out var current) && current.GetBoolean()))
                {
                    throw new InvalidOperationException("git_branches did not identify the current branch.");
                }
                
                var gitRunResult = await EnsureSuccess(byName["talvora_git_run"], new()
                {
                    ["repositoryPath"] = repositoryPath,
                    ["arguments"] = new[] { "rev-parse", "HEAD" },
                    ["timeoutSeconds"] = 30,
                });
                if (gitRunResult.StructuredContent is not { } gitRunJson ||
                    gitRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                    !string.Equals(
                        (gitRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
                        gitHead,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("git_run rev-parse HEAD did not match git_info HEAD.");
                }
    }
}
