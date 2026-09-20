# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Pre-#176 committed HEAD: `f00df75b2659fdcca39b3c323ea2ad0b4b2be6dd`
- Gitea `origin/main` ve GitHub `github/main`: aynı HEAD.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `949272eb05d78f956d1b2f34cbb760bacd88d13b`.
- Working tree #176 source + regression + closeout belgelerini içeriyor; sıradaki adım tek #176 commit/push.

## #176 — SOURCE FIXED / LIVE DEPLOY PENDING

Kök neden:
- `talvora_archive_list` bütün ZIP entry'lerini `archive.Entries.Select(ToArchiveEntry).ToArray()` ile tek response'a materialize ediyordu.
- Archive extraction aynı subsystem'de 1,000,000 entry emergency ceiling kabul ediyor; legacy list response'u gerçek yüksek-cardinality risk taşıyordu.

Düzeltme:
- Absolute list ceiling: **20,000 entry**.
- Absolute response-character ceiling: **8 MiB**.
- `maxResults=0` finite server maximum.
- Archive order korunarak `resultOffset/nextResultOffset` deterministic continuation eklendi.
- Response schema: `Count`, `TotalEntries`, `ResultOffset`, `Truncated`, `NextResultOffset`.

Kanıt:
- 3-entry ZIP regression: ilk page 2 entry + `totalEntries=3` + `nextResultOffset=2`; ikinci page 1 entry + clean termination.
- `--response-bounds-only`: **TALVORA RESPONSE BOUNDS REGRESSION GREEN**.
- Talvora Release build: **0 warning / 0 error**.
- `git diff --check`: exit 0.
- BUG-AUDIT #176 FIXED; TODO checked.

### Sıradaki zorunlu adımlar

1. Yalnız #176 source/test/docs dosyalarını stage et ve tek bug commit'i oluştur.
2. Commit'i Gitea `origin/main` ve GitHub `github/main` üzerine push et.
3. Canonical clean installer build + manifest-bound SYSTEM deploy.
4. Reconnect sonrası service Running/Automatic ve exact-installed `sourceCommit` = #176 fix commit.
5. Canlı 3-entry ZIP üzerinde `maxResults=2` first/second page smoke.
6. Live acceptance BUG-AUDIT/HANDOFF/TODO docs-only commit + iki remote push.
7. Deeper bug audit'e devam.

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
