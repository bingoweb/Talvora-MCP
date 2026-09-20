# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Last runtime-affecting fix commit: `270e98923ac78c2fbc495afa7254bf6b7bc261b6` (`fix: paginate archive listings`).
- Gitea `origin/main` ve GitHub `github/main`: her bug closeout commit'inden sonra birlikte güncellenir.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `572025c91b1cd25173342a306e937d5116f6615c`.
- #177 source fix hazır: Talvora lifecycle işlemleri ortak operation coordinator ile serialize ediliyor; targeted regression + Tray Release build GREEN; commit/push/live deploy sırada.

## #177 — SOURCE FIXED / COMMIT + LIVE DEPLOY PENDING

Kök neden:
- Talvora `Start/Stop/Restart` yolu, Gitea ve generic MCP lifecycle yollarının aksine `ManagedMcpOperationCoordinator` lease almıyordu.
- Otomatik recovery kendi `_talvoraOperationGate` kilidini kullanırken Control Center kullanıcı işlemleri bu gate'i paylaşmadığı için servis/tünel state mutation'ları yarışabiliyordu.

Düzeltme ve kanıt:
- `ExecuteTalvoraAsync` girişine `TalvoraId` bazlı fail-fast operation lease eklendi; mevcut Gitea/generic koordinasyon modeliyle hizalandı.
- `NativeInstallerSourceRegression.ps1`: `TalvoraLifecycleUsesOperationCoordinator=True`; suite GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.

### Aktif devam noktası

1. #177 source/test/docs dosyalarını tek bug commit'i olarak commit et; Gitea ve GitHub `main` üzerine push et.
2. Canonical installer build + manifest-bound live deploy yap; service/runtime identity doğrula.
3. #177 live acceptance'ı belgelendirip docs-only closeout commit'ini iki remote'a push et; ardından deeper audit'e devam et.

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
