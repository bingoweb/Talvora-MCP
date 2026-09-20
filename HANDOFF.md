# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Last runtime-affecting fix commit: `36b68b24e14a72b5cf44cd0ae2b91356e882733a` (`fix: isolate manual stop state per Windows session`).
- Gitea `origin/main` ve GitHub `github/main`: her bug closeout commit'inden sonra birlikte güncellenir.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `36b68b24e14a72b5cf44cd0ae2b91356e882733a`.
- #178 tamamlandı: manual-stop persistence Windows SessionId bazlı dosyaya ayrıldı; targeted regression GREEN; fix commit Gitea + GitHub'a push edildi ve canonical live deploy exact-installed olarak doğrulandı.
- #179 source fix hazır: Control Center exit artık window-placement yazımını shutdown öncesi tamamlıyor; yeni regression GREEN ve Tray Release build 0 warning / 0 error; commit/push/live deploy sırada.

## #178 — CLOSED / LIVE VERIFIED

Kök neden:
- Manual-stop state belgesi logon `SessionId`/`AuthenticationId` doğruluyordu ama tüm oturumlar aynı `%LOCALAPPDATA%\Talvora\ControlCenter\session-state.json` dosyasını kullanıyordu.
- Named mutex `Local\...` olduğundan farklı Windows session'ları birbirini kilitlemiyor; aynı kullanıcıyla ikinci session/RDP oturumu diğer session'ın state'ini mismatch görüp sıfırlayabiliyordu.

Düzeltme ve kanıt:
- State yolu `session-state.<SessionId>.json` oldu; aynı session içindeki process'ler mevcut Local mutex'i paylaşmaya devam ediyor, farklı session'ların dosyaları ayrışıyor.
- Eski `session-state.json` yalnız current SessionId/UserSid/AuthenticationId ile eşleşirse yeni session dosyasına migrate ediliyor; mismatch legacy state'e dokunmuyor.
- Runtime `AssertContract` farklı SessionId'lerin farklı dosya yoluna gittiğini doğruluyor.
- `NativeInstallerSourceRegression.ps1`: `ManualStopStateIsWindowsSessionScoped=True`; suite GREEN. Tray Release build **0 warning / 0 error**.
- Fix commit: `36b68b24e14a72b5cf44cd0ae2b91356e882733a`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Canonical installer build GREEN; artifact SHA-256 `3A57702ECD153F71B744B871420D7E1626693B9D62246875E5ADF05B039203B1`.
- Self-update sırasında MCP bağlantısı servis değişimi nedeniyle kesildi; reconnect sonrası `system_info.sourceCommit=36b68b24e14a72b5cf44cd0ae2b91356e882733a` ve service Running doğrulandı.

### Aktif devam noktası

1. #179 source/test/docs değişikliklerini tek bug commit'i olarak commit et; Gitea ve GitHub `main` üzerine push et.
2. Canonical installer build + manifest-bound live deploy yap; exact-installed runtime commit'i doğrula.
3. #179 live acceptance'ı docs-only closeout commit'iyle kapat; ardından deeper audit'e #180'den devam et.

## #179 — SOURCE FIXED / COMMIT + LIVE DEPLOY PENDING

Kök neden:
- `ControlCenterApplication.ExitFromTray()`, `PrepareForApplicationExit()` çağrısından hemen sonra window'u kapatıp WPF `Shutdown()` çağırıyor.
- `PrepareForApplicationExit()` içindeki `_ = SaveWindowPlacementAsync()` fire-and-forget olduğu için son `window-placement.json` yazımı process kapanışıyla yarışabiliyor ve son konum/boyut kaybolabiliyordu.

Düzeltme ve kanıt:
- Exit path artık `SaveWindowPlacementAsync().GetAwaiter().GetResult()` ile persist işlemini shutdown'dan önce tamamlıyor.
- Alt I/O await'i `ConfigureAwait(false)` kullanıyor; böylece WPF UI thread üzerinde sync-wait deadlock'u oluşturulmuyor.
- `NativeInstallerSourceRegression.ps1` önce RED (`ControlCenterExitPersistsWindowPlacementBeforeShutdown=False`), fix sonrası GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.

## Sabit çalışma kuralları

- Source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision al.
- Her bug: kanıt -> kök neden -> minimal patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy.
- Git stage/commit için yalnız açık dosya listesi; `talvora_git_run explicitAdmin=true`.
- `git add .`, reset, clean, stash, revert kullanma.
- Gereksiz geniş testleri tekrar etme.
- Broad catch/fallback ile problemi gizleme.
- Çalışan capability'leri gereksiz yere kaldırma.
- Kesintide bu HANDOFF + `git status --short` + son commitlerden doğrudan ACTIVE maddeden devam et.

## Bitiş kriteri

Yeni audit turunda açık doğrulanmış bug kalmadığında, targeted testler GREEN, working tree temiz, iki remote senkron ve runtime-affecting son commit canlı olduğunda `C:\Users\tayla\Desktop\bitti.txt` oluştur. İçine tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve final test özetini yaz.
