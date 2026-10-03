using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraStitchInfoResponse(
    bool Found,
    string? Version,
    bool Authenticated,
    bool CanvasCredentialPresent,
    string? AuthMethod,
    string? CredentialsPath,
    string? GlobalConfigPath,
    string? NodeExecutable,
    string? CliScript,
    int? StatusExitCode,
    string? Error);

public sealed record TalvoraStitchServeResponse(
    int SessionId,
    string User,
    int ProcessId,
    int Port,
    string Url,
    string Project,
    string WorkingDirectory);

[McpServerToolType]
public static class StitchTools
{
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumArgumentCharacters = 4 * 1024 * 1024;

    private static readonly Regex SimpleTokenPattern = new(
        @"^[a-z][a-z0-9-]{0,63}$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex JsonSecretPattern = new(
        "\"(?<name>apiKey|api_key|accessToken|access_token|refreshToken|refresh_token|idToken|id_token|clientSecret|client_secret)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private sealed record StitchCommand(
        string NodeExecutable,
        string CliScript);

    [McpServerTool(
        Name = "talvora_stitch_info",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStitchInfoResponse)),
     Description("Report the managed Google Stitch CLI version and the logged-on Windows user's Stitch authentication state without returning API keys, OAuth tokens, or other credential values.")]
    public static async Task<TalvoraStitchInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        var command = ResolveStitch();
        if (command is null)
        {
            return new TalvoraStitchInfoResponse(
                false, null, false, false, null, null, null, null, null, null,
                "Google Stitch CLI was not found in Talvora's managed tools directory.");
        }

        try
        {
            var versionResult = await RunStitchAsync(
                ["--version"],
                workingDirectory: null,
                timeoutSeconds: 30,
                cancellationToken);
            var version = TextLines.FirstNonEmpty(
                versionResult.StandardOutput,
                versionResult.StandardError);

            var statusResult = await RunStitchAsync(
                ["status", "--json"],
                workingDirectory: null,
                timeoutSeconds: 60,
                cancellationToken);

            if (statusResult.ExitCode != 0 ||
                statusResult.TimedOut ||
                string.IsNullOrWhiteSpace(statusResult.StandardOutput))
            {
                return new TalvoraStitchInfoResponse(
                    true, version, false, false, null, null, null,
                    command.NodeExecutable, command.CliScript,
                    statusResult.ExitCode,
                    TextLines.FirstNonEmpty(
                        statusResult.StandardError,
                        statusResult.StandardOutput));
            }

            using var document =
                JsonDocument.Parse(statusResult.StandardOutput);
            var root = document.RootElement;
            var authenticated =
                root.TryGetProperty("authenticated", out var authenticatedElement) &&
                authenticatedElement.ValueKind is JsonValueKind.True;

            var canvasCredentialPresent = false;
            string? authMethod = null;
            if (root.TryGetProperty("canvas", out var canvas) &&
                canvas.ValueKind == JsonValueKind.Object)
            {
                canvasCredentialPresent =
                    canvas.TryGetProperty("present", out var present) &&
                    present.ValueKind is JsonValueKind.True;
                authMethod = TryGetString(canvas, "authMethod");
            }

            return new TalvoraStitchInfoResponse(
                true,
                version,
                authenticated,
                canvasCredentialPresent,
                authMethod,
                TryGetString(root, "credentialsPath"),
                TryGetString(root, "globalConfigPath"),
                command.NodeExecutable,
                command.CliScript,
                statusResult.ExitCode,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return new TalvoraStitchInfoResponse(
                true, null, false, false, null, null, null,
                command.NodeExecutable, command.CliScript, null,
                RedactStitchText(ex.Message));
        }
    }

    [McpServerTool(
        Name = "talvora_stitch_schema",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Inspect Google Stitch CLI's authoritative Draft 2020-12 JSON schema for a command or command/resource pair. Use this before invoking newly added or unfamiliar Stitch operations instead of guessing their payload shape.")]
    public static Task<TalvoraCliCommandResponse> Schema(
        [Description("Stitch command name, for example generate, get, find, create, edit, capture, upload, or mcp.")] string command,
        [Description("Optional Stitch resource name accepted by the command, for example screen, project, design-system, or status.")] string? resource = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(command, nameof(command));
        if (!string.IsNullOrWhiteSpace(resource))
        {
            ValidateSimpleToken(resource, nameof(resource));
        }

        var arguments = new List<string> { command.Trim() };
        if (!string.IsNullOrWhiteSpace(resource))
        {
            arguments.Add(resource.Trim());
        }
        arguments.Add("--schema");

        return RunStitchAsync(
            arguments,
            workingDirectory,
            60,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_find",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("List Google Stitch resources through the official CLI with JSON output under the logged-on Windows user's Stitch profile. Supports projects, screens, design systems, and future resource spellings accepted by the live CLI.")]
    public static Task<TalvoraCliCommandResponse> Find(
        [Description("Stitch resource name such as projects, screens, or design-systems.")] string resource,
        string? project = null,
        string? filter = null,
        string? fields = null,
        int limit = 0,
        int pageSize = 0,
        string? pageToken = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        if (limit < 0 || limit > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        if (pageSize < 0 || pageSize > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var arguments = new List<string>
        {
            "find",
            resource.Trim(),
            "--format",
            "json",
        };
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--filter", filter);
        AddOption(arguments, "--fields", fields);
        if (limit > 0)
        {
            AddOption(arguments, "--limit", limit.ToString());
        }
        if (pageSize > 0)
        {
            AddOption(arguments, "--page-size", pageSize.ToString());
        }
        AddOption(arguments, "--page-token", pageToken);

        return RunStitchAsync(
            arguments,
            workingDirectory,
            DefaultTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Read one Google Stitch resource through the official CLI with JSON output under the logged-on Windows user's Stitch profile. Use talvora_stitch_schema first when the live resource contract is unfamiliar.")]
    public static Task<TalvoraCliCommandResponse> Get(
        [Description("Stitch resource name such as project, screen, design-system, or status.")] string resource,
        [Description("Optional resource ID. Status-like resources do not require one.")] string? id = null,
        string? project = null,
        string? fields = null,
        bool expand = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        var arguments = new List<string>
        {
            "get",
            resource.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(id))
        {
            arguments.Add(id.Trim());
        }
        arguments.Add("--format");
        arguments.Add("json");
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--fields", fields);
        if (expand)
        {
            arguments.Add("--expand");
        }

        return RunStitchAsync(
            arguments,
            workingDirectory,
            DefaultTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_generate",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Generate a Google Stitch screen or variants using the official CLI under the logged-on Windows user's authenticated Stitch profile. This creates remote design content but does not delete existing content.")]
    public static Task<TalvoraCliCommandResponse> Generate(
        [Description("Art-direction prompt for the screen or variants.")] string prompt,
        string? project = null,
        string? newProject = null,
        string? screen = null,
        string? device = null,
        string? model = null,
        string? title = null,
        int count = 0,
        string? creativeRange = null,
        string? aspects = null,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (count is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var arguments = new List<string>
        {
            "generate",
            "screen",
            "--prompt",
            prompt,
            "--format",
            "json",
        };
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--new-project", newProject);
        AddOption(arguments, "--screen", screen);
        AddOption(arguments, "--device", device);
        AddOption(arguments, "--model", model);
        AddOption(arguments, "--title", title);
        if (count > 0)
        {
            AddOption(arguments, "--count", count.ToString());
        }
        AddOption(arguments, "--creative-range", creativeRange);
        AddOption(arguments, "--aspects", aspects);
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }

        return RunStitchAsync(
            arguments,
            workingDirectory,
            timeoutSeconds: 600,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_create",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Create a Stitch project, screen, or design-system through the official CLI. Supports raw JSON payloads and dry-run previews while keeping OAuth in the logged-on user's Stitch profile.")]
    public static Task<TalvoraCliCommandResponse> Create(
        string resource,
        string? json = null,
        string? project = null,
        string? title = null,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        var arguments = new List<string>
        {
            "create",
            resource.Trim(),
        };
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--title", title);
        if (!string.IsNullOrWhiteSpace(json))
        {
            arguments.Add("--json");
            arguments.Add(json);
        }
        else
        {
            arguments.Add("--format");
            arguments.Add("json");
        }
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        return RunStitchAsync(
            arguments,
            workingDirectory,
            DefaultTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_edit",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Edit a Stitch screen or design-system through the official CLI, including prompt-based art direction, device/model changes, design-system application, raw JSON payloads, and dry-run previews.")]
    public static Task<TalvoraCliCommandResponse> Edit(
        string resource,
        string id,
        string? project = null,
        string? prompt = null,
        string? device = null,
        string? model = null,
        string? designSystem = null,
        string? json = null,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var arguments = new List<string>
        {
            "edit",
            resource.Trim(),
            id.Trim(),
        };
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--prompt", prompt);
        AddOption(arguments, "--device", device);
        AddOption(arguments, "--model", model);
        AddOption(arguments, "--design-system", designSystem);
        if (!string.IsNullOrWhiteSpace(json))
        {
            arguments.Add("--json");
            arguments.Add(json);
        }
        else
        {
            arguments.Add("--format");
            arguments.Add("json");
        }
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        return RunStitchAsync(
            arguments,
            workingDirectory,
            timeoutSeconds: 600,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_delete",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Delete a Stitch resource through the official CLI. Use dryRun=true to preview. Actual deletion requires confirm=true and is intentionally marked destructive.")]
    public static Task<TalvoraCliCommandResponse> Delete(
        string resource,
        string id,
        string? project = null,
        bool dryRun = true,
        bool confirm = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!dryRun && !confirm)
        {
            throw new ArgumentException(
                "Actual Stitch deletion requires confirm=true.",
                nameof(confirm));
        }

        var arguments = new List<string>
        {
            "delete",
            resource.Trim(),
            "--id",
            id.Trim(),
            "--format",
            "json",
        };
        AddOption(arguments, "--project", project);
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        else
        {
            arguments.Add("--yes");
        }
        return RunStitchAsync(
            arguments,
            workingDirectory,
            DefaultTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_capture",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Capture a URL, local HTML file, or open Chrome DevTools tab into self-contained HTML using Stitch. Supports auth state, hydration waits, preparation scripts, redirect policy, and explicit output paths.")]
    public static Task<TalvoraCliCommandResponse> Capture(
        string? target = null,
        string? output = null,
        bool openTab = false,
        string? tabId = null,
        string? auth = null,
        string? origin = null,
        bool allowRedirect = false,
        string? waitFor = null,
        string? waitUntil = null,
        int settleTimeoutMilliseconds = 15000,
        string? prepare = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!openTab)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
        }
        if (settleTimeoutMilliseconds is < 0 or > 300000)
        {
            throw new ArgumentOutOfRangeException(nameof(settleTimeoutMilliseconds));
        }

        var arguments = new List<string> { "capture" };
        if (openTab)
        {
            arguments.Add(
                string.IsNullOrWhiteSpace(tabId)
                    ? "--tab"
                    : $"--tab={tabId.Trim()}");
        }
        else
        {
            arguments.Add(target!.Trim());
        }
        AddOption(arguments, "--output", output);
        AddOption(arguments, "--auth", auth);
        AddOption(arguments, "--origin", origin);
        if (allowRedirect)
        {
            arguments.Add("--allow-redirect");
        }
        AddOption(arguments, "--wait-for", waitFor);
        AddOption(arguments, "--wait-until", waitUntil);
        AddOption(
            arguments,
            "--settle-timeout",
            settleTimeoutMilliseconds.ToString());
        AddOption(arguments, "--prepare", prepare);
        arguments.Add("--json");
        return RunStitchAsync(
            arguments,
            workingDirectory,
            timeoutSeconds: 600,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_upload",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Upload a DESIGN.md specification or an HTML/PNG/JPG/JPEG/WEBP screen asset to Stitch Canvas. This is the preferred path for bringing real screenshots, teacher photos, and captured UI references into a Stitch project.")]
    public static Task<TalvoraCliCommandResponse> Upload(
        [Description("Upload kind: design or screen.")] string kind,
        string path,
        string? project = null,
        string? title = null,
        string? route = null,
        string? cwd = null,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(kind, nameof(kind));
        if (!kind.Equals("design", StringComparison.OrdinalIgnoreCase) &&
            !kind.Equals("screen", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "kind must be 'design' or 'screen'.",
                nameof(kind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var arguments = new List<string>
        {
            "upload",
            kind.Trim().ToLowerInvariant(),
            path,
        };
        AddOption(arguments, "--project", project);
        AddOption(arguments, "--title", title);
        AddOption(arguments, "--route", route);
        AddOption(arguments, "--cwd", cwd);
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        arguments.Add("--json");
        return RunStitchAsync(
            arguments,
            workingDirectory,
            timeoutSeconds: 600,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_url",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Return the canonical Stitch Canvas URL for a project or screen instead of guessing URLs.")]
    public static Task<TalvoraCliCommandResponse> Url(
        string resource,
        string id,
        string? project = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var arguments = new List<string>
        {
            "url",
            resource.Trim(),
            id.Trim(),
            "--json",
        };
        AddOption(arguments, "--project", project);
        return RunStitchAsync(
            arguments,
            workingDirectory,
            60,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_open",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Open a Stitch project or screen in the logged-on user's default browser through the official CLI.")]
    public static Task<TalvoraCliCommandResponse> Open(
        string resource,
        string id,
        string? project = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(resource, nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var arguments = new List<string>
        {
            "open",
            resource.Trim(),
            id.Trim(),
            "--format",
            "json",
        };
        AddOption(arguments, "--project", project);
        return RunStitchAsync(
            arguments,
            workingDirectory,
            60,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_serve_start",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStitchServeResponse)),
     Description("Start Stitch's local preview server for a project in the logged-on Windows user's session and return the process id plus loopback preview URL. The process continues independently after the tool returns.")]
    public static TalvoraStitchServeResponse ServeStart(
        string project,
        int port = 3000,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var command = ResolveStitch()
            ?? throw new FileNotFoundException(
                "Google Stitch CLI was not found in Talvora's managed tools directory.");
        var cwd = NormalizeWorkingDirectory(workingDirectory);
        var launch = WindowsSessionLauncher.StartProcess(
            command.NodeExecutable,
            [
                command.CliScript,
                "serve",
                "--project",
                project.Trim(),
                "--port",
                port.ToString(),
            ],
            workingDirectory: cwd,
            environment: new Dictionary<string, string?>
            {
                ["NO_COLOR"] = "1",
                ["FORCE_COLOR"] = "0",
            },
            visible: false,
            newConsole: false);

        return new TalvoraStitchServeResponse(
            launch.SessionId,
            launch.User,
            launch.ProcessId,
            port,
            $"http://127.0.0.1:{port}/",
            project.Trim(),
            launch.WorkingDirectory);
    }

    [McpServerTool(
        Name = "talvora_stitch_agent_skills",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("List, install, or update the official Stitch agent skill in project or user scope. Supports dry-run and overwrite controls.")]
    public static Task<TalvoraCliCommandResponse> AgentSkills(
        [Description("Action: list, add, or update.")] string action,
        bool user = false,
        string? directory = null,
        bool overwrite = false,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(action, nameof(action));
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is not ("list" or "add" or "update"))
        {
            throw new ArgumentException(
                "action must be list, add, or update.",
                nameof(action));
        }

        var arguments = new List<string>
        {
            "agent-skills",
            normalized,
            "--json",
        };
        if (normalized == "add")
        {
            arguments.Add(user ? "--user" : "--project");
            AddOption(arguments, "--dir", directory);
        }
        if (overwrite && normalized is "add" or "update")
        {
            arguments.Add("--overwrite");
        }
        if (dryRun && normalized is "add" or "update")
        {
            arguments.Add("--dry-run");
        }
        return RunStitchAsync(
            arguments,
            workingDirectory,
            DefaultTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_config",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Read or update non-secret Stitch CLI configuration. Supports list/get/set/unset while explicitly blocking API keys and access-token values from being read or written through Talvora.")]
    public static Task<TalvoraCliCommandResponse> Config(
        [Description("Action: list, get, set, or unset.")] string action,
        string? key = null,
        string? value = null,
        bool global = false,
        bool dryRun = false,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSimpleToken(action, nameof(action));
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is not ("list" or "get" or "set" or "unset"))
        {
            throw new ArgumentException(
                "action must be list, get, set, or unset.",
                nameof(action));
        }
        if (normalized is not "list")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (IsSensitiveConfigKey(key!))
            {
                throw new ArgumentException(
                    "Secret Stitch configuration keys are not exposed through Talvora.",
                    nameof(key));
            }
        }
        if (normalized == "set")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        }

        var arguments = new List<string>
        {
            "config",
            normalized,
        };
        if (normalized is not "list")
        {
            arguments.Add(key!.Trim());
        }
        if (normalized == "set")
        {
            arguments.Add(value!);
        }
        if (global && normalized is "set" or "unset")
        {
            arguments.Add("--global");
        }
        if (dryRun && normalized is "set" or "unset")
        {
            arguments.Add("--dry-run");
        }
        arguments.Add("--json");
        return RunStitchAsync(
            arguments,
            workingDirectory,
            60,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_stitch_run",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the official Google Stitch CLI in the logged-on Windows user's session with a caller-supplied argument vector. Use the typed Stitch tools for safe reads and generation. Secret-bearing flags, token-printing, and raw HTTP tracing are rejected; authenticate with the user's stored Stitch OAuth session instead.")]
    public static Task<TalvoraCliCommandResponse> Run(
        string[] arguments,
        string? workingDirectory = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0)
        {
            throw new ArgumentException(
                "At least one Stitch CLI argument is required.",
                nameof(arguments));
        }
        if (timeoutSeconds is < 1 or > 7200)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        ValidateGeneralArguments(arguments);
        return RunStitchAsync(
            arguments,
            workingDirectory,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<TalvoraCliCommandResponse> RunStitchAsync(
        IEnumerable<string> arguments,
        string? workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var command = ResolveStitch()
            ?? throw new FileNotFoundException(
                "Google Stitch CLI was not found in Talvora's managed tools directory.");

        var requestedArguments = arguments.ToArray();
        var fullArguments = new string[requestedArguments.Length + 1];
        fullArguments[0] = command.CliScript;
        Array.Copy(
            requestedArguments,
            0,
            fullArguments,
            1,
            requestedArguments.Length);

        var result =
            await InteractiveUserProcessRunner.RunAsync(
                command.NodeExecutable,
                NormalizeWorkingDirectory(workingDirectory),
                fullArguments,
                new Dictionary<string, string?>
                {
                    ["NO_COLOR"] = "1",
                    ["FORCE_COLOR"] = "0",
                },
                timeoutSeconds,
                cancellationToken);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            RedactStitchText(result.StandardOutput),
            RedactStitchText(result.StandardError),
            result.TimedOut,
            result.ProcessId,
            command.CliScript,
            result.WorkingDirectory,
            requestedArguments.Select(RedactStitchText).ToArray(),
            result.ElapsedMilliseconds);
    }

    private static StitchCommand? ResolveStitch()
    {
        var node = CommandResolver.Resolve(
            ["node.exe", "node"],
            [@"C:\Program Files\nodejs\node.exe"]);
        if (node is null)
        {
            return null;
        }

        var configured =
            Environment.GetEnvironmentVariable("TALVORA_STITCH_CLI");
        var cliScript =
            !string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "Talvora",
                    "Tools",
                    "Stitch",
                    "node_modules",
                    "@google",
                    "stitch",
                    "dist",
                    "stitch.js");

        return File.Exists(cliScript)
            ? new StitchCommand(node, cliScript)
            : null;
    }

    private static string NormalizeWorkingDirectory(
        string? workingDirectory)
    {
        string full;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            var context =
                WindowsSessionLauncher.GetDefaultInteractiveUser();
            full = context.UserProfile
                ?? throw new InvalidOperationException(
                    "Logged-on Windows user's profile directory could not be resolved.");
        }
        else
        {
            full = Path.GetFullPath(workingDirectory);
        }

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(
                $"Working directory was not found: {full}");
        }

        return full;
    }

    private static void ValidateSimpleToken(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!SimpleTokenPattern.IsMatch(value.Trim()))
        {
            throw new ArgumentException(
                "Value must be a simple Stitch command or resource token.",
                parameterName);
        }
    }

    private static void ValidateGeneralArguments(
        IReadOnlyList<string> arguments)
    {
        var totalCharacters = 0;
        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException(
                    "Stitch CLI arguments must not contain null values.",
                    nameof(arguments));
            }

            totalCharacters += argument.Length;
            if (totalCharacters > MaximumArgumentCharacters)
            {
                throw new ArgumentException(
                    $"Stitch CLI arguments exceed the {MaximumArgumentCharacters} character limit.",
                    nameof(arguments));
            }

            var normalized = argument.Trim();
            if (normalized.Equals("--trace", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("--token", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("--api-key", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("--api-key=", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("--access-token", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("--access-token=", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Secret-bearing Stitch flags, token printing, and raw HTTP tracing are not exposed through Talvora.",
                    nameof(arguments));
            }
        }

        if (arguments.Count >= 3 &&
            arguments[0].Equals("config", StringComparison.OrdinalIgnoreCase) &&
            arguments[1].Equals("get", StringComparison.OrdinalIgnoreCase) &&
            IsSensitiveConfigKey(arguments[2]))
        {
            throw new ArgumentException(
                "Reading secret Stitch configuration values is not exposed through Talvora.",
                nameof(arguments));
        }
    }

    private static bool IsSensitiveConfigKey(string value) =>
        value.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("api_key", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("accessToken", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("access_token", StringComparison.OrdinalIgnoreCase);

    private static void AddOption(
        ICollection<string> arguments,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        arguments.Add(name);
        arguments.Add(value);
    }

    private static string? TryGetString(
        JsonElement parent,
        string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string RedactStitchText(string? value)
    {
        var redacted = FileLog.RedactSensitiveData(value);
        return JsonSecretPattern.Replace(
            redacted,
            match =>
                $"\"{match.Groups["name"].Value}\": \"[REDACTED]\"");
    }
}
