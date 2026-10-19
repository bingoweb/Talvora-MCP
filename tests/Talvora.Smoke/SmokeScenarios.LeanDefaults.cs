using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;

internal static partial class SmokeScenarios
{
    internal static async Task RunLeanDefaultsLiveAsync(string repositoryPath)
    {
        await using var client = await CreateClientAsync(
            "http://127.0.0.1:7676/mcp/dev");

        async Task<JsonElement> Call(string tool, Dictionary<string, object?> arguments)
        {
            var result = await client.CallToolAsync(tool, arguments);
            if (result.IsError is true)
            {
                throw new InvalidOperationException(
                    tool + ": " + string.Join(" ", result.Content.OfType<TextContentBlock>()
                        .Select(block => block.Text)));
            }

            if (result.StructuredContent is { } structured)
            {
                return structured.Clone();
            }

            var content = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException(tool + " returned no JSON text.");
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }

        static void Check(bool value, string description)
        {
            if (!value)
            {
                throw new InvalidOperationException("E3: " + description);
            }
        }

        var scratch = Path.Combine(Path.GetTempPath(), "Talvora-E3-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var gitStart = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-C", scratch, "init", "--quiet" })
            {
                gitStart.ArgumentList.Add(arg);
            }

            using (var git = Process.Start(gitStart)
                ?? throw new InvalidOperationException("Git fixture could not start."))
            {
                await git.WaitForExitAsync();
                Check(git.ExitCode == 0, "Git fixture initialization failed.");
            }

            for (var index = 0; index < 10; index++)
            {
                File.WriteAllText(Path.Combine(scratch, $"untracked-{index}.txt"), "fixture");
            }

            var status = await Call("talvora_git_status", new()
            {
                ["repositoryPath"] = scratch,
            });
            var entries = status.GetProperty("entries");
            Check(entries.GetArrayLength() == 10 &&
                entries[0].TryGetProperty("path", out _) &&
                entries[0].TryGetProperty("index", out _) &&
                entries[0].TryGetProperty("worktree", out _) &&
                !status.TryGetProperty("raw", out _),
                "Git status must return compact parsed entries without raw text.");
            var statusFull = await Call("talvora_git_status", new()
            {
                ["repositoryPath"] = scratch,
                ["includeRaw"] = true,
            });
            Check(statusFull.GetProperty("raw").GetString()!.Contains("untracked-"),
                "Git status includeRaw must preserve porcelain output.");

            var commits = await Call("talvora_git_log", new()
            {
                ["repositoryPath"] = Path.GetFullPath(repositoryPath),
            });
            var first = commits.GetProperty("commits")[0];
            Check(commits.GetProperty("count").GetInt32() <= 20 &&
                first.TryGetProperty("shortCommit", out _) &&
                first.TryGetProperty("date", out _) &&
                first.TryGetProperty("subject", out _) &&
                !first.TryGetProperty("authorName", out _),
                "Git log default must be short.");
            var fullLog = await Call("talvora_git_log", new()
            {
                ["repositoryPath"] = Path.GetFullPath(repositoryPath),
                ["detail"] = "full",
                ["maxCommits"] = 2,
            });
            Check(fullLog.GetProperty("commits")[0].TryGetProperty("authorName", out _) &&
                fullLog.GetProperty("commits")[0].TryGetProperty("commit", out _),
                "Git log detail=full must preserve existing fields.");

            var toolsRoot = Path.Combine(repositoryPath, "src", "Talvora", "Tools");
            var files = await Call("talvora_find_files", new()
            {
                ["root"] = toolsRoot,
            });
            var file = files.GetProperty("entries")[0];
            Check(file.TryGetProperty("path", out var relativePath) &&
                !Path.IsPathRooted(relativePath.GetString()) &&
                file.TryGetProperty("size", out _) &&
                !file.TryGetProperty("name", out _),
                "File search default must use relative path and size only.");
            var fullFiles = await Call("talvora_find_files", new()
            {
                ["root"] = toolsRoot,
                ["detail"] = "full",
                ["maxResults"] = 0,
            });
            Check(fullFiles.GetProperty("entries")[0].TryGetProperty("name", out _) &&
                Path.IsPathRooted(fullFiles.GetProperty("entries")[0]
                    .GetProperty("path").GetString()),
                "File search detail=full must preserve existing entries.");

