using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceProcessSearchAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var smokeId = context.SmokeId;
        var root = context.Root;
        var file = context.File;
        var searchToken = context.SearchToken;
await EnsureSuccess(byName["talvora_write_text"], new()
        {
            ["path"] = file,
            ["content"] = searchToken,
        });

        await EnsureSuccess(byName["talvora_read_text"], new() { ["path"] = file });
        await EnsureSuccess(byName["talvora_list"], new() { ["path"] = root });
        await EnsureSuccess(byName["talvora_run_process"], new()
        {
            ["executable"] = "cmd.exe",
            ["arguments"] = new[] { "/d", "/c", "echo", "talvora-smoke" },
            ["timeoutSeconds"] = 30,
        });

        var powerShellRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "PowerShell Smoke " + smokeId);
        Directory.CreateDirectory(powerShellRoot);
        try
        {
            var powerShellResult = await EnsureSuccess(byName["talvora_run_powershell"], new()
            {
                ["script"] = """
                    $ErrorActionPreference = 'Stop'
                    [Console]::Out.WriteLine('talvora-ps-stdout')
                    [Console]::Error.WriteLine('talvora-ps-stderr')
                    [Console]::Out.WriteLine((Get-Location).Path)
                    exit 7
                    """,
                ["engine"] = "auto",
                ["workingDirectory"] = powerShellRoot,
                ["timeoutSeconds"] = 30,
            });
            if (powerShellResult.StructuredContent is not { } psJson ||
                !psJson.TryGetProperty("exitCode", out var psExit) ||
                psExit.GetInt32() != 7 ||
                !psJson.TryGetProperty("timedOut", out var psTimedOut) ||
                psTimedOut.GetBoolean() ||
                !psJson.TryGetProperty("standardOutput", out var psStdout) ||
                !psStdout.GetString()!.Contains("talvora-ps-stdout", StringComparison.Ordinal) ||
                !psStdout.GetString()!.Contains(powerShellRoot, StringComparison.OrdinalIgnoreCase) ||
                !psJson.TryGetProperty("standardError", out var psStderr) ||
                !psStderr.GetString()!.Contains("talvora-ps-stderr", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PowerShell tool did not preserve multiline script, working directory, stdout/stderr, and exit code.");
            }
        }
        finally
        {
            if (Directory.Exists(powerShellRoot)) Directory.Delete(powerShellRoot, recursive: true);
        }

        int? processSmokePid = null;
        try
        {
            var processStart = await EnsureSuccess(byName["talvora_run_powershell"], new()
            {
                ["script"] = """
                    $exe = Join-Path $env:SystemRoot 'System32\cmd.exe'
                    $p = Start-Process -FilePath $exe -ArgumentList @('/d','/c','ping -t 127.0.0.1 >NUL') -WindowStyle Hidden -PassThru
                    [Console]::Out.Write($p.Id)
                    """,
                ["engine"] = "auto",
                ["timeoutSeconds"] = 30,
            });
            if (processStart.StructuredContent is not { } processStartJson ||
                !processStartJson.TryGetProperty("standardOutput", out var processStartOutput) ||
                !int.TryParse(processStartOutput.GetString()?.Trim(), out var processChildPid))
            {
                throw new InvalidOperationException("failed to spawn process smoke child.");
            }
            processSmokePid = processChildPid;

            var processGet = await EnsureSuccess(byName["talvora_process_get"], new()
            {
                ["processId"] = processChildPid,
            });
            if (processGet.StructuredContent is not { } processGetJson ||
                !processGetJson.TryGetProperty("found", out var processFound) ||
                !processFound.GetBoolean() ||
                !processGetJson.TryGetProperty("process", out var processInfo) ||
                !processInfo.TryGetProperty("processId", out var returnedPid) ||
                returnedPid.GetInt32() != processChildPid)
            {
                throw new InvalidOperationException("process get did not return the spawned process.");
            }

            var processList = await EnsureSuccess(byName["talvora_process_list"], new()
            {
                ["query"] = "cmd",
            });
            if (processList.StructuredContent is not { } processListJson ||
                !processListJson.TryGetProperty("processes", out var processes) ||
                processes.ValueKind != System.Text.Json.JsonValueKind.Array ||
                !processes.EnumerateArray().Any(item =>
                    item.TryGetProperty("processId", out var listedPid) &&
                    listedPid.GetInt32() == processChildPid))
            {
                throw new InvalidOperationException("process list did not return the spawned process.");
            }

            var processKill = await EnsureSuccess(byName["talvora_process_kill"], new()
            {
                ["processId"] = processChildPid,
                ["entireProcessTree"] = true,
                ["timeoutSeconds"] = 30,
            });
            if (processKill.StructuredContent is not { } processKillJson ||
                !processKillJson.TryGetProperty("found", out var killFound) ||
                !killFound.GetBoolean() ||
                !processKillJson.TryGetProperty("exited", out var exited) ||
                !exited.GetBoolean())
            {
                throw new InvalidOperationException("process kill did not terminate the spawned process.");
            }

            var processMissing = await EnsureSuccess(byName["talvora_process_get"], new()
            {
                ["processId"] = processChildPid,
            });
            if (processMissing.StructuredContent is not { } processMissingJson ||
                !processMissingJson.TryGetProperty("found", out var processMissingFound) ||
                processMissingFound.GetBoolean())
            {
                throw new InvalidOperationException("process get must return found=false after process termination.");
            }

            processSmokePid = null;
        }
        finally
        {
            if (processSmokePid is int leakedPid)
            {
                await EnsureSuccess(byName["talvora_run_powershell"], new()
                {
                    ["script"] = $"Stop-Process -Id {leakedPid} -Force -ErrorAction SilentlyContinue",
                    ["engine"] = "auto",
                    ["timeoutSeconds"] = 30,
                });
            }
        }

        using var searchDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var searchResult = await EnsureSuccess(
            byName["search"],
            new() { ["query"] = searchToken },
            searchDeadline.Token);
        if (searchResult.StructuredContent is not { } searchJson)
            throw new InvalidOperationException("search did not return structured content.");
        if (!searchJson.TryGetProperty("results", out var results) || results.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("search did not return a results array.");

        var found = results.EnumerateArray().Any(item =>
            item.TryGetProperty("id", out var id) &&
            string.Equals(id.GetString(), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (!found) throw new InvalidOperationException("search did not return the smoke document.");

        var fetchResult = await EnsureSuccess(byName["fetch"], new() { ["id"] = Path.GetFullPath(file) });
        if (fetchResult.StructuredContent is not { } fetchJson)
            throw new InvalidOperationException("fetch did not return structured content.");
        if (!fetchJson.TryGetProperty("text", out var text) || !string.Equals(text.GetString(), searchToken, StringComparison.Ordinal))
            throw new InvalidOperationException("fetch did not return the full smoke document content.");

        await EnsureSuccess(byName["talvora_delete"], new() { ["path"] = file });
    }
}
