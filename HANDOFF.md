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
- #176 canlı doğrulandı: service Running/Automatic; 3-entry ZIP pagination smoke GREEN.

## #176 — FIXED / LIVE VERIFIED

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

### Aktif devam noktası

1. #176 live acceptance tamamlandı; daha derin bug audit'e 0 OPEN baseline üzerinden devam et.
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
