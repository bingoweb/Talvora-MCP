# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Last runtime-affecting fix commit: `234f26ec74d4e5b01710d45b46f4cc373068de72` (`fix: serialize Talvora lifecycle operations`).
- Gitea `origin/main` ve GitHub `github/main`: her bug closeout commit'inden sonra birlikte güncellenir.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `234f26ec74d4e5b01710d45b46f4cc373068de72`.
- #177 canlı doğrulandı: service Running/Automatic; active Tray `Versions\234f26e...\Tray` altından çalışıyor.

## #177 — FIXED / LIVE VERIFIED

Kök neden:
- Talvora `Start/Stop/Restart` yolu, Gitea ve generic MCP lifecycle yollarının aksine `ManagedMcpOperationCoordinator` lease almıyordu.
- Otomatik recovery kendi `_talvoraOperationGate` kilidini kullanırken Control Center kullanıcı işlemleri bu gate'i paylaşmadığı için servis/tünel state mutation'ları yarışabiliyordu.

Düzeltme ve kanıt:
- `ExecuteTalvoraAsync` girişine `TalvoraId` bazlı fail-fast operation lease eklendi; mevcut Gitea/generic koordinasyon modeliyle hizalandı.
- `NativeInstallerSourceRegression.ps1`: `TalvoraLifecycleUsesOperationCoordinator=True`; suite GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.

### Aktif devam noktası

1. #177 commit/push/live acceptance tamamlandı; 0 OPEN baseline üzerinden deeper audit'e devam et.
2. Yeni doğrulanan her bug için minimal fix + targeted test + HANDOFF/TODO + ayrı commit + Gitea/GitHub push uygula.
3. Runtime etkileyen yeni commit olursa canonical installer/deploy ve canlı kimlik doğrulamasını tekrarla.

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
