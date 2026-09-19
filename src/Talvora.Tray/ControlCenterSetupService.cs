using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ControlCenterSetupState(
    bool IsComplete,
    bool NeedsAdminCredential,
    bool RuntimeFoundationMissing,
    int PendingTunnelCount,
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
            Summary: summary,
            Detail: detail);
    }

    public static async Task<ControlCenterSetupState> CompleteAsync(
        string? adminKey,
        CancellationToken cancellationToken)
    {
        var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
            cancellationToken).ConfigureAwait(false);
        var before = Evaluate(registry);

        if (before.IsComplete)
        {
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
            Summary: "Kurulum tamam",
            Detail: "Yönetilen MCP bağlantıları için gerekli temel yapılandırma hazır.");
}