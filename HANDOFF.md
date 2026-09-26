# Talvora MCP — Canonical Handoff

## CURRENT — 2026-09-26

Bu dosya kesinti ve yeni oturum devamı için tek kısa kanonik handoff'tur. Eski kronoloji burada tutulmaz. Ayrıntılı bulgular `BUG-AUDIT.md`, görev geçmişi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

## Repo / remote / canlı durum

- Repository: `%USERPROFILE%\\Talvora-MCP`
- Branch: `main`
- Repo/remote `main`: runtime commit `e2f2442322bf9310428f58194133e6d7e5f0a9a2` iki remote'a push edildi; bu handoff değişikliği docs-only closeout'tur.
- Çalışma ağacı: final docs-only closeout commit'i sonrası **clean olmalıdır**; reset/clean/stash/revert yapma.
- Son runtime-affecting commit: `e2f2442322bf9310428f58194133e6d7e5f0a9a2` — retired integration cleanup.
- Exact-installed canonical runtime artifact source commit: `e2f2442322bf9310428f58194133e6d7e5f0a9a2`.
- Canonical installer SHA-256: `2BEAA18B451A1FFADEECF021E85626E5FAEF91F764348D76397B9428F79C72C7`; artifact size: 261,874,959 bytes.
- Gitea remote: `origin` -> local loopback Gitea `Talvora-MCP.git`
- GitHub remote: `github` -> `https://github.com/bingoweb/Talvora-MCP.git`
- Exact-installed canlı Talvora runtime `sourceCommit=e2f2442322bf9310428f58194133e6d7e5f0a9a2` bildiriyor.
- Structured Git `info/status/log/diff/branches` LocalSystem altında kullanıcıya ait ana repoda GREEN; `main` -> `origin/main`, ahead=0 / behind=0.
- Gitea ve GitHub `fetch --dry-run` + `push --dry-run` Talvora'nın canlı `git_run` aracıyla GREEN; SSH private key user-only kalıyor.
- Talvora service `Running/Automatic`; exact-installed Service/Tray runtime baseline `e2f2442...`.
- Bu HANDOFF closeout değişikliği yalnız dokümantasyondur; sırf docs HEAD değişti diye yeniden deploy etme ve self-referential fingerprint döngüsü oluşturma.

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
- Focused MCP yüzeyleri güncel durumda **Full 212 / Dev 182 / Admin 91**. Admin'in 61 aracı Dev ile ortak, 30'u Admin-only. Admin 91/91 isim benzersiz; exact duplicate description yok; Admin-only 30/30 test kaynaklarında temsil ediliyor.
- Güncel audit zinciri **#121–#218** kapalı kalır. Modal + Penpot genişletmeleri sonrasında metadata source/live ve focused-surface source/live GREEN; shared-infrastructure hedefli regresyonu Docker image çekimi sırasındaki tek timing kırmızısı sonrası sakin ortamda GREEN tekrar doğrulandı. Eski 204-tool full-smoke sonucu tarihsel baseline'dır; güncel 212 araç için full smoke yeniden çalıştırılmadı.

## Penpot / Talvora entegrasyonu — CURRENT

