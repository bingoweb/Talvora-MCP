using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record TunnelProvisioningScope(
    IReadOnlyList<string> OrganizationIds,
    IReadOnlyList<string> WorkspaceIds,
    string ReferenceTunnelId,
    DateTimeOffset UpdatedAtUtc);

internal sealed record TunnelProvisioningAssessment(
    bool Required,
    bool HasTunnelId,
    bool HasConfig,
    bool HasRuntimeCredential,
    bool HasAdminCredential,
    bool CanAutoProvision,
    string Summary);

internal sealed record TunnelProvisioningPreflight(
    string ClientVersion,
    int OrganizationScopeCount,
    int WorkspaceScopeCount,
    bool AdminCredentialPresent);

internal sealed record ManagedMcpTunnelRuntimeStatus(
    bool Ready,
    bool ProcessRunning,
    bool Healthy,
    bool IdentityMatches,
    string Detail);

internal static partial class ManagedMcpTunnelProvisioningService
{
    private const int TunnelActivationDelaySeconds = 30;
    private static readonly TimeSpan RuntimeStatusCacheDuration =
        TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, RuntimeStatusCacheEntry> RuntimeStatusCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim TunnelClientReleaseGate = new(1, 1);
    private static readonly HttpClient TunnelClientReleaseHttp = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(25));
    private static DateTimeOffset _tunnelClientLatestCheckedAtUtc;
    private static string? _cachedTunnelClientLatestTag;
    private static readonly Regex TunnelIdPattern = new(
        "^tunnel_[0-9a-f]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string ScopePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "tunnel-scope.json");

    public static bool HasReusableRuntimeCredential(
        ManagedMcpRegistryDocument registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return FindReusableRuntimeSource(registry) is not null;
    }

    public static TunnelProvisioningAssessment Assess(
        ManagedMcpRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var tunnel = registration.Tunnel;
        if (tunnel is null || !tunnel.Required)
        {
            return new TunnelProvisioningAssessment(
                Required: false,
                HasTunnelId: false,
                HasConfig: false,
                HasRuntimeCredential: false,
                HasAdminCredential: ControlCenterAdminCredentialStore.Exists(),
                CanAutoProvision: false,
                Summary: "Bu MCP için tünel gerekmiyor.");
        }

        var configPath = ResolveConfigPath(registration);
        var credentialPath = GetRuntimeCredentialPath(configPath);
        var hasTunnelId = IsTunnelId(tunnel.TunnelId);
        var hasConfig = File.Exists(configPath);
        var hasRuntime = IsRuntimeCredentialUsable(credentialPath);
        var hasAdmin = ControlCenterAdminCredentialStore.Exists();
        var hasReusableRuntime = hasRuntime || HasReusableRuntimeSource();
        var hasPendingTunnelId =
            !hasTunnelId &&
            HasPendingRemoteTunnelId(
                registration.Id);

        var canAutoProvision =
            hasReusableRuntime &&
            (hasTunnelId ||
             hasPendingTunnelId ||
             hasAdmin);

        var summary = hasTunnelId && hasConfig && hasRuntime
            ? "Tünel yapılandırması hazır."
            : !hasReusableRuntime
                ? "Yeniden kullanılabilir Runtime API key bulunamadı."
                : !hasTunnelId &&
                  !hasPendingTunnelId &&
                  !hasAdmin
                    ? "Yeni tünel oluşturmak için OpenAI Admin API key gerekiyor."
                    : "Tünel otomatik olarak hazırlanabilir.";

        return new TunnelProvisioningAssessment(
            Required: true,
            HasTunnelId: hasTunnelId,
            HasConfig: hasConfig,
            HasRuntimeCredential: hasRuntime,
            HasAdminCredential: hasAdmin,
            CanAutoProvision: canAutoProvision,
            Summary: summary);
    }

    public static async Task<ManagedMcpRegistryDocument> EnsureMissingTunnelsAsync(
        ManagedMcpRegistryDocument registry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var current = registry;
        foreach (var registration in registry.Mcps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteCommittedPendingProvision(
                registration);

            var assessment = Assess(registration);
            if (!assessment.Required ||
                (assessment.HasTunnelId &&
                 assessment.HasConfig &&
                 assessment.HasRuntimeCredential) ||
                !assessment.CanAutoProvision)
            {
                continue;
            }

            try
            {
                var provisioned = await ProvisionAsync(
                    registration,
                    current,
                    cancellationToken).ConfigureAwait(false);

                current = await ManagedMcpRegistryCoordinator.UpsertAsync(
                    provisioned,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                TrayLog.Write(
                    $"Automatic tunnel provisioning failed. MCP={registration.Id}",
                    ex);
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Warning,
                    "tunnel",
                    $"{registration.DisplayName} tüneli hazırlanamadı",
                    "Eksik bilgi tamamlandığında veya sonraki başlangıçta yeniden denenecek.",
                    registration.Id,
                    $"tunnel:{registration.Id}:provision-failed");
            }
        }

        return current;
    }

    public static async Task<ManagedMcpRegistration> ProvisionAsync(
        ManagedMcpRegistration registration,
        ManagedMcpRegistryDocument registry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registry);

        if (registration.Tunnel is null || !registration.Tunnel.Required)
        {
            return registration;
        }

        var reference = FindReusableRuntimeSource(registry)
            ?? throw new InvalidOperationException(
                "Yeniden kullanılabilir güvenli MCP tünel Runtime API key kaynağı bulunamadı.");

        string? runtimeKey = null;
        string? adminKey = null;

        try
        {
            runtimeKey = DpapiSecretStore.ReadString(
                reference.RuntimeCredentialPath,
                "tunnel Runtime API key");

            var tunnelId = registration.Tunnel.TunnelId;
            if (!IsTunnelId(tunnelId))
            {
                if (ControlCenterAdminCredentialStore.Exists())
                {
                    adminKey =
                        ControlCenterAdminCredentialStore.Read();
                }

                var scope = await GetOrDiscoverScopeAsync(
                    reference,
                    runtimeKey,
                    cancellationToken).ConfigureAwait(false);

                tunnelId = await ResolveOrCreatePendingRemoteTunnelAsync(
                    reference.Config,
                    registration,
                    scope,
                    runtimeKey,
                    adminKey,
                    cancellationToken).ConfigureAwait(false);

                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "tunnel",
                    $"{registration.DisplayName} tüneli oluşturuldu",
                    "OpenAI tünel kaydı oluşturuldu; yerel profil hazırlanıyor.",
                    registration.Id,
                    $"tunnel:{registration.Id}:created");

                await Task.Delay(
                    TimeSpan.FromSeconds(TunnelActivationDelaySeconds),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!IsTunnelId(tunnelId))
            {
                throw new InvalidOperationException(
                    "OpenAI tünel oluşturma yanıtında geçerli tunnel_id bulunamadı.");
            }

            var configPath = ResolveConfigPath(registration);
            var root = Path.GetDirectoryName(configPath)
                ?? throw new InvalidOperationException(
                    "Tunnel config dizini çözümlenemedi.");
            var stateRoot = string.IsNullOrWhiteSpace(registration.Tunnel.StateRoot)
                ? Path.Combine(root, "state")
                : Path.GetFullPath(registration.Tunnel.StateRoot);
            var profileRoot = Path.Combine(stateRoot, "profiles");
            var runtimeCredentialPath = GetRuntimeCredentialPath(configPath);

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(stateRoot);
            Directory.CreateDirectory(profileRoot);

            DpapiSecretStore.WriteString(runtimeCredentialPath, runtimeKey);

            var config = new BusinessConfig(
                registration.Tunnel.Alias,
                tunnelId!,
                registration.Endpoint,
                reference.Config.TunnelClient,
                reference.Config.TunnelClientVersion,
                stateRoot,
                DateTimeOffset.UtcNow.ToString("O"));

            WriteBusinessConfig(configPath, config);

            var updatedTunnel = registration.Tunnel with
            {
                TunnelId = tunnelId,
                ConfigPath = configPath,
                StateRoot = stateRoot,
            };
            var updatedRegistration = registration with
            {
                Tunnel = updatedTunnel,
            };

            await ManagedMcpRegistryCoordinator.UpsertAsync(
                updatedRegistration,
                cancellationToken).ConfigureAwait(false);

            await DeletePendingProvisionAfterCommitAsync(
                registration.Id,
                tunnelId!).ConfigureAwait(false);

            await ConnectExistingAsync(
                updatedRegistration,
                cancellationToken).ConfigureAwait(false);

            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "tunnel",
                $"{registration.DisplayName} güvenli tüneli hazır",
                "Runtime profili oluşturuldu ve health/ready doğrulaması geçti.",
                registration.Id,
                $"tunnel:{registration.Id}:ready");

            return updatedRegistration;
        }
        finally
        {
            runtimeKey = null;
            adminKey = null;
        }
    }

    public static async Task<bool> EnsureLatestClientAndReconnectIfNeededAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.Tunnel is not { Required: true })
        {
            return false;
        }

        var configPath = ResolveConfigPath(registration);
        if (!File.Exists(configPath))
        {
            return false;
        }

        using var lease =
            ManagedMcpOperationCoordinator.TryAcquire(
                registration.Id);
        if (lease is null)
        {
            return false;
        }

        var recovery =
            await RecoverInterruptedClientUpdateAsync(
                registration,
                cancellationToken).ConfigureAwait(false);
        if (recovery.Handled)
        {
            return recovery.Updated;
        }

        var existing = LoadBusinessConfig(configPath);
        var updated = await StageLatestTunnelClientAsync(
            existing,
            cancellationToken).ConfigureAwait(false);

        if (string.Equals(
                existing.TunnelClientVersion,
                updated.TunnelClientVersion,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                existing.TunnelClient,
                updated.TunnelClient,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        InvalidateRuntimeStatusCache(registration.Id);

        await ExecuteClientUpdateTransactionAsync(
            registration,
            configPath,
            existing,
            updated,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public static async Task ConnectExistingAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        InvalidateRuntimeStatusCache(registration.Id);

        if (registration.Tunnel is null ||
            !registration.Tunnel.Required ||
            !IsTunnelId(registration.Tunnel.TunnelId))
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} için bağlanılabilir tünel kaydı yok.");
        }

        var configPath = ResolveConfigPath(registration);
        var recovery =
            await RecoverInterruptedClientUpdateAsync(
                registration,
                cancellationToken).ConfigureAwait(false);
        if (recovery.Handled)
        {
            return;
        }

        var config = LoadBusinessConfig(configPath);
        var candidate = await StageLatestTunnelClientAsync(
            config,
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(
                config.TunnelClientVersion,
                candidate.TunnelClientVersion,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                config.TunnelClient,
                candidate.TunnelClient,
                StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteClientUpdateTransactionAsync(
                registration,
                configPath,
                config,
                candidate,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await ConnectExistingWithConfigAsync(
            registration,
            configPath,
            config,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConnectExistingWithConfigAsync(
        ManagedMcpRegistration registration,
        string configPath,
        BusinessConfig config,
        CancellationToken cancellationToken)
    {
        InvalidateRuntimeStatusCache(registration.Id);
        var credentialPath = GetRuntimeCredentialPath(configPath);

        string? runtimeKey = null;
        try
        {
            runtimeKey = DpapiSecretStore.ReadString(
                credentialPath,
                "tunnel Runtime API key");

            var profileRoot = Path.Combine(config.StateRoot, "profiles");
            Directory.CreateDirectory(config.StateRoot);
            Directory.CreateDirectory(profileRoot);

            var args = BuildRuntimeConnectArguments(
                registration,
                config,
                profileRoot);

            var result = await RunClientAsync(
                config.TunnelClient,
                config.StateRoot,
                args,
                runtimeKey,
                adminKey: null,
                timeout: TimeSpan.FromSeconds(45),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"tunnel-client connect başarısız: {CollapseSafe(result.StandardError, result.StandardOutput, runtimeKey)}");
            }

            await WaitForReadyAsync(
                config,
                runtimeKey,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeKey = null;
        }
    }

    public static async Task DisconnectExistingAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        InvalidateRuntimeStatusCache(registration.Id);

        if (registration.Tunnel is null)
        {
            return;
        }

        var configPath = ResolveConfigPath(registration);
        if (!File.Exists(configPath))
        {
            return;
        }

        var config = LoadBusinessConfig(configPath);
        var result = await RunClientAsync(
            config.TunnelClient,
            config.StateRoot,
            ["runtimes", "stop", config.Alias, "--json"],
            runtimeKey: null,
            adminKey: null,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            var diagnostic = CollapseSafe(
                result.StandardError,
                result.StandardOutput);

            if (IsIdempotentRuntimeStopResult(diagnostic))
            {
                return;
            }

            throw new InvalidOperationException(
                $"tunnel-client stop başarısız: {diagnostic}");
        }
    }

    public static async Task<ManagedMcpTunnelRuntimeStatus> GetRuntimeStatusCachedAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var now = DateTimeOffset.UtcNow;
        if (RuntimeStatusCache.TryGetValue(
                registration.Id,
                out var cached) &&
            cached.ExpiresAtUtc > now)
        {
            return cached.Status;
        }

        var status = await GetRuntimeStatusAsync(
            registration,
            cancellationToken);

        RuntimeStatusCache[registration.Id] =
            new RuntimeStatusCacheEntry(
                now.Add(RuntimeStatusCacheDuration),
                status);
        return status;
    }

    public static void InvalidateRuntimeStatusCache(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        _ = RuntimeStatusCache.TryRemove(mcpId, out _);
    }

    public static async Task<ManagedMcpTunnelRuntimeStatus> GetRuntimeStatusAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.Tunnel is null ||
            !registration.Tunnel.Required ||
            !IsTunnelId(registration.Tunnel.TunnelId))
        {
            return new ManagedMcpTunnelRuntimeStatus(
                Ready: false,
                ProcessRunning: false,
                Healthy: false,
                IdentityMatches: false,
                Detail: "Yönetilen tünel kimliği eksik.");
        }

        var configPath = ResolveConfigPath(registration);
        if (!File.Exists(configPath))
        {
            return new ManagedMcpTunnelRuntimeStatus(
                Ready: false,
                ProcessRunning: false,
                Healthy: false,
                IdentityMatches: false,
                Detail: "Tünel yapılandırma dosyası bulunamadı.");
        }

        var config = LoadBusinessConfig(configPath);
        var result = await RunClientAsync(
            config.TunnelClient,
            config.StateRoot,
            ["runtimes", "status", config.Alias, "--json"],
            runtimeKey: null,
            adminKey: null,
            timeout: TimeSpan.FromSeconds(12),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new ManagedMcpTunnelRuntimeStatus(
                Ready: false,
                ProcessRunning: false,
                Healthy: false,
                IdentityMatches: false,
                Detail: "Tünel runtime durumu okunamadı: " +
                    CollapseSafe(
                        result.StandardError,
                        result.StandardOutput));
        }

        var json = ExtractJson(result.StandardOutput);
        if (json is null)
        {
            return new ManagedMcpTunnelRuntimeStatus(
                Ready: false,
                ProcessRunning: false,
                Healthy: false,
                IdentityMatches: false,
                Detail: "Tünel runtime durumu geçerli JSON içermiyor.");
        }

        using var document = JsonDocument.Parse(json);
        return ParseRuntimeStatus(
            config,
            document.RootElement);
    }

    public static async Task<TunnelProvisioningPreflight> RunReadOnlyPreflightAsync(
        CancellationToken cancellationToken)
    {
        var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
            cancellationToken).ConfigureAwait(false);
        var reference = FindReusableRuntimeSource(registry)
            ?? throw new InvalidOperationException(
                "Provisioning preflight için mevcut bir tünel bulunamadı.");

        string? runtimeKey = null;
        try
        {
            runtimeKey = DpapiSecretStore.ReadString(
                reference.RuntimeCredentialPath,
                "tunnel Runtime API key");

            var scope = await GetOrDiscoverScopeAsync(
                reference,
                runtimeKey,
                cancellationToken,
                forceRefresh: true).ConfigureAwait(false);

            return new TunnelProvisioningPreflight(
                reference.Config.TunnelClientVersion,
                scope.OrganizationIds.Count,
                scope.WorkspaceIds.Count,
                ControlCenterAdminCredentialStore.Exists());
        }
        finally
        {
            runtimeKey = null;
        }
    }

    internal static void AssertPolicyContract()
    {
        ControlCenterAdminCredentialStore.AssertSeparationContract();

        if (!IsTunnelId("tunnel_0123456789abcdef0123456789abcdef") ||
            IsTunnelId("tunnel_0123456789ABCDEF0123456789ABCDEF") ||
            IsTunnelId("not-a-tunnel"))
        {
            throw new InvalidOperationException(
                "Tunnel ID validation contract failed.");
        }


        var registration = new ManagedMcpRegistration
        {
            Id = "sample",
            DisplayName = "Sample MCP",
            Description = "Sample",
            Endpoint = "http://127.0.0.1:9999/mcp",
            Tunnel = new ManagedMcpTunnelRegistration
            {
                Alias = "sample-business",
                TunnelId = "tunnel_0123456789abcdef0123456789abcdef",
                ConfigPath = @"C:\Temp\sample\business.json",
                StateRoot = @"C:\Temp\sample\state",
            },
        };
        var collisionOne = registration with
        {
            Id = "foo/bar",
            Tunnel = registration.Tunnel! with
            {
                ConfigPath = null,
            },
        };
        var collisionTwo = collisionOne with
        {
            Id = "foo?bar",
        };
        if (string.Equals(
                ResolveConfigPath(collisionOne),
                ResolveConfigPath(collisionTwo),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Managed MCP tunnel fallback path identity collision contract failed.");
        }
        var config = new BusinessConfig(
            "sample-business",
            "tunnel_0123456789abcdef0123456789abcdef",
            registration.Endpoint,
            @"C:\Temp\tunnel-client.exe",
            "v0.0.14",
            @"C:\Temp\sample\state",
            DateTimeOffset.UtcNow.ToString("O"));

        var args = BuildRuntimeConnectArguments(
            registration,
            config,
            @"C:\Temp\sample\state\profiles");

        if (!args.Contains("env:CONTROL_PLANE_API_KEY") ||
            args.Any(value => value.Contains("OPENAI_ADMIN_KEY", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Runtime/Admin key separation in connect arguments failed.");
        }

        const string fakeSecret = "sk-test-secret-never-log";
        var collapsed = CollapseSafe(
            $"failure {fakeSecret}",
            "details",
            fakeSecret);
        if (collapsed.Contains(fakeSecret, StringComparison.Ordinal) ||
            !collapsed.Contains("[REDACTED]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Tunnel diagnostic secret-redaction contract failed.");
        }
    }

    private sealed record RuntimeStatusCacheEntry(
        DateTimeOffset ExpiresAtUtc,
        ManagedMcpTunnelRuntimeStatus Status);

    private sealed record RuntimeSource(
        BusinessConfig Config,
        string ConfigPath,
        string RuntimeCredentialPath);

    private static RuntimeSource? FindReusableRuntimeSource(
        ManagedMcpRegistryDocument registry)
    {
        var candidates = registry.Mcps
            .OrderBy(entry =>
                string.Equals(entry.Id, "talvora", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1);

        foreach (var candidate in candidates)
        {
            if (candidate.Tunnel is null ||
                !IsTunnelId(candidate.Tunnel.TunnelId))
            {
                continue;
            }

            var configPath = ResolveConfigPath(candidate);
            var credentialPath = GetRuntimeCredentialPath(configPath);
            if (!File.Exists(configPath) || !IsRuntimeCredentialUsable(credentialPath))
            {
                continue;
            }

            try
            {
                var config = LoadBusinessConfig(configPath);
                if (File.Exists(config.TunnelClient))
                {
                    return new RuntimeSource(
                        config,
                        configPath,
                        credentialPath);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                JsonException or
                InvalidOperationException)
            {
                TrayLog.Write(
                    $"Reusable tunnel source could not be read. MCP={candidate.Id}",
                    ex);
            }
        }

        return null;
    }

    private static bool HasReusableRuntimeSource()
    {
        try
        {
            var registry = ManagedMcpRegistryCoordinator
                .LoadOrRecoverAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return FindReusableRuntimeSource(registry) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<TunnelProvisioningScope> GetOrDiscoverScopeAsync(
        RuntimeSource reference,
        string runtimeKey,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = TryReadScope();
            if (cached is not null &&
                string.Equals(
                    cached.ReferenceTunnelId,
                    reference.Config.TunnelId,
                    StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow - cached.UpdatedAtUtc <=
                    TimeSpan.FromHours(24) &&
                (cached.OrganizationIds.Count > 0 ||
                 cached.WorkspaceIds.Count > 0))
            {
                return cached;
            }
        }

        var result = await RunClientAsync(
            reference.Config.TunnelClient,
            reference.Config.StateRoot,
            ["admin", "--json", "tunnels", "get", reference.Config.TunnelId],
            runtimeKey,
            adminKey: null,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Mevcut tünel scope bilgisi okunamadı: {CollapseSafe(result.StandardError, result.StandardOutput, runtimeKey)}");
        }

        var json = ExtractJson(result.StandardOutput)
            ?? throw new InvalidOperationException(
                "Mevcut tünel scope yanıtı JSON içermiyor.");

        using var document = JsonDocument.Parse(json);
        var organizations = FindStringArray(
            document.RootElement,
            "organization_ids");
        var workspaces = FindStringArray(
            document.RootElement,
            "workspace_ids");

        if (organizations.Count == 0 && workspaces.Count == 0)
        {
            throw new InvalidOperationException(
                "Mevcut tünel metadata'sında organization/workspace scope bulunamadı.");
        }

        var scope = new TunnelProvisioningScope(
            organizations,
            workspaces,
            reference.Config.TunnelId,
            DateTimeOffset.UtcNow);

        JsonFileStore.WriteAsync(
                ScopePath,
                scope,
                ConfigJsonOptions,
                createBackup: false,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return scope;
    }

    private static TunnelProvisioningScope? TryReadScope()
    {
        if (!File.Exists(ScopePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TunnelProvisioningScope>(
                File.ReadAllText(ScopePath),
                ConfigJsonOptions);
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write("Tunnel provisioning scope cache could not be read", ex);
            return null;
        }
    }

    private static async Task<string> CreateRemoteTunnelAsync(
        BusinessConfig clientConfig,
        string tunnelName,
        string tunnelDescription,
        TunnelProvisioningScope scope,
        string adminKey,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "admin",
            "--json",
            "tunnels",
            "create",
            "--name",
            tunnelName,
            "--description",
            tunnelDescription,
        };

        foreach (var organizationId in scope.OrganizationIds)
        {
            args.Add("--organization-id");
            args.Add(organizationId);
        }

        foreach (var workspaceId in scope.WorkspaceIds)
        {
            args.Add("--workspace-id");
            args.Add(workspaceId);
        }

        var result = await RunClientAsync(
            clientConfig.TunnelClient,
            clientConfig.StateRoot,
            args,
            runtimeKey: null,
            adminKey,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"OpenAI tunnel oluşturma başarısız: {CollapseSafe(result.StandardError, result.StandardOutput, adminKey)}");
        }

        var json = ExtractJson(result.StandardOutput)
            ?? throw new InvalidOperationException(
                "OpenAI tunnel oluşturma yanıtı JSON içermiyor.");

        using var document = JsonDocument.Parse(json);
        var tunnelId = FindFirstTunnelId(document.RootElement);
        if (!IsTunnelId(tunnelId))
        {
            throw new InvalidOperationException(
                "OpenAI tunnel oluşturma yanıtında tunnel_id bulunamadı.");
        }

        return tunnelId!;
    }

    private static IReadOnlyList<string> BuildRuntimeConnectArguments(
        ManagedMcpRegistration registration,
        BusinessConfig config,
        string profileRoot) =>
        [
            "runtimes",
            "connect",
            "--alias",
            config.Alias,
            "--tunnel-id",
            config.TunnelId,
            "--runtime-api-key",
            "env:CONTROL_PLANE_API_KEY",
            "--mcp-server-url",
            registration.Endpoint,
            "--profile",
            config.Alias,
            "--profile-dir",
            profileRoot,
            "--json",
        ];

    private static async Task WaitForReadyAsync(
        BusinessConfig config,
        string runtimeKey,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await RunClientAsync(
                config.TunnelClient,
                config.StateRoot,
                ["runtimes", "status", config.Alias, "--json"],
                runtimeKey,
                adminKey: null,
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                var json = ExtractJson(result.StandardOutput);
                if (json is not null)
                {
                    using var document = JsonDocument.Parse(json);
                    var status = ParseRuntimeStatus(
                        config,
                        document.RootElement);
                    if (status.Ready)
                    {
                        return;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Yeni güvenli MCP tüneli 90 saniye içinde hazır olmadı.");
    }

    private static async Task<ProcessExecutionResult> RunClientAsync(
        string clientPath,
        string stateRoot,
        IReadOnlyList<string> arguments,
        string? runtimeKey,
        string? adminKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(clientPath))
        {
            throw new FileNotFoundException(
                "OpenAI tunnel-client bulunamadı.",
                clientPath);
        }

        Directory.CreateDirectory(stateRoot);

        var environment = new Dictionary<string, string?>
        {
            ["TUNNEL_CLIENT_STATE_DIR"] = stateRoot,
            ["LOG_LEVEL"] = "warn",
            ["ADMIN_UI_LOG_BUFFER_EVENTS"] = "500",
            ["MCP_STARTUP_WAIT_TIMEOUT"] = "30s",
            ["CONTROL_PLANE_API_KEY"] = runtimeKey,
            ["OPENAI_API_KEY"] = null,
            ["OPENAI_ADMIN_KEY"] = adminKey,
        };

        var result = await ProcessRunner.RunAsync(
            clientPath,
            Path.GetDirectoryName(clientPath),
            arguments,
            environment,
            timeoutSeconds: Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"tunnel-client komutu {timeout.TotalSeconds:F0} saniye içinde tamamlanmadı.");
        }

        return result;
    }

    private static BusinessConfig LoadBusinessConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "Tunnel bağlantı yapılandırması bulunamadı.",
                configPath);
        }

        var config = JsonSerializer.Deserialize<BusinessConfig>(
            File.ReadAllText(configPath),
            ConfigJsonOptions);

        if (config is null ||
            string.IsNullOrWhiteSpace(config.Alias) ||
            !IsTunnelId(config.TunnelId) ||
            string.IsNullOrWhiteSpace(config.McpUrl) ||
            string.IsNullOrWhiteSpace(config.TunnelClient) ||
            string.IsNullOrWhiteSpace(config.StateRoot))
        {
            throw new InvalidOperationException(
                "Tunnel bağlantı yapılandırması geçersiz.");
        }

        return config;
    }

    private static void WriteBusinessConfig(
        string configPath,
        BusinessConfig config)
    {
        var json = JsonSerializer.Serialize(config, ConfigJsonOptions);
        AtomicFile.WriteAllTextAsync(
                configPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                createBackup: File.Exists(configPath),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static string ResolveConfigPath(
        ManagedMcpRegistration registration)
    {
        if (!string.IsNullOrWhiteSpace(registration.Tunnel?.ConfigPath))
        {
            return Path.GetFullPath(registration.Tunnel.ConfigPath);
        }

        var storageKey =
            ManagedMcpIdentityKey.Create(
                registration.Id);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ManagedTunnels",
            storageKey,
            "business.json");
    }

    private static async Task<BusinessConfig> StageLatestTunnelClientAsync(
        BusinessConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var latestTag = await GetLatestTunnelClientTagAsync(
                cancellationToken).ConfigureAwait(false);

            if (string.Equals(
                    config.TunnelClientVersion,
                    latestTag,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(config.TunnelClient))
            {
                return config;
            }

            var architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "amd64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"OpenAI tunnel-client Windows architecture desteklenmiyor: {RuntimeInformation.OSArchitecture}"),
            };

            var archiveName =
                $"tunnel-client-{latestTag}-windows-{architecture}.zip";
            var releaseBase =
                $"https://github.com/openai/tunnel-client/releases/download/{latestTag}";
            var archiveBytes = await DownloadBytesAsync(
                $"{releaseBase}/{archiveName}",
                cancellationToken).ConfigureAwait(false);
            var checksums = Encoding.UTF8.GetString(
                await DownloadBytesAsync(
                    $"{releaseBase}/SHA256SUMS.txt",
                    cancellationToken).ConfigureAwait(false));

            var expectedHash = ParseExpectedSha256(
                checksums,
                archiveName);
            var actualHash = Convert.ToHexString(
                    SHA256.HashData(archiveBytes))
                .ToLowerInvariant();

            if (!string.Equals(
                    expectedHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Downloaded tunnel-client archive SHA256 does not match the official release checksum.");
            }

            var versionRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Talvora",
                "TunnelClient",
                "versions",
                latestTag.TrimStart('v'));
            Directory.CreateDirectory(versionRoot);

            var destination = Path.Combine(
                versionRoot,
                "tunnel-client.exe");
            var tempDestination =
                destination + "." +
                Guid.NewGuid().ToString("N") +
                ".tmp";

            try
            {
                using var archiveStream =
                    new MemoryStream(archiveBytes);
                using var archive =
                    new ZipArchive(
                        archiveStream,
                        ZipArchiveMode.Read,
                        leaveOpen: false);

                var entry = archive.Entries.FirstOrDefault(candidate =>
                    string.Equals(
                        Path.GetFileName(candidate.FullName),
                        "tunnel-client.exe",
                        StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException(
                        "Official tunnel-client archive does not contain tunnel-client.exe.");

                await using (var source = entry.Open())
                await using (var target = new FileStream(
                    tempDestination,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    useAsync: true))
                {
                    await source.CopyToAsync(
                        target,
                        cancellationToken).ConfigureAwait(false);
                }

                File.Move(
                    tempDestination,
                    destination,
                    overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(tempDestination);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException)
                {
                }
            }

            var updated = config with
            {
                TunnelClient = destination,
                TunnelClientVersion = latestTag,
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            };

            TrayLog.Write(
                $"OpenAI tunnel-client latest release staged. Version={latestTag}; Path={destination}");

            return updated;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            IOException or
            JsonException or
            InvalidDataException or
            PlatformNotSupportedException)
        {
            if (File.Exists(config.TunnelClient))
            {
                TrayLog.Write(
                    $"OpenAI tunnel-client latest check/update failed; existing verified binary retained. Version={config.TunnelClientVersion}",
                    ex);
                return config;
            }

            throw;
        }
    }

    private static async Task<string> GetLatestTunnelClientTagAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(
                _cachedTunnelClientLatestTag) &&
            now - _tunnelClientLatestCheckedAtUtc <
                TimeSpan.FromHours(6))
        {
            return _cachedTunnelClientLatestTag;
        }

        await TunnelClientReleaseGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(
                    _cachedTunnelClientLatestTag) &&
                now - _tunnelClientLatestCheckedAtUtc <
                    TimeSpan.FromHours(6))
            {
                return _cachedTunnelClientLatestTag;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.github.com/repos/openai/tunnel-client/releases/latest");
            request.Headers.UserAgent.ParseAdd(
                "Talvora-ControlCenter/1.0");

            using var response =
                await TunnelClientReleaseHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(
                    cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty(
                    "tag_name",
                    out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "OpenAI tunnel-client latest release response does not contain tag_name.");
            }

            var tag = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tag) ||
                !Regex.IsMatch(
                    tag,
                    @"^v[0-9]+\.[0-9]+\.[0-9]+$",
                    RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException(
                    $"OpenAI tunnel-client latest release tag is invalid: {tag}");
            }

            _cachedTunnelClientLatestTag = tag;
            _tunnelClientLatestCheckedAtUtc = now;
            return tag;
        }
        finally
        {
            TunnelClientReleaseGate.Release();
        }
    }

    private static async Task<byte[]> DownloadBytesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);
        request.Headers.UserAgent.ParseAdd(
            "Talvora-ControlCenter/1.0");

        using var response =
            await TunnelClientReleaseHttp.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static string ParseExpectedSha256(
        string checksums,
        string archiveName)
    {
        foreach (var line in checksums.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(
                line,
                "^(?<hash>[0-9A-Fa-f]{64})\\s+\\*?(?<name>.+)$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            if (string.Equals(
                    Path.GetFileName(
                        match.Groups["name"].Value.Trim()),
                    archiveName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"]
                    .Value
                    .ToLowerInvariant();
            }
        }

        throw new InvalidDataException(
            $"Official SHA256SUMS.txt does not contain {archiveName}.");
    }

    private static ManagedMcpTunnelRuntimeStatus ParseRuntimeStatus(
        BusinessConfig config,
        JsonElement root)
    {
        var alias = TryGetTopLevelString(root, "alias");
        var tunnelId = TryGetTopLevelString(root, "tunnel_id");
        var profilePath = TryGetTopLevelString(root, "profile_path");
        var processRunning = TryGetTopLevelBoolean(
            root,
            "process_running");
        var healthy = TryGetTopLevelBoolean(
            root,
            "healthy");
        var ready = TryGetTopLevelBoolean(
            root,
            "ready");

        var expectedProfilePath = Path.Combine(
            config.StateRoot,
            "profiles",
            config.Alias + ".yaml");

        var identityMatches =
            string.Equals(
                alias,
                config.Alias,
                StringComparison.Ordinal) &&
            string.Equals(
                tunnelId,
                config.TunnelId,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(profilePath) &&
            string.Equals(
                Path.GetFullPath(profilePath),
                Path.GetFullPath(expectedProfilePath),
                StringComparison.OrdinalIgnoreCase);

        var overallReady =
            identityMatches &&
            processRunning &&
            healthy &&
            ready;

        var detail = overallReady
            ? $"Tünel {config.Alias} kimliği, process ve ready zinciri doğrulandı."
            : $"Tünel runtime doğrulaması eksik. Identity={identityMatches}; Process={processRunning}; Healthy={healthy}; Ready={ready}.";

        return new ManagedMcpTunnelRuntimeStatus(
            overallReady,
            processRunning,
            healthy,
            identityMatches,
            detail);
    }

    private static string? TryGetTopLevelString(
        JsonElement root,
        string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryGetTopLevelBoolean(
        JsonElement root,
        string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(
                propertyName,
                out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false,
        };
    }

    private static bool IsIdempotentRuntimeStopResult(
        string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return false;
        }

        var knownAbsentMessages = new[]
        {
            "is not known",
            "not known",
            "not running",
            "already stopped",
            "runtime not found",
            "alias not found",
        };

        return knownAbsentMessages.Any(message =>
            diagnostic.Contains(
                message,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRuntimeCredentialUsable(string credentialPath)
    {
        if (!File.Exists(credentialPath))
        {
            return false;
        }

        string? secret = null;
        try
        {
            secret = DpapiSecretStore.ReadString(
                credentialPath,
                "tunnel Runtime API key");
            return !string.IsNullOrWhiteSpace(secret);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.Security.Cryptography.CryptographicException)
        {
            TrayLog.Write(
                $"Tunnel runtime credential validation failed. Path={credentialPath}",
                ex);
            return false;
        }
        finally
        {
            secret = null;
        }
    }

    private static string GetRuntimeCredentialPath(string configPath) =>
        Path.Combine(
            Path.GetDirectoryName(configPath)
                ?? throw new InvalidOperationException(
                    "Tunnel config dizini çözümlenemedi."),
            "runtime-key.dpapi");

    private static bool IsTunnelId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        TunnelIdPattern.IsMatch(value);

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start
            ? text[start..(end + 1)]
            : null;
    }

    private static string? FindFirstTunnelId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("id") ||
                     property.NameEquals("tunnel_id")) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var candidate = property.Value.GetString();
                    if (IsTunnelId(candidate))
                    {
                        return candidate;
                    }
                }

                var nested = FindFirstTunnelId(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstTunnelId(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static List<string> FindStringArray(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                var nested = FindStringArray(property.Value, propertyName);
                if (nested.Count > 0)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringArray(item, propertyName);
                if (nested.Count > 0)
                {
                    return nested;
                }
            }
        }

        return [];
    }

    private static bool FindBoolean(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }

                if (FindBoolean(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindBoolean(item, propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string CollapseSafe(
        string first,
        string second,
        params string?[] secrets)
    {
        var value = string.Join(
            " ",
            new[] { first, second }
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));

        foreach (var secret in secrets.Where(value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            value = value.Replace(
                secret!,
                "[REDACTED]",
                StringComparison.Ordinal);
        }

        return value.Length <= 500 ? value : value[..500];
    }
}