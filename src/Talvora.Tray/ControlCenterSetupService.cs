using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ControlCenterSetupState(
    bool IsComplete,
    bool NeedsAdminCredential,
    bool RuntimeFoundationMissing,
    int PendingTunnelCount,
    IReadOnlyList<string> MissingTunnelRegistrationIds,
    string Summary,
    string Detail);

internal static class ControlCenterSetupService
{
    public static ControlCenterSetupState Evaluate(
        ManagedMcpRegistryDocument registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return EvaluateCore(
            registry,
            ControlCenterAdminCredentialStore.Exists(),
            ManagedMcpTunnelProvisioningService.HasReusableRuntimeCredential(
                registry));
    }

    internal static void AssertPolicyContract()
    {
        var synthetic = new ManagedMcpRegistryDocument
        {
            Mcps =
            [
                new ManagedMcpRegistration
                {
                    Id = "incomplete",
                    DisplayName = "Incomplete MCP",
                    Description = "Synthetic setup contract",
                    Endpoint = "http://127.0.0.1:65530/mcp",
                    Tunnel = new ManagedMcpTunnelRegistration
                    {
                        Alias = "incomplete-business",
                        ConfigPath = @"C:\Temp\talvora-incomplete\business.json",
                        Required = true,
                    },
                },
            ],
        };

        var needsAdmin = EvaluateCore(
            synthetic,
            hasAdminCredential: false,
            hasReusableRuntime: true);
        if (needsAdmin.IsComplete ||
            !needsAdmin.NeedsAdminCredential ||
            needsAdmin.RuntimeFoundationMissing)
        {
            throw new InvalidOperationException(
                "First-run Admin credential setup contract failed.");
        }

        var missingRuntime = EvaluateCore(
            synthetic,
            hasAdminCredential: true,
            hasReusableRuntime: false);
        if (missingRuntime.IsComplete ||
            !missingRuntime.RuntimeFoundationMissing)
        {
            throw new InvalidOperationException(
                "First-run runtime foundation setup contract failed.");
        }
    }

    private static ControlCenterSetupState EvaluateCore(
        ManagedMcpRegistryDocument registry,
        bool hasAdminCredential,
        bool hasReusableRuntime)
    {
        var required = registry.Mcps
            .Where(entry => entry.Tunnel?.Required == true)
            .ToArray();

        if (required.Length == 0)
        {
            return Complete();
        }

        var assessments = required
            .Select(registration => new
            {
                Registration = registration,
                Assessment = ManagedMcpTunnelProvisioningService.Assess(
                    registration),
            })
            .ToArray();

        var pending = assessments
            .Where(item =>
                !item.Assessment.HasTunnelId ||
                !item.Assessment.HasConfig ||
                !item.Assessment.HasRuntimeCredential)
            .ToArray();

        if (pending.Length == 0)
        {
            return Complete();
        }

        var needsAdmin =
            pending.Any(item => !item.Assessment.HasTunnelId) &&
            !hasAdminCredential;
        var runtimeFoundationMissing =
            !hasReusableRuntime &&
            pending.Any(item => !item.Assessment.HasRuntimeCredential);
        var missingTunnelRegistrationIds = pending
            .Where(item => !item.Assessment.HasTunnelId)
            .Select(item => item.Registration.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string summary;
        string detail;

        if (runtimeFoundationMissing)
        {
            summary = "Güvenli tünel temel kurulumu eksik";
            detail =
                "Yeniden kullanılabilir Runtime API key ve tunnel-client kurulumu bulunamadı. " +
                "Mevcut ChatGPT Business tünel temel kurulumunu tamamladıktan sonra Talvora otomatik devam edecek.";
        }
        else if (needsAdmin)
        {
            summary = "Yeni MCP tüneli için tek bilgi gerekiyor";
            detail =
                "OpenAI Admin API key yalnız yeni tüneli oluşturmak için kullanılacak. " +
                "Runtime key'den ayrı, Windows current-user DPAPI ile saklanacak ve ekranda gösterilmeyecek.";
        }
        else
        {
            summary = "Eksik güvenli tünel otomatik hazırlanacak";
            detail =
                "Gerekli credential ve scope hazır. Talvora tüneli oluşturup profili bağlayabilir.";
        }

        return new ControlCenterSetupState(
            IsComplete: false,
            NeedsAdminCredential: needsAdmin,
            RuntimeFoundationMissing: runtimeFoundationMissing,
            PendingTunnelCount: pending.Length,
            MissingTunnelRegistrationIds: missingTunnelRegistrationIds,
            Summary: summary,
            Detail: detail);
    }

    public static Task<ControlCenterSetupState> CompleteAsync(
        string? adminKey,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            adminKey,
            existingTunnelIds: null,
            cancellationToken);

    public static async Task<ControlCenterSetupState> CompleteAsync(
        string? adminKey,
        IReadOnlyDictionary<string, string?>? existingTunnelIds,
        CancellationToken cancellationToken)
    {
        var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
            cancellationToken).ConfigureAwait(false);
        var before = Evaluate(registry);

        if (before.IsComplete)
        {
            return before;
        }

        var suppliedTunnelIds = existingTunnelIds is null
            ? new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(
                existingTunnelIds,
                StringComparer.OrdinalIgnoreCase);

        foreach (var entry in suppliedTunnelIds)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            if (!ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                    entry.Value.Trim()))
            {
                throw new InvalidOperationException(
                    $"{entry.Key} için tunnel ID biçimi geçersiz.");
            }