- Resmi Penpot **2.18.0** self-host kurulumu Docker Compose ile `C:\ProgramData\Talvora\Penpot` altında çalışıyor. UI için kanonik adres `http://localhost:9001/`; host bind `127.0.0.1:9001` ile loopback'tır. Docker içindeki resmi MCP container'ı `--multi-user` modunda Penpot'un kendi ağı için kalır ve artık host `4401/4402` portlarını publish etmez.
- Resmi compose içindeki Penpot secret yerelde rastgele üretildi; secret değeri repo, HANDOFF veya kalıcı log içine alınmadı. `.env` `PENPOT_VERSION=latest` kullanıyor. Docker Desktop kullanıcı ayarında `AutoStart=True`; değişiklik öncesi `settings-store.json.talvora-penpot.bak` yedeği bırakıldı ve Penpot container restart policy `always`.
- Talvora'nın production design endpoint'i Docker multi-user MCP değildir. Exact Penpot **2.18.0** tag'ı `C:\ProgramData\Talvora\Penpot\penpot-2.18.0` altına sparse checkout edildi; upstream commit `5baffdc213f0deaaeb318e97a41d611ac0656a94`. `mcp` workspace'i `pnpm install --frozen-lockfile` + `pnpm run build` ile yerelde build edildi.
- Local/single-user Penpot MCP kullanıcı oturumunda `Talvora Penpot MCP` scheduled task'iyle gizli ve kalıcı çalışır: plugin manifest `http://127.0.0.1:4400/manifest.json`, MCP `http://127.0.0.1:4401/mcp`, WebSocket bridge `http://127.0.0.1:4402`. Supervisor `C:\ProgramData\Talvora\Penpot\Start-Talvora-Penpot-Mcp.ps1`; tüm host listener'lar yalnız `127.0.0.1`.
- NPM `@penpot/mcp` stable/latest canlı kontrolde 2.15.4 kaldığı için npm latest paketi production yolu yapılmadı; local MCP exact 2.18.0 release source'undan build edildi.
- Canlı local Penpot MCP 5 native araç yayımlıyor: `execute_code`, `high_level_overview`, `penpot_api_info`, `export_shape`, `import_image`. `import_image` acceptance gate'e özellikle eklendi; böylece Talvora yanlışlıkla yeniden Docker multi-user endpoint'ine bağlanırsa regression kırılır.
- Talvora Dev yüzeyine 4 wrapper eklendi: `talvora_penpot_status`, `talvora_penpot_overview`, `talvora_penpot_read_tool`, `talvora_penpot_call_tool`. Native Penpot text/structured/image result blokları korunur; bilinmeyen veya mutating araçlar full-call yolunda kalır.
- `e45ef95` sonrasında wrapper resolver, kullanılabilir olduğunda Penpot'un managed authenticated endpoint'ini tercih eder. Bu endpoint canlı durumda 4 araç yayımlıyor: `execute_code`, `high_level_overview`, `penpot_api_info`, `export_shape`. Direct local `127.0.0.1:4401/mcp` fallback'i 5 araçlıdır ve ayrıca `import_image` içerir. Dedicated smoke bu iki sözleşmeyi artık bilinçli biçimde ayrı doğrular; authenticated endpoint için yanlış 5-tool parity varsayımı kaldırıldı.
- Penpot 2.18 upstream tool metadata'sı read-only annotation yayımlamadığı için güvenli read yolu yalnız bilinen non-mutating `high_level_overview`, `penpot_api_info`, `export_shape` isimlerini fallback olarak kabul eder; `execute_code` generic mutating çağrı yolundadır.
- Control Center canonical managed-MCP recovery discovery'deki `penpot` kaydı `autoStart=true`; `Talvora Penpot MCP` scheduled task bileşenini, exact source-root `process-match` cleanup'ını ve direct local MCP için 5 native required-tool probe'unu içeriyor. Canlı kullanıcı registry'si bu sözleşmeyle yenilendi; Tray exact-installed `345947a...` version root'undan çalışıyor.
- Lifecycle acceptance GREEN: Control Center stop semantiği taklit edildiğinde `4400/4401/4402` üçü de kapandı; start sonrası üçü de geri geldi ve plugin manifest HTTP 200 döndürdü. `TALVORA PENPOT INTEGRATION GREEN`, metadata live ve surface live targeted gate'leri deploy sonrasında tekrar GREEN.
- Penpot'un kendi `high_level_overview` sözleşmesine göre gerçek design mutation için açık bir Penpot dosyasının Penpot MCP Plugin ile MCP sunucusuna bağlanması gerekir; bu bağlantı dosya/proje kullanım bağlamında yapılır ve Talvora entegrasyonundan ayrı bir kullanıcı-proje oturumudur.
- Custom **Talvora AI** Penpot plugin'i artık Talvora Windows Service tarafından doğrudan servis edilir; ekstra Node servisi/port/startup görevi yoktur. Manifest `http://127.0.0.1:7676/penpot-ai/manifest.json`, health `/penpot-ai/healthz`; `plugin.js`, `index.html` ve `icon.svg` aynı loopback origin'den sunulur.
- Talvora AI manifest v2 permissions: `content:write`, `library:write`, `allow:downloads`, `allow:localstorage`. Plugin iframe/message modelini Penpot'un resmi API'siyle kullanır; canlı selection/page/theme bilgisini izler.
- Talvora AI v0.1 yetenekleri: selection inspection, premium radius/stroke/shadow polish, 390x844 mobile board clone, local library component oluşturma, solid fill -> color token extraction/binding, Penpot native `generateMarkup/generateStyle/generateFontFaces` ile HTML/CSS developer handoff ve prototype viewer açma.
- Serbest metin kutusu v0.1'de haricî LLM çağırmaz; yerel/deterministik intent router ile premium/mobile/component/token/handoff/prototype/inspect niyetlerini bir veya birden çok komuta dönüştürür. Gerçek generative model backend'i ayrı bir sonraki fazdır.
- Canlı Talvora AI acceptance GREEN: plugin demo profile'a manifest üzerinden kuruldu ve permission grant edildi; panel `Talvora hazir` gösterdi. Gerçek board üzerinde mobile clone, premium polish, 15 yeni color token + 27 token binding, component creation, doğal dil `incele + HTML CSS handoff` zinciri ve prototype viewer doğrulandı. Son handoff örneği component instance board için 13,042 karakter HTML + 13,849 karakter CSS üretti.
- Plugin ilk runtime commit'i `a0abcc3`; component dönüşümü sonrası selection continuity ve authenticated/direct smoke contract düzeltmesi `345947a`. İki commit Gitea + GitHub'a push edildi. `345947a` canonical installer SHA-256 `93CBB68249D9EC26298AE01FFB51345D41C28BFCFB97B9D66516AEF36F4B33E2` ile SYSTEM deploy edildi; live `sourceCommit=345947a...`. `TALVORA PENPOT INTEGRATION GREEN`.

## Modal / Qwen entegrasyonu — CURRENT

