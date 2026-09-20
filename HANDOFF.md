# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Pre-#174 HEAD: `67cd1f162fe3211cfcd61c4e5bcc8c2d6d434441`
- Bu HEAD Gitea `origin/main` ve GitHub `github/main` üzerinde senkron.
- Canlı Talvora service: Running / Automatic.
- Exact-installed source commit: `642c2158aebbe91e99e9349e92262c94b9b17e43`.
- Working tree: #174 source + regression + closeout belgeleri; henüz commitlenmedi.

## #174 — SOURCE FIXED / LIVE DEPLOY PENDING

Kök neden: #159 sonrasında bazı structured/list araçlarında caller limitinin `0` olması tek response'u gerçekten limitsiz bırakıyordu.

Düzeltme:
- XML query, test-report failures, project discovery, workspace inspect/commands, dev-server/job lists, Git log, artifact inventory ve diagnostics listelerine finite absolute server ceiling uygulandı.
- `0` artık unlimited değil, finite server maximum anlamına geliyor.
- Deterministic continuation metadata eklendi: result/failure/project/command/diagnostic offset'leri ve Git `skip/nextSkip`.
- Artifact continuation, okunamayan/hash alınamayan matching dosyalarda duplicate/rewind üretmemesi için dönen item sayısı yerine tüketilen `matchingIndex` üzerinden ilerliyor.
- Tool descriptions ve response schema'ları yeni kontratla senkron.

Doğrulama:
- Talvora Release build: **0 warning / 0 error**.
- `--response-bounds-only`: **TALVORA RESPONSE BOUNDS REGRESSION GREEN**.
- Regression XML, JUnit failures, project/workspace, commands, artifacts, diagnostics, 3-commit Git log, job/dev-server finite ceiling contracts ve önceki #159 bounds fixture'larını kapsıyor.
- `git diff --check`: exit 0; yalnız working-copy LF->CRLF uyarısı var.
- `BUG-AUDIT.md`: #174 FIXED.
- `MCP-CONTROL-CENTER-TODO.md`: #174 checked.

### Sıradaki zorunlu adımlar

1. Yalnız #174 ile ilgili source/test/docs dosyalarını stage et ve tek bug commit'i oluştur.
2. Commit'i Gitea `origin/main` ve GitHub `github/main` üzerine push et.
3. Repository'deki canonical installer/deploy akışını kullanarak canlıya al.
4. Reconnect sonrası service Running/Automatic ve exact-installed `sourceCommit` = #174 fix commit doğrula.
5. Live acceptance kaydını BUG-AUDIT + HANDOFF'a işle ve iki remote'a push et.
6. Ardından audit'te yeni doğrulanabilir bug taramasına devam et. Yeni bug yoksa final kriterlerini doğrula ve ancak o zaman `C:\Users\tayla\Desktop\bitti.txt` oluştur.

## Sabit çalışma kuralları

- Source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision al.
- Her bug: kanıt -> kök neden -> minimal patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> live deploy.
- `git add .`, reset, clean, stash, revert kullanma.
- Gereksiz geniş testleri tekrar etme.
- Broad catch/fallback ile problemi gizleme.
- Çalışan capability'leri gereksiz yere kaldırma.
- Kesintide bu HANDOFF + `git status --short` + son commitlerden doğrudan devam et.

## Bitiş kriteri

Açık doğrulanmış bug yok, targeted testler GREEN, working tree temiz, iki remote senkron ve gerekli runtime değişiklikleri canlı olduğunda masaüstünde `bitti.txt` oluştur. İçine tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve final test özetini yaz.
