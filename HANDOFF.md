# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Pre-#175 committed HEAD: `a96723c85ec214f88b7ccd3a005ad3aeccfadcdb`
- Gitea `origin/main` ve GitHub `github/main`: aynı HEAD.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `7ea7024d7da09074d1e84d6e10610d991d2edcc3`.
- `1c7a0a5` docs-only ve `a96723c` test-only olduğundan son live deploy gerektirmedi.
- Working tree #175 source + regression + closeout belgelerini içeriyor; sıradaki adım tek #175 commit/push.

## #175 — SOURCE FIXED / LIVE DEPLOY PENDING

Kök neden:
- `talvora_read_text` doğrudan `File.ReadAllTextAsync` ile bütün dosyayı limitsiz materialize ediyordu.
- `talvora_list(recursive=true)` bütün ağacı `EnumerateFileSystemInfos(...).Select(...).ToArray()` ile limitsiz materialize ediyor ve cancellation kabul etmiyordu.
- Bu iki legacy compatibility yüzeyi #159/#174 response ceiling'lerini bypass ediyordu.

Düzeltme:
- Legacy dönüş tipleri değişmedi.
- `talvora_read_text` canonical bounded `ReadTextRange` streaming yolunu kullanıyor. Whole-file response finite line/character bütçesine sığmazsa sessiz truncation yerine `talvora_read_text_range` continuation'a yönlendiren açık hata veriyor.
- `talvora_list` finite entry + response-character budget altında lazy enumerate ediyor ve cancellation kabul ediyor.
- Legacy list bütçeyi aşarsa eksik liste döndürmüyor; `talvora_find_files resultOffset/nextResultOffset` pagination'a açık hata ile yönlendiriyor.
- Internal bounded-list helper production ceiling'i değiştirmeden küçük regression fixture ile overflow yolunu doğruluyor.

Kanıt:
- `--response-bounds-only`: **TALVORA RESPONSE BOUNDS REGRESSION GREEN**.
- Küçük legacy read içeriği aynen korunuyor.
- Over-budget text read kontrollü biçimde `talvora_read_text_range`a yönleniyor.
- Küçük legacy list çalışıyor; düşük regression budget'ında overflow `talvora_find_files`a yönleniyor.
- Talvora Release build: **0 warning / 0 error**.
- `git diff --check`: exit 0.
- BUG-AUDIT #175 FIXED; TODO checked.

### Sıradaki zorunlu adımlar

1. Yalnız #175 source/test/docs dosyalarını stage et ve tek bug commit'i oluştur.
2. Commit'i Gitea `origin/main` ve GitHub `github/main` üzerine push et.
3. Canonical `Build-Windows-Installer.ps1` ile clean installer üret.
4. Manifest-bound `Deploy-Windows-Installer.ps1 -WaitForCompletion` ile canlıya al.
5. Reconnect sonrası service Running/Automatic ve exact-installed `sourceCommit` = #175 fix commit doğrula.
6. Canlı küçük `talvora_read_text` / `talvora_list` davranışını ve mümkünse over-budget read refusal yolunu doğrula.
7. Live acceptance kaydını BUG-AUDIT + HANDOFF'a işle ve docs-only commit'i iki remote'a push et.
8. Ardından deeper bug audit'e devam et.

## Sabit çalışma kuralları

- Source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision al.
- Her bug: kanıt -> kök neden -> minimal patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy.
- Git stage/commit için yalnız açık dosya listesi; `talvora_git_run explicitAdmin=true`.
- `git add .`, reset, clean, stash, revert kullanma.
- Gereksiz geniş testleri tekrar etme.
- Broad catch/fallback ile problemi gizleme.
- Çalışan capability'leri gereksiz yere kaldırma.
- Kesintide bu HANDOFF + `git status --short` + son commitlerden doğrudan devam et.

## Bitiş kriteri

Yeni audit turunda açık doğrulanmış bug kalmadığında, targeted testler GREEN, working tree temiz, iki remote senkron ve runtime-affecting son commit canlı olduğunda `C:\Users\tayla\Desktop\bitti.txt` oluştur. İçine tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve final test özetini yaz.