- Resmi Modal Python SDK/CLI **1.5.5** sistem Python 3.14 altına kuruldu; executable `C:\Python314\Scripts\modal.exe`.
- Modal için ayrı üçüncü taraf MCP yerine Talvora Dev yüzeyinde 4 native yönetim aracı vardır: `talvora_modal_info`, `talvora_modal_app_list`, `talvora_modal_endpoint_list`, `talvora_modal_run`. `app_list`, yeni Endpoint ürünü öncesi klasik Modal App deployment'larını da keşfeder.
- Modal CLI çağrıları credential/profile sahipliği için logged-on Windows user session'ında çalışır. `talvora_modal_info` aktif profili ve credential kullanılabilirliğini ayrı raporlar; credential değeri response'a alınmaz. Modal API/proxy/OAuth credential prefix'leri persistent log redaction kapsamındadır. Generic run, resmi CLI yüzeyini korur.
- Modal hardening aşamasındaki yüzey **Full 217 / Dev 187 / Admin 91** idi; Penpot entegrasyonu sonrasında yüzey bir aşamada **Full 221 / Dev 191 / Admin 91** oldu. Güncel global yüzey **Full 212 / Dev 182 / Admin 91**. Metadata source/live + surface source/live GREEN, Talvora + Smoke Release build 0 warning / 0 error.
- Modal user profile setup tamamlandı; aktif profile `taylansoylu`. Native `modal endpoint list` boş çünkü bu model yeni Endpoint ürünü değil, custom Modal App olarak deploy edilmiş.
- Custom app `codepilot-huihui-qwen38` deployed; public OpenAI-compatible base `https://taylansoylu--codepilot-huihui-qwen38-serve.modal.run/v1`.
- Auth secret adı `codepilot-inference-api`; required env key adı `LLAMA_API_KEY`. Secret değeri okunmadı, loglanmadı veya HANDOFF'a yazılmadı.
- Cold start davranışı doğrulandı: ilk istek `503 Loading model`; loglarda GGUF yaklaşık 12 saniyede yüklenip `llama_server: model loaded` ve `0.0.0.0:8000` listen durumuna geçiyor. Auth header olmadan sıcak endpoint `401 Invalid API Key` döndürüyor.
- Model kimliği `codepilot-huihui-qwen38-27b`; context `n_ctx=65536`, train context 262144, 27.32B parametre.
- **2026-09-26 model refresh:** eski Huihui `UD-Q4_K_XL` içeriği, güncel stok-`llama.cpp` uyumlu `Huihui-Qwen3.8-27B-abliterated-UD-DW-Q4_K_M.gguf` ile değiştirildi. Yeni dosya 16,551,316,384 bayt; SHA-256 `0c7cfe3060493485bb9a6a51195897b0c1d347a49929206adb9a52170183ea03` olarak Volume içinde tekrar hash edilip doğrulandı.
- Mevcut deployed app kaynak kodunu ve public endpoint'i değiştirmemek için Volume'daki runtime dosya adı geriye uyumluluk amacıyla `Huihui-Qwen3.8-27B-abliterated-UD-Q4_K_XL.gguf` olarak bırakıldı; dosya içeriği ve hash'i artık yukarıdaki UD-DW Q4_K_M artefact'ına aittir.
- `codepilot-huihui-qwen38` recreate rollover sonrası yeni container 17:09 civarında modeli yeniden yükledi. Yetkili post-update `/v1/models` probe 200, `/v1/chat/completions` probe 200, content `UPDATED_OK`; `reasoning_content` alanı da mevcut.
- Tekrarlanabilir bakım yardımcıları `C:\\Users\\tayla\\Documents\\Modal-CodePilot\\refresh_model.py` ve `probe_endpoint.py` altında tutuluyor; refresh indirme boyutunu + SHA-256'yı doğrulamadan Volume publish etmez ve inference secret'ı response/log içine çıkarmaz.
- Dyad hedef yolu **Dyad -> Modal /v1**; Talvora yönetim/diagnostic yapıyor. `custom::modal-qwen` provider'ı base URL `https://taylansoylu--codepilot-huihui-qwen38-serve.modal.run/v1`, model `codepilot-huihui-qwen38-27b`, context 65,536 olarak kurulu ve seçili.
- 2026-09-26 API credential onarımı: eski inference key nedeniyle Dyad loglarında `401 Invalid API Key` görülüyordu. Yeni rastgele inference key oluşturuldu, Modal `codepilot-inference-api` Secret içindeki `LLAMA_API_KEY` değiştirildi ve app recreate rollover edildi. Yeni key ile `/v1/models` 200 ve chat probe 200 doğrulandı.
- Dyad'ın `providerSettings["custom::modal-qwen"].apiKey` alanı Electron Safe Storage (`v10`/OSCrypt) formatında yeni key ile yeniden şifrelendi; logged-on user context'inde decrypt round-trip doğrulaması GREEN. Dyad 1.17.0-beta1 yeniden başlatıldı. Plaintext key yalnız kullanıcının istediği `C:\\Users\\tayla\\Desktop\\MODAL-QWEN-API-KEY.txt` dosyasında tutuluyor; repo/HANDOFF/log içine yazılmadı.

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

