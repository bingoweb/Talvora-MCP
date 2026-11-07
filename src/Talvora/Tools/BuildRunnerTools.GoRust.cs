using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraGoInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    string? GoRoot,
    string? GoPath,
    string? GoOs,
    string? GoArch,
    string? GoToolchain);

public sealed record TalvoraRustInfoResponse(
    bool CargoFound,
    string? CargoExecutable,
    string? CargoVersion,
    bool RustcFound,
    string? RustcExecutable,
    string? RustcVersion,
    string? Host,
    bool RustupFound,
    string? RustupExecutable,
    string? RustupVersion,
    string? ActiveToolchain);

public static partial class BuildRunnerTools
{
    [McpServerTool(
        Name = "talvora_go_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGoInfoResponse)),
     Description("Resolve the Go CLI and report version plus core go env values including GOROOT, GOPATH, GOOS, GOARCH, and GOTOOLCHAIN. Missing Go is reported structurally.")]
    public static async Task<TalvoraGoInfoResponse> GoInfo(
        CancellationToken cancellationToken = default)
    {
        var go = ResolveGo();
        if (go is null)
        {
            return new TalvoraGoInfoResponse(
                false, null, null, null, null, null, null, null);
        }

        var versionResult = await RunCliCheckedAsync(
            go,
            Environment.CurrentDirectory,
            ["version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var envResult = await RunCliCheckedAsync(
            go,
            Environment.CurrentDirectory,
            ["env", "-json", "GOROOT", "GOPATH", "GOOS", "GOARCH", "GOTOOLCHAIN"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        using var document = JsonDocument.Parse(envResult.StandardOutput);
        var root = document.RootElement;

        return new TalvoraGoInfoResponse(
            true,
            go,
            versionResult.StandardOutput.Trim(),
            GetJsonString(root, "GOROOT"),
            GetJsonString(root, "GOPATH"),
            GetJsonString(root, "GOOS"),
            GetJsonString(root, "GOARCH"),
            GetJsonString(root, "GOTOOLCHAIN"));
    }

    [McpServerTool(
        Name = "talvora_go_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Go CLI with an arbitrary argument vector, working directory, environment overrides, and timeout. No command/module/package/proxy/toolchain/flag allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> GoRun(
        string workingDirectory,
        string[] arguments,
        string? goExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var executable = ResolveRequestedExecutable(
            goExecutable,
            ResolveGo,
            "go");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_rust_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRustInfoResponse)),
     Description("Resolve Cargo, rustc, and rustup and report versions, rustc host triple, and active rustup toolchain. Missing components are reported structurally.")]
    public static async Task<TalvoraRustInfoResponse> RustInfo(
        CancellationToken cancellationToken = default)
    {
        // RS1: report the signed-in user's toolchain, the one their builds use (never the service account's own).
        var cwd = Environment.SystemDirectory;
        var where = await RunAsSignedInUserAsync(
            "where.exe", cwd, ["cargo", "rustc", "rustup"], null, 30, cancellationToken);
        var found = where.StandardOutput.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? Find(string tool) => found.FirstOrDefault(path => string.Equals(
            Path.GetFileNameWithoutExtension(path), tool, StringComparison.OrdinalIgnoreCase));
        var cargo = Find("cargo");
        var rustc = Find("rustc");
        var rustup = Find("rustup");

        string? cargoVersion = null;
        string? rustcVersion = null;
        string? rustupVersion = null;
        string? activeToolchain = null;
        string? host = null;

        if (cargo is not null)
        {
            cargoVersion = (await RunAsSignedInUserAsync(
                cargo, cwd, ["--version"], null, 30, cancellationToken)).StandardOutput.Trim();
        }

        if (rustc is not null)
        {
            var verbose = (await RunAsSignedInUserAsync(
                rustc, cwd, ["-vV"], null, 30, cancellationToken)).StandardOutput;
            rustcVersion = verbose.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault();
            host = ParseLineValue(verbose, "host: ");
        }

        if (rustup is not null)
        {
            rustupVersion = (await RunAsSignedInUserAsync(
                rustup, cwd, ["--version"], null, 30, cancellationToken)).StandardOutput.Trim();
            activeToolchain = (await RunAsSignedInUserAsync(
                rustup, cwd, ["show", "active-toolchain"], null, 30, cancellationToken)).StandardOutput.Trim();
        }

        return new TalvoraRustInfoResponse(
            cargo is not null,
            cargo,
            cargoVersion,
            rustc is not null,
            rustc,
            rustcVersion,
            host,
            rustup is not null,
            rustup,
            rustupVersion,
            activeToolchain);
    }

    [McpServerTool(
        Name = "talvora_cargo_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Cargo with an arbitrary argument vector in any accessible working directory. No subcommand/package/registry/target/toolchain/feature/flag allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> CargoRun(
        string workingDirectory,
        string[] arguments,
        string? cargoExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunRustTool(
            workingDirectory,
            arguments,
            cargoExecutable,
            "cargo",
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_rustc_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run rustc with an arbitrary compiler argument vector in any accessible working directory. No crate/source/target/linker/codegen/unstable-option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> RustcRun(
        string workingDirectory,
        string[] arguments,
        string? rustcExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunRustTool(
            workingDirectory,
            arguments,
            rustcExecutable,
            "rustc",
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_rustup_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run rustup with an arbitrary argument vector for toolchains, targets, components, overrides, profiles, and updates. No toolchain/target/component/command allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> RustupRun(
        string workingDirectory,
        string[] arguments,
        string? rustupExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunRustTool(
            workingDirectory,
            arguments,
            rustupExecutable,
            "rustup",
            environment,
            timeoutSeconds,
            cancellationToken);

    // RS1: cargo, rustc and rustup run as the signed-in user: the user's rustup toolchain and crate cache,
    // and build output owned by the user. A bare name resolves on that user's PATH.
    private static Task<TalvoraCliCommandResponse> RunRustTool(
        string workingDirectory,
        string[] arguments,
        string? explicitExecutable,
        string tool,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunAsSignedInUserAsync(
            string.IsNullOrWhiteSpace(explicitExecutable) ? tool : explicitExecutable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<TalvoraCliCommandResponse> RunAsSignedInUserAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await InteractiveUserProcessRunner.RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken,
            ProcessRunner.DefaultMaximumCapturedCharacters,
            WindowsSessionLauncher.GetActiveInteractiveUser(),
            closeStandardInput: true).ConfigureAwait(false);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments,
            result.ElapsedMilliseconds);
    }

    private static string? ResolveGo() =>
        CommandResolver.Resolve(
            ["go.exe", "go"],
            [@"C:\Program Files\Go\bin\go.exe"]);

    private static string? GetJsonString(
        JsonElement root,
        string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