            var searchPath = Path.Combine(scratch, "lines.txt");
            File.WriteAllLines(searchPath,
                Enumerable.Repeat("needle " + new string('x', 420), 125));
            var search = await Call("talvora_search_text", new()
            {
                ["root"] = searchPath,
                ["query"] = "needle",
            });
            Check(search.GetProperty("matchCount").GetInt32() == 100 &&
                search.GetProperty("matches")[0].GetProperty("line")
                    .GetString()!.Length <= 300,
                "Text search should default to 100 matches and 300-char lines.");
            var expandedSearch = await Call("talvora_search_text", new()
            {
                ["root"] = searchPath,
                ["query"] = "needle",
                ["maxMatches"] = 0,
                ["maxLineChars"] = 0,
            });
            Check(expandedSearch.GetProperty("matchCount").GetInt32() == 125 &&
                expandedSearch.GetProperty("matches")[0].GetProperty("line")
                    .GetString()!.Length > 400,
                "Text search must keep explicit 0/unlimited behavior.");

            const string expression =
                "[Console]::Out.Write('BEGIN'+('x'*24000)+'END')";
            var processArgs = new Dictionary<string, object?>
            {
                ["executable"] = "pwsh",
                ["arguments"] = new[] { "-NoProfile", "-NonInteractive", "-Command", expression },
                ["maxCapturedCharactersPerStream"] = 60000,
            };
            var process = await Call("talvora_run_process", processArgs);
            var processText = process.GetProperty("standardOutput").GetString()!;
            Check(processText.StartsWith("BEGIN", StringComparison.Ordinal) &&
                processText.EndsWith("END", StringComparison.Ordinal) &&
                processText.Contains("characters omitted", StringComparison.Ordinal) &&
                process.GetProperty("standardOutputTotalCharacters").GetInt64() > 16000,
                "Process result must keep a compact head and tail with real totals.");
            processArgs["maxOutputCharacters"] = 50000;
            var processFull = await Call("talvora_run_process", processArgs);
            Check(processFull.GetProperty("standardOutput").GetString()!.Length > 24000 &&
                !processFull.GetProperty("standardOutput").GetString()!
                    .Contains("characters omitted", StringComparison.Ordinal),
                "Process maxOutputCharacters must restore full captured output.");

            var psArgs = new Dictionary<string, object?>
            {
                ["script"] = expression,
                ["maxCapturedCharactersPerStream"] = 60000,
            };
            var ps = await Call("talvora_run_powershell", psArgs);
            Check(ps.GetProperty("standardOutput").GetString()!
                    .Contains("characters omitted", StringComparison.Ordinal),
                "PowerShell default must compact large output.");
            psArgs["maxOutputCharacters"] = 50000;
            var psFull = await Call("talvora_run_powershell", psArgs);
            Check(psFull.GetProperty("standardOutput").GetString()!.Length > 24000,
                "PowerShell opt-in full output must be available.");

            foreach (var tool in new[] { "talvora_dotnet_build", "talvora_dotnet_test" })
            {
                var dotnetArgs = new Dictionary<string, object?>
                {
                    ["workingDirectory"] = Path.GetFullPath(repositoryPath),
                    ["additionalArguments"] = new[] { "--help" },
                };
                var result = await Call(tool, dotnetArgs);
                var summary = result.GetProperty("summary");
                Check(summary.GetProperty("errors").GetInt32() >= 0 &&
                    summary.GetProperty("warnings").GetInt32() >= 0,
                    tool + " must return diagnostic summary.");
                dotnetArgs["fullOutput"] = true;
                var full = await Call(tool, dotnetArgs);
                Check(full.GetProperty("standardOutput").GetString()!.Length >=
                    result.GetProperty("standardOutput").GetString()!.Length,
                    tool + " fullOutput must not shorten output.");
            }

            Console.WriteLine($"E3_MEASURE status={status.GetRawText().Length} " +
                $"log={commits.GetRawText().Length} files={files.GetRawText().Length}");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
