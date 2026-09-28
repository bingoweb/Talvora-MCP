namespace Talvora.Shared;

public sealed record TalvoraToolSelectionEvalCase(
    string Id,
    string Prompt,
    bool ExpectTalvora,
    TalvoraMcpToolSurface? RequiredSurface,
    IReadOnlyList<string> ExpectedTools);

/// <summary>
/// Deterministic tool-selection evaluation fixtures.
/// These cases do not emulate an LLM. They preserve the intended routing contract so metadata,
/// focused surfaces, and future routing changes can be regression-tested without flaky model calls.
/// </summary>
public static class TalvoraToolSelectionPolicy
{
    public static IReadOnlyList<TalvoraToolSelectionEvalCase> Cases { get; } =
    [
        new(
            "ordinary-source-edit",
            "Bu dosyadaki bug'i düzelt ve normal kod değişikliğini uygula.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_read_source", "talvora_apply_patch"]),
        new(
            "exact-generated-ranges",
            "Elimde revision'lı exact UTF-16 edit aralıkları hazır; uygula.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_apply_edits"]),
        new(
            "structural-rewrite",
            "Aynı AST biçimindeki dönüşümü çok sayıda kaynak dosyada uygula.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_structural_edit"]),
        new(
            "semantic-rename",
            "C# solution içinde sembol kimliğine göre rename yap.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_semantic_edit"]),
        new(
            "dotnet-build",
            "Projeyi Release olarak derle.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_dotnet_build"]),
        new(
            "dotnet-test",
            "İlgili .NET testlerini çalıştır.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_dotnet_test"]),
        new(
            "git-status",
            "Repository durumuna bak; değişiklik yapma.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_git_status"]),
        new(
            "penpot-design-development",
            "Penpot'ta mevcut tasarımı incele, tasarım sistemini anla ve gerekli arayüz değişikliğini uygula.",
            true,
            TalvoraMcpToolSurface.Development,
            [
                "talvora_penpot_status",
                "talvora_penpot_overview",
                "talvora_penpot_call_tool",
            ]),
        new(
            "service-restart",
            "Yerel Windows servisini yeniden başlat.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_service_restart"]),
        new(
            "registry-set",
            "Yerel Windows registry değerini güncelle.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_registry_set"]),
        new(
            "package-install",
            "Chocolatey ile makineye bir paket kur.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_choco_install"]),
        new(
            "package-upgrade",
            "Chocolatey ile makinedeki paketi güncelle.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_choco_upgrade"]),
        new(
            "package-uninstall",
            "Chocolatey ile makinedeki paketi kaldır.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_choco_uninstall"]),
        new(
            "environment-mutation",
            "Windows kullanıcı environment variable değerini kalıcı olarak güncelle.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_env_set"]),
        new(
            "arbitrary-process-kill",
            "Belirli PID'deki yerel Windows prosesini sonlandır.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_process_kill"]),
        new(
            "long-running-development-job",
            "Geliştirme sunucusunu uzun süreli job olarak başlat, çıktısını oku ve gerektiğinde durdur.",
            true,
            TalvoraMcpToolSurface.Development,
            [
                "talvora_job_start",
                "talvora_job_read_output",
                "talvora_job_stop",
            ]),
        new(
            "shared-powershell-fallback",
            "Typed Talvora aracı yeterli değil; geliştirme klasöründeki PowerShell otomasyonunu çalıştır.",
            true,
            TalvoraMcpToolSurface.Development,
            ["talvora_run_powershell"]),
        new(
            "shared-process-fallback",
            "Typed sistem aracı yeterli değil; gerekli yerel executable'ı doğrudan çalıştır.",
            true,
            TalvoraMcpToolSurface.Administration,
            ["talvora_run_process"]),
        new(
            "negative-weather",
            "Bugün hava nasıl?",
            false,
            null,
            []),
        new(
            "negative-general-knowledge",
            "Fotosentez nedir?",
            false,
            null,
            []),
        new(
            "negative-casual-chat",
            "Nasılsın?",
            false,
            null,
            []),
    ];
}
