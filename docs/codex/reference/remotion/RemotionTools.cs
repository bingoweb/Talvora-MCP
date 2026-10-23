using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraRemotionGuide(
    string Version,
    string DefaultProjectsRoot,
    IReadOnlyList<string> Workflow,
    IReadOnlyList<string> AuthoringRules,
    string License);

public sealed record TalvoraRemotionRunResponse(
    string RunId,
    string Action,
    string? ProjectRoot,
    string? OutputPath,
    string ResultPath,
    string LogPath,
    int ProcessId,
    string User);

public sealed record TalvoraRemotionStatusResponse(
    string RunId,
    string State,
    string? Action,
    string? ProjectRoot,
    string? OutputPath,
    long? OutputBytes,
    IReadOnlyList<string> Compositions,
    int? FramesDone,
    int? FramesTotal,
    string? Error,
    string? Diagnostics,
    string LogTail);

/// <summary>
/// Remotion (https://www.remotion.dev) animation and video rendering for any request for an animation,
/// motion graphic, intro, explainer video or animated GIF (ROADMAP RM1). Every Remotion command runs as the
/// signed-in user, detached, with a log and a result file that talvora_remotion_status reads, so a render
/// never outlives the caller's tool-call time limit (lesson of P-26).
/// </summary>
[McpServerToolType]
public static partial class RemotionTools
{
    /// <summary>Pinned Remotion release (create-video and the project's packages). Updated by a DEP card only.</summary>
    public const string RemotionVersion = "4.0.538";

    /// <summary>Appended to the MCP server instructions so every client routes animation requests here.</summary>
    public const string ServerInstructions =
        "When the user asks for an animation, motion graphic, animated intro/logo, explainer or promo video, or an animated GIF (Turkish: animasyon, video, hareketli görsel, tanıtım videosu, GIF), call talvora_remotion_guide first and follow its workflow: Talvora renders it with Remotion as the signed-in user.";

    internal static TalvoraDesktopProgressNotifier? DesktopNotifier { get; set; }

    private static readonly string[] EntryPoints =
        ["src/index.ts", "src/index.tsx", "src/index.js", "src/index.jsx", "remotion/index.ts", "remotion/index.tsx"];

