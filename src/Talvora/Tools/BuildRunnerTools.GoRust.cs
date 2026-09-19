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
        var cargo = ResolveRustTool("cargo");
        var rustc = ResolveRustTool("rustc");
        var rustup = ResolveRustTool("rustup");

        string? cargoVersion = null;
        string? rustcVersion = null;
        string? rustupVersion = null;
        string? activeToolchain = null;
        string? host = null;

        if (cargo is not null)
        {
            cargoVersion = (await RunCliCheckedAsync(
                cargo,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken)).StandardOutput.Trim();
        }

        if (rustc is not null)
        {
            rustcVersion = (await RunCliCheckedAsync(
                rustc,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken)).StandardOutput.Trim();

            var verbose = await RunCliCheckedAsync(
                rustc,
                Environment.CurrentDirectory,
                ["-vV"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
            host = ParseLineValue(verbose.StandardOutput, "host: ");
        }

        if (rustup is not null)
        {
            rustupVersion = (await RunCliCheckedAsync(
                rustup,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken)).StandardOutput.Trim();

            activeToolchain = (await RunCliCheckedAsync(
                rustup,
                Environment.CurrentDirectory,
                ["show", "active-toolchain"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken)).StandardOutput.Trim();
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

        var executable = ResolveRequestedExecutable(
            explicitExecutable,
            () => ResolveRustTool(tool),
            tool);

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static string? ResolveGo() =>
        CommandResolver.Resolve(
            ["go.exe", "go"],
            [@"C:\Program Files\Go\bin\go.exe"]);

    private static string? ResolveRustTool(string tool)
    {
        var candidates = new List<string>();
        var cargoHome = Environment.GetEnvironmentVariable("CARGO_HOME");

        if (!string.IsNullOrWhiteSpace(cargoHome))
        {
            candidates.Add(Path.Combine(cargoHome, "bin", tool + ".exe"));
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, ".cargo", "bin", tool + ".exe"));
        }

        return CommandResolver.Resolve(
            [tool + ".exe", tool],
            candidates);
    }

    private static string? GetJsonString(
        JsonElement root,
        string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
