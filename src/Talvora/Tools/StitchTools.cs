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
