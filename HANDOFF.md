# Talvora MCP — Canonical Handoff

## CURRENT — 2026-09-20

Bu dosya kesinti ve yeni oturum devamı için tek kısa kanonik handoff'tur. Eski kronoloji burada tutulmaz. Ayrıntılı bulgular `BUG-AUDIT.md`, görev geçmişi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

## Repo / remote / canlı durum

- Repository: `%USERPROFILE%\\Talvora-MCP`
- Branch: `main`
- Çalışma ağacı: **dirty yalnız #190 docs closeout / public-metadata hygiene nedeniyle**; security source/test commit'i ayrı kapatıldı. Reset/clean/stash/revert yapma.
- Son runtime-affecting commit: `689a7ba` — `security: harden secret persistence and logging`; Gitea + GitHub `main` üzerine push edildi.
- Mevcut exact-installed runtime: `d8f04696a1b1ef9dd8b60dc19796b0ca88ce163b` — security commit henüz canonical deploy edilmedi.
- Gitea remote: `origin` -> local loopback Gitea `Talvora-MCP.git`
- GitHub remote: `github` -> `https://github.com/bingoweb/Talvora-MCP.git`
- Handoff resetinden hemen önce docs HEAD `10ab9ad1994e2af8fe5402bf230822687dc51b13` idi; `origin/main` ve `github/main` aynı commit'teydi. Bu docs closeout, runtime-affecting `60c5e9a` fix'inden sonradır.
- Exact-installed canlı Talvora runtime `talvora_system_info.sourceCommit=d8f04696a1b1ef9dd8b60dc19796b0ca88ce163b` bildiriyor.
- Bu HANDOFF reset commit'i yalnız dokümantasyondur; canlı runtime commit'ini sırf docs HEAD değişti diye yeniden deploy etme ve self-referential fingerprint döngüsü oluşturma.

## Mevcut ürün/mimari baseline

Aşağıdaki ana çalışma alanları tamamlanmış ve korunmalıdır:

- Canonical Windows installer + bağımsız SYSTEM deploy akışı; servis/tray aynı version root ve manifest/provenance doğrulaması.
- Control Center / Tray lifecycle, restart/recovery ve managed MCP operation koordinasyonu.
- Gitea backend + Caddy + MCP + Secure MCP Tunnel health/readiness zinciri.
- Playwright MCP kaldırıldı; resmi Playwright CLI yönü kullanılıyor.
- Source Edit Transaction Engine production baseline:
  - `talvora_read_source` revision handshake
  - `talvora_apply_patch` normal source/config/repository-doc değişikliklerinde PRIMARY/default
  - `talvora_apply_edits` yalnız exact generated range specialist
  - `talvora_structural_edit` ast-grep structural specialist
  - `talvora_semantic_edit` Roslyn C# symbol-aware specialist
  - SHA-256 optimistic concurrency, durable WAL/receipt, rollback/recovery, idempotency/tombstone, source mutation policy
- Response/resource bounds, pagination/continuation, process output bounding, watcher/HTTP mock backpressure ve archive/read/list sınırları.
- Güncel audit üst özeti: **#121–#189 remediation complete + live verified; #190 privacy/security hardening commit/push + source gates GREEN, canonical live acceptance pending.**

## Son tamamlanan bug-fix zinciri

### #178 — Windows session scoped manual-stop state
- Tek ortak `session-state.json` farklı logon/RDP session'larında çakışabiliyordu.
- State `session-state.<SessionId>.json` olarak session bazında ayrıldı; güvenli legacy migration eklendi.
- Targeted regression GREEN; fix iki remote'a push edildi ve live verified.

### #179 — Control Center exit / window-placement kaybı
- Exit sırasında placement save fire-and-forget kalıp WPF shutdown ile yarışıyordu.
- Exit path son placement yazımını shutdown öncesi deterministik tamamlıyor; UI-context deadlock riski engellendi.
- Targeted regression RED -> GREEN; live verified.

### #180 — Gitea browser launch hata yolu
- Beklenen shell launch exception'ları async WPF event zincirinden kaçabiliyordu.
- Yalnız beklenen launch exception'ları kontrollü log/event/UI hata yoluna alındı.
- Targeted regression RED -> GREEN; live verified.

### #181 — Window-placement stale save yarışı
- Eşzamanlı async save'lerde eski snapshot daha sonra publish olup yeni konumu ezebiliyordu.
- Publication semaphore ile serialize edildi; monoton save-version stale queued snapshot'ı skip ediyor.
- Targeted regression GREEN; live verified.

### #182 — Browser launch Process handle sahipliği
- Control Center ve Tray `Process.Start` dönüşlerini dispose etmiyordu.
- Her iki yol disposable Process nesnesini scope sonunda serbest bırakıyor.
- Targeted regression GREEN; live verified.

### #183 — Source-edit worker kill/cleanup yarışı
- ast-grep ve semantic worker timeout/error yolları `Kill` sonrasında process exit'i beklemeden staging/response cleanup'a geçiyordu.
- Bounded `WaitForExitAsync` eklendi; cleanup process termination sonrasına taşındı.
- Targeted regression GREEN; live verified.

### #184 — HTTP mock CTS dispose yarışı
- In-flight handler'lar dispose edilmiş CTS üzerinden tekrar `Token` okuyabiliyordu.
- Runtime lifetime token constructor'da cache'leniyor; async yollar immutable token kopyasını kullanıyor.
- Runtime-bounds regression GREEN; live verified.

### #185 — HTTP mock reply retry claim
- Cancelled/failed response send `pending.Replied=1` claim'ini kalıcı bırakıp retry'ı engelliyordu.
- Başarısız/cancelled attempt claim'i atomik geri bırakıyor; başarılı send claim'i koruyor.
- Gerçek loopback regression RED -> GREEN; fix `43d2228`; live verified.

### #186 — HTTP mock stop pending request 503 semantiği
- Listener lifetime cancellation ile gerçek pending timeout aynı cancellation catch'ine giriyor; Stop yarışını handler kazanırsa timeout default response dönebiliyordu.
- Lifetime cancellation ayrı 503 yoluna ayrıldı; gerçek timeout mevcut default-response davranışını koruyor.
- 64 concurrent loopback regression RED -> GREEN.
- Fix: `60c5e9a`
- Canonical installer SHA-256: `F43AB687532B9E5211CD456CB58B484712AE526035F1FB63D8B00A074AB88B35`
- Gitea + GitHub push GREEN; exact-installed live runtime aynı commit.

### #187 — HTTP mock request/response encoding ayrımı
- `requestEncoding` incoming request body decode için doğruydu ancak text `defaultBody` response byte'larını da aynı encoding ile üretiyordu; varsayılan response content type UTF-8 kaldığı için örn. UTF-16 request encoding seçimi auto-reply gövdesini bozuyordu.
- Text default response artık `Reply()` ile tutarlı biçimde UTF-8 üretiliyor; request decode davranışı korunuyor, binary/custom byte yolu `defaultBodyBase64` üzerinden değişmeden kalıyor.
- Gerçek loopback regression önce RED, minimal fix sonrası `PASS http-mock-request-encoding-does-not-change-response`; tüm `--runtime-bounds-only` gate GREEN.
- Fix commit: `26c26e53b3ca58c3521148d67d3f6233d6cd63fd`; Gitea + GitHub `main` aynı commit'te.
- Canonical installer SHA-256: `A43F89339155318FE1E40842E044EA7A7301F2B6890895CFFD75C942E0D2A7A2`.
- Manifest-bound SYSTEM deploy sonrası exact-installed runtime aynı commit'i bildiriyor; #187 live verified.

### #188 — recovered background job retention zamanı
- Servis yeniden başladıktan sonra diskte hâlâ `Running` görünen fakat gerçekte bitmiş job `ExitedUnknown` olarak yenilenirken cleanup, retention zamanını refresh'ten önce eski `StartedAtUtc` üzerinden cache'liyordu.
- Uzun süre çalışmış bir job böylece yeni bitmiş olmasına rağmen anında expired sayılıp log/metadata klasörü silinebiliyordu.
- Retention/sıralama zamanı artık `RefreshStateAsync` sonrasında güncellenmiş `ExitedAtUtc` üzerinden hesaplanıyor.
- Targeted job-storage regression önce RED, minimal fix sonrası `TALVORA JOB STORAGE REGRESSION GREEN`; Release build 0 warning / 0 error.
- Fix commit: `08302059d0236349064ab2cfa81e23ce74ff1b97`; Gitea + GitHub `main` aynı commit'te.
- Canonical clean-worktree installer SHA-256: `ABB9D1D074D2AB22CB36FB279DC647916CE9183D2E72CE437A1837C015AA1AEF`.
- Manifest-bound SYSTEM deploy sonrası exact-installed runtime aynı commit'i bildiriyor; #188 live verified.

### #189 — SQLite zero-timeout lock wait — live verified
- Fix `d8f0469` olarak commit edildi, Gitea + GitHub `main` üzerine push edildi ve exact-installed runtime aynı commit'i bildiriyor.
- Microsoft.Data.Sqlite 10.0.12'de timeout 0 no-timeout anlamına geliyor. İzole locked-DB fixture timeout 1'de yaklaşık 1.1 s sonra SQLITE_BUSY döndürdü; timeout 0 + 1.5 s cancellation token 6 s dış process bütçesine kadar dönmedi.
- `ValidateTimeout` için `timeoutSeconds <= 0`; dört SQLite yüzeyini bağlantı açmadan koruyor.
- `PASS sqlite-zero-timeout-rejected`; targeted `--runtime-bounds-only` GREEN.
- #189 kapanmıştır.