### #190 — Privacy & Security Hardening — LIVE VERIFIED
- Talvora'nın tool/capability yüzeyi kısıtlanmadı.
- Persistent `FileLog` ve Control Center raw-log görünümü ortak secret redaction kullanıyor.
- Interactive-user process handoff `request.json` dosyasını deserialize sonrası hemen siliyor; 24 saatlik stale-run cleanup active lease'i koruyor.
- Tunnel-client hata/UI diagnostikleri secret-safe redaction kullanıyor.
- `.gitignore` local secret/credential dosyalarını dışlıyor; `SECURITY.md` trust-boundary ve privacy politikasını belgeliyor.
- Public dokümanlardaki gereksiz kullanıcı/path metadata'sı genelleniyor.
- Dedicated `--privacy-security-only` smoke ve privacy/security source regression GREEN; Tray Release build 0 warning / 0 error.
- 266 dosyalık high-confidence tracked-tree secret scan 0 bulgu.
- Security commit `689a7ba` iki remote'a push edildi.
- Clean `1d388ab` HEAD canonical installer SHA-256 `6CFA67594FDC8709F2773F214E7077882486B9D03FA4F8FDCF89A19E8A55F9D5`; SYSTEM deploy GREEN ve exact-installed runtime aynı commit'i bildiriyor.
- Installed `Talvora.Shared.dll` live redaction probe GREEN; MCP surface 204/204 unique tool.
- #190 kapanmıştır.

### #191 — MCP Host Metadata Accuracy — LIVE VERIFIED
- Exact-installed eski baseline: 204 tool, 170 `openWorldHint=true`.
- Kanonik host-facing metadata policy 204/204 tool'u review ediyor; final live dağılım 56 open-world + 148 closed-world.
- Tüm 204 tool için ReadOnly/Destructive/Idempotent/OpenWorld annotation alanları artık wire seviyesinde explicit; omitted değer 0.
- Read-only tool'lar explicit non-destructive + idempotent; gerçek external/open-ended tool'lar open-world kalıyor.
- Host-facing description normalizer ve yüksek-sinyal override'ları capability'yi değiştirmeden implementasyon-risk dilini sadeleştiriyor.
- Live `tools/list` taramasında `arbitrary/unrestricted/allowlist/denylist/escape-hatch` legacy terimleri 0 eşleşme.
- Initialize `ServerInstructions` artık `unrestricted` / `escape-hatch` içermiyor; `talvora_apply_patch` primary routing ve PowerShell/process administration capability korunuyor.
- Targeted gates: Talvora Release 0 warning / 0 error; Smoke Release 0 warning / 0 error; `TALVORA MCP METADATA POLICY SOURCE GREEN`; `TALVORA MCP METADATA POLICY LIVE GREEN`.
- Runtime commits: `8b16760` + final wording fix `6a31805`; iki remote'a push edildi.
- Final canonical installer SHA-256 `C42FFEF7D0720D057CFC4ABED2DACEED2318C0CB841FDA8B730DE70DA1027A4E`; exact-installed runtime `6a31805...`.
- Tool surface 204/204; PowerShell/process/Git/Docker/HTTP/TCP/ADB/registry/service dahil capability kaybı yok.
- #191 kapanmıştır.

### #192 — Focused MCP Surfaces + Tool Selection Quality — LIVE VERIFIED
- Legacy/full `/mcp` exact 204 tool olarak korunuyor.
- Yeni local focused endpoints: `/mcp/dev` = 174 tool; `/mcp/admin` = 91 tool.
- Focused surface filtering per-request ToolCollection seviyesinde; listede olmayan tool direct invocation ile bypass edilemiyor.
- Full ↔ focused ortak tool'larda title, input schema, output schema, description ve annotations birebir korunuyor; yalnız visibility değişiyor.
- Deterministik selection eval ordinary source edit / exact range / structural / semantic / build-test / Git / service-registry / negative non-Talvora intent fixture'larını doğruluyor.
- Description quality ikinci turu grammar/implementation noise azaltıyor; full capability ve tool schemas değişmedi.
- DestructiveHint deep audit sonrası reversible lifecycle/control örnekleri non-destructive; caller-controlled/data-loss sınıfları conservative destructive kalıyor.
- Control Center live registry `talvora-dev` + `talvora-admin` focused registrations içeriyor; ikisi tunnel-only, ana Talvora Windows service ownership full `talvora` kaydında kalıyor.
- Targeted gates GREEN: metadata live, surface source/live, focused surface source regression; Tray/Smoke Release 0 warning / 0 error.
- Runtime commits `24eee60` + `508e513`; final installer SHA-256 `9205EB952BBC243AC655391C792FFAF36BF3E1353075B08EE5043F3523E4602B`; exact-installed `508e513...`.
- Ayrı remote ChatGPT Dev/Admin tunnel publication yalnız dış prerequisite olarak bekliyor: OpenAI Admin credential bu makinede mevcut değil. Credential uydurulmayacak; local focused endpoints ve full existing tunnel bundan etkilenmiyor.
- #192 kapanmıştır; external publication prerequisite bug değildir.