            if (!registry.Mcps.Any(registration =>
                    string.Equals(
                        registration.Id,
                        entry.Key,
                        StringComparison.OrdinalIgnoreCase) &&
                    registration.Tunnel?.Required == true))
            {
                throw new InvalidOperationException(
                    $"{entry.Key} için yönetilen tunnel kaydı bulunamadı.");
            }
        }

        if (before.NeedsAdminCredential &&
            string.IsNullOrWhiteSpace(adminKey))
        {
            var allMissingTunnelIdsSupplied =
                before.MissingTunnelRegistrationIds.All(id =>
                    suppliedTunnelIds.TryGetValue(
                        id,
                        out var supplied) &&
                    !string.IsNullOrWhiteSpace(supplied) &&
                    ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                        supplied.Trim()));

            if (!allMissingTunnelIdsSupplied)
            {
                throw new InvalidOperationException(
                    "Eksik tunnel kayıtları için mevcut tunnel ID veya OpenAI Admin API key gerekiyor.");
            }
        }

        foreach (var entry in suppliedTunnelIds)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            var registration = registry.Mcps.Single(item =>
                string.Equals(
                    item.Id,
                    entry.Key,
                    StringComparison.OrdinalIgnoreCase));
            var assessment =
                ManagedMcpTunnelProvisioningService.Assess(
                    registration);
            if (assessment.HasTunnelId &&
                assessment.HasConfig &&
                assessment.HasRuntimeCredential)
            {
                continue;
            }

            var updated =
                await ManagedMcpTunnelProvisioningService
                    .BindExistingTunnelAsync(
                        registration,
                        registry,
                        entry.Value.Trim(),
                        cancellationToken)
                    .ConfigureAwait(false);

            registry = registry with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Mcps = registry.Mcps
                    .Select(item =>
                        string.Equals(
                            item.Id,
                            updated.Id,
                            StringComparison.OrdinalIgnoreCase)
                            ? updated
                            : item)
                    .ToList(),
            };
        }

        before = Evaluate(registry);
        if (before.IsComplete)
        {
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "setup",
                "Yönetim Merkezi kurulumu tamamlandı",
                "Mevcut güvenli MCP tünelleri bağlandı.",
                dedupKey: "setup:completed");

            return before;
        }

        if (before.NeedsAdminCredential)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                throw new InvalidOperationException(
                    "Yeni tünel oluşturmak için OpenAI Admin API key gerekiyor.");
            }

            ControlCenterAdminCredentialStore.Save(adminKey);
        }

        registry = await ManagedMcpTunnelProvisioningService
            .EnsureMissingTunnelsAsync(
                registry,
                cancellationToken)
            .ConfigureAwait(false);

        var after = Evaluate(registry);
        if (!after.IsComplete)
        {
            if (after.RuntimeFoundationMissing)
            {
                throw new InvalidOperationException(
                    "Güvenli MCP tünel temel kurulumu henüz tamamlanmadı.");
            }

            throw new InvalidOperationException(
                "Eksik tünel kurulumu tamamlanamadı. Olaylar bölümündeki son kaydı kontrol edin.");
        }

        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Info,
            "setup",
            "Yönetim Merkezi kurulumu tamamlandı",
            "Eksik güvenli MCP tüneli yapılandırması hazırlandı.",
            dedupKey: "setup:completed");

        return after;
    }

    private static ControlCenterSetupState Complete() =>
        new(
            IsComplete: true,
            NeedsAdminCredential: false,
            RuntimeFoundationMissing: false,
            PendingTunnelCount: 0,
            MissingTunnelRegistrationIds: Array.Empty<string>(),
            Summary: "Kurulum tamam",
            Detail: "Yönetilen MCP bağlantıları için gerekli temel yapılandırma hazır.");
}