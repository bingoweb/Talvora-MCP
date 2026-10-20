$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$store = [IO.File]::ReadAllText((Join-Path $repo 'src\Talvora.Tray\ControlCenterAdminCredentialStore.cs'))
$setup = [IO.File]::ReadAllText((Join-Path $repo 'src\Talvora.Tray\ControlCenterSetupService.cs'))
$ui = [IO.File]::ReadAllText((Join-Path $repo 'src\Talvora.Tray\ControlCenterWindow.Setup.cs'))
$provider = [IO.File]::ReadAllText((Join-Path $repo 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'))
$startup = [IO.File]::ReadAllText((Join-Path $repo 'src\Talvora.Tray\TrayApplicationContext.cs'))

$checks = [ordered]@{
    NoPersistentAdminWrites = -not $store.Contains('DpapiSecretStore.WriteString(')
    NoPersistentAdminReads = -not $store.Contains('DpapiSecretStore.ReadString(')
    NoSaveAtSetup = -not $setup.Contains('ControlCenterAdminCredentialStore.Save(')
    NoAdminKeyFromDiskInProvider = -not $provider.Contains('ControlCenterAdminCredentialStore.Read(')
    TransientPasswordBuffer = $ui.Contains('CopyTransientKey(') -and $ui.Contains('ClearTransientKey(adminKeyBuffer)')
    TransientKeyPassedToProvisioner = $setup.Contains('EnsureMissingTunnelsAsync(') -and $setup.Contains('adminKeyBuffer')
    BufferErasedAfterProvision = $setup.Contains('ClearTransientKey(adminKeyBuffer)') -and $store.Contains('CryptographicOperations.ZeroMemory(')
    StartupDeletesLegacyKey = $startup.Contains('DeleteLegacyIfPresent(')
    LegacyFileOnly = $store.Contains('openai-admin-key.dpapi') -and -not $store.Contains('File.Delete(BusinessTunnelClient.RuntimeCredentialPath)')
    UpgradeEventCopy = $startup.Contains('Yönetici anahtarı artık saklanmıyor; yalnızca kurulum sırasında kullanılıyor.')
    UpgradePurgeBehaviorFixture = $store.Contains('AssertNoPersistenceContract(') -and $store.Contains('DeleteLegacyIfPresent(')
    RuntimeDPAPIPathNotModified = -not $setup.Contains('BusinessTunnelClient.RuntimeCredentialPath') -and -not $ui.Contains('BusinessTunnelClient.RuntimeCredentialPath')
}
$failures = @($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
if ($failures.Count -gt 0) { throw ('S5 admin-credential regression failed: ' + ($failures -join ', ')) }
$checks.GetEnumerator() | ForEach-Object { 'PASS ' + $_.Key }
'TALVORA ADMIN CREDENTIAL NONPERSISTENCE GREEN'