### #193 — LocalSystem Git ownership / canonical build — LIVE VERIFIED
- Kullanıcıya ait repolar LocalSystem altında Git 2.55 `dubious ownership` korumasına takılıyor; structured Git family ve canonical installer build etkileniyordu.
- `GitTools` gerçek repo kökünü filesystem ile bulup yalnız process-local `-c safe.directory=<root>` ekliyor; `safe.directory=*` veya kalıcı global gevşetme yok.
- `Build-Windows-Installer.ps1` tüm repo Git çağrılarını aynı scoped `Invoke-RepositoryGit` helper'ından geçiriyor.
- Targeted gates: `GIT_SAFE_DIRECTORY_SOURCE_GREEN`, `NATIVE_INSTALLER_SOURCE_GREEN`, Talvora Release 0 warning / 0 error.
- Runtime commits `ba655ec` + `ab313ec`; iki remote'a push edildi.
- Canonical installer SHA-256 `1F9BDD3557C514511E479B84827C091F9B047E6D344420659B1A671C284E5639`; exact-installed runtime `ab313ec...`.
- Live structured Git family ana kullanıcı reposunda GREEN; #193 kapanmıştır.

### #194 — background job terminal metadata publication race — LIVE VERIFIED
- Interaktif `cmd.exe` job'ında stdin/stdout sonrası `talvora_job_stop`, terminal metadata publication sırasında `AtomicFile.MoveDurable` Win32 error 5 ile hata döndürebiliyordu.
- Job metadata read/write erişimi `MetadataAccessGate` altında serialize edildi; observer ile Stop stale terminal metadata'yı birbirinin üstüne yazmıyor.
- Dedicated 32/32 stop-race regression + job-storage regression + Release build GREEN.
- Fix `2f3620d`; canonical installer SHA-256 `A2D03A8E55CE0BE56CD500B5621F9FBFC15AB27A9DBC7AAB57802AFF96AFAD05`; live verified.

### #195 — .NET smoke / Source Edit policy drift — TEST VERIFIED
- Full smoke önce `.csproj` yazarak fixture'ı development workspace'e dönüştürüyor, sonra `talvora_write_text` ile `Program.cs` yazıp doğru Source Edit policy'ye takılıyordu.
- Runtime policy değiştirilmedi; fixture `Program.cs` marker'dan önce, `.csproj` sonra yazılacak şekilde düzeltildi.
- Full smoke bu aşamayı geçmiştir.

### #196 — read_text_range newline contract — LIVE VERIFIED
- `talvora_read_text_range` exact slice semantiği EOF olmayan seçili son logical line'ın line terminator'ını korur.
- Smoke artık `line-two\\r\\nline-three\\r\\n` + `endReached=false` bekliyor; host-facing description bu kontratı açıkça söylüyor.
- Fix `2ea6a3b`; canonical installer SHA-256 `AEC593991FC028CE011EF090623D984A15C5689BEA490624ACD6F454A4F3DFEB`; exact-installed live runtime aynı commit.

### #197 — StructuredConfig smoke workspace-marker drift — TEST VERIFIED
- StructuredConfig smoke, `compose.yaml` ve `pyproject.toml` güçlü workspace marker adlarıyla compatibility YAML/TOML mutation testini kendi kendine development workspace'e dönüştürüyordu.
- Fixture adları `config.yaml` / `config.toml` yapıldı; runtime source-mutation policy değiştirilmedi.
- Full 204-tool live MCP smoke GREEN.

### #198 — SSH Git credential context — LIVE VERIFIED
- LocalSystem Git, user-scoped Gitea SSH key/host trust'ını kullanamadığı için `origin` fetch/push başarısız oluyordu.
- SSH fetch/push ve GitHub HTTPS fetch/push artık logged-on user session'ında çalışıyor; local/status/history Git işlemleri SYSTEM altında kalıyor.
- `id_ed25519_gitea` ACL'i yalnız kullanıcı FullControl; repo-local `core.sshCommand` özel key ve `known_hosts_gitea` dosyasını seçiyor.
- Targeted regression + Release build + metadata source/live GREEN; canlı Gitea/GitHub fetch/push dry-run'ları exit 0.
- Fix `8eb6c60`; installer SHA-256 `06690E5CA2FEC527DC4A3FE593A0E9B8F7DA4D0B1326716E7F0C6B5905F1B8B2`; exact-installed runtime aynı commit.

### #199 — Admin Event Log response bounds — LIVE VERIFIED
- `talvora_eventlog_query` pre-fix `maxEvents=2147483647` kabul ediyordu.
- Runtime artık 1..500 event ve event message başına 16 KiB ceiling uyguluyor; `messageTruncated` structured metadata taşıyor.
- Fix `8dc8aaf`; live 501 reject + normal structured event payload GREEN.

### #200 — Talvora self service stop/restart — LIVE VERIFIED
- Self restart pre-fix host servisini durdurup MCP request'i response vermeden öldürüyordu.
- Talvora self stop/restart artık 2 saniye gecikmeli detached LocalSystem helper'a handoff ediliyor; tool önce `StopScheduled` / `RestartScheduled` döndürüyor.
- Live restart yeni PID ile otomatik geri geldi; controlled self-stop acceptance GREEN.
- Fix `8dc8aaf`.

