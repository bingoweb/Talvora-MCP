# Talvora MCP — Canonical Handoff

## CURRENT — 2026-09-26

Bu dosya kesinti ve yeni oturum devamı için tek kısa kanonik handoff'tur. Eski kronoloji burada tutulmaz. Ayrıntılı bulgular `BUG-AUDIT.md`, görev geçmişi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

## Repo / remote / canlı durum

- Repository: `%USERPROFILE%\\Talvora-MCP`
- Branch: `main`
- Repo/remote `main`: runtime commit `b6fb7def9c5c11ce85f170950cd83d138a53cf07` iki remote'a push edildi; bu handoff için docs-only closeout commit'i bunun üzerinde gelecektir.
- Çalışma ağacı: final docs-only closeout commit'i sonrası **clean olmalıdır**; reset/clean/stash/revert yapma.
- Son runtime-affecting commit: `b6fb7def9c5c11ce85f170950cd83d138a53cf07` — `fix: bound structured config response payloads`.
- Exact-installed canonical runtime artifact source commit: `b6fb7def9c5c11ce85f170950cd83d138a53cf07`.
- Canonical installer SHA-256: `7CEC30CE7C422F484F9E8E386238BCF14B139483233E1085CCB77262F4149CDA`; artifact size: 261,848,335 bytes.
- Gitea remote: `origin` -> local loopback Gitea `Talvora-MCP.git`
- GitHub remote: `github` -> `https://github.com/bingoweb/Talvora-MCP.git`
- Exact-installed canlı Talvora runtime `talvora_system_info.sourceCommit=b6fb7def9c5c11ce85f170950cd83d138a53cf07` bildiriyor.
- Structured Git `info/status/log/diff/branches` LocalSystem altında kullanıcıya ait ana repoda GREEN; `main` -> `origin/main`, ahead=0 / behind=0.
- Gitea ve GitHub `fetch --dry-run` + `push --dry-run` Talvora'nın canlı `git_run` aracıyla GREEN; SSH private key user-only kalıyor.
- Talvora service `Running/Automatic`; exact-installed Service/Tray runtime baseline `b6fb7de...`.
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
- Focused MCP yüzeyleri: **Dev 174**, **Admin 91**; Admin'in 61 aracı Dev ile ortak, 30'u Admin-only. Admin 91/91 isim benzersiz; exact duplicate description yok; Admin-only 30/30 test kaynaklarında temsil ediliyor.
- Güncel audit üst özeti: **#121–#207 remediation complete; #199–#207 LIVE VERIFIED; full 204-tool live MCP smoke baseline GREEN; focused surface live + metadata live GREEN ve capability korunuyor.**

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

## Doküman tutarlılığı notu

- `BUG-AUDIT.md` dosyasının en üstteki CURRENT/current remediation summary bölümü otoritatiftir: #121–#207 tamamlandı; #199–#207 canlı doğrulandı.
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

**#193–#207 kapanmıştır; Dev 174 / Admin 91 focused surface policy GREEN, full 204-tool smoke baseline GREEN ve #199–#207 Admin live gates GREEN. Sonraki yeni doğrulanmış bulgudan deep audit'e devam et.**

Başlangıç sırası:

1. Bu HANDOFF'u oku.
2. `git status --short` ile tree'nin temiz olduğunu doğrula.
3. `HEAD`, `origin/main`, `github/main` durumunu kontrol et.
4. `talvora_system_info.sourceCommit` ile canlı runtime baseline'ını doğrula.
5. `BUG-AUDIT.md` üst CURRENT özetini oku; #121–#188'i tekrar tarama.
6. #190 privacy/security, #191 MCP metadata, #192 focused-surface/tool-selection, #193 Git ownership ve #194–#207 son smoke/runtime/Admin düzeltmelerini yeni kanıt veya gerçek regresyon yoksa kapalı kabul et.
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
