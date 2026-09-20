# Talvora MCP — Canonical Handoff

## CURRENT — 2026-09-20

Bu dosya kesinti ve yeni oturum devamı için tek kısa kanonik handoff'tur. Eski kronoloji burada tutulmaz. Ayrıntılı bulgular `BUG-AUDIT.md`, görev geçmişi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

## Repo / remote / canlı durum

- Repository: `C:\\Users\\tayla\\Talvora-MCP`
- Branch: `main`
- Çalışma ağacı handoff resetinden hemen önce: **clean**
- Son runtime-affecting commit: `60c5e9ac3c8470a883900a595b272e43b4812790` — `fix: return 503 when HTTP mock stops pending requests`
- Gitea remote: `origin` -> `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git`
- GitHub remote: `github` -> `https://github.com/bingoweb/Talvora-MCP.git`
- Handoff resetinden hemen önce docs HEAD `10ab9ad1994e2af8fe5402bf230822687dc51b13` idi; `origin/main` ve `github/main` aynı commit'teydi. Bu docs closeout, runtime-affecting `60c5e9a` fix'inden sonradır.
- Exact-installed canlı Talvora runtime `talvora_system_info.sourceCommit=60c5e9ac3c8470a883900a595b272e43b4812790` bildiriyor.
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
- Güncel audit üst özeti: **#121–#186 remediation complete + live verified; deeper audit #187'den devam edecek.**

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

## Doküman tutarlılığı notu

- `BUG-AUDIT.md` dosyasının en üstteki CURRENT/current remediation summary bölümü otoritatiftir: #121–#186 tamamlandı ve canlı doğrulandı; sıradaki audit numarası #187.
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

**#187 ile yeni deep bug audit turuna başla.**

Başlangıç sırası:

1. Bu HANDOFF'u oku.
2. `git status --short` ile tree'nin temiz olduğunu doğrula.
3. `HEAD`, `origin/main`, `github/main` durumunu kontrol et.
4. `talvora_system_info.sourceCommit` ile canlı runtime baseline'ını doğrula.
5. `BUG-AUDIT.md` üst CURRENT özetini oku; #121–#186'yı tekrar tarama.
6. #187 için henüz derin incelenmemiş çağrı zincirlerinden devam et. Öncelik: async/await ve cancellation edge'leri, process/service lifecycle, HTTP mock/watch/job cleanup, SQLite transaction/locking, reconnect/retry/backoff, dashboard/backend stale state, Windows path/session/encoding, resource ownership ve restart persistence.
7. Şüpheyi bug diye yazmadan önce gerçek çağrı zinciri veya minimal reproduction ile doğrula.
8. İlk doğrulanmış #187 bulgusunu hemen kullanıcıya bildir; ardından minimal fix + targeted regression uygula.
9. Fix sonrası docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa canonical live deploy sırasını tamamla.
10. Sonraki bug'a ancak önceki bug tamamen kapandıktan sonra geç.

## Bitiş kriteri

Yeni audit turunda doğrulanmış açık bug kalmadığında:
- targeted/regression gate'ler GREEN,
- working tree clean,
- Gitea ve GitHub senkron,
- son runtime-affecting commit exact-installed live,
- ardından `C:\\Users\\tayla\\Desktop\\bitti.txt` oluştur ve final HEAD/remote/live/test özetini yaz.