### #201 — Admin registry list response bounds — LIVE VERIFIED
- Pre-fix geçici HKLM fixture 1,200 registry value'yu tek response'a döndürüyordu.
- `talvora_registry_list` artık combined ordinal subkey/value stream için max 5,000 entry + 8 MiB response ceiling ve `maxResults/resultOffset/nextResultOffset` pagination kullanıyor.
- Live 1,200-value acceptance: 500 + 500 + 200 deterministic pages; son sayfa `truncated=false`.
- Fix `e6fe0a8`; installer SHA-256 `39D1D70B9E6897B764131AB13E387A8C9C27B00CA281DC84FD67C2E09B02D0A1`; exact-installed runtime aynı commit.

### #202 — logged-on user environment scope — LIVE VERIFIED
- Pre-fix `target=user`, LocalSystem service account user profile'ını hedefliyordu.
- `user` / `interactive-user` artık logged-on user session'ında çalışıyor; eski service-account scope explicit `service-user` olarak korunuyor.
- Fix `8ae1a5a`; independent `MSI\tayla` user-session read ve service-user isolation GREEN.

### #203 — persistent user empty-string environment value — LIVE VERIFIED
- İlk #202 helper'ı Windows PowerShell 5.1/.NET Framework nedeniyle empty-string'i deletion'a dönüştürüyordu.
- Helper PowerShell 7 / .NET 10'a taşındı; persistent user empty value artık `found=true,value=""`.
- Fix `8f6887f`; installer SHA-256 `2525B66A2A7E22CCFD9F61BD06587ACAAB99FC558CB6A62C9B4D264190A83BC6`; exact-installed aynı commit; independent user-session `<empty>` probe GREEN.

### #204 — INI/.env EOF newline preservation — LIVE VERIFIED
- Pre-fix compatibility INI/.env set/delete, final newline olmayan existing file'a mutation sırasında CR/LF ekliyordu.
- Shared text helper original EOF newline presence'i ve CR-only newline style'ı izliyor; existing file formatting korunuyor.
- Fix `bb1eaf5`; installer SHA-256 `FA5B0E70E78D5687920C72DA998690D440E0800320FE2B3E0D563AE5D36CF27A`; exact-installed aynı commit.
- Live dotenv + INI set/delete no-final-newline acceptance GREEN; fixtures temizlendi.

### #205 — INI/.env list response bounds — LIVE VERIFIED
- Pre-fix `dotenv_list` ve `ini_list` live 12,000-entry fixture'ın tamamını tek MCP response'a döndürüyordu.
- Her iki liste artık filtered stream üzerinde default 500, absolute 10,000 entry + 8 MiB response ceiling ve deterministic continuation metadata kullanıyor.
- Fix `a1c71d8`; installer SHA-256 `B4641A788B4A24157EDD3583B9A4560F2701E930E0E38500DF5172AB8C4E52D3`; exact-installed aynı commit.
- Live dotenv 500+500+200 ve INI 200/1200 terminal page sırası GREEN; focused-surface + metadata live GREEN.

### #206 — XML query response bounds — LIVE VERIFIED
- Pre-fix node count bounded olsa da büyük node `value/outerXml/attributes` ve scalar XPath payload karakterleri sınırsızdı.
- Runtime node pages ve scalar sonuçlar için hard 8 MiB response-character ceiling kullanıyor; continuation destekli node pages korunuyor.
- Fix `aa55cad`; installer SHA-256 `B06CE2FFE231356AA117DB22EEC0BD1E1488B6399D3CF51FE43DD21E827E758D`; exact-installed runtime aynı commit.
- Live 4.3M-character fixture: oversized node-set + scalar reject, küçük scalar GREEN; fixture temizlendi.

### #207 — Structured config transport-safe response bounds — LIVE VERIFIED
- Canlı ölçümde 5.0M JSON response GREEN iken 5.5M/6.0M JSON ve 6.0M XML scalar generic transport failure veriyordu.
- JSON/YAML/TOML get ve XML node/scalar sonuçları artık ortak 4 MiB structured-value ceiling kullanıyor.
- Fix `b6fb7de`; installer SHA-256 `7CEC30CE7C422F484F9E8E386238BCF14B139483233E1085CCB77262F4149CDA`; exact-installed runtime aynı commit.
- Live 4.0M JSON GREEN; 4.3M JSON/YAML/TOML/XML controlled INVALID_ARGUMENT; metadata + focused surface live GREEN.

### #208 — Registry value transport-safe response bounds — LIVE VERIFIED
- Pre-fix 3 MB binary value 4.0M Base64 chars olarak dönüyordu; 4 MB binary value get/list yolunda generic transport failure üretiyordu.
- `registry_get` ve `registry_list` artık ortak 4 MiB structured-value response ceiling kullanıyor ve oversized tek value'yu serialization öncesi reddediyor.
- Fix `42b24dd`; installer SHA-256 `A090E582E315B31634CCA384C654B55179A2189CDBBD85915C7D8EE9F2EDEF46`; exact-installed runtime aynı commit.
- Live B3 get GREEN; B4 get/list controlled INVALID_ARGUMENT; registry fixture temizlendi; metadata + focused surface live GREEN.

