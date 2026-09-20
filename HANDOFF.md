# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Runtime code/test HEAD: `7ea7024d7da09074d1e84d6e10610d991d2edcc3`
- Gitea `origin/main` ve GitHub `github/main`: `7ea7024d7da09074d1e84d6e10610d991d2edcc3` (live-docs commit henüz oluşturulmadı).
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed source commit: `7ea7024d7da09074d1e84d6e10610d991d2edcc3`.
- Canonical installer: **257,296,143 bytes**, SHA-256 `2A3B8B1695DAF302499552713F8CD4E83E11F50A98A844846B7A6B52F960DA69`.
- Working tree yalnız #174 live-acceptance dokümantasyonunu içeriyor.

## #174 — FIXED / PUSHED / LIVE

Kök neden: #159 sonrasında bazı structured/list araçlarında caller limitinin `0` olması tek response'u gerçekten limitsiz bırakıyordu.

Düzeltme:
- XML query, test-report failures, project discovery, workspace inspect/commands, dev-server/job lists, Git log, artifact inventory ve diagnostics listelerine finite absolute server ceiling uygulandı.
- `0` artık unlimited değil, finite server maximum.
- Deterministic continuation metadata eklendi: result/failure/project/command/diagnostic offset'leri ve Git `skip/nextSkip`.
- Artifact continuation, okunamayan/hash alınamayan matching dosyalarda duplicate/rewind üretmemesi için tüketilen `matchingIndex` üzerinden ilerliyor.
- Tool descriptions ve response schema'ları kontratla senkron.

Kanıt:
- Fix commit: `750bdb47e40ba9195ffcf5154001906757a021fe`.
- Zero-limit regression follow-up: `7ea7024d7da09074d1e84d6e10610d991d2edcc3`.
- İki commit de Gitea + GitHub'a push edildi.
- Talvora Release build: **0 warning / 0 error**.
- `--response-bounds-only`: **TALVORA RESPONSE BOUNDS REGRESSION GREEN**.
- Canonical clean installer build + manifest-bound independent SYSTEM deploy: GREEN.
- Reconnect sonrası exact-installed source commit = `7ea7024...`.
- Canlı Git log: `maxCount=2 -> truncated=true,nextSkip=2`.
- Canlı diagnostics: 3 kayıttan 2 kayıt -> `truncated=true,nextDiagnosticOffset=2`.

## ACTIVE — deeper bug audit

#121–#174 aralığında açık doğrulanmış bug yok. Sıradaki iş yeni doğrulanabilir bug aramak; özellikle #174 sonrası response bütünlüğünde kalan error-list/per-item bounds, async/race, lifecycle, SQLite/locking, reconnect/state ve resource cleanup yolları.

Bir sonraki gerçek bug bulunursa yeni numara ile BUG-AUDIT/TODO'ya yaz; tek bug patch/test/docs/commit/push/live döngüsünü uygula.

## Sabit çalışma kuralları

- Source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision al.
- Her bug: kanıt -> kök neden -> minimal patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy.
- `git add .`, reset, clean, stash, revert kullanma.
- Gereksiz geniş testleri tekrar etme.
- Broad catch/fallback ile problemi gizleme.
- Çalışan capability'leri gereksiz yere kaldırma.
- Kesintide bu HANDOFF + `git status --short` + son commitlerden doğrudan devam et.

## Bitiş kriteri

Yeni audit turunda açık doğrulanmış bug kalmadığında, targeted testler GREEN, working tree temiz, iki remote senkron ve runtime-affecting son commit canlı olduğunda masaüstünde `C:\Users\tayla\Desktop\bitti.txt` oluştur. İçine tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve final test özetini yaz.