    [McpServerTool(
        Name = "talvora_remotion_guide",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRemotionGuide)),
     Description("Call FIRST whenever the user wants an animation, motion graphic, animated intro/logo, explainer or promo video, or animated GIF (animasyon, video, GIF). Returns Talvora's Remotion workflow, authoring rules and the default projects folder.")]
    public static TalvoraRemotionGuide Guide()
    {
        var user = WindowsSessionLauncher.GetActiveInteractiveUser();
        return new TalvoraRemotionGuide(
            RemotionVersion,
            DefaultProjectsRoot(user),
            [
                "1. Reuse a project the user names; otherwise talvora_remotion_run {action:\"create\", name:\"<kebab-case>\"} (creates <defaultProjectsRoot>\\<name>), then poll talvora_remotion_status every 15 s until state is not \"running\".",
                "2. Write the animation with talvora_apply_patch in that project: components in src/, register each <Composition> in src/Root.tsx. Read files with talvora_read_source first.",
                "3. talvora_remotion_run {action:\"compositions\", projectRoot} and poll status: the id must be listed; a failure here is a code error to fix in step 2.",
                "4. Optional quick look: talvora_remotion_run {action:\"still\", projectRoot, compositionId, frame} → PNG.",
                "5. talvora_remotion_run {action:\"render\", projectRoot, compositionId, format:\"mp4\"|\"webm\"|\"gif\", props?} and poll talvora_remotion_status every 15 s; never start a second render of the same project while one is running.",
                "6. Report to the user in Turkish: what the animation shows, length, size and the output path. openWhenDone (default true) already opened it in the user's player.",
            ],
            [
                "Remotion is React: export a component, animate with useCurrentFrame(), useVideoConfig(), interpolate(...,{extrapolateLeft:'clamp',extrapolateRight:'clamp'}) and spring(); layout with <AbsoluteFill>; time with <Sequence from durationInFrames>.",
                "durationInFrames = seconds × fps. Default 1920×1080 at 30 fps unless the user asks otherwise (social square 1080×1080, story 1080×1920).",
                "Everything must be deterministic: no Math.random() (use random('seed') from remotion), no Date.now(), no network data at render time.",
                "Images, audio and fonts go in public/ and are referenced with staticFile('name'); <Img>, <Audio>, <Video> from remotion, never plain <img>.",
                "Turkish text needs a font with Turkish glyphs (Segoe UI, Inter, Cascadia, Arial are fine); pass user-visible text as defaultProps/props so it can change without code edits.",
                "Keep each composition in its own file; keep Root.tsx a list of <Composition id=… component=… durationInFrames fps width height defaultProps>.",
                "Composition ids use letters, digits and hyphens only (e.g. TalvoraIntro).",
            ],
            "Remotion is free for individuals, non-profits and companies with up to 3 employees; larger companies need a Remotion company license (https://www.remotion.pro/license).");
    }

    [McpServerTool(
        Name = "talvora_remotion_run",
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRemotionRunResponse)),
     Description("Start one Remotion step as the signed-in user, detached: create a project, list compositions, render a still (PNG) or render a video/GIF. Returns at once with a runId; poll talvora_remotion_status. Call talvora_remotion_guide first.")]
    public static TalvoraRemotionRunResponse Run(
        [Description("create | compositions | still | render")] string action,
        [Description("Existing Remotion project folder (compositions, still, render).")] string? projectRoot = null,
        [Description("create: new project folder name, lowercase letters, digits and hyphens.")] string? name = null,
        [Description("create: parent folder; default is the user's Videos\\Talvora.")] string? parentDirectory = null,
        [Description("still/render: composition id from Root.tsx.")] string? compositionId = null,
        [Description("render: mp4 (default), webm or gif.")] string? format = null,
        [Description("still/render: input props object for the composition.")] JsonElement? props = null,
        [Description("still: frame number (default 0).")] int? frame = null,
        [Description("still/render: output scale 0.1–4 (default 1).")] double? scale = null,
        [Description("Open the finished file in the user's default app (default true).")] bool openWhenDone = true)
    {
        var user = WindowsSessionLauncher.GetActiveInteractiveUser();
        var runs = RunsDirectory(user);
        Directory.CreateDirectory(runs);
        var runId = Guid.NewGuid().ToString("N");
        var request = new Dictionary<string, object?>
        {
            ["runId"] = runId,
            ["action"] = action,
            ["version"] = RemotionVersion,
            ["openWhenDone"] = openWhenDone,
        };

        string workingDirectory;
        string? outputPath = null;
        string? root = null;
        switch (action)
        {
            case "create":
                if (name is null || !ProjectName().IsMatch(name))
                {
                    throw new ArgumentException("Proje adı küçük harf, rakam ve tire içermeli (ör. talvora-tanitim).", nameof(name));
                }
                var parent = Path.GetFullPath(parentDirectory ?? DefaultProjectsRoot(user));
                root = Path.Combine(parent, name);
                if (Directory.Exists(root))
                {
                    throw new InvalidOperationException($"Bu klasör zaten var: {root}");
                }
                request["parentDirectory"] = parent;
                request["name"] = name;
                request["openWhenDone"] = false;
                workingDirectory = user.UserProfile ?? parent;
                break;
            case "compositions":
            case "still":
            case "render":
                root = Path.GetFullPath(projectRoot ?? throw new ArgumentException("projectRoot gerekli.", nameof(projectRoot)));
                var entry = EntryPoint(root);
                EnsureNotRunning(runs, root);
                request["projectRoot"] = root;
                request["entryPoint"] = entry;
                workingDirectory = root;
                if (action == "compositions")
                {
                    request["openWhenDone"] = false;
                    break;
                }
                if (compositionId is null || !CompositionId().IsMatch(compositionId))
                {
                    throw new ArgumentException("compositionId yalnızca harf, rakam ve tire içermeli.", nameof(compositionId));
                }
                if (scale is { } s && (s < 0.1 || s > 4))
                {
                    throw new ArgumentOutOfRangeException(nameof(scale), "scale 0.1 ile 4 arasında olmalı.");
                }
                var (codec, extension) = action == "still"
                    ? ("png", "png")
                    : (format ?? "mp4") switch
                    {
                        "mp4" => ("h264", "mp4"),
                        "webm" => ("vp9", "webm"),
                        "gif" => ("gif", "gif"),
                        _ => throw new ArgumentException("format mp4, webm ya da gif olmalı.", nameof(format)),
                    };
                outputPath = Path.Combine(root, "out",
                    $"{compositionId}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
                request["compositionId"] = compositionId;
                request["codec"] = codec;
                request["outputPath"] = outputPath;
                request["frame"] = Math.Max(0, frame ?? 0);
                request["scale"] = scale;
                if (props is { ValueKind: JsonValueKind.Object } supplied)
                {
                    var json = supplied.GetRawText();
                    if (json.Length > 64 * 1024)
                    {
                        throw new ArgumentException("props en fazla 64 KB olabilir.", nameof(props));
                    }
                    var propsPath = Path.Combine(runs, runId + ".props.json");
                    File.WriteAllText(propsPath, json, new UTF8Encoding(false));
                    request["propsPath"] = propsPath;
                }
                break;
            default:
                throw new ArgumentException("action create, compositions, still ya da render olmalı.", nameof(action));
        }

        var requestPath = Path.Combine(runs, runId + ".request.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request), new UTF8Encoding(false));
        var runner = EnsureRunnerScript(user);
        var pwsh = CommandResolver.Resolve(["pwsh.exe"], [@"C:\Program Files\PowerShell\7\pwsh.exe"])
            ?? throw new InvalidOperationException("PowerShell 7 (pwsh.exe) bulunamadı.");
        var launch = WindowsSessionLauncher.StartProcess(
            pwsh,
            ["-NoProfile", "-NonInteractive", "-File", runner, "-RequestPath", requestPath],
            user.SessionId,
            workingDirectory,
            visible: false,
            expectedUserSid: user.Sid);

        var resultPath = Path.Combine(runs, runId + ".json");
        var logPath = Path.Combine(runs, runId + ".log");
        DesktopNotifier?.BeginRemotionRun(runId, action, root, compositionId, resultPath, logPath);
        return new TalvoraRemotionRunResponse(runId, action, root, outputPath, resultPath, logPath, launch.ProcessId, user.User);
    }

    [McpServerTool(
        Name = "talvora_remotion_status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRemotionStatusResponse)),
     Description("Read the state of a talvora_remotion_run step: running, succeeded or failed, frames rendered, output file and size, composition ids, error and the last log lines. Poll every 15 s.")]
    public static TalvoraRemotionStatusResponse Status(string runId)
    {
        if (!RunIdPattern().IsMatch(runId))
        {
            throw new ArgumentException("runId geçersiz.", nameof(runId));
        }
        var runs = RunsDirectory(WindowsSessionLauncher.GetActiveInteractiveUser());
        return ReadStatus(runId, Path.Combine(runs, runId + ".json"), Path.Combine(runs, runId + ".log"));
    }

    internal static TalvoraRemotionStatusResponse ReadStatus(string runId, string resultPath, string logPath)
    {
        var log = Ansi().Replace(File.Exists(logPath) ? ReadShared(logPath) : string.Empty, string.Empty);
        var lines = log.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0 && !StackLine().IsMatch(line)).ToArray();
        var tail = string.Join('\n', lines.TakeLast(12));
        var (done, total) = Progress(log);
        if (!File.Exists(resultPath))
        {
            return new(runId, "starting", null, null, null, null, [], done, total, null, null, FileLog.RedactSensitiveData(tail));
        }

        using var document = JsonDocument.Parse(ReadShared(resultPath));
        var json = document.RootElement;
        string? Text(string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var state = Text("state") ?? "unknown";
        if (state == "running" && json.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var processId) && !IsAlive(processId))
        {
            state = "failed";
        }
        var output = Text("outputPath");
        var compositions = json.TryGetProperty("compositions", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToArray()
            : [];
        return new(runId, state, Text("action"), Text("projectRoot"), output,
            output is not null && File.Exists(output) ? new FileInfo(output).Length : null,
            compositions, done, total,
            state == "failed" ? FileLog.RedactSensitiveData(Text("error") ?? "Remotion işlemi beklenmedik biçimde sona erdi.") : null,
            state == "failed" ? Diagnose(lines) : null,
            FileLog.RedactSensitiveData(tail));
    }

    /// <summary>The compiler/runtime error block of a failed run (first "Error" line, at most 20 lines, no stack frames).</summary>
    internal static string? Diagnose(IReadOnlyList<string> lines)
    {
        var start = lines.ToList().FindIndex(line => ErrorLine().IsMatch(line));
        return start < 0
            ? null
            : FileLog.RedactSensitiveData(string.Join('\n', lines.Skip(start).TakeWhile(line => !line.StartsWith("FAILED:", StringComparison.Ordinal)).Take(20)));
    }

    /// <summary>"Rendered 450/900" / "Encoded 900/900" → the latest real counter.</summary>
    internal static (int? Done, int? Total) Progress(string log)
    {
        var matches = ProgressPattern().Matches(log);
        if (matches.Count == 0)
        {
            return (null, null);
        }
        var last = matches[^1];
        return (int.Parse(last.Groups["done"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(last.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static string DefaultProjectsRoot(InteractiveUserContext user) =>
        Path.Combine(user.UserProfile ?? throw new InvalidOperationException("Oturum kullanıcısının profil klasörü bulunamadı."),
            "Videos", "Talvora");

    private static string RunsDirectory(InteractiveUserContext user) =>
        Path.Combine(user.LocalAppData ?? throw new InvalidOperationException("Oturum kullanıcısının yerel uygulama klasörü bulunamadı."),
            "Talvora", "Remotion", "runs");

    private static string EntryPoint(string root)
    {
        var manifest = Path.Combine(root, "package.json");
        if (!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("\"remotion\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bu klasör bir Remotion projesi değil (package.json içinde remotion yok): {root}");
        }
        return EntryPoints.FirstOrDefault(entry => File.Exists(Path.Combine(root, entry)))
            ?? throw new InvalidOperationException("Remotion giriş dosyası bulunamadı (src/index.ts).");
    }

    private static void EnsureNotRunning(string runs, string root)
    {
        foreach (var result in Directory.EnumerateFiles(runs, "*.json").Where(path => !path.EndsWith(".request.json", StringComparison.Ordinal) && !path.EndsWith(".props.json", StringComparison.Ordinal)))
        {
            try
            {
                using var document = JsonDocument.Parse(ReadShared(result));
                var json = document.RootElement;
                if (json.TryGetProperty("state", out var state) && state.GetString() == "running" &&
                    json.TryGetProperty("projectRoot", out var project) &&
                    string.Equals(project.GetString(), root, StringComparison.OrdinalIgnoreCase) &&
                    json.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var processId) && IsAlive(processId))
                {
                    throw new InvalidOperationException("Bu projede bir Remotion işlemi zaten sürüyor; önce talvora_remotion_status ile bitmesini bekleyin.");
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static string EnsureRunnerScript(InteractiveUserContext user)
    {
        var path = Path.Combine(user.LocalAppData!, "Talvora", "Remotion", "Invoke-TalvoraRemotion.ps1");
        if (!File.Exists(path) || File.ReadAllText(path) != RunnerScript)
        {
            File.WriteAllText(path, RunnerScript, new UTF8Encoding(false));
        }
        return path;
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectName();

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CompositionId();

    [GeneratedRegex(@"^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex(@"(?:Rendered|Encoded) (?<done>\d+)/(?<total>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressPattern();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex Ansi();

    [GeneratedRegex(@"^\s+at\s", RegexOptions.CultureInvariant)]
    private static partial Regex StackLine();

    [GeneratedRegex(@"\bError\b|error TS\d+|SyntaxError|TypeError|ReferenceError", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorLine();

    /// <summary>
    /// Runs as the signed-in user. Reads the request, runs npx, mirrors every line to the log and writes the
    /// result file the service reads (state running → succeeded | failed). No secrets pass through it.
    /// </summary>
    internal const string RunnerScript = """
param([Parameter(Mandatory)][string] $RequestPath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
$runDirectory = Split-Path -Parent $RequestPath
$logPath = Join-Path $runDirectory "$($request.runId).log"
$resultPath = Join-Path $runDirectory "$($request.runId).json"
$result = [ordered]@{ runId = $request.runId; action = $request.action; projectRoot = $request.projectRoot; state = 'running'; pid = $PID }

function Save-Result {
    $result.updatedAtUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
}

function Write-Log([string] $Text) {
    Add-Content -LiteralPath $logPath -Value $Text -Encoding utf8
}

function Invoke-Npx([string[]] $Arguments) {
    Write-Log "> npx $($Arguments -join ' ')"
    $lines = @(& npx.cmd @Arguments 2>&1 | ForEach-Object { $line = "$_"; Write-Log $line; $line })
    if ($LASTEXITCODE -ne 0) { throw "npx $($Arguments[0]) $($Arguments[1]) failed with exit code $LASTEXITCODE." }
    return $lines
}

Save-Result
try {
    switch ($request.action) {
        'create' {
            New-Item -ItemType Directory -Force -Path $request.parentDirectory | Out-Null
            Set-Location -LiteralPath $request.parentDirectory
            Invoke-Npx @('--yes', "create-video@$($request.version)", '--yes', '--blank', $request.name) | Out-Null
            $result.projectRoot = Join-Path $request.parentDirectory $request.name
        }
        'compositions' {
            Set-Location -LiteralPath $request.projectRoot
            $lines = Invoke-Npx @('remotion', 'compositions', $request.entryPoint, '--quiet')
            $result.compositions = @(($lines | Where-Object { $_.Trim() } | Select-Object -Last 1) -split '\s+' | Where-Object { $_ })
        }
        default {
            Set-Location -LiteralPath $request.projectRoot
            $command = if ($request.action -eq 'still') { 'still' } else { 'render' }
            $arguments = @('remotion', $command, $request.entryPoint, $request.compositionId, $request.outputPath)
            if ($command -eq 'still') { $arguments += "--frame=$($request.frame)" } else { $arguments += "--codec=$($request.codec)" }
            if ($request.propsPath) { $arguments += "--props=$($request.propsPath)" }
            if ($request.scale) { $arguments += "--scale=$($request.scale)" }
            Invoke-Npx $arguments | Out-Null
            $result.outputPath = $request.outputPath
            if ($request.openWhenDone) { Invoke-Item -LiteralPath $request.outputPath }
        }
    }
    $result.state = 'succeeded'
    Save-Result
}
catch {
    Write-Log "FAILED: $($_.Exception.Message)"
    $result.state = 'failed'
    $result.error = $_.Exception.Message
    Save-Result
    exit 1
}
""";
}