### #209 — replace_text encoding/BOM preservation — LIVE VERIFIED
- Pre-fix UTF-16LE BOM dosya tek kelimelik replacement sonrası BOM'suz UTF-8'e dönüyordu.
- `replace_text` artık canonical `SourceTextCodec` ile encoding/BOM algılıyor ve aynı UTF-8/BOM, UTF-16 LE/BE BOM veya UTF-32 LE/BE BOM semantiğiyle geri yazıyor.
- Fix `5723afa`; installer SHA-256 `22E9320578CBC4ECAE64E424E6538D3683E69E5AC1BA2B86C815DFC87EA14FD3`; exact-installed runtime aynı commit.
- Live UTF-16LE BOM `FF FE` ve UTF-8 BOM `EF BB BF` korunuyor; newline + replacement içerikleri GREEN; metadata + focused surface live GREEN.

### #210 — Config mutation encoding/BOM preservation — LIVE VERIFIED
- Pre-fix JSON/YAML/TOML/XML set işlemleri UTF-16LE BOM dosyaları BOM'suz UTF-8'e dönüştürüyordu.
- Encoding probe + writer factory `SourceTextCodec` içinde merkezileştirildi; JSON set/delete, YAML/TOML set/delete ve XML set/delete aynı politika üzerinden orijinal encoding/BOM semantiğini koruyor.
- Fix `c69d7f8`; installer SHA-256 `48E7EB33A0F0B113749254374C542AEEF4821AE7A436E079A334A7C26B54269A`; exact-installed runtime aynı commit.
- Live UTF-16LE JSON/YAML/TOML/XML set fixture'larının tümü `FF FE` BOM'u ve yeni değeri korudu; metadata + focused surface live GREEN.

### #211 — Knowledge fetch transport bounds — LIVE VERIFIED
- Shared Admin/Dev `fetch`, `search` tarafından uygulanan 4 MiB knowledge-file budget'ını direct/stale document ID yolunda tekrar uygulamıyordu.
- Runtime file size ve decoded response text'i mevcut 4 MiB knowledge budget'ına göre serialization öncesinde doğruluyor; büyük dosyalarda `talvora_read_text_range` yönlendirmesi veriyor.
- Fix `73e13b7`; installer SHA-256 `7D8AD53D87E1F7C3BEEDBAC6BA1C3F0794C5B51275D1BF5FE17002592EC9D794`; exact-installed runtime aynı commit.
- Live Admin 4.0M-character fetch GREEN; 5.5M-character fetch controlled INVALID_ARGUMENT; fixture temizlendi.

### #212 — replace_text atomic publication — LIVE VERIFIED
- Encoding-preserving replace path hedefi hâlâ doğrudan in-place yazıyor ve interruption halinde truncate/partial publication riski taşıyordu.
- Fix `ed275bb`, target ve optional `.bak` publication'ını durable `AtomicFile.WriteAllTextAsync` üzerinden yapıyor.
- Dedicated durability regression + Release build GREEN; clean deploy HEAD `5b990da`, installer SHA-256 `AF38CDED1D4CE97520CB776D353AB4FF659937A7E41905DF25DBE53B861FDC38`.
- Live UTF-16LE BOM target + backup + replacement + no-temp-file acceptance GREEN; fixture temizlendi.

### #213 — replace_text encoding regression drift — TEST VERIFIED
- #210 encoding mapping'lerini `SourceTextCodec` içine merkezileştirdikten sonra eski regression implementation literal'larını yanlış dosyada arıyordu.
- Test artık replace_text delegation'ını ve canonical codec mapping ownership'ini doğruluyor; durability ayrı regression'da.
- `REPLACE_TEXT_ENCODING_SOURCE_GREEN`.

### #214 — write_text atomic publication — LIVE VERIFIED
- Admin-only complete-file UTF-8 overwrite doğrudan in-place yazılıyor ve interruption halinde truncate/partial publication riski taşıyordu.
- Fix `242f0a2`, BOM'suz UTF-8 kontratını değiştirmeden durable `AtomicFile.WriteAllTextAsync` kullanıyor.
- `WRITE_TEXT_DURABILITY_SOURCE_GREEN` + Release 0/0; installer SHA-256 `ED755C04E88B274480037A9ECA756388A12D4B4A10D6BD5381D4F340EC348AA0`; exact-installed aynı commit.
- Live exact Türkçe text/no-BOM/no-temp-file acceptance GREEN; fixture temizlendi.

### #215 — append_text existing encoding preservation — LIVE VERIFIED
- Pre-fix UTF-16LE dosyaya append, raw UTF-8 bytes ekleyip karma encoding oluşturuyordu; `beta` -> `敢慴`.
- Fix `5b2c899`, mevcut non-empty supported text dosyasının body encoding'ini kullanıyor; yeni/empty dosya strict BOM-less UTF-8 kalıyor ve append ikinci BOM üretmiyor.
- `APPEND_TEXT_ENCODING_SOURCE_GREEN` + Release 0/0 + metadata source/live + focused-surface live GREEN.
- Installer SHA-256 `68080D1EDCBF26F37AE6FE26DF8D152C1D7A363E9E461598B940DA8D3370494C`; exact-installed aynı commit. Live UTF-16 tail/body/BOM ve yeni UTF-8 no-BOM acceptance GREEN; fixture temizlendi.