### #190 — Privacy & Security Hardening — committed/pushed / live pending
- Talvora'nın tool/capability yüzeyi kısıtlanmadı.
- Persistent `FileLog` ve Control Center raw-log görünümü ortak secret redaction kullanıyor.
- Interactive-user process handoff `request.json` dosyasını deserialize sonrası hemen siliyor; 24 saatlik stale-run cleanup active lease'i koruyor.
- Tunnel-client hata/UI diagnostikleri secret-safe redaction kullanıyor.
- `.gitignore` local secret/credential dosyalarını dışlıyor; `SECURITY.md` trust-boundary ve privacy politikasını belgeliyor.
- Public dokümanlardaki gereksiz kullanıcı/path metadata'sı genelleniyor.
- Dedicated `--privacy-security-only` smoke ve privacy/security source regression GREEN; Tray Release build 0 warning / 0 error.
- 266 dosyalık high-confidence tracked-tree secret scan 0 bulgu.
- Security commit `689a7ba` iki remote'a push edildi.
- Sıradaki iş: docs closeout commit -> clean canonical installer/deploy -> exact-installed live acceptance.

## Doküman tutarlılığı notu

- `BUG-AUDIT.md` dosyasının en üstteki CURRENT/current remediation summary bölümü otoritatiftir: #121–#189 tamamlandı ve canlı doğrulandı; #190 source/test commit'i push edildi, yalnız canonical live acceptance ve docs closeout kaldı.
- Aynı dosyanın daha eski gövde satırlarında ve `MCP-CONTROL-CENTER-TODO.md` içinde tarihsel `pending`, eski `OPEN` veya pre-live ifadeler kalmış olabilir. Bunları yeni oturumda gerçek repo/remote/live durumunun önüne koyma.
- Eski tamamlanmış bug'ları tekrar test edip yeniden açma; yalnız yeni kanıt veya gerçek regresyon varsa dön.

## Sabit çalışma kuralları

1. Yeni oturumda önce bu HANDOFF'u oku.
2. Ardından yalnız `git status --short`, son birkaç commit ve `BUG-AUDIT.md` üst CURRENT özetini doğrula.
3. Normal source/config/doc editlerinde önce `talvora_read_source`, sonra PRIMARY `talvora_apply_patch`.
4. Bir seferde tek doğrulanmış bug: kanıt -> kök neden -> minimal patch -> en küçük anlamlı targeted test.
5. Aynı geniş test paketlerini gereksiz yere tekrar etme.
6. Her bug sonrasında ilgili yaşayan belgeleri güncelle.
7. Her düzeltilen bug için yalnız ilgili dosyaları stage et; ayrı commit oluştur.
8. Her bug commit'ini hem Gitea `origin/main` hem GitHub `github/main` üzerine push et.
9. Runtime-affecting değişiklikte canonical installer/deploy yolunu kullan; servis değişiminde beklenen MCP disconnect sonrası reconnect et ve `talvora_system_info.sourceCommit` ile exact-installed commit'i doğrula.
10. Docs-only commit için sırf HEAD değişti diye runtime redeploy etme.
11. `git add .`, reset, clean, stash, revert kullanma.
12. Broad catch/fallback ile gerçek hatayı gizleme; çalışan capability'yi gereksiz kısıtlama veya kaldırma.
13. Framework/API davranışı sürüme bağlıysa kurulu sürümü kontrol et; Context7/resmi dokümantasyonla doğrula.

## NEXT SESSION — kesin devam noktası

**#190 privacy/security hardening'i canonical live acceptance ile kapat; ardından deep audit'e devam et.**

Başlangıç sırası:

1. Bu HANDOFF'u oku.
2. `git status --short` ile tree'nin temiz olduğunu doğrula.
3. `HEAD`, `origin/main`, `github/main` durumunu kontrol et.
4. `talvora_system_info.sourceCommit` ile canlı runtime baseline'ını doğrula.
5. `BUG-AUDIT.md` üst CURRENT özetini oku; #121–#188'i tekrar tarama.
6. #190 security commit `689a7ba` iki remote'dadır. Targeted privacy smoke/source regression + Tray build sonucunu gereksiz tekrar etme. Docs closeout sonrası clean HEAD'den canonical installer/deploy yap ve live kabulü tamamla.
7. Şüpheyi bug diye yazmadan önce gerçek çağrı zinciri veya minimal reproduction ile doğrula.
8. #190 live verified olduktan sonra deep audit'teki ilk yeni doğrulanmış bulguyu kullanıcıya bildir; ardından minimal fix + targeted regression uygula.
9. Fix sonrası docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa canonical live deploy sırasını tamamla.
10. Sonraki bug'a ancak önceki bug tamamen kapandıktan sonra geç.

## Bitiş kriteri

Yeni audit turunda doğrulanmış açık bug kalmadığında:
- targeted/regression gate'ler GREEN,
- working tree clean,
- Gitea ve GitHub senkron,
- son runtime-affecting commit exact-installed live,
- ardından `%USERPROFILE%\\Desktop\\bitti.txt` oluştur ve final HEAD/remote/live/test özetini yaz.
