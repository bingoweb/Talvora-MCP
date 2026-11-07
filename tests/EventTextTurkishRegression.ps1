$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$paths = @(
    'src/Talvora.Tray/ManagedMcpTunnelProvisioningService.ClientUpdate.cs'
    'src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs'
    'src/Talvora.Tray/TrayApplicationContext.cs'
    'src/Talvora.Tray/ControlCenterSetupService.cs'
)
$sourceText = ($paths | ForEach-Object { [IO.File]::ReadAllText((Join-Path $repoRoot $_)) }) -join "`n"

$old = @(
    '{registration.DisplayName} tunnel-client güncellendi'
    'OpenAI tunnel-client {candidateConfig.TunnelClientVersion} sürümüne readiness doğrulamasından sonra atomik olarak geçirildi.'
    '{registration.DisplayName} tunnel-client update recovered'
    'Interrupted update recovered with {journal.CandidateConfig.TunnelClientVersion}; runtime readiness was verified before commit.'
    '{registration.DisplayName} tunnel-client update geri alındı'
    'Interrupted candidate {journal.CandidateConfig.TunnelClientVersion} readiness doğrulamasını geçemedi; previous client {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.'
    '{registration.DisplayName} tunnel-client update toparlandı'
    'Incomplete update journal was rolled back to {journal.PreviousConfig.TunnelClientVersion}.'
    'Runtime profili oluşturuldu ve health/ready doğrulaması geçti.'
    'Talvora MCP otomatik olarak düzeltildi'
    'Yerel servis ile Dev ve Admin güvenli MCP tünelleri yeniden hazır.'
    'Talvora Dev ve Admin güvenli MCP tünelleri yeniden bağlandı.'
    'Gitea MCP otomatik kurtarma denemesi başarısız'
    'Gitea MCP sağlık bakımı başarısız'
    'Gitea MCP yeniden başlatıldı'
    'Gitea MCP zinciri sağlıklı ve kullanıma hazır.'
    'Gitea MCP yeniden başlatılamadı'
    '{registration.DisplayName} tunnel-client güncellemesi ertelendi'
    'Mevcut çalışan sürüm korunuyor; latest kontrolü daha sonra yeniden denenecek.'
    '{registry.Mcps.Count} yönetilen MCP kaydı yüklendi.'
    'Yönetilen MCP kayıtları veya otomatik kurtarma hazırlanamadı.'
    'Mevcut güvenli MCP tünelleri bağlandı.'
    'Eksik güvenli MCP tüneli yapılandırması hazırlandı.'
)

$new = @(
    '{registration.DisplayName} bağlantı istemcisi güncellendi'
    'Güvenli bağlantı istemcisi {candidateConfig.TunnelClientVersion} sürümüne geçti; hazır olduğu doğrulandıktan sonra devreye alındı.'
    '{registration.DisplayName} yarım kalan güncelleme tamamlandı'
    'Yarıda kesilen güncelleme {journal.CandidateConfig.TunnelClientVersion} sürümüyle tamamlandı; sürüm hazır olduğu doğrulandıktan sonra devreye alındı.'
    '{registration.DisplayName} güncellemesi geri alındı'
    'Yeni sürüm {journal.CandidateConfig.TunnelClientVersion} hazırlık denetimini geçemedi; önceki sürüm {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.'
    '{registration.DisplayName} yarım güncelleme toparlandı'
    'Yarım kalan güncelleme geri alındı; {journal.PreviousConfig.TunnelClientVersion} sürümü kullanılıyor.'
    'Bağlantı profili oluşturuldu ve sağlık denetimi geçti.'
    'Talvora otomatik olarak düzeltildi'
    'Yerel servis ile Dev ve Admin güvenli bağlantıları yeniden hazır.'
    'Talvora Dev ve Admin güvenli bağlantıları yeniden kuruldu.'
    'Gitea otomatik kurtarma denemesi başarısız'
    'Gitea sağlık bakımı yarıda kaldı'
    'Gitea yeniden başlatıldı'
    'Gitea ve bağlantı zinciri sağlıklı, kullanıma hazır.'
    'Gitea yeniden başlatılamadı'
    '{registration.DisplayName} bağlantı istemcisi güncellemesi ertelendi'
    'Çalışan sürüm korunuyor; yeni sürüm denetimi daha sonra yeniden yapılacak.'
    '{registry.Mcps.Count} yönetilen hizmet kaydı yüklendi.'
    'Yönetilen hizmet kayıtları ya da otomatik kurtarma hazırlanamadı.'
    'Mevcut güvenli bağlantılar kuruldu.'
    'Eksik güvenli bağlantı ayarları hazırlandı.'
)

foreach ($item in $old) {
    if ($sourceText.Contains($item)) {
        throw "D37: old event text still present: $item"
    }
}
foreach ($item in $new) {
    if (-not $sourceText.Contains($item)) {
        throw "D37: new event text missing: $item"
    }
}
Write-Output 'EVENT_TEXT_TURKISH_GREEN'