### #216 — append_text newline preservation — LIVE VERIFIED
- Pre-fix `appendNewLine=true` mevcut LF/CR stilini yok sayıp Windows CRLF ekliyordu.
- Fix `a304b0a`, mevcut encoding ile bounded prefix probe yapıp ilk CRLF/LF/CR stilini koruyor; stil bulunmazsa platform default kullanılıyor.
- `APPEND_TEXT_NEWLINE_SOURCE_GREEN` + Release 0/0 + metadata source/live + focused-surface live GREEN.
- Installer SHA-256 `AE65AF7C91617915EA55B9B5E7690AF75E44181AC5D4672C5FCF7B2D1C929455`; exact-installed aynı commit. Live LF/CRLF/CR fixtures exact stilini korudu; fixture temizlendi.

### #217 — AtomicFile destination metadata preservation — LIVE VERIFIED
- Pre-fix existing-file publication `MoveFileEx(REPLACE_EXISTING)` ile temp file metadata'sını hedefe taşıyor ve custom DACL'i kaybediyordu.
- Microsoft'in documented metadata-preserving replacement primitive'i kullanılarak existing destination `File.Replace(..., ignoreMetadataErrors:false)` ile publish ediliyor; new destination write-through move yolunu koruyor.
- `ATOMIC_FILE_DURABLE_METADATA_REGRESSION_GREEN` + Job Storage + Native Installer source regression + Release 0/0 GREEN.
- Fix `668f77c`; installer SHA-256 `9729695F3D1CC48DF00AD673DFEF56FD91BCD2660459EFCA02D222C9F112D0CA`; exact-installed aynı commit. Live target ve backup exact SDDL/creation/Hidden+Archive/content preserved; temp file yok; fixture temizlendi.

### #218 — write_bytes complete replacement durability — LIVE VERIFIED
- Full replacement pre-fix `FileMode.Create` ile hedefi publish tamamlanmadan truncate ediyordu; kesinti existing binary target'ı bozabilirdi.
- Shared `AtomicFile.WriteAllBytesAsync` staged write-through + flush-to-disk + metadata-preserving publication ekliyor.
- Yalnız `append=false, offset=null` complete replacement bu yola taşındı; append ve offset write davranışı değişmedi.
- `ATOMIC_FILE_DURABLE_METADATA_REGRESSION_GREEN` + Talvora Release 0/0 + metadata/focused-surface live GREEN.
- Fix `bf91648`; installer SHA-256 `16380C9A48ED69447EBE30F97D580D2ABCB21BEB7CBDFC1F8E3ED4A69E31F085`; exact-installed aynı commit. Live binary replacement exact bytes + SDDL + creation time + Hidden/Archive + zero-temp ile GREEN; fixture temizlendi.

## Doküman tutarlılığı notu

- `BUG-AUDIT.md` dosyasının en üstteki CURRENT/current remediation summary bölümü otoritatiftir: #121–#218 tamamlandı; #199–#212 ve #214–#218 canlı, #213 test doğrulandı.
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

**#193–#218 kapanmıştır; Dev 174 / Admin 91 focused surface policy GREEN, full 204-tool smoke baseline GREEN ve #199–#218 Admin/live/test gates GREEN. Sonraki yeni doğrulanmış bulgudan deep audit'e devam et.**

Başlangıç sırası:

1. Bu HANDOFF'u oku.
2. `git status --short` ile tree'nin temiz olduğunu doğrula.
3. `HEAD`, `origin/main`, `github/main` durumunu kontrol et.
4. `talvora_system_info.sourceCommit` ile canlı runtime baseline'ını doğrula.
5. `BUG-AUDIT.md` üst CURRENT özetini oku; #121–#188'i tekrar tarama.
6. #190 privacy/security, #191 MCP metadata, #192 focused-surface/tool-selection, #193 Git ownership ve #194–#218 son smoke/runtime/Admin düzeltmelerini yeni kanıt veya gerçek regresyon yoksa kapalı kabul et.
7. Deep audit'te ilk yeni doğrulanmış bulguyu kullanıcıya bildir; şüpheyi bug diye yazmadan önce gerçek çağrı zinciri veya minimal reproduction ile doğrula.
8. Her yeni bulguda minimal fix + targeted regression uygula.
9. Fix sonrası docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa canonical live deploy sırasını tamamla.
10. Sonraki bug'a ancak önceki bug tamamen kapandıktan sonra geç.

## Bitiş kriteri

Yeni audit turunda doğrulanmış açık bug kalmadığında:
- targeted/regression gate'ler GREEN,
- working tree clean,
- Gitea ve GitHub senkron,
- son runtime-affecting commit exact-installed live,
- ardından `%USERPROFILE%\\Desktop\\bitti.txt` oluştur ve final HEAD/remote/live/test özetini yaz.
