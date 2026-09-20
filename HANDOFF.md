# Talvora MCP Handoff — 2026-09-20

## 2026-09-20 CURRENT — Source audit + semantic #124 + installer/deploy #132/#133/#136/#139/#140/#148 + tunnel #151/#153 + durability #165 tamamlandı

- Bu oturumda dört hata RED -> GREEN ile düzeltildi: #127 YAML yardımcı kural dosyası tarama/isim çakışması; #135 ordinary C#/.csx syntax preflight; #137 online/offline private-cache executable hash + provenance doğrulaması; #138 installer tek metadata snapshot.
- Devam oturumunda #136 kapatıldı: legacy `Cloudflared` servisi yalnız registry ImagePath'ten ayrıştırılmış gerçek executable eski Talvora `cloudflared.exe` ile exact eşleşirse emekliye ayrılıyor; generic `ProgramData\cloudflared` korunuyor ve retirement kurulumun post-commit/best-effort bölümüne taşındı. `NativeInstallerSourceRegression.ps1` GREEN; C# Roslyn syntax preflight GREEN. Direct Installer build yalnız canonical build'in ürettiği `Payload.zip` mevcut olmadığı için beklenen CS1566 noktasında durdu.
- #132 de kapatıldı: eski `Talvora Playwright MCP` task'ı yalnız task XML URI + launcher + WorkingDirectory ownership kanıtıyla siliniyor; eski root path'lerini taşıyan owned process tree sonlandırılıyor, legacy `PlaywrightMCP` root'u local tunnel/state/log/profile/runtime ile birlikte temizleniyor ve durable migration marker yazılıyor. Port bazlı global process cleanup yok. Targeted NativeInstaller source regression GREEN; geçici build-only Payload.zip fixture ile Installer Release compile **0 warning / 0 error**, fixture silindi.
- #133 kapatıldı: canonical deploy `Run()` tarafından döndürülen task instance GUID + pre-run `LastRunTime` baseline'ına bağlandı; hızlı task'ın polling başlamadan bitmesi artık false failure üretmiyor. PowerShell parse ve NativeInstaller source contract GREEN. Gerçek Task Scheduler regression probe 750 ms zorunlu gecikmeyle running instance'ı hiç görmeden `LastRunTime` transition + terminal `LastTaskResult=0` üzerinden **FAST_TASK_COMPLETION_GREEN** verdi.
- #148 kapatıldı: canonical build current HEAD + index tree + seçilmiş tüm runtime input path/SHA-256 kimliğini yakalayıp doğrulanmış `artifacts\installer-work\source-snapshot` kopyasından Service/Tray/Installer publish ediyor. Payload'a `source-snapshot.json` provenance eklendi. PowerShell parser GREEN, `BuildScriptUsesImmutableSourceSnapshot=True`, NativeInstaller regression GREEN. Gerçek canonical build exit 0; artifact 261,736,207 byte ve SHA-256 `ABC7AB270EF31D36AAEFB442ECFA12933EF58F4940C57FD9E8BF94E1434A40BE`.
- #139 kapatıldı: service health sonrasında user-state mutation başlamadan `current.json`, Codex `config.toml` ve Tray Run registry value exact snapshotlanıyor; service rollback sonrasında dosya existence/bytes/attributes ve registry value/kind eski haline getiriliyor. Önceden bulunmayan installer-created state rollback'te kaldırılıyor. Dedicated `InstallerRollbackStateRegression.ps1` GREEN; geçici Payload.zip fixture ile Installer Release build **0 warning / 0 error**.
- #140 kapatıldı: canonical build final installer için sidecar `Talvora-Setup.manifest.json` içinde file name, size, SHA-256, source commit/HEAD/index-tree/runtime-input identity yayımlıyor. Deploy aynı global build mutex'ini non-blocking alıyor, manifesti validate ediyor ve SYSTEM task `Run` çağrısından önce installer identity'yi exact SHA-256 + size + name ile doğruluyor; task kaydı ile launch arasındaki son sınırda ikinci identity gate var. Mutated fixture `CANONICAL_DEPLOY_ARTIFACT_IDENTITY_GREEN`, NativeInstaller source contract GREEN ve build/deploy PowerShell AST parse GREEN.
- #165 kapatıldı: `AtomicFile` primary/backup publication staged temp + `FileOptions.WriteThrough` + `Flush(true)` kullanıyor; publish `MoveFileExW(MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)` ile tamamlanıyor ve publish sonrası final dosyayı yeniden write-handle ile açan belirsiz-success yarışı kaldırıldı. Kalıcı `AtomicFileDurabilityRegression.ps1` overwrite/backup/pre-cancel/temp-cleanup akışını GREEN doğruladı; Shared Release **0 warning / 0 error**.
- #151 kapatıldı: remote tunnel create öncesi request/scope pending journal, create/list sonucu ID için non-cancellable durable commit, bilinen ID için `get`, ambiguous sonuç için request-marker + scope-filtered `list` reconciliation var. Sonuç belirsizse otomatik ikinci create engelleniyor. `TunnelProvisioningPendingRegression.ps1` GREEN; Tray Release **0 warning / 0 error**.
- #153 kapatıldı: tunnel-client release keşfi/download artık stage-only; hiçbir reconnect yolu candidate config'i readiness öncesi publish etmiyor. Cutover öncesi durable `.client-update.pending.json` journal previous/candidate config'i kaydediyor; stop-old -> candidate config -> exact candidate connect/ready -> committed journal sırası uygulanıyor. Failure/cancellation exact previous config'i geri yazıp candidate alias state'ini durduruyor ve eski client/runtime'ı `CancellationToken.None` ile yeniden başlatıyor; rollback başarısızsa journal korunuyor. Crash recovery bir sonraki connect/update girişinde candidate'ı ready gate ile tamamlıyor veya previous runtime'a geri dönüyor. `TUNNEL_CLIENT_UPDATE_ROLLBACK_GREEN`; Tray Release **0 warning / 0 error**.
- #124 kapatıldı: semantic workspace/symbol/diagnostics receipt'i Source Edit durable receipt + checksum-protected WAL planına adapter metadata olarak bağlandı. Normal replay kadar receipt kaybı sonrası Committed WAL recovery de aynı semantic metadata'yı geri kuruyor. Güncellenen semantic replay testi ilk receipt'i bilerek sildi; recovery sonrası metadata birebir korundu ve receipt yeniden üretildi. Source Edit regression **60/60 GREEN**, regression Release build **0 warning / 0 error**.
- Son birleşik Source Edit regression **60/60 GREEN** (başka oturumun watcher/HTTP mock testleri dahil). Son ek bozuk/null/array JSON ve eksik cache manifest kontrolleriyle --source-cache-only **2/2 GREEN**; InstallerAstGrepMetadataRegression **3/3 GREEN**; NativeInstallerSourceRegression **GREEN**; Talvora Release **0 warning / 0 error**; git diff --check **exit 0**.
- #148 için tam canonical installer build yapıldı; live deploy yapılmadı. #148 düzeltmesi `d254433` commit'iyle hem `origin/main` hem `github/main` üzerine pushlandı.
- Eşzamanlı başka çalışma HTTP mock/watcher, Gitea ve yaşayan belgeleri güncelliyordu; o ürün değişiklikleri korundu. Ayrı --artifacts-path C:\Windows\Temp\TalvoraSourceAudit-20260920 ve -p:UseSharedCompilation=false ile build çakışması önlendi.
- Kalıcı regresyonlar: SourceEditRegressionRunner.Audit.cs, SourceEditRegressionRunner.CacheAudit.cs ve tests/InstallerAstGrepMetadataRegression.ps1. Tümü canonical apply_patch ile eklendi. Plain-text transaction fixture uzantıları .txt yapıldı; gerçek C# semantic testleri korundu.
- Bu checkpoint anında **7 OPEN**: #128, #129, #130, #155, #156, #159, #169. Güncel tekil durumlar BUG-AUDIT.md içinde; diğer oturum ilerlerse buradaki sayı checkpoint olarak kalır.
- Araştırma: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.csharpsyntaxtree.parsetext ; https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals ; https://docs.npmjs.com/cli/v11/commands/npm-view/ .

## 2026-09-20 PREVIOUS CHECKPOINT — Düzeltme / kalan remediation durum raporu

### Net durum

- Historical ürün geliştirme ve bug-fix hattı #1–#120 boyunca büyük ölçüde tamamlandı; özellikle Control Center, installer/deploy, Playwright CLI migration, Gitea/tunnel lifecycle, Source Edit core, Routing Contract v3, structural edit ve Roslyn semantic edit exact-installed doğrulamaları GREEN durumuna getirildi.
- Yeni deep-audit kümesi **#121–#171** arasında **51 kayıt** içeriyor. Bunların **19'u OPEN**: **0 CRITICAL, 7 HIGH, 12 MEDIUM**. Current remediation pass'te **#121/#122/#123/#125/#126/#127/#131/#134/#135/#141/#142/#144/#145/#146/#147/#149/#150/#152/#154/#157/#158/#160/#161/#162/#163/#164/#168/#170/#171 FIXED**; **#143 CLOSED FALSE POSITIVE**, **#166/#167 CLOSED DUPLICATE**.
- Ürün/source remediation başladı. P0 Source Edit transaction-integrity, semantic correctness #121–#123 ve canonical source-routing #125/#126/#131/#146 targeted regression ile kapatıldı; diğer açık bulgular kendi doğrulama kapıları geçmeden “fixed” sayılmayacak.
- Kullanıcı açıkça istemediği için Git commit/push yapılmadı. Intentional geniş dirty working tree korunuyor.

### Şu ana kadar tamamlanan ana düzeltme aileleri

- **Process/runtime temeli:** process timeout/termination, batch quoting, transport environment temizliği, background-job startup/observer/PID-reuse sorunları ve çeşitli cleanup/lifetime problemleri önceki auditlerde düzeltildi.
- **Control Center / Tray:** pencere lifecycle, görünürlük/render, title bar/drag/close, placement, responsive UX, structured events/logs, health/recovery, manual-stop semantics ve cross-process lifecycle koordinasyonu tamamlandı.
- **Gitea ve managed MCP temeli:** backend/proxy/MCP/tunnel health zinciri, start/stop/restart davranışları, standard-user service lifecycle, registry coordination ve recovery altyapısının önceki ana kusurları düzeltildi.
- **Installer/deploy:** self-update için bağımsız SYSTEM task deploy yolu, service registration kaybı, rollback temel akışı, Task Scheduler XML encoding ve canonical deploy #109 explicit `--silent` davranışı düzeltildi ve korunuyor.
- **Playwright yönü:** embedded/managed Playwright MCP mimarisi kaldırılıp resmi Playwright CLI modeline geçildi; Talvora source/installer/Tray payload ownership'ten çıkarıldı.
- **Source Edit core (#110–#119):** revision handshake, WAL/receipt, replay/idempotency, rollback/recovery, commit barrier, direct legacy text/config guard, transaction-domain errors ve deterministic routing tamamlandı. Ordinary source/config/repo-doc mutation PRIMARY/default `talvora_apply_patch`.
- **Structural + semantic adapters (#114/#120):** ast-grep proposal-only structural route ve Roslyn symbol-aware semantic rename eklendi; live workspace'e doğrudan yazmadan canonical Source Edit transaction'a bağlandı. Routing Contract v3 exact-installed doğrulandı.
- **Exact-installed baseline:** audit öncesi Source Edit/Semantic targeted regression 33/33 GREEN, Release 0 warning/0 error, raw MCP `tools/list` 204/204 unique ve canonical deploy live snapshot GREEN idi.

### Kalan remediation — öncelikli kümeler

1. **P0 / transaction integrity:** #147 parent identity, #168 crash-recovery file-step ownership, #170 WAL final-record integrity ve #171 durable idempotency retention **FIXED**. Current Source Edit targeted regression **42/42 GREEN**.
2. **P1 / semantic correctness:** #121–#123 **FIXED**; kalan semantic kapsam #124/#128/#129/#130 MEDIUM.
3. **P1 / canonical source routing:** #125/#126/#131/#146 **FIXED**. Copy/move/Git working-tree/archive/download default source intent canonical `talvora_apply_patch`; deliberate administration için `explicitAdmin=true` ve PowerShell/process TAM YETKI korunuyor.
4. **P1 / file/archive mutation:** #141/#142/#161/#162 **FIXED**. HTTP staged publication; archive full preflight + isolated staging + rollback commit + parent identity guard + high emergency resource ceilings.
5. **P1 / recovery failure domains:** #144 WAL isolation, #134/#152 collision-proof managed-MCP identity, #145 redundancy health ve #154 primary registry refresh **FIXED**.
6. **P1 / resource bounds:** #149/#150 background-job log/response bounds, #157 shared process output capture, #158 streaming canonical source read, #160 post-parent pipe drain ve #163/#164 watcher/HTTP-mock completeness/resource ceilings **FIXED**; kalan #159 generic single-response unlimited modes.
7. **P1/P2 / installer-build provenance:** #148 source snapshot identity, #169 dependency provenance drift, #140 deploy artifact pinning, #139 installer state rollback.
8. **P1/P2 / tunnel & managed MCP lifecycle:** #151 remote tunnel create durability, #153 client update rollback, #154 registry self-heal, #155 protocol readiness, #156 full-chain stop verification; ayrıca #132 legacy Playwright cleanup ve #136 owned legacy cleanup.
9. **P2 / correctness/quality:** #127 structural control-file self-scan ve #135 C# syntax validation **FIXED**; kalan #133 deploy completion observation, #137/#138 ast-grep cache/provenance, #165 AtomicFile crash durability ve diğer MEDIUM kayıtlar.

### Kesin bitmemiş işler — 19 OPEN

- **Semantic correctness (4):** #124 semantic replay metadata durability; #128 semantic resource bounds; #129 semantic graph concurrency fingerprint; #130 workspace-scoped MSBuild/toolchain fidelity.
- **Installer / upgrade / deploy (8):** #132 retired Playwright upgrade cleanup; #133 fast-task completion observation; #136 legacy Cloudflared ownership-aware cleanup; #137 ast-grep private-cache executable revalidation; #138 ast-grep exact-version provenance snapshot; #139 installer post-health state rollback; #140 deploy artifact hash/identity pinning; #169 dependency graph provenance snapshot.
- **Managed MCP / tunnel / Gitea (4):** #151 remote tunnel create durable reconciliation; #153 tunnel-client update rollback; #155 real MCP protocol readiness; #156 full-chain Gitea stop verification.
- **Runtime / resource / process bounds (1):** #159 absolute response budgets/continuations across remaining generic read/network/query surfaces.
- **Core / installer durability (2):** #165 crash-durable AtomicFile publication; #148 immutable installer source snapshot / HEAD identity.

### Remediation başlangıç kuralı

- P0 pass tamamlandı. #147 için directory volume/file-ID anchors + WAL persistence + handle-bound add/delete/move ve transaction-lifetime directory rename/delete sharing pin'i eklendi. Update/edited-move `File.Replace` atomik metadata davranışını korurken parent path identity artık değiştirilemiyor.
- Semantic correctness pass #121–#123 tamamlandı: project-mode unique containing solution'a otomatik yükseliyor; incomplete/ambiguous scope fail-closed; baseline compiler Error diagnostics görünür ve mutation öncesi gate; rename sonrası compiler Error `SEMANTIC_CONFLICT`. Current Source Edit regression **46/46 GREEN**.
- Canonical routing pass #125/#126/#131/#146 tamamlandı: nested-project move, generic copy, destination ingress, Git working-tree commands, archive extraction ve HTTP download ordinary development-source intent'i defaultta canonical route'a bağlı; `explicitAdmin=true` generic administrative capability'yi koruyor. Current Source Edit regression **47/47 GREEN**.
- HTTP download #141 tamamlandı: same-directory stage + body-length verification + `Flush(true)` + final replace/move; truncated/network failure existing destination'ı değiştirmiyor. Current Source Edit regression **48/48 GREEN**.
- Archive #142/#161/#162 tamamlandı: full preflight, isolated extraction staging, same-directory commit temps, backup/reverse rollback, contained-mode directory identity pinning ve configurable entry/decompressed-byte budgets. Current Source Edit regression **51/51 GREEN**.
- #144 tamamlandı: WAL enumeration artık transaction-bazlı izolasyon yapıyor; `identity.json` ve `quarantine.json` ile bozuk journal kimliği durable tutuluyor, yalnız ilişkili workspace fail-closed kalıyor, diğer workspace'ler çalışmaya devam ediyor ve startup quarantine diagnostics loglanıyor. Current Source Edit regression **52/52 GREEN**, Talvora Release **0 warning / 0 error**.
- #134/#152 tamamlandı: bütün managed-MCP persistence/lifecycle fallback kimlikleri `ManagedMcpIdentityKey` SHA-256 key modeline bağlandı; recovery manifests healthy-primary fast-path'te de reseed edilerek eski dosya adları migrate ediliyor. `foo/bar` ve `foo?bar` registry recovery fixture'ı iki kaydı da korudu; tunnel fallback path probe farklı dizinler üretti; Talvora.Shared ve Tray Release **0 warning / 0 error**.
- #145/#154 tamamlandı: ownership copies artık parse/validation + canonical content eşitliğiyle health-check ediliyor; missing/stale copy reseed ediliyor. Primary registry backup/recovery kaynağından geldiğinde equality fast-path artık primary'yi yeniden publish ediyor. Temp-path contracts missing/invalid primary ve ownership-copy repair akışlarını GREEN doğruladı; Tray Release **0 warning / 0 error**.
- #157/#160 tamamlandı: shared `ProcessRunner` stdout/stderr capture 4 Mi-character inline budget + head/tail preservation + truncation metadata kullanıyor; output pump aynı overall timeout token'ına bağlı. Parent erken bittiğinde inherited pipe açık kalırsa deadline sonucu `OutputDrainTimedOut=true` ile dönüyor. Interactive-user helper RAM toplamak yerine shared .NET pump ile UTF-8 temp output'a stream ediyor ve parent bounded read yapıyor. `ProcessRunnerRegression` GREEN; real interactive-user echo smoke GREEN; Service + Tray Release **0 warning / 0 error**.
- #149/#150 tamamlandı: background-job stdout/stderr 32 MiB segment sınırıyla current + previous segment olarak bounded tutuluyor; generation sidecar rotation sonrasında stale offset'i görünür kılıyor. `talvora_job_read_output` tek response'u en fazla 4 MiB ile sınırlandırıyor; `maxBytes=0` bounded maximum chunk anlamına geliyor ve `nextOffset/generation/resetRequired/responseLimited` continuation durumunu taşıyor. Completed-job retention 14 gün / 500 kayıt / 4 GiB toplam kota ile çalışıyor; restart sonrası stale `Running` metadata gerçek process state'iyle reconcile ediliyor ve exit zamanı bilinmeyen eski kayıt retention için original start time ile sıralanıyor. Repo-dışı targeted fixture `JOB_BOUNDS_GREEN current=5424 previous=8192 generation=3 responseChars=1000 remaining=new-exited`; Talvora Release **0 warning / 0 error**.
- #158 tamamlandı: `talvora_read_source` whole-file revision'ı streaming SHA-256 ile hesaplarken encoding/newline/final-newline taramasını bounded `FileStream + StreamReader` buffer'larıyla yapıyor ve yalnız caller'ın response penceresini materialize ediyor. Default response 1 Mi UTF-16 character, absolute ceiling 4 Mi character; büyük tek satırlar dahil continuation `nextStartLine/nextStartCharacter` ile sürdürülüyor. Full-document mutation snapshot'ı 256 MiB hard materialization ceiling'i aşarsa allocation başlamadan `SOURCE_EDIT_RESOURCE_LIMIT` üretiyor. Yeni `streaming-source-read-bounded` regression + mevcut UTF-16/mixed-newline/semantic testleri dahil Source Edit suite **53/53 GREEN**; targeted Release build **0 warning / 0 error**.
- #163/#164 tamamlandı: watcher queue `maxQueuedEvents=0` için sonsuz yerine 100,000-event emergency ceiling kullanıyor; watcher error state'i `overflowCount/resyncRequired` ile completeness kaybını görünür kılıyor ve caller rescan sonrasında `acknowledgeResync=true` ile state'i temizleyebiliyor. HTTP mock zero limits yüksek ama finite ceilings'e çözülüyor; per-listener handler semaphore'una ek process-wide 512-slot `GlobalHandlerSlots` backpressure uygulanıyor. Capture queue process genelinde 100,000 request ve yaklaşık 512 MiB retained-memory ceiling ile korunuyor; enqueue/dequeue/stop aynı global muhasebe yolunu kullanıyor. Targeted `--runtime-bounds-only` regression watcher + HTTP-mock bounds için GREEN; Talvora Release **0 warning / 0 error**; NativeInstaller source contract **NATIVE_INSTALLER_SOURCE_GREEN**.
- Her mevcut source/config/repository-document editinden hemen önce fresh `talvora_read_source` revision alınmalı ve mutation PRIMARY/default `talvora_apply_patch` ile yapılmalı.
- Aynı broad test suite tekrar tekrar çalıştırılmamalı; yalnız değişen alanın targeted regression/fault-injection testi, ardından gerekiyorsa final build/smoke gate.
- Working tree reset/revert/checkout ile temizlenmeyecek; concurrent living-doc writer varsayılacak.

## 2026-09-20 CURRENT — Deep Bug Audit checkpoint / next-session handoff

- Bu oturumda ürün/source kodunda düzeltme yapılmadı; amaç son Source Edit + Routing Contract v3 + structural/semantic adapter + installer/deploy + managed MCP/tunnel/Gitea + shared runtime yüzeylerinde **derin bug analizi** idi. Yaşayan kayıt `BUG-AUDIT.md` güncel olarak **#171'e kadar** genişledi. Commit/push yapılmadı.
- Repo için temel kural değişmedi: ordinary source/config/repo-doc editleri PRIMARY/default `talvora_apply_patch`; repetitive AST/syntax -> `talvora_structural_edit`; gerçek C# symbol identity -> `talvora_semantic_edit`; exact generated zero-based UTF-16 ranges -> `talvora_apply_edits`; read -> `talvora_read_source`; ambiguity -> `talvora_source_edit_guide`. Mutator trial-and-error yasak.
- Kullanıcı açıkça routing/tool-choice kısıtına izin verdi: yanlış generic aracın ordinary source edit için seçilmesi server-side reddedilebilir/yönlendirilebilir. Ancak Talvora'nın genel TAM YETKI/explicit-admin yetenekleri keyfi biçimde azaltılmayacak; PowerShell/process gibi explicit admin yüzeyleri korunacak.
- Mevcut exact-installed semantic/source-edit baseline audit başlamadan önce GREEN idi: HEAD `747cbfc560c8f7e9d4c3def699ee986d5c164a71`, installed fingerprint `...-dirty-8726cde50619`, Source Edit/Semantic targeted regression 33/33, Release 0 warning/0 error, raw MCP tools/list 204/204 unique. Bu baseline yeni audit bulgularının doğru olmadığı anlamına gelmez; yeni bulgular stress/live fixture'larla mevcut tasarım sınırlarını ortaya çıkardı.

### Deep audit sırasında doğrulanan başlıca açık bulgular

- **#121–#123 HIGH — semantic correctness/completeness:** project-mode rename reverse dependent projeleri kaçırabiliyor; unresolved reference/compilation diagnostics sessiz kalabiliyor; rename yeni compiler error üretse bile commit edebiliyor.
- **#124 MEDIUM — durable semantic replay metadata kaybı:** Source Edit receipt replay oluyor fakat semantic workspace/symbol/diagnostic metadata ilk response ile aynı biçimde durable değil.
- **#125/#126/#131/#146 HIGH — canonical source-routing lifecycle-gap ailesi:** nested project classification, generic copy, generic Git mutators, archive/download ve destination-side move ingress ordinary development source'unu Source Edit WAL/revision/rollback dışında değiştirebiliyor. Kullanıcının routing kısıtı izni bu aile için merkezi `SourceMutationIntent/Policy` tasarımını mümkün kılıyor; explicit admin override path korunmalı.
- **#127 MEDIUM — ast-grep YAML rule self-scan:** mirror içine yazılan control rule kendisi scan edilip valid rule'u `STRUCTURAL_PROPOSAL_INVALID` ile bozabiliyor.
- **#128/#129/#130 MEDIUM — semantic resource/concurrency/toolchain:** bounds load/generation sonrasında uygulanıyor; semantic graph tüm girdileriyle concurrency-versioned değil; process-wide MSBuild locator multi-SDK/workspace fidelity için tasarım riski.
- **#132 HIGH — Playwright MCP retirement upgrade cleanup eksik:** yeni installer source'u retired scheduled task/process/AppData surface'ini eski kurulumdan otomatik temizlemiyor. Current machine daha önce manuel temizlendi; sorun upgrade path.
- **#133 MEDIUM — deploy WaitForCompletion race:** çok hızlı SYSTEM task ilk poll'dan önce biterse script running instance hiç görmeyip timeout'a gidebilir.
- **#134 HIGH + #152 HIGH — managed MCP ID/path collision:** lossy safe-id filename/path mapping farklı registry ID'lerini aynı recovery/ownership/tunnel config/credential/state yoluna çarpıştırabilir.
- **#135 MEDIUM — `validateSyntax=true` ordinary C# için fiilen no-op:** C# syntax gate Roslyn ile preflight edilmemiş.
- **#136 HIGH — legacy Cloudflared cleanup ownership/rollback:** generic `Cloudflared` service ve ProgramData path'i Talvora ownership kanıtı olmadan temizlenebiliyor; işlem rollback boundary öncesinde.
- **#137/#138 MEDIUM — ast-grep supply/provenance:** offline cache reuse executable hash'ini tekrar doğrulamıyor; installer ayrı mutable `@latest` metadata sorgularıyla mixed-version provenance üretebilir.
- **#139/#140 MEDIUM — installer/deploy transaction provenance:** post-health `current.json`/Codex state rollback'ta restore edilmiyor; canonical deploy artifact expected hash/manifest pinlemeden installer'ı launch ediyor.
- **#141 HIGH — HTTP overwrite atomic değil:** malformed/truncated response existing destination'ı doğrudan truncate edip partial content bırakabiliyor.
- **#142 HIGH — archive extract transaction değil:** sonraki entry failure olduğunda önceki entry mutation'ları live destination'da kalıyor.
- **#143 CLOSED FALSE POSITIVE:** RecoveryRequired workspace quarantine aslında her canonical mutation öncesi `RecoverWorkspaceLockedAsync` ile uygulanıyor; önceki kritik bulgu geri çekildi.
- **#144 HIGH — corrupt WAL global failure domain:** tek bozuk journal enumeration'ı patlatıp ilgisiz healthy workspace canonical editlerini ve diğer recovery'leri engelleyebilir.
- **#145 MEDIUM — recovery redundancy latent corruption:** primary sağlıklıyken corrupt recovery/ownership copies sürekli health-check/reseed edilmiyor.
- **#147 CRITICAL — Source Edit reparse/junction TOCTOU:** canonical commit path parent identity değişimini handle-bound biçimde sabitlemiyor; deterministic delete/simple-move/add fixtures doğruladı. **#148 HIGH — canonical build source-snapshot provenance:** clean HEAD değişimi runtime fingerprint kontrolünden geçebiliyor. **#149/#150 — job log/response resource bounds.** **#151–#156 — tunnel/managed-MCP/Gitea lifecycle ve recovery bulguları.**
- **#157 HIGH — shared process runners unbounded stdout/stderr capture:** verbose child process tek invocation'da service/helper memory'sini sınırsız büyütebilir.
- **#158 HIGH — `talvora_read_source` küçük slice için bile full file materialize ediyor:** byte[] + full string + split/join allocation nedeniyle büyük file'da canonical read ciddi memory riski.
- **#159 MEDIUM — multiple tools `0=unlimited` tek-response memory/transport modes:** read/tail/http/tcp/websocket/sqlite/search gibi yüzeylerde absolute response budget/continuation eksik.
- **#160 HIGH — ProcessRunner timeout pipe-drain'i kapsamıyor:** parent exit 0 sonrası descendant redirected pipe handle'ı açık tutarsa requested timeout aşılabiliyor; live fixture timeout=2s iken ~8.3s döndü.
- **#161 HIGH — archive junction containment violation:** `allowOutsideDestination=false` yalnız lexical check; live junction fixture ZIP içeriğini destination dışına yazdı.
- **#162 HIGH — archive decompression/resource expansion budgets yok:** entry-count/per-entry/aggregate decompressed-size hard caps eksik.
- **#163 MEDIUM — FileSystemWatcher OS buffer overflow completeness state'e yansımıyor:** `DroppedEvents=0` iken gerçek event loss mümkün; `resyncRequired` benzeri state yok.
- **#164 HIGH — watcher/HTTP mock unbounded queues/inflight/pending:** process-wide emergency ceiling/backpressure yok.
- **#165 MEDIUM — `AtomicFile` rename atomicity var ama crash durability yok:** temp publish öncesi `Flush(true)`/write-through ve backup durability sözleşmesi eksik.
- **#166/#167 CLOSED DUPLICATE:** yeni ayrı bug değiller; #166 evidence #147'ye, #167 evidence #148'e birleştirildi.
- **#168 HIGH — crash recovery file-step ownership kaybı:** durable recovery applied/attempted file index geçmişini yeniden kurmuyor; hiç uygulanmamış add step'i dış writer tarafından aynı-content oluşturulduğunda multi-file rollback bu dosyayı silebiliyor. Deterministic repo-dışı fixture ile doğrulandı.
- **#169 MEDIUM — dependency provenance restore drift:** service/Tray publish sonrasında mutable `obj/project.assets.json` yeniden restore edilirse `resolved-dependencies.json` published binary'den farklı package versionları raporlayabiliyor. Local NuGet fixture published deps 1.0.0 / assets 2.0.0 ayrışmasını doğruladı.
- **#170 HIGH — final WAL integrity/torn-tail ayrımı:** complete checksum-invalid terminal record son satır olduğu için ignore edilebiliyor; `RecoveryRequired` geçici olarak önceki state'e düşüyor ve quarantine yeniden kurulmadan önce rollback file-step'i live dosyayı değiştirebiliyor. İki repo-dışı fixture ile doğrulandı.
- **#171 HIGH — completed-transaction retention idempotency kaybı:** 30 gün/count/size cleanup receipt+journal identity'sini tamamen siliyor; aynı `transactionId + requestHash`, state tekrar precondition'a uyduğunda yeniden `committed/replayed=false` çalışabiliyor. Isolated fixture immediate replay ile eviction-sonrası re-execution farkını doğruladı.
### Canlı/reasoned audit kanıtlarından önemli örnekler

- Project-scope semantic rename sonrası reverse consumer build'i `CS0246` ile kırıldı (#121).
- Missing HintPath dependency içeren project'te semantic rename `success=true`, empty diagnostics ve commit döndürdü (#122).
- `Alpha -> Beta` collision rename commit edildi; build `CS0101` verdi (#123).
- `talvora_git_run restore`, archive extract, HTTP download, copy ve move fixture'ları canonical source lifecycle dışında kalan mutation sınıfını canlı gösterdi (#125/#126/#131/#146).
- Truncated HTTP body existing file'ı `BROKENBODY` partial içeriğine düşürdü (#141).
- ZIP'te ilk source overwrite edildi, sonraki `../escape` entry fail olunca ilk mutation geri alınmadı (#142).
- Inherited-pipe fixture `timeoutSeconds=2` iken ~8.3 saniye sonra `timedOut=false` döndü (#160).
- Junction fixture `allowOutsideDestination=false` ile gerçek destination dışına dosya çıkardı (#161).

### Son doğrulanan araştırma sonuçları

- **#147 Source Edit reparse/junction TOCTOU:** ek friend-assembly fault-injection fixture tek-dosya ve multi-file late-step parent identity değişimini deterministik olarak yeniden doğruladı; handle/final-path/file-ID tabanlı identity modeli fix yönüdür.
- **#148 Build provenance source-snapshot race:** clean commit A -> clean commit B repo-dışı fixture current-HEAD fingerprint'in değişmeden kalabildiğini ve captured SourceCommit'in stale kaldığını deterministik olarak doğruladı.
- **#168 Crash recovery ownership:** üç-file fixture'da yalnız ilk step uygulanıp crash oldu; ikinci add hiç uygulanmadı, ancak dış writer aynı-content hedef oluşturduktan sonra recovery üçüncü before-state nedeniyle rollback'e girip bu dosyayı kaldırdı. Recovery yalnız proven-applied step'leri mutate etmeli; indeterminate step `RecoveryRequired` olmalı.
- **#169 Dependency provenance:** local-feed fixture publish outputunda `Audit.Dep/1.0.0` kalırken bağımsız restore `project.assets.json`'ı 2.0.0'a taşıdı ve current manifest reader 2.0.0 raporladı. Aynı build için restore graph immutable snapshot'a bağlanmalı.
- **#170 WAL tail integrity:** complete checksum-invalid final `RecoveryRequired` record `Prepared` gibi görüldü; recovery sonunda yeniden quarantine olsa da arada bir rollback step `second-after -> second-before` mutation yaptı. Integrity-invalid complete record immediate zero-mutation `RecoveryRequired` olmalı.
- **#171 Idempotency retention:** first retry `replayed=true`; aged cleanup receipt+journal'ı kaldırdı; file before-state'e getirildikten sonra aynı transaction yeniden `committed/replayed=false` oldu. Ağır WAL retention ayrı tutulsa bile minimal durable transaction identity/tombstone korunmalı veya expiry contract açık ve fail-closed olmalı.

### Sonraki oturum öncelik sırası

1. Önce `HANDOFF.md`, sonra `BUG-AUDIT.md` #121–#171, `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`, Faz 14 ve canonical routing roadmap'i oku; working tree'yi reset/revert/checkout etme.
2. Deep audit'i kalan yüksek değerli yüzeylerde sürdür; #166/#167 duplicate olarak kapatılmıştır, canonical karşılıkları #147/#148'dir.
3. Audit tamamlanınca kullanıcı isterse fix fazına geç. İlk remediation grubu: **#121 -> #122/#123 -> source-routing lifecycle-gap ailesi (#125/#126/#131/#146) -> #141/#142/#161/#162 -> #144 -> #157/#160/#158/#159/#164 -> installer/tunnel/recovery bulguları -> lower-risk MEDIUM maddeler**.
4. Routing remediation'da wrong-tool ordinary source mutation server-side stable policy error ile canonical tool'a yönlendirilebilir; explicit admin intent/override path korunmalı. Capability kırpma yapılmamalı.
5. Source Edit/recovery değişikliklerinde revisioned read + `talvora_apply_patch`; broad repeated tests yok. Yalnız değişen alanın targeted regression'ı, sonra gerekli build/smoke.
6. Canonical deploy davranışında #109 explicit `--silent` asla kaybolmamalı.
7. Commit/push yalnız kullanıcı açıkça isterse yapılacak.

### Geçici audit artıkları

- Repo dışı fixture'lar geçmiş audit'te `%LOCALAPPDATA%\Temp\TalvoraGitAudit` ve `TalvoraGenericMutationAudit` altında oluşturuldu; semantic fixture daha önce temizlendi. Audit bitince kullanılmayan temp fixture/job metadata'sı temizlenebilir; repo source için generic mutator kullanılmamalı.

## 2026-09-20 CURRENT — Routing Contract v3 + Roslyn Semantic Edit exact-installed GREEN

- Repo remains `main` at HEAD `747cbfc560c8f7e9d4c3def699ee986d5c164a71`. The pre-existing intentional dirty Playwright CLI migration, #109, Source Edit, Routing and structural work was preserved; no reset/revert/checkout/overwrite was used.
- Canonical routing is now complete: ordinary code/config/repo-doc changes including ordinary C# -> PRIMARY/default `talvora_apply_patch`; repetitive AST/syntax transformations -> `talvora_structural_edit`; real C# symbol/semantic identity operations -> `talvora_semantic_edit`; already-generated exact UTF-16 ranges -> `talvora_apply_edits`; reads -> `talvora_read_source`; ambiguity -> `talvora_source_edit_guide`. Mutator trial-and-error remains forbidden.
- `talvora_semantic_edit` is intentionally narrow. Current capability is solution/project-aware C# symbol rename. Roslyn never writes the live workspace; it returns an in-memory changed Solution which is converted into exact revisioned Source Edit changes and committed through the existing SHA-256/WAL/idempotency/rollback/recovery engine.
- Current resolved semantic dependencies: `Microsoft.CodeAnalysis.CSharp.Workspaces 5.9.0`, `Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0`, `Microsoft.Build.Locator 1.11.2`, and compile-only/private `Microsoft.Build.Framework 17.14.28` with runtime assets excluded.
- Semantic workspace behavior is deterministic and bounded: Locator registration precedes workspace creation; disposable `MSBuildWorkspace`; current `RegisterWorkspaceFailedHandler`; revisioned Roslyn/disk text equality; linked/multi-project semantic ambiguity rejection; cancellation/timeout/resource limits; no hidden partial success; no automatic Formatter/Simplifier churn.
- Targeted Source Edit/Semantic regression is **33/33 GREEN**. Talvora Release/targeted build is **0 warning / 0 error**. NativeInstaller source regression is GREEN including `SemanticEditToolContract=True` and #109 canonical deploy `--silent`.
- `git diff --check` was exit 0 before installer build, with only the existing Windows LF/CRLF warnings.
- Canonical installer build GREEN: current `Talvora-Setup.exe` SHA-256 `28171A926EA96D78B05724201BCAFA30E3351208DB56EAB517384FFB0AFC991A`.
- Canonical deploy GREEN. Latest exact-installed live snapshot reports `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-8726cde50619`, running as LocalSystem.
- Raw wire-level MCP verification used the server's latest supported handshake protocol `2025-11-25`; `tools/list` reports **204/204 unique tools** and contains `talvora_semantic_edit`. The older handoff phrase `MCP 2026-07-28` was not a valid initialize protocol version and is superseded by this wire-level evidence.
- Raw routing smoke GREEN: `talvora_source_edit_guide` returns PRIMARY patch / read / exact-range / structural / semantic routes exactly as intended and explicitly keeps ordinary C# on `talvora_apply_patch`.
- Latest exact-installed live semantic smoke GREEN: a two-project `.slnx` renamed `LiveSmoke.Widget` declaration plus cross-project references to `RenamedWidget`; declaration UTF-16 LE BOM and exact declaration/reference CRLF + deliberate spacing were preserved. Raw `tools/list` remained 204/204 unique and routing metadata remained canonical.
- Same semantic transaction `semantic-live-96c4169c9c9e4536a2f87a908fbe9f3e` retry returned outer + Source Edit `replayed=true` without reapplying the rename.
- BUG-AUDIT #120 and Faz 14B are closed. Canonical architecture is `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`; roadmap source-edit routing is Routing Contract v3.
- This living-doc closeout intentionally follows the exact-installed deploy snapshot above. Do not rebuild merely to embed a docs-only dirty fingerprint back into the same docs; the next code/runtime change should create the next normal canonical build/deploy fingerprint.
- User has not requested Git commit/push; none was performed.

## [SUPERSEDED — 2026-09-20] Source Edit Engine core + Routing Contract v2 + Structural adapter exact-installed GREEN

- Faz 14 / BUG-AUDIT #110 core implementation is complete. Canonical technical contract remains `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.
- Real starting state reverified: branch `main`, HEAD `747cbfc560c8f7e9d4c3def699ee986d5c164a71`, intentional dirty Playwright CLI migration/#109 tree preserved.
- Live Talvora reverified before coding: `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-ce803e6e4f95`, service PID 9312.
- Research decisions: LSP WorkspaceEdit is the ordered/versioned multi-file reference; AHP contributes sequencing/reconciliation but Talvora owns SHA-256 revisions; TxF rejected; durable WAL + same-volume staging + ReplaceFileW/rename/rollback selected.
- Source-edit routing is now deterministic: `talvora_apply_patch` is PRIMARY/default for ordinary source/config/repository-document work, one file or many; `talvora_apply_edits` is only for exact UTF-16 ranges/revisions already known or generated. `talvora_read_source` remains the revision handshake and new read-only `talvora_source_edit_guide` is the ambiguity resolver.
- Routing Contract v2 is emitted through MCP `ServerInstructions`, canonical tool Title/Description metadata, server-side policy errors, the guide tool, docs, and regression tests. Agent mutator trial-and-error is explicitly forbidden by the contract.
- `talvora_read_source` is required for the source revision handshake so stale-read protection is not left to a separate hash call.
- Patch DSL v1 uses Begin/End Talvora Patch + Add/Update/Delete/Move blocks, mandatory revision for existing files, exact unique hunks, no fuzzy/regex fallback.
- `talvora_apply_patch` also accepts unified-diff compatibility input through the same normalized transaction/WAL core. Unsupported Git-copy metadata routes back to the same tool's Talvora DSL rather than to another editor.
- SourceMutationPolicy blocks legacy write_text/replace_text/append_text/text-like write_bytes plus typed JSON/dotenv/INI/XML/YAML/TOML mutators for source/config targets inside recognized development workspaces. It now also guards direct source-file delete and same-workspace source-file move; directory/generated/binary/non-workspace and cross-workspace filesystem capability remains available. Generic PowerShell/process remains unrestricted.
- ast-grep structural backend is implemented and exact-installed. Canonical installer resolves the latest official Windows platform package, installs with scripts disabled into build staging, vendors only `ast-grep.exe` + provenance into the versioned Service payload, and runtime verifies version + executable SHA-256 before use. Current exact-installed package: `@ast-grep/cli-win32-x64-msvc 0.45.3`, MIT.
- `talvora_structural_edit` is the specialist route for repetitive AST/syntax-shaped transformations. It supports pattern/kind + rewrite and workspace-relative YAML rule/fix modes, runs ast-grep only as proposal producer against an isolated UTF-8 mirror, cross-checks Unicode-scalar positions + UTF-8 byte offsets + matched text, then commits exact revision/range edits through the same WAL/rollback/idempotency engine. ast-grep never writes the live workspace directly.
- Audit #110/#111/#112/#113/#114/#115/#116/#117/#118/#119 are fixed and exact-installed. Roslyn 5.9.0 remains the preferred later C# semantic adapter; DiffPlex 1.9.0 remains advisory three-way preview only.
- Dedicated Source Edit regression is 27/27 GREEN; Talvora Release build 0 warning / 0 error; NativeInstaller source regression GREEN; `git diff --check` clean apart from line-ending warnings.
- Exact-installed raw MCP gate is GREEN on `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-b55f88d972ca`: MCP 2026-07-28 `tools/list` reports 203 tools; `talvora_apply_patch` remains PRIMARY/default, `talvora_structural_edit` is the structural specialist, `talvora_apply_edits` remains exact-range specialist, and the read-only guide resolves ambiguity.
- Exact-installed ast-grep provenance is GREEN: executable SHA-256 `DAFF0F5963FAAB7617045833132A3538C85EEE65F3AFEEDF347F829A7B8D83FB` matches installer provenance. Live pattern/rewrite smoke correctly edited a TypeScript line containing an emoji; live YAML rule/fix smoke updated two matches; same structural transaction retry returned `replayed=true`.
- Live two-file ordinary change stayed on one `talvora_apply_patch` transaction, committed both files, and same-transaction retry returned `replayed=true`; source delete/same-workspace move routing guards and preserved cross-workspace move capability were also verified through direct MCP calls.
- Canonical installer/deploy sequence remains unchanged and #109 explicit `--silent` must be preserved.
- User has not requested commit/push; none will be performed.

## [COMPLETED — 2026-09-20] Roslyn semantic edit implementation plan

- Startup was reverified without mutation. Repo remains `main` at HEAD `747cbfc560c8f7e9d4c3def699ee986d5c164a71`; the intentional dirty Source Edit / Routing / Structural / Playwright CLI migration / #109 tree is preserved. Live Talvora currently reports `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-c39cc97f5d63`, so the old `...dirty-b55f88d972ca` fingerprint is a historical exact-installed snapshot, not the current runtime identity.
- Live routing was rechecked: PRIMARY/default remains `talvora_apply_patch`; `talvora_structural_edit` is AST/syntax specialist; `talvora_apply_edits` is exact generated-range specialist; `talvora_source_edit_guide` forbids mutator trial-and-error.
- Current local toolchain: .NET SDK 10.0.401 / dotnet MSBuild 18.9.11; Visual Studio Build Tools 17.14.37710.0 is installed and complete. Semantic implementation must report the actual Roslyn/MSBuild workspace identity it uses rather than assuming one from this machine inventory.
- Context7 `/dotnet/roslyn` plus current Microsoft/Roslyn sources were revalidated before coding. Current stable `Microsoft.CodeAnalysis.Workspaces.MSBuild` / C# Workspaces line is 5.9.0. Current rename API is `Renamer.RenameSymbolAsync(Solution, ISymbol, SymbolRenameOptions, string, CancellationToken)` and returns a changed in-memory `Solution`. `Workspace.WorkspaceFailed` is obsolete; use `RegisterWorkspaceFailedHandler`.
- Microsoft MSBuild guidance requires `MSBuildLocator` registration before MSBuild types are used. The semantic adapter will therefore bootstrap locator once, deterministically record the selected compatible instance, then create a disposable `MSBuildWorkspace` per semantic request.
- First capability is intentionally narrow: solution/project-aware C# symbol rename. It is not a generic C# editor. Ordinary C# edits continue on PRIMARY `talvora_apply_patch`.
- External semantic request: workspace root + transactionId + explicit solution/project path + source document path + zero-based UTF-16 line/character anchor + expected SHA-256 anchor revision + new name; optional project selector disambiguates linked/multi-project contexts; Roslyn rename options are explicit and conservative by default.
- Symbol lookup is semantic, not textual: resolve the anchored document through SemanticModel/SymbolFinder across relevant project contexts. Missing symbol or divergent linked-file identities rejects before mutation. No global search/replace fallback exists.
- Roslyn only proposes. It never calls `TryApplyChanges` and never writes the live repository. `Solution.GetChanges` / changed documents are reduced to exact UTF-16 text changes with expected old text, then fed to the existing Source Edit transaction engine.
- The adapter will use `SourceEditEngine.ApplyGeneratedEditsAsync`, with a semantic external request hash. This preserves the existing durable receipt-first replay behavior: a lost-response retry can return `replayed=true` without reloading Roslyn.
- Before proposal acceptance, Roslyn's old document text must exactly equal the revisioned Talvora source snapshot. Changed target files receive their own SHA-256 expected revisions. The core revalidates targets again at commit barrier; stale/concurrent writer therefore produces zero committed mutation.
- Encoding/BOM/newline stays owned by the existing Source Edit codec. Rename will not automatically run Formatter/Simplifier; only Roslyn's actual rename text changes are committed, preventing formatting churn.
- Workspace/project load problems are captured through current workspace-failure registration + diagnostics/progress. Stable semantic domain codes and a structured receipt will expose MSBuild identity, symbol identity, bounded diagnostics and the underlying transaction result. Hidden partial success is forbidden.
- Resource behavior is bounded and cancellation-aware. Per-call workspace state is disposed; diagnostic/document/change counts and total changed text are bounded through caller-visible limits instead of unbounded service state.
- Routing Contract v3 in this phase must update MCP ServerInstructions, semantic tool Title/Description, `talvora_source_edit_guide`, tool manifest, regression contracts and canonical docs so the agent chooses semantic only for real symbol identity operations.
- Targeted test scope: solution-wide cross-document rename; no-symbol/ambiguity; stale anchor/target; invalid load diagnostics; cancellation; linked document consistency; minimal text/no formatter churn; encoding/newline preservation; durable same-transaction replay that does not rerun semantic generation; routing selection.
- Final gate remains: targeted semantic/source-edit tests -> Talvora Release 0/0 -> only necessary regressions -> `git diff --check` -> canonical Build-Windows-Installer -> canonical Deploy-Windows-Installer preserving #109 explicit `--silent` -> exact-installed system_info -> raw tools/list/routing smoke -> real solution-aware semantic live smoke -> same transaction retry `replayed=true` -> living docs closeout.
- No commit/push unless explicitly requested.

## [SUPERSEDED — 2026-09-20] NEXT SESSION — Roslyn semantic editing subphase

Continue directly from the exact-installed Source Edit + Routing + Structural state above. Do not redesign or replace the completed core.

### Mandatory startup verification

1. Read this CURRENT section first, then `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`, Faz 14 in `MCP-CONTROL-CENTER-TODO.md`, `BUG-AUDIT.md` #110-#119, and the canonical routing section in `APPLICATION-DEVELOPMENT-ROADMAP.md`.
2. Reverify real repo state with Talvora Git status/log and reverify live `talvora_system_info`. Preserve the intentionally dirty tree; never reset/revert/overwrite existing Playwright CLI migration, #109, Source Edit, Routing or Structural work.
3. Reverify current Roslyn/.NET APIs with Context7 + current official Microsoft documentation before dependency/API decisions. Do not rely on the old planning snapshot alone.
4. Do not commit or push unless the user explicitly asks.

### Non-negotiable routing contract

- `talvora_apply_patch` remains the PRIMARY/default source editor for ordinary development work. Do not weaken, narrow or demote it.
- `talvora_structural_edit` remains the specialist only for repetitive syntax/AST-shaped transformations.
- `talvora_apply_edits` remains the specialist only when exact zero-based UTF-16 ranges/revisions already exist or are generated programmatically.
- The next semantic tool is only for operations that genuinely require C# symbol/semantic identity such as solution-aware rename/refactor. Multi-file scope alone is never a reason to choose it.
- If tool choice is ambiguous, routing metadata/guide must resolve it before mutation; do not discover the right editor through trial-and-error.
- PowerShell/process remain unrestricted TAM YETKI explicit-admin surfaces, but are not normal source editors.

### Next engineering target

Design and implement a commercial-quality Roslyn-backed C# semantic proposal adapter that feeds the existing Source Edit transaction core rather than writing the live workspace directly.

Required design goals:
- Load project/solution/workspace deterministically and report explicit diagnostics when MSBuild/project loading is incomplete.
- Resolve symbols by stable semantic identity; detect and reject ambiguous/no-symbol cases.
- First capability should be a genuinely semantic operation such as solution-aware symbol rename, not a duplicate text/structural replacement feature.
- Generate changed-document proposals in memory, preserve trivia/formatting as far as Roslyn permits, and convert only the actual changed documents into normalized exact Source Edit changes.
- Re-read/revalidate target files with SHA-256 revisions before commit; stale/concurrent writer => zero mutation.
- Commit through existing WAL/idempotency/rollback/recovery engine. Roslyn must always enter the transaction core.
- Stable domain errors, cancellation, bounded resource behavior, deterministic output, structured receipts, and no hidden partial success.
- Update MCP ServerInstructions, tool Title/Description, `talvora_source_edit_guide`, routing regression and docs so the agent knows exactly when semantic editing is appropriate.
- Keep the semantic surface narrow enough that it does not compete with PRIMARY `talvora_apply_patch` for normal edits.

### Validation discipline

- Follow the existing rule: test only changed scope first; do not repeatedly rerun unchanged broad suites.
- Expected targeted gates: Talvora Release 0/0, Source Edit/Semantic targeted regressions, `git diff --check`, then canonical installer build -> canonical deploy -> exact-installed `talvora_system_info` -> raw MCP tools/list/routing/semantic live smoke.
- Preserve canonical installer behavior including #109 explicit `--silent`.
- Any new external dependency/provisioning must be current-version researched, provenance-aware and compatible with the existing installer/runtime model.

### Verified starting baseline

- Branch/HEAD: `main` / `747cbfc560c8f7e9d4c3def699ee986d5c164a71`.
- Exact-installed sourceCommit: `747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-b55f88d972ca`.
- MCP tools/list: 203.
- Source Edit regression: 27/27 GREEN.
- Talvora Release build: 0 warning / 0 error.
- NativeInstaller source regression: GREEN.
- ast-grep exact-installed: 0.45.3 / MIT; executable SHA-256 `DAFF0F5963FAAB7617045833132A3538C85EEE65F3AFEEDF347F829A7B8D83FB`.
- Live structural pattern/rewrite + YAML rule/fix + durable replay: GREEN.
- No commit/push has been requested or performed for this work.

## [SUPERSEDED — 2026-09-20] Source Edit Transaction Engine planning handoff

**Bu bölüm tarihsel planlama handoff'udur. Güncel gerçek için yukarıdaki Source Edit Engine core implemented / live GREEN bölümü esas alınır.**

- Canonical architecture handoff: `C:\Users\tayla\Talvora-MCP\SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.
- Canonical TODO: `MCP-CONTROL-CENTER-TODO.md` / Faz 14.
- Canonical audit item: `BUG-AUDIT.md` / #110 OPEN.
- Application roadmap artık canonical source-edit routing contract'ını içerir.
- İlk kaba `git apply` wrapper fikri **geçersiz/superseded**. Git unified diff yalnız compatibility backend olarak değerlendirilecek.
- Araştırma yönü: agent-friendly custom patch DSL + LSP WorkspaceEdit/AHP Changeset ilkelerinden esinlenen ortak transaction core; revision/hash optimistic concurrency; idempotent transactionId/journal; multi-file all-or-nothing; atomic commit/rollback; structured receipts/errors.
- Structural editing için ast-grep, C# semantic editing için Roslyn, conflict/three-way preview için DiffPlex, polyglot syntax validation için Tree-sitter araştırma adaylarıdır. Yeni oturumda güncel Context7 + resmî kaynaklarla tekrar doğrulanmadan dependency kararı verme.
- Fuzzy matching yalnız candidate/diagnostic için kullanılacak; fuzzy-only otomatik source mutation yasak.
- Agent tool selection deneme-yanılmaya bırakılmayacak. `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindeki routing contract tool descriptions + server policy + regression tests ile enforce edilecek.
- Development workspace içindeki legacy `write_text` / `replace_text` / `append_text` / text-like `write_bytes` yolları için `SourceMutationPolicy` planlanacak; canonical edit araçlarına yönlendiren `SOURCE_EDIT_POLICY_VIOLATION` üretilecek.
- Unrestricted PowerShell/process capability TAM YETKI gereği korunacak; fakat normal source editing fallback'i olarak kullanılmayacak ve tool açıklamalarında bu açıkça belirtilecek.
- Yeni oturumda önce ayrıntılı implementation plan + exact schemas/data model/journal/atomicity/rollback/workspace classification/test matrix hazırlanıp TODO/HANDOFF'a işlenecek. Ardından soru beklemeden kodlamaya geçilecek.
- Mevcut dirty working tree korunacak; Playwright MCP -> CLI migration ve #109 değişikliklerini resetleme/ezme.
- Current repository HEAD: `747cbfc560c8f7e9d4c3def699ee986d5c164a71` (`main`).
- Current live Talvora sourceCommit: `747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-ce803e6e4f95`; service PID 9312.
- Kullanıcı açıkça istemeden commit/push YOK.

## 2026-09-20 CURRENT — Official Playwright CLI / Playwright MCP removed

**Bu bölüm Playwright ile ilgili aşağıdaki tüm eski MCP kurulum/hardening notlarının yerine geçer.** Eski Playwright MCP bölümleri yalnız tarihsel kayıt olarak okunmalıdır.

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch/HEAD baseline: `main` / `747cbfc560c8f7e9d4c3def699ee986d5c164a71`
- Working tree: Playwright MCP -> CLI migration + deploy-helper #109 düzeltmeleri nedeniyle kasıtlı dirty; kullanıcı açıkça istemeden commit/push yapılmayacak.
- Canlı Talvora `sourceCommit`: `747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-ce803e6e4f95`
- Canlı version root: `C:\Program Files\Talvora\Versions\747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-ce803e6e4f95-20260919225335895`
- Service PID: **9312**
- Tray PID: **2264**
- Canonical deploy task son sonuç: `LastTaskResult=0`; installer process count 0.
- #109: `scripts\Deploy-Windows-Installer.ps1` bağımsız SYSTEM task action'ında artık explicit `--silent` kullanır. Session 0 görünmez GUI hang'i regression ile engellenmiştir.
- Official Playwright CLI current-user global kurulum: `playwright-cli 0.1.21`
- CLI command: `C:\Users\tayla\AppData\Roaming\npm\playwright-cli.cmd`
- Bağımsız persistent profile: `C:\Users\tayla\AppData\Local\PlaywrightCLI\profile`
- Playwright CLI **Talvora tarafından kurulmaz/yönetilmez**; roadmap kuralı `Do not add Playwright to Talvora` yeniden sağlandı.
- Talvora installer, Tray managed-MCP discovery/registry ownership, installer payload, Playwright supervisor/proxy assetleri ve Playwright-specific smoke harness artık Playwright MCP kurmaz/yönetmez.
- Eski `Talvora Playwright MCP` Scheduled Task: **yok**.
- Eski `C:\Users\tayla\AppData\Local\Talvora\PlaywrightMCP` root: **yok**.
- Eski `playwright-business` / `@playwright\mcp` process sayısı: **0**.
- Port 8931/8932 listener: **0**.
- Global current-user `@playwright` npm scope: yalnız **cli**; global MCP paketi yok.
- Control Center managed registry/recovery/ownership seti: yalnız **gitea + talvora**; `playwright` kaydı yok.
- CLI full representative smoke GREEN: persistent named session, navigate, snapshot/find, run-code, requests/console, tabs, tracing, screenshot, PDF ve close.
- Eski Talvora Playwright root silindikten sonra bağımsızlık smoke'u tekrar yapıldı: CLI 0.1.21 + migrated profile ile Chrome open -> snapshot -> close **GREEN**.
- Native installer source regression: **GREEN**; `PlaywrightMcpRemovedFromTalvora=True`.
- Tray + Talvora.Smoke Release build: **0 warning / 0 error**.
- Canonical installer build: **GREEN**, artifact SHA256 `693A83454C1771FB91C1FE6BED8ABFADA07DA9B71DBC6F2583AE0847806C43FF`.
- Canonical exact-installed deploy: **GREEN**; yeni install logunda 82% Tray aşamasından sonra doğrudan 90% cleanup'a geçiliyor, Playwright MCP kurulum aşaması yok.
- `Deploy-Windows-Installer.ps1` ve kapanış dokümanları runtime payload dışıdır; #109 düzeltmesi source regression + canlı launcher doğrulamasıyla kapatıldı, runtime fingerprint yukarıdaki exact-installed değerdir.
- GitHub remote işlemleri yalnız Talvora `talvora_git_run` GitHub-aware yolu ile yapılacak; kullanıcı açıkça istemeden commit/push YOK.

## [TARİHSEL — 2026-09-19] Oturum kapanış snapshot — final doğrulanmış durum

Bu bölüm 2026-09-19 tarihli tarihsel snapshot'tır; güncel gerçek için yukarıdaki **2026-09-20 CURRENT** bölümü esas alınacaktır.

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Son canlı runtime `SourceCommit`: `74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5`
- Repository HEAD (dokümantasyon dahil): `74601595e4727d1cfd09388ae3c8b4d25dc136fa`
- Repository HEAD subject: `docs: redesign GitHub project presentation`
- Primary remote: `origin = ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git`
- Secondary backup remote: `github = https://github.com/bingoweb/Talvora-MCP.git`
- `origin/main` ve `github/main`: `74601595e4727d1cfd09388ae3c8b4d25dc136fa` olarak doğrulandı.
- 2026-09-19 GitHub sunum batch'i yalnız dokümantasyon/README değişikliğidir; runtime/source değişikliği değildir ve yeniden deploy gerektirmez.
- Kök `README.md`, GitHub ziyaretçisinin Talvora'yı hızlı anlaması için modern hero/badge, Mermaid mimari, capability matrix, quick-start ve proje haritası yapısına dönüştürüldü.
- Talvora Control Center Faz 0-11 tamamlandı; ürün, recovery, event/log, tunnel provisioning, first-run ve final doğrulama kapıları kapandı.
- `MCP-CONTROL-CENTER-TODO.md` kanonik uygulama planıdır; yeni oturumda mevcut tamamlanma noktasından devam edilecektir.
- Control Center kaynak/doküman değişiklikleri çalışma ağacında kasıtlı olarak uncommitted durumdadır; kullanıcı açıkça istemeden commit/push yapılmayacak.
- Yeni oturum başlangıcında gerçek repo HEAD/working-tree durumu `git status` / `git log -1` ile tekrar doğrulanacak.

### Canlı Talvora

- `talvora_system_info.sourceCommit`: `74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5`
- Service PID: **1872**
- Tray PID: **2356**
- Service root:
  `C:\Program Files\Talvora\Versions\74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5-20260919183843581\Service`
- Tray root:
  `C:\Program Files\Talvora\Versions\74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5-20260919183843581\Tray`
- Service ve Tray aynı dirty-fingerprint version root'tan çalışıyor.
- Canonical tool manifest: **198**
- Son Control Center exact-installed visual smoke: **GREEN**.
- Final exact-deployed **198-tool MCP smoke GREEN** (`TALVORA MCP SMOKE GREEN`, exit 0). Bu runtime kaynak kodu değişmedikçe tekrar çalıştırma.
- Runtime değişikliği yapılmadıkça full smoke'u tekrar etme.

### Gitea / Caddy / resmî Gitea MCP

- Gitea: **1.27.3**
- Browser URL: `http://127.0.0.1:3000/`
- Backend: `http://127.0.0.1:3001/`
- Caddy: **2.11.4**, localhost passwordless SSO proxy.
- Resmî Gitea MCP: **1.7.0**
- Gitea MCP endpoint: `http://127.0.0.1:8081/mcp`
- Gitea MCP araç sayısı: **55**, write yetenekleri açık; özellik kırpma yok.
- Secure MCP Tunnel ID: `tunnel_6aae935c5da0819193da4a5160b81aaa`
- Runtime alias: `gitea-business`
- Tunnel target: `http://127.0.0.1:8081/mcp`
- Final tunnel state: **process_running=true, healthy=true, ready=true**
- Watchdog task: `Gitea MCP Tunnel` -> **Running**
- Watchdog PowerShell 7 action'ında `-WindowStyle Hidden` vardır.
- Kullanıcı oturumunda görünür PowerShell watchdog penceresi: **0**
- Watchdog runtime prosesini foreground gözetler; runtime beklenmedik kapanırsa task hata ile çıkar ve Task Scheduler 1 dakika aralıkla yeniden dener.
- Task execution time limit: unlimited.

### [TARİHSEL / ARTIK GEÇERSİZ] Ayrı Gitea tray ikonu

- Talvora ikonundan bağımsız ikinci Gitea `NotifyIcon` vardır.
- Çift tıklama: Gitea browser UI açılır.
- Menü:
  - canlı durum
  - `Gitea'yı Aç`
  - `Gitea'yı Yeniden Başlat`
  - `Durumu Yenile`
- Health iki katmanlı:
  - backend `3001/api/healthz`
  - browser/SSO proxy `3000/api/healthz`
- Backend sağlıklı fakat Caddy/proxy bozuksa ikon yanlışlıkla yeşil kalmaz; **degraded/sarı** durum gösterir.
- Restart akışı LocalSystem Talvora MCP üzerinden:
  Caddy process stop -> SCM Stopped bekle -> Gitea restart/start -> Caddy start -> health doğrulama.
- Installed Tray üzerinden `--gitea-status` ve `--gitea-restart`: **GREEN**
- Restart sonrası Gitea backend/proxy/MCP: **HTTP 200**
- Restart sonrası Secure MCP Tunnel: **running/healthy/ready**

### Son bug/optimizasyon kapanışı

Context7 + resmî doküman doğrulamasıyla son Gitea/Tray denetiminde düzeltilenler:
- Gitea status yalnız backend'i kontrol ediyordu; proxy failure artık degraded olarak görülüyor.
- MCP restart yolundaki gereksiz `tools/list` kaldırıldı; doğrudan `McpClient.CallToolAsync` kullanılıyor.
- Manuel oluşturulan `HttpClientTransport` artık async dispose ediliyor.
- Tray shutdown sırasında async status/restart ile `SemaphoreSlim.Dispose` yarış riski kaldırıldı; lifetime cancellation ile güvenli unwind yapılıyor.
- Secure MCP Tunnel task'in connect sonrası bitmesi nedeniyle runtime'ın sonradan durması düzeltildi; watchdog artık runtime prosesini canlı tutuyor.
- Watchdog terminal penceresinin açık kalması `-WindowStyle Hidden` ile düzeltildi.
- Tray dependency vulnerability taraması: **0 vulnerable package**.
- `ModelContextProtocol` kullanılan sürüm: **2.2.0**.
- Statik TODO/FIXME/HACK/blocking taraması: temiz.
- Native installer source regression: GREEN.
- ProcessRunner regression: GREEN.
- `git diff --check`: GREEN.
- Final exact deployed 198-tool MCP smoke: GREEN.

### Talvora Control Center — planlama tamamlandı

Kullanıcı MCP sayısının hızla artması nedeniyle merkezi bir yönetim UI istedi. Kodlamaya geçmeden önce soru-cevapla ürün kararları netleştirildi. Ayrıntılı yaşayan plan:
`C:\Users\tayla\Talvora-MCP\MCP-CONTROL-CENTER-TODO.md`

Kesinleşen ürün/mimari kararları:
- Ürün adı: **Talvora Control Center**.
- Tamamen Windows-native.
- **C# + WPF**, mevcut .NET 10 Windows hedefi.
- Mevcut Talvora Tray ile **tek uygulama / tek EXE**.
- WPF + mevcut WinForms tray aynı kullanıcı prosesinde yaşayacak.
- Temel WPF + **WPF-UI 4.3.0** kullanılıyor; ancak görünüm kütüphane demosu gibi bırakılmıyor. Fluent altyapısı Talvora'ya özgü koyu ürün tokenları, sıkı spacing/type ramp ve kontrollü etkileşimlerle özelleştiriliyor.
- Varsayılan görünüm koyu, modern developer dashboard; çok hafif premium control-room dokunuşları.
- UI tamamen Türkçe.
- Teknik terimler kullanıcıya dönük yerde Türkçe + gerektiğinde İngilizce teknik karşılığı parantez içinde.
- Tek Talvora tray ikonu:
  - sol tık -> Control Center
  - sağ tık -> MCP bazlı hızlı işlemler
  - ikon -> genel sistem sağlığı
- Ayrı Gitea tray ikonu yeni mimaride kaldırılacak/birleştirilecek.
- Control Center yalnız **bizim bu Windows makinesine kurduğumuz/yönettiğimiz yerel MCP'leri** kapsar.
- Yeni yönetilen MCP'ler **tam otomatik keşfedilir ve sahiplenilir**; kullanıcıdan ekleme/onay beklenmez.
- Keşif modeli hibrit:
  - primary: Talvora managed-MCP merkezi registry
  - recovery: bilinen servis/process/tunnel/config kurulum izleri
  - kayıt kaybolursa otomatik onarım.
- Ana ekran: tek Home Dashboard; derin navigation/menü yok.
- Üstte sade sağlık cümlesi + küçük teknik özet.
- Responsive MCP kartları; arama kolay erişilebilir; filtreler yalnız gerekince; sorunlu MCP'ler üste.
- Kartlarda resmi logo varsa kullan; yoksa Talvora fallback MCP ikonu.
- Kartta yalnız durum + kısa açıklama + tek bağlamsal ana eylem; teknik ayrıntılar detail'de.
- Detail: sekmesiz tek sayfa + açılır gelişmiş bölümler.
- Gitea gibi çok bileşenli MCP ana ekranda tek kart; detail'de health chain.
- CPU/RAM/süreç sayısı dashboard kapsamı dışında.
- MCP tools/list / Inspector / tool execution UI **yok**.
- Config düzenleme **yok**.
- MCP uninstall **yok**.
- MCP update kurma **yok**; yalnız sürüm bilgisi gösterilebilir.
- Ayrı Ayarlar sayfası **yok**.
- İlk kullanımda wizard'a kilitleme yok; dashboard açılır, kritik eksik varsa `Kurulumu Tamamla` kartı.
- Yetkili işlemler mevcut **Talvora LocalSystem MCP servisi** üzerinden; UI normal kullanıcı.
- Windows açılışında Control Center tray'de sessiz başlar ve tüm yönetilen MCP + gerekli tunnel'ları ayağa kaldırır.
- Ciddi/kullanıcı müdahalesi isteyen sorun varsa pencere otomatik açılabilir; küçük/geçici sorunlarda açılmaz.
- Kullanıcı `Durdur` derse yerel MCP + tunnel birlikte durur; onay istenir.
- Elle durdurulan MCP o oturum boyunca auto-recovery tarafından yeniden başlatılmaz.
- Sonraki Windows boot'ta tüm yönetilen MCP'ler tekrar otomatik başlar.
- `Yeniden Başlat` onay istemeden çalışır; sonunda health doğrulaması yapılır.
- Recovery: bilinen/güvenli sorunları otomatik düzelt; belirsiz/riskli durumda kullanıcıya anlaşılır öneri.
- Bildirimler düşük gürültülü: küçük/kendi kendine düzelen olaylar sessiz; önemli/tekrarlayan hata bildirilir.
- Son olaylar: ana dashboard'da küçük özet, aynı pencere içinde genişleyen panel.
- Event retention akıllı: info kısa, warning/error daha uzun, tekrarlar gruplanır, otomatik temizlik.
- Canlı log gelişmiş panelde; arama/filtre/kopya; ham üçüncü taraf logu çevrilmeden gösterilir.
- Yeni managed MCP'de Secure MCP Tunnel yoksa **otomatik oluşturulacak**.
- Runtime key ile Admin key rolleri ayrı:
  - runtime key mevcut DPAPI modeli
  - Admin API key ilk gerektiğinde bir kez alınır, ayrı current-user DPAPI ile saklanır
  - secret değerler UI/log'a açık yazılmaz.
- Pencere X -> yalnız UI kapanır/gizlenir; tray devam eder.
- Tam çıkış -> tray menüsünden.
- Pencere son boyut/konum/maximized durumunu hatırlar; off-screen/multi-monitor recovery yapar.
- UI/UX önceliği: acemi kullanıcı menülerde kaybolmamalı, bir sonraki adım açık olmalı.

Control Center implementasyonu tamamlandı. Faz 0-11 kapalıdır; dashboard/detail, recovery, Faz 7 olay/log, Faz 8 Secure MCP Tunnel provisioning, Faz 9 first-run/incomplete setup, Faz 10 pencere UX ve Faz 11 exact-deployed verification tamamlandı.

2026-09-19 canlı durum:
- Kullanıcı Material Design yönünü beğenmeyip önceki Wpf.Ui/Fluent tabanına dönülmesini istedi.
- UI tabanı: WPF + **WPF-UI 4.3.0**; `ThemesDictionary` + `ControlsDictionary` ve explicit Dark theme uygulanıyor.
- Görsel yön: ticari kalite hedefli koyu grafit Fluent yüzey; tek soğuk mavi vurgu, 8/12/16 tabanlı spacing, Segoe UI Variable Text, ince border, düşük elevation, Wpf.Ui SymbolIcon/TextBox/Button kullanımı, küçük durum pill'leri ve dengeli kart iç boşlukları.
- MaterialDesignThemes / PackIcon / ElevationAssist / HintAssist kaynak referansı artık **0**.
- NuGet vulnerability taraması WPF-UI 4.3.0 dahil mevcut paketlerde bilinen güvenlik açığı bulmadı.
- Ayrı Gitea tray ikonu kaldırıldı; tek Talvora tray aktif.
- Managed-MCP registry Talvora + Gitea için çalışıyor ve bozuk registry recovery testi GREEN.
- Dashboard + aynı-pencere detail navigation + component health/version alanları çalışıyor. Kartlarda bağlamsal ana eylem; detail'de Start/Stop/Restart, stop confirmation, işlem sonucu banner'ı ve varsayılan kapalı `CardExpander` teknik ayrıntıları tamamlandı.
- Gerçek `Wpf.Ui.Controls.TitleBar` eklendi: pencere sürüklenebilir; küçült/büyüt/kapat caption düğmeleri görünür. X yalnız pencereyi gizler, tray çalışmaya devam eder.
- Pencere boyut/konum/maximized durumu kalıcıdır; ekran dışı/multi-monitor recovery, responsive dar-geniş header, DPI kontrolü ve Ctrl+F / Ctrl+R / F5 / Esc / Alt+Left klavye akışları tamamlandı.
- Beyaz/boş pencere regresyonu visual smoke ile kapatıldı.
- Smoke penceresi artık off-screen çalışıyor; kullanıcı masaüstünde test penceresi görmüyor.
- Son canlı deploy sourceCommit: **74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5**.
- Son doğrulanan Service PID: **1872**.
- Son doğrulanan Tray PID: **2356**.
- Kurulu version root: `C:\Program Files\Talvora\Versions\74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5-20260919183843581`.
- Installed Control Center visual smoke: GREEN (exact deployed Tray; TitleBar/caption controls, DPI, dashboard/detail/technical panel, X-hide/reopen doğrulandı).
- Installed `--self-test`: GREEN; managed-MCP registry + Faz 6 backoff/serious-incident policy contract doğrulandı.
- Faz 6 canlı fault injection: `Gitea MCP Tunnel` görevi elle durduruldu; hiçbir manuel restart verilmeden **26,5 saniyede** otomatik recovery tamamlandı. Tunnel + MCP Server görevleri Running; backend 3001, proxy 3000, MCP 8081, tunnel `/healthz` ve `/readyz` sağlıklı. Log: `Automatic Gitea MCP recovery succeeded.`
- Exact-installed Gitea full-chain restart: GREEN; Gitea/Caddy Running, backend/proxy/MCP HTTP 200, tunnel `/healthz` + `/readyz` HTTP 200.
- Talvora standard-user service lifecycle regression: `MSI\tayla`, stop exit 0 -> Stopped -> start exit 0 -> Running -> `/healthz` 200.
- Talvora tunnel lifecycle regression: stop exit 0 -> reconnect exit 0 -> tunnel health/ready true.
- Talvora service `CanStop=true`; installer install-user SID'e sınırlı service query/start/stop/interrogate DACL ekliyor.
- Gitea/Caddy stop zinciri `sc.exe stop` + 4 saniye bounded grace + PID force fallback kullanıyor; `StopPending` takılması kapatıldı.
- Final exact deployed full **198-tool MCP smoke GREEN**; bu runtime için yalnız bir kez çalıştırıldı ve TODO final gate kapatıldı.
- Faz 7 structured events/logs tamam: current-user `events.json`, Info=3 gün / Warning=14 gün / Error=30 gün retention, 6 saat dedup, 500 kayıt hard cap, dashboard event summary, expandable event panel ve bounded canlı raw log viewer.
- Faz 8 tunnel provisioning tamam: `tunnel-client v0.0.14`; Runtime/Admin credential ayrımı; current-user DPAPI; mevcut Runtime key reuse; mevcut tunnel metadata'sından 1 organization + 1 workspace scope keşfi; missing tunnel için admin CRUD + profile generation + health/ready doğrulaması. Bu makinede iki tunnel zaten mevcut olduğu için Admin credential dosyası yok ve kullanıcıdan Admin key istenmiyor.
- Faz 9 first-run/incomplete setup tamam: dashboard-first açılış ve yalnız gerçekten eksik bilgi varsa tek `Kurulumu Tamamla` kartı; ayrı Settings sayfası yok.
- Canonical installer SHA256: `BEF881E6C1E20B871661345394715A7B6E92C9CBD4B1D39573AC444C1AF6CC54`.
- Exact-installed CLI regression: self-test, Control Center smoke, tunnel provisioning preflight, Talvora reconnect, Gitea status ve Gitea restart GREEN.
- Native installer runtime regression: `NATIVE_INSTALLER_RUNTIME_GREEN`; `ServiceCanStop=True`, `ServiceCanPauseAndContinue=False`, ToolCount=198.
- Interactive tray mutex regression GREEN: ikinci `--replace` instance doğru şekilde geri çekildi; canlı Tray PID **2356** olarak sabit kaldı.
- Smoke placement isolation doğrulandı: off-screen visual smoke gerçek `window-placement.json` dosyasının hash'ini değiştirmiyor.
- Kullanıcı açıkça istemediği için Control Center çalışma ağacı commit/push edilmedi.
- Kullanıcı açıkça istemediği için commit/push yapılmadı.

Yeni oturumda bu kararlar **tekrar sorulmayacak**. Önce bu HANDOFF ve `MCP-CONTROL-CENTER-TODO.md` okunacak; TODO'daki mevcut tamamlanma işaretlerinden devam edilecek.

### Yeni oturumda ilk yapılacaklar

1. Önce bu `HANDOFF.md` dosyasını oku.
2. Hemen ardından `MCP-CONTROL-CENTER-TODO.md` dosyasını baştan sona oku; ürün kararlarını tekrar sorma.
3. Talvora MCP araçlarını keşfet ve `talvora_system_info` ile bağlantıyı doğrula.
4. Talvora git araçlarıyla `main`, HEAD, remotes ve çalışma ağacını doğrula. Planlama sonunda beklenen docs-only değişiklikler `HANDOFF.md` + yeni `MCP-CONTROL-CENTER-TODO.md` olabilir; bunları kaybetme veya kullanıcı istemeden commit/push yapma.
5. Gerekmedikçe yukarıdaki full testleri tekrar çalışma.
6. Context7 + resmi dokümanla WPF/WPF-UI ve OpenAI tunnel-client güncel durumunu doğrula; ardından TODO'daki mevcut tamamlanma noktasından **Talvora Control Center implementasyonuna otomatik devam et**.
7. Runtime kodu değişirse zorunlu canlı-update zinciri uygulanacak:
   source -> targeted test -> canonical installer -> deploy -> PID/version root -> live behavior -> gerektiğinde SHA256.
8. Geliştirme sırasında yalnız değişen alanları hedefli test et; final exact deployed full smoke en fazla bir kez.
9. Bulunan hata/riskleri mevcut `BUG-AUDIT.md` yaşayan envanterine anında kaydet.


## Zorunlu ve ihlal edilemez proje kuralları

Aşağıdaki maddeler tavsiye değil, **kesin proje kurallarıdır**. Yeni oturumlarda, refactorlarda, hata düzeltmelerinde ve yeni özellik eklerken aynen uygulanacaktır.

1. **Her runtime değişikliğinden sonra çalışan Talvora prosesleri mutlaka güncellenecek.**
   - `src/`, Talvora.Shared, Talvora service, Tray, installer veya runtime davranışını etkileyen build girdilerinde değişiklik yapıldıysa yalnız kaynak kodu değiştirmek yeterli değildir.
   - Zorunlu sıra:
     `source change -> targeted build/test -> canonical installer build -> live deploy -> talvora_system_info -> service/Tray PID + version root doğrulaması -> ilgili canlı davranış testi`.
   - Canonical live deploy Talvora MCP üzerinden başlatılıyorsa installer doğrudan Talvora LocalSystem servisinin child process'i olarak çalıştırılmayacak; `scripts\Deploy-Windows-Installer.ps1` ile bağımsız `SYSTEM` Task Scheduler instance'ı kullanılacak. Bu kural self-update sırasında servis process tree'sinin installer yaşam döngüsünü bozmasını engeller.
   - Final doğrulamada gerekiyorsa current source publish edilip kurulu `Talvora.dll`, `Talvora.Shared.dll` ve `Talvora.Tray.exe` ile SHA256 karşılaştırması yapılacak.
   - Eski/stale binary ile geliştirmeye devam edilmeyecek.
   - Çalışan servis/Tray güncellenmeden bir runtime değişikliği “tamamlandı” kabul edilmeyecek.

2. **Context7 kullanımı zorunludur.**
   - Yeni framework, kütüphane, SDK, CLI, API, runtime veya yazılım geliştirme yeteneği eklenirken/değiştirilirken önce Context7 kullanılacak.
   - Context7 çıktısı mümkün olduğunda resmi üretici dokümantasyonu ile birlikte değerlendirilecek.
   - Eski hafızaya veya deprecated kullanım örneklerine dayanarak API/CLI tasarlanmayacak.
   - Sürüm, API veya davranış güncelliği önemliyse Context7 ve resmi doküman doğrulanmadan implementasyon yapılmayacak.

3. **Modern mimari ve güncel stabil teknoloji zorunludur.**
   - Her zaman güncel, desteklenen ve modern yaklaşım tercih edilecek.
   - Deprecated CLI, eski SDK, legacy API veya uyumluluk katmanı yalnız “eski sistem desteklensin” diye tutulmayacak.
   - Yeni ve güncel teknoloji mevcutsa eski teknoloji korunmayacak.
   - Project-local wrapper/toolchain pin kullanılabilir; fakat deprecated teknolojiye fallback eklenmeyecek.
   - Modern async, cancellation, structured diagnostics, deterministic build/provenance ve atomic persistence yaklaşımları tercih edilecek.

4. **Özellik kırpma veya yapay yetenek kısıtlaması yasaktır.**
   - Talvora yalnız kullanıcının kendi Windows bilgisayarında çalışan kişisel geliştirme MCP'sidir.
   - Güvenlik/gizlilik kolaylığı adına path, command, host, package, user, session, repository veya geliştirme yeteneği yapay olarak kısıtlanmayacak.
   - Refactor veya bug fix sırasında mevcut yetenek sessizce azaltılmayacak.
   - Mümkün olan yerlerde performans, gözlemlenebilirlik, hata raporlama ve geliştirici ergonomisi artırılacak.

5. **Windows-native mimari korunacaktır.**
   - Talvora'nın ana çalışma ortamı Windows'tur.
   - Windows Service, LocalSystem, interactive user session, registry, Windows SDK ve native installer davranışları birinci sınıf desteklenecek.
   - Installer ve MCP session araçları ortak WindowsSessionLauncher altyapısını kullanmaya devam edecek; paralel/tekrarlı session-launch stack oluşturulmayacak.

6. **Paket yönetiminde Chocolatey kullanılacak; WinGet kullanılmayacak.**
   - Yeni sistem paketi kurulumlarında Chocolatey tercih edilecek.
   - WinGet Talvora production/bootstrap yollarında kullanılmayacak.
   - Chocolatey paketi güncel stabil sürümün gerisindeyse resmi vendor installer/package tercih edilebilir; eski paketi sırf Chocolatey'de var diye kurmak zorunlu değildir.

7. **En yeni uygun stabil sürüm kullanılacaktır.**
   - Yeni kurulumlarda veya yeni yetenek eklerken mevcut en güncel uygun/stabil sürüm doğrulanacak.
   - Eski sürümler yalnız zorunlu teknik gerekçe varsa tutulacak.
   - Legacy JDK, Android tools, Yarn Classic, eski SDK/CLI gibi kalıntılar modern karşılığı mevcutsa temizlenecek.

8. **Test disiplini uygulanacaktır.**
   - Geliştirme sırasında yalnız değişen alan hedefli test edilecek.
   - Aynı geniş smoke/regression testleri gereksiz yere tekrar tekrar çalıştırılmayacak.
   - Shared/runtime değişiklikleri ilgili targeted regression geçmeden deploy edilmeyecek.
   - Exact deployed build üzerinde final aşamada bir kez full MCP smoke çalıştırılabilir.
   - Bir test hata verirse aynı full test körlemesine yeniden çalıştırılmayacak; önce kök neden hedefli olarak bulunacak.

9. **Canlılık doğrulaması sadece PID görmekle sınırlı değildir.**
   - `talvora_system_info`, `current.json`, Service PathName/version root ve Tray executable path birlikte doğrulanacak.
   - Service ve Tray aynı güncel version root'tan çalışmalı.
   - ToolCount canonical `TalvoraToolManifest` ile eşleşmeli.
   - Gerekli durumlarda installed binary hashleri current source publish hashleriyle karşılaştırılmalı.

10. **Canonical installer tek kurulum otoritesidir.**
    - `scripts\Build-Windows-Installer.ps1` canonical build/package yoludur.
    - Native installer canonical runtime install/update motorudur.
    - `Install.ps1` ikinci bir service/state/install engine'e dönüştürülmeyecek.
    - Stale installer artifact kullanılmayacak; runtime source değiştiyse installer yeniden build edilecek.

11. **Tek canonical tool manifest kullanılacaktır.**
    - Araç sayısı ve araç isimleri `TalvoraToolManifest.cs` üzerinden türetilecek.
    - Test/CI içinde 159, 162, 198 gibi tarihsel sayılar hard-code edilmeyecek.
    - Legacy aliaslar yalnız araç sayısını yükseltmek amacıyla geri getirilmeyecek.

12. **Tek canonical handoff dosyası kullanılacaktır.**
    - Yalnız `HANDOFF.md` tutulacak.
    - Yeni tarihli handoff dosyaları veya NEXT-SESSION-PROMPT benzeri paralel geçiş dosyaları oluşturulmayacak.
    - Her devirde aynı `HANDOFF.md` tamamen güncellenip yeniden yazılacak.

13. **Kod yapısı performans ve sağlıklılık odaklı tutulacaktır.**
    - Çöp, dead code, yarım implementasyon, sessiz exception yutma, kaynak sızıntısı, sınırsız büyüyen log/queue ve gereksiz sync blocking düzenli olarak temizlenecek.
    - Kod yalnız satır sayısını düşürmek için parçalanmayacak.
    - Shared abstraction ancak gerçek tekrar/sorumluluk sınırı varsa oluşturulacak.
    - Atomic file operations, bounded logs, cancellation-aware async ve deterministic state tercih edilecek.

14. **Git kuralları kesindir ve birincil Git sunucusu yerel Gitea'dır.**
    - Ana dal `main`dir.
    - Birincil remote `origin` = yerel Gitea: `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git`.
    - GitHub artık birincil remote değildir; yalnız ikincil/yedek remote adı `github` ile tutulur: `https://github.com/bingoweb/Talvora-MCP.git`.
    - Kullanıcı yalnız “commit/push” derse varsayılan hedef kesinlikle `origin/main` yani yerel Gitea'dır.
    - GitHub'a push/sync yalnız kullanıcı açıkça GitHub'ı isterse yapılacak.
    - **GitHub remote işlemleri için yalnız Talvora'nın `talvora_git_run` GitHub-aware yolu kullanılacak.** `github` remote'una push/fetch/sync sırasında ham `git`/`gh` komutlarını LocalSystem PowerShell/CMD üzerinden çalıştırma; `talvora_git_run` github.com HTTPS işlemlerini logged-on Windows kullanıcı oturumunda GCM/OAuth ile yürütür ve credential yoksa fail-fast eder. Bu kural yeni oturumlarda da varsayılan ve zorunludur.
    - Gereksiz alt dallar bırakılmayacak.
    - Commit/push yalnız kullanıcı açıkça istediğinde yapılacak.
    - Push istenirse önce `git diff --check`, branch/upstream ve working tree kontrol edilecek.
    - Talvora LocalSystem bağlamından `git fetch origin` ve `git ls-remote origin` çalıştığı doğrulanmıştır; Git işlemleri interaktif kullanıcı terminaline bağımlı değildir.
    - Runtime clean commit SHA ile temsil edilecekse commit sonrası canonical installer yeniden build/deploy edilip SourceCommit doğrulanacak.

15. **Geliştirme tamamlandı demeden önce uygulama gerçeği doğrulanacaktır.**
    - “Kod yazıldı”, “build geçti” veya “test geçti” tek başına yeterli değildir.
    - Runtime değişikliği varsa çalışan Talvora'nın yeni kodu kullandığı kesinleştirilmelidir.
    - ChatGPT tarafındaki Talvora tool schema değiştiyse bu oturumun/connector'ın yeni tool yüzeyini gerçekten gördüğü de doğrulanmalıdır.

## Canonical repository and live runtime

- Repository: `C:\Users\tayla\Talvora-MCP`
- Primary remote/origin: `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git`
- Secondary backup remote/github: `https://github.com/bingoweb/Talvora-MCP.git`
- Branch: `main`
- Branch/upstream: `main -> origin/main`.
- Current live runtime/source baseline: `74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5`.
- Current repo state: `main`, ahead/behind `0/0`, dirty çalışma ağacı beklenen Control Center implementasyonunu içeriyor; commit/push yapılmadı.
- MCP endpoint: `http://127.0.0.1:7676/mcp`
- Canonical tool manifest: **198 tools**
- Current live runtime identity: `74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5`.
- Current live version root:
  `C:\Program Files\Talvora\Versions\74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5-20260919183843581`.
- Current service PID: **1872**.
- Current Tray PID: **2356**.
- Canonical installer build/deploy tamamlandı; live `SourceCommit` dirty working-tree fingerprint ile doğrulandı.
- Service account/SID: LocalSystem / `S-1-5-18`
- `current.json`: `C:\Users\tayla\AppData\Local\Talvora\current.json`, ToolCount=198.

## Yerel Gitea — birincil Git altyapısı

- Gitea sürümü: **1.27.3**.
- Kurulum binary: `C:\Program Files\Gitea\gitea.exe`.
- Work/data root: `C:\ProgramData\Gitea`.
- Config: `C:\ProgramData\Gitea\custom\conf\app.ini`.
- SQLite DB: `C:\ProgramData\Gitea\data\gitea.db`, WAL modu.
- Repository root: `C:\ProgramData\Gitea\data\gitea-repositories`.
- Windows service: `gitea`, LocalSystem, Automatic/Delayed Auto.
- Built-in SSH: `127.0.0.1:2222`.
- Built-in SSH kullanıcı adı: `git` (`BUILTIN_SSH_SERVER_USER = git`, `SSH_USER = git`).
- Gitea backend HTTP yalnız localhost: `127.0.0.1:3001`.
- Dış/browser URL: `http://127.0.0.1:3000/`.
- Kullanıcı/admin: `taylan`.
- Registration kapalı; Actions, Packages, LFS ve normal Gitea yetenekleri açık.
- Push-to-create açık; yeni kullanıcı repoları varsayılan private ve default branch `main`.
- Dedicated local Git SSH key:
  - private: `C:\Users\tayla\.ssh\id_ed25519_gitea`
  - public: `C:\Users\tayla\.ssh\id_ed25519_gitea.pub`
  - known hosts: `C:\Users\tayla\.ssh\known_hosts_gitea`
- Talvora repo-local `core.sshCommand` dedicated Gitea keyini ve known_hosts dosyasını kullanır.
- `taylan/Talvora-MCP` reposu push-to-create ile oluşturuldu ve `main` başarıyla push edildi.
- Talvora LocalSystem bağlamından `fetch origin --prune` ve `ls-remote --heads origin` GREEN.

### Parolasız tarayıcı erişimi

- Caddy sürümü: **2.11.4**, güncel stabil ve Chocolatey paketi resmi latest ile eşleşiyor.
- Caddy config: `C:\ProgramData\Caddy\Caddyfile`.
- Windows service: `caddy`, LocalSystem, Automatic.
- `caddy` servisi `gitea` servisine bağımlıdır; reboot sonrası Gitea önce ayağa kalkar.
- Caddy yalnız `127.0.0.1:3000` dinler ve `127.0.0.1:3001` Gitea backend'ine proxy eder.
- Caddy upstream isteğinde `X-WEBAUTH-USER: taylan` başlığını zorla set eder.
- Gitea'da `ENABLE_REVERSE_PROXY_AUTHENTICATION = true`; auto-registration kapalıdır.
- Böylece bu makinede tarayıcıdan `http://127.0.0.1:3000/` açıldığında parola/login ekranı olmadan doğrudan `taylan` admin oturumu kullanılır.
- Doğrulama:
  - `http://127.0.0.1:3001/user/settings` -> **303 /user/login**
  - `http://127.0.0.1:3000/user/settings` -> **200**, `taylan` görünür
  - `http://127.0.0.1:3000/taylan/Talvora-MCP` -> **200**, private repo oturumsuz HTTP client ile erişilebilir
- Gitea ve Caddy yalnız localhost'a bağlıdır; bu SSO modeli ağdaki başka cihazlara açılmış değildir.

## Resmî Gitea MCP — ChatGPT entegrasyonu

- Resmî proje: Gitea'nın kendi `gitea-mcp` sunucusu.
- Kurulu sürüm: **1.7.0**.
- Binary: `C:\Program Files\Gitea MCP\gitea-mcp.exe`.
- Release SHA256 doğrulandı.
- Transport: HTTP/stateless.
- Yerel MCP endpoint: `http://127.0.0.1:8081/mcp`.
- Health endpoint: `http://127.0.0.1:8081/healthz` -> **200 ok**.
- Gitea host: `http://127.0.0.1:3000`.
- Gitea reverse-proxy API authentication açık olduğu için MCP ayrı PAT taşımadan `taylan` admin yetkileriyle localhost Gitea API'sine bağlanır.
- Read-only veya scope/tool filtresi kullanılmıyor; resmî sunucunun tüm araçları yükleniyor.
- MCP initialize doğrulandı:
  - server name: `Gitea MCP Server`
  - server version: `1.7.0`
  - negotiated protocol: `2025-06-18`
  - tool count: **55**
  - write araçları açık; ör. `create_repo`, `create_or_update_file`, `delete_file`, Actions/PR/issue/release araçları.
- Kalıcılık:
  - Scheduled Task: `Gitea MCP Server`
  - principal: SYSTEM / Highest
  - trigger: AtStartup
  - restart: 1 dakika aralıkla, 999 deneme
  - execution time limit: unlimited
  - lifecycle stop/start testi GREEN
  - son doğrulanan PID: **9488**
- ChatGPT yerel MCP'ye doğrudan bağlanamaz; ChatGPT Business custom MCP oluştururken **Secure MCP Tunnel / Tünel** kullanılacak.
- ChatGPT Create App ekranında `Sunucu URL'si` yerine `Tünel` seçilecek.
- Bu MCP için public internet URL açılmayacak; localhost MCP Secure MCP Tunnel üzerinden ChatGPT'ye taşınacak.
- ChatGPT app oluşturulup tool scan tamamlandıktan sonra 55 aracın tamamının göründüğü doğrulanmalı.
- Secure MCP Tunnel:
  - tunnel ID: `tunnel_6aae935c5da0819193da4a5160b81aaa`
  - runtime alias: `gitea-business`
  - local target: `http://127.0.0.1:8081/mcp`
  - final audit status: `process_running=true`, `healthy=true`, `ready=true`
  - watchdog Scheduled Task: `Gitea MCP Tunnel`
  - watchdog keeps the managed runtime alive and fails on unexpected runtime exit so Task Scheduler retries
  - task execution time limit: unlimited
  - PowerShell 7 action includes `-WindowStyle Hidden`; no terminal window remains open in the user session
  - final integration audit: Gitea backend/proxy/MCP all HTTP 200; visible user PowerShell window count 0.

## Standing project rules

- Windows-native Talvora.
- Full capability / TAM YETKI philosophy. Do not add artificial path, command, host, package, user, session, repository, or development-capability restrictions.
- Package management is Chocolatey; WinGet remains forbidden in Talvora production/bootstrap paths.
- Use the newest suitable/current stable software versions. Do not retain deprecated CLIs or old technologies merely for compatibility.
- Project-local wrappers/toolchain pins are fine; compatibility fallbacks to deprecated tooling are not.
- Use Context7 plus official documentation when adding/changing library/framework/software-development capabilities.
- Playwright MCP artık sıradaki ana entegrasyon hedefidir. Yeni oturumda resmî Microsoft @playwright/mcp sunucusu dikkatle kurulacak ve Talvora Control Center'ın managed MCP registry/lifecycle/health/recovery/dashboard modeline entegre edilecektir. Kurulum sırasında Context7 + resmî Microsoft Playwright/Playwright MCP belgeleri zorunludur.
- Do not split code merely to reduce line counts; split only at real responsibility boundaries.
- Test only changed areas while developing. Run a full MCP smoke only as a final runtime gate after the exact build has been deployed.
- Every runtime/shared/installer/Tray source change must follow:
  source change -> targeted build/regression -> canonical installer build -> live deploy -> system_info/version-root/PID check -> relevant live behavior check.
- For final runtime confidence, verify installed binaries against a publish from current source by SHA256.
- Git commit/push only when explicitly requested by the user.

## Exact live-deployment verification

The final deployed runtime was built by the canonical:
`scripts\Build-Windows-Installer.ps1 -RuntimeIdentifier win-x64`

Final installer artifact produced:
- SourceCommit: `c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-882cb1fde36e`
- Installer: `artifacts\installer\Talvora-Setup.exe`
- SHA256 at build: `ECBA99B2C0936B5EA83EEC5DF87429BD414B64544746844F289B229E7BDC685D`

The installer completed successfully and started both service and Tray from the same version root. Installer log explicitly confirmed the Tray remained stable for the required interval.

A fresh publish from the current source was compared against installed binaries. All matched exactly:
- `Service\Talvora.dll`: SHA256 match
- `Service\Talvora.Shared.dll`: SHA256 match
- `Tray\Talvora.Tray.exe`: SHA256 match

Therefore the currently running service/Tray are not stale artifacts; they are byte-for-byte aligned with the current runtime source.

## Major fixes in this audit batch

### ProcessRunner
- Timeout/cancellation no longer waits forever after a failed tree kill; termination waiting is bounded.
- Batch files use a short-lived wrapper and unique environment transport variables so cmd metacharacters are not expanded by the wrapper.
- Internal transport variables are filtered from captured VS developer environments.
- Embedded quote handling was hardened: literal `"` in batch arguments is encoded as `""`, preventing following arguments from collapsing into the quoted token.
- Native executable ArgumentList semantics remain unchanged/unrestricted.
- Added service-independent `tests\ProcessRunnerRegression.ps1`.
- Added `--shared-infrastructure-only` smoke mode.
- Live multi-argument batch test passed with quote, `<`, `>`, `&&`, spaces, `&`, pipe, percent, exclamation, parentheses, empty values, and trailing backslash cases.

### Background jobs
- Failed job startup no longer leaves an orphan process/job directory.
- Persisted PID reuse is guarded by actual process start time and executable-path validation where applicable.
- Stale/reused PID smoke confirms an unrelated process is not killed.
- Removed redundant per-chunk flush from stdout/stderr pumps.
- Exit metadata is persisted even when an output pump faults.
- Observer failures go to bounded Talvora job error logging instead of being swallowed.

### HTTP mock / watchers / HTTP ownership
- HTTP mock runtime exposes `LastError`.
- Listener/context failures are recorded and cancellation is propagated correctly.
- File watcher disposed-state races were hardened.
- FIFO watch queues no longer perform redundant sequence sorting.
- HttpClient handler/client ownership is explicit so construction failures do not leak handlers.

### Installer / Tray / Windows session launcher
- Installer and MCP session tools share the same WindowsSessionLauncher.
- WindowsSessionLauncher supports CreateProcessWithTokenW fallback when CreateProcessAsUserW fails for access/privilege reasons.
- Installer Tray startup is retried and requires the launched PID to remain stable.
- Tray supports `--replace` and waits for the single-instance mutex when replacing an old instance.
- Installer and Tray now use the same global graceful-shutdown event.
- Tray/menu/timer/event/synchronization resources are explicitly disposed.
- A previously observed state where service was updated but Tray stayed old was eliminated and verified by SHA256.
- Dirty installer provenance now includes a deterministic 12-hex working-tree fingerprint.
- Fingerprint inputs are runtime/build inputs (`src/`, `assets/`, canonical installer build script, root build/toolchain config), so test/docs-only changes do not create fake runtime identities.

### Developer/tooling capability additions
Canonical manifest is 198 tools and includes the added capabilities:
- JVM/JDK/Gradle/Maven
- Go/Rust
- Flutter/Dart
- modern Android CLI/ADB/emulator
- pnpm/Yarn Modern/Bun
- workspace inspection and inferred commands
- coverage summary
- normalized diagnostics parser
- artifact inventory
- Windows release/signing/resource tools
- GitHub CLI

Removed/kept removed:
- `talvora_sdkmanager_run`
- `talvora_corepack_info`
- old legacy VS aliases
- deprecated Android `tools\bin` fallback
- JDK 21 compatibility fallback
- Yarn Classic compatibility path

### SQLite / Git / misc quality
- SQLite backup stages beside target, closes stream before hash, then atomically publishes.
- SQLite transaction cleanup relies on transaction disposal instead of an empty rollback catch.
- Numeric conversions use invariant culture.
- Git bare repository inspection/log/branches/diff support was corrected.
- Multiple broad operational catches were narrowed where failure families are known.
- Machine-data parsing was made invariant in several diagnostic/process/service paths.
- No production TODO/FIXME/HACK, old 159/162 tool-count constants, old sdkmanager/Corepack/Yarn Classic/JDK21 references, async-void, Thread.Sleep, .Result, or .Wait() remained in the final static scan.

## Smoke and regression state

Final exact deployed runtime:
- Full **198-tool MCP smoke: GREEN**.
- Native installer runtime regression: `NATIVE_INSTALLER_RUNTIME_GREEN`.
- Native installer source regression: `NATIVE_INSTALLER_SOURCE_GREEN`.
- ProcessRunner regression: `PROCESS_RUNNER_REGRESSION_GREEN`.
- File list metadata regression: `FILE_LIST_METADATA_GREEN`.
- Knowledge search scan: `KNOWLEDGE_SEARCH_SCAN_GREEN`.
- Runtime identity cache: `RUNTIME_IDENTITY_CACHE_GREEN`.
- Runtime metadata cache: `RUNTIME_METADATA_CACHE_GREEN`.
- Smoke knowledge-root isolation: `SMOKE_KNOWLEDGE_ROOTS_ISOLATION_GREEN`.
- ChatGPT Business bootstrap self-test: GREEN under PowerShell 7 and Windows PowerShell 5.1.
- YAML parse of `.github\workflows\windows-ci.yml`: GREEN.
- Final `git diff --check`: GREEN.
- Final static TODO/legacy/blocking scan: no hits.

The full MCP smoke should not be repeated again unless runtime source changes after this handoff.

## CI changes

`.github\workflows\windows-ci.yml` now:
- builds current projects including Talvora.Shared,
- runs the real ProcessRunner regression,
- derives tool count from TalvoraToolManifest,
- builds and installs the canonical native installer,
- verifies installed LocalSystem runtime and then runs MCP smoke,
- rejects WinGet in production/bootstrap paths using tracked-source `git grep -I` rather than recursively scanning generated bin/obj binaries,
- allows regression tests themselves to mention the string "WinGet" while asserting the prohibition.

## Current toolchain verified on the Windows machine

- Windows SDK: **10.0.28000.0**
- Temurin JDK: **25.0.4.1 LTS**
- Flutter: **3.47.5 stable**
- Dart: **3.13.4 stable**
- pnpm: **12.4.2**
- Yarn Modern: **4.18.0**
- Bun: **1.4.2**
- GitHub CLI: **2.101.0**
- GitHub CLI in LocalSystem context is not authenticated; bu artık birincil Git akışını etkilemez çünkü birincil Git sunucusu yerel Gitea'dır.
- Gitea: **1.27.3**
- Caddy: **2.11.4**

The earlier pending Windows SDK 10.0.28000 install note is obsolete; 10.0.28000.0 is now installed and selected by Talvora.

## Logging/retention decision

`Talvora-LogMaintenance` runs hourly.
- Tunnel/client and small operational logs are bounded/rotated.
- Completed job stdout/stderr files are trimmed after exit.
- Live job stdout/stderr are intentionally not truncated while the job is running, preserving stream offsets/full-capability behavior.

## Working-tree notes

Control Center ve GitHub-push auth düzeltmeleri commit/push edildi. Son doğrulanan repo durumu main, HEAD 7a24fdc4bb53028d880762db7d8187af153d064b, origin/main ve github/main aynı commit'te, ahead/behind 0/0, working tree clean. Yeni oturum başında yine git status --short --branch ve iki remote HEAD doğrulanacak. Kullanıcı açıkça istemeden sonraki değişiklikler commit/push edilmeyecek.

Stale dated handoffs and the old transition prompt are deleted in the working tree:
- `HANDOFF-2026-09-18.md`
- `HANDOFF-2026-09-19.md`
- `NEXT-SESSION-PROMPT.txt`

`HANDOFF.md` is the single canonical handoff going forward.
`BUG-AUDIT.md` is the living detailed defect log.

Do not restore the old handoff files or old tool-count assumptions.

## Next action

### Yeni oturum hedefi: Playwright MCP kurulumu + Talvora Control Center entegrasyonu

Yeni oturumda soru sormadan doğrudan bu işe başla. İlk iş HANDOFF.md dosyasını oku, Talvora MCP bağlantısını talvora_system_info ile doğrula ve repo/runtime baseline'ını teyit et. Hedef, Microsoft'un resmî Playwright MCP sunucusunu Windows makineye kalıcı ve bakımı kolay şekilde kurmak ve mevcut Talvora Control Center'a üçüncü bir managed MCP olarak tam entegre etmektir.

#### Başlangıç baseline'ı
- Repo: C:\Users\tayla\Talvora-MCP
- Branch: main
- HEAD: 7a24fdc4bb53028d880762db7d8187af153d064b
- Yerel Gitea origin/main: aynı commit.
- GitHub github/main: aynı commit.
- Working tree: clean.
- Canlı Talvora SourceCommit: 7a24fdc4bb53028d880762db7d8187af153d064b.
- Talvora LocalSystem MCP endpoint: http://127.0.0.1:7676/mcp.
- Canonical manifest: 198 tools.
- Control Center Faz 0-11 tamamlandı.
- GitHub HTTPS push problemi kökten düzeltildi: talvora_git_run github.com HTTPS push'larını logged-on Windows kullanıcı oturumunda GCM/OAuth ile çalıştırıyor; credential yoksa fail-fast. MSI\tayla kullanıcısında GCM hesabı bingoweb, credential store wincredman.
- Canonical installer: artifacts\installer\Talvora-Setup.exe.
- Clean-commit installer SHA256: BC06AD93D0B2949F19D86A4D5403FDC472FB89B08DE4EC4BF8630547099042FC.

#### Playwright MCP için zorunlu araştırma
- İlk kod/kurulum değişikliğinden önce Context7 ve resmî Microsoft Playwright MCP belgelerini kullan.
- Birincil kaynak: microsoft/playwright-mcp.
- Resmî paket: @playwright/mcp.
- Resmî minimum: Node.js 20+.
- Paket/sürüm/CLI bayrakları güncel kaynaktan doğrulanacak; eski blog/config örnekleri kopyalanmayacak.
- Varsayılan profil davranışı, --user-data-dir, --isolated, --extension, browser/channel seçimi, config dosyası ve transport seçenekleri güncel resmî dokümandan kontrol edilecek.
- Kullanıcının mevcut login/session'larını kullanmak gerekiyorsa resmî browser-extension modeli özellikle değerlendirilecek; secret/token hiçbir log veya Control Center UI'da gösterilmeyecek.

#### Kurulum ilkeleri
1. Önce mevcut Node/npm/npx/Chrome/Edge/Playwright durumunu Talvora araçlarıyla tespit et. Gereksiz yeniden kurulum yapma.
2. En güncel uygun stable Playwright MCP paketini kullan; mümkünse npx @playwright/mcp@latest ile özellik keşfi yap, kalıcı çalışma için sürüm pinleme gerekip gerekmediğini resmî belgeler ve operasyonel güvenilirliğe göre değerlendir.
3. MCP'yi kullanıcı terminal penceresi açık bırakmayacak şekilde kalıcı çalıştır.
4. Windows restart sonrası otomatik başlamalı ve crash sonrası recover olmalı.
5. Playwright browser/profile state'inin hangi kullanıcı SID/profilinde tutulduğu açık ve deterministik olmalı. LocalSystem altında yanlış profile yazma problemi yaratma.
6. Browser automation kullanıcı oturumu gerektiriyorsa süreç doğru interactive Windows session/token altında çalıştırılmalı; GitHub auth fix'inde kullanılan WindowsSessionLauncher / interactive-user pattern'ini yeniden kullan.
7. Full capability korunacak. Güvenlik bahanesiyle domain/path/tool kısıtlaması ekleme. Ancak resmî Playwright MCP'nin zorunlu host/origin güvenlik davranışlarını doğru yapılandır; özellik kırpma yapma.
8. Secret/token gerekiyorsa current-user DPAPI kullan; plaintext config/log/UI yok.
9. Playwright MCP için localhost endpoint ve transport gerçek çalışma biçimine göre seçilecek. ChatGPT Business tarafına gerekiyorsa Talvora'nın mevcut Secure MCP Tunnel provisioning modeli kullanılacak; tunnel gerekmiyorsa gereksiz tunnel oluşturma.
10. Bir özellik çalışıyor görünmeden registry/dashboard'a Ready yazma.

#### Talvora Control Center entegrasyonu
Playwright MCP, mevcut generic managed MCP altyapısına mümkün olduğunca özel-case eklemeden kaydedilecek:
- managed registry entry; stable id tercihen playwright;
- Türkçe display name/açıklama;
- local endpoint / transport metadata;
- process/service/task component metadata;
- varsa secure tunnel metadata;
- AutoStart, health polling, startup recovery, capped backoff ve manual Stop session suppression;
- Start / Stop / Restart;
- ciddi incident classification;
- event store + raw log bağlantısı;
- first-run/incomplete setup değerlendirmesi.

Dashboard kartında Playwright logosu veya düzgün fallback icon, ortak Hazır/Dikkat/Kapalı status modeli, kısa Türkçe açıklama ve doğru contextual primary action olacak.

Ayrıntı görünümünde en az MCP server health, browser/runtime health, endpoint/transport, paket sürümü, browser/channel/profile mode, process/task state, tunnel varsa tunnel state, profile/state path, son olaylar ve Başlat/Durdur/Yeniden Başlat bulunacak.

#### Sağlık modelinde doğrulanması gerekenler
Playwright kartını yalnız process var diye Ready sayma. Mümkün olan en kuvvetli zinciri doğrula:
1. launcher/task/process çalışıyor;
2. MCP transport endpoint cevap veriyor;
3. MCP initialize başarılı;
4. tool list alınabiliyor ve beklenen Playwright browser araçları mevcut;
5. gerçek browser launch/connect başarılı;
6. basit bir sayfada navigate + accessibility snapshot veya eşdeğer gerçek browser işlemi başarılı;
7. extension modu seçildiyse extension bağlantısı gerçekten hazır.

#### Test disiplini
- Geliştirme sırasında yalnız değişen alanların targeted testlerini çalıştır.
- Aynı testi tekrar tekrar çalıştırma.
- Önce source build/test; ardından gerçek Playwright MCP initialize/tool-list/browser smoke.
- Registry/dashboard için source Control Center smoke.
- Runtime/shared/installer/Tray değiştiyse zorunlu sıra: source -> targeted test -> canonical installer -> live deploy -> talvora_system_info -> Service/Tray PID + version root -> Playwright live behavior.
- Final exact-deployed full MCP smoke yalnız runtime/tool yüzeyi gerçekten değiştiyse ve final gate olarak bir kez.
- Her bug/risk anında BUG-AUDIT.md içine yaz.
- Handoff'u çalışma boyunca güncel tut.
- Kullanıcı açıkça istemedikçe commit/push yapma.

#### İlk oturumda yapılacak somut sıra
1. HANDOFF.md oku.
2. talvora_system_info, git status, origin/github HEAD, live version root doğrula.
3. Context7 + resmî Playwright MCP belgelerini doğrula.
4. Node/npm/npx + Chrome/Edge + mevcut Playwright/Playwright MCP kurulumlarını envanterle.
5. Bu makinedeki en doğru çalışma modelini seç ve kısa teknik karar kaydını HANDOFF.md / BUG-AUDIT.md içine yaz.
6. Resmî MCP'yi kur ve dashboard entegrasyonundan bağımsız gerçek MCP initialize + tool list + browser smoke'u GREEN yap.
7. Kalıcı Windows startup/recovery/lifecycle modelini kur.
8. Managed registry'ye Playwright kaydını ekle.
9. Control Center dashboard kartı + detail + health + lifecycle + recovery + events entegrasyonunu tamamla.
10. Gerekliyse Secure MCP Tunnel provisioning yap ve gerçek health/ready doğrula.
11. Source targeted smoke.
12. Canonical installer build/deploy.
13. Exact-installed Playwright lifecycle/health/browser/Control Center smoke.
14. Handoff ve BUG-AUDIT'i son durumla güncelle.

Önemli: Yeni oturumda tekrar ürün tasarımı soruları sorma. Control Center UX/mimari kararları zaten sabit. Playwright MCP'yi mevcut tamamlanmış mimariye dikkatle eklemeye doğrudan başla.

## [TARİHSEL / ARTIK GEÇERSİZ] Gitea bağımsız sistem tepsisi ikonu

- Talvora ana ikonundan ayrı ikinci bir `NotifyIcon` bulunur.
- Sorumluluk sınıfı: `src\Talvora.Tray\GiteaNotifyIconController.cs`.
- Gitea ikonu Talvora ikonunun durumunu değiştirmez; iki ikon bağımsızdır.
- Görsel:
  - Gitea/Git branch temalı yeşil ana ikon.
  - sağ-alt durum rozeti:
    - yeşil + check: Gitea sağlıklı,
    - sarı: yeniden başlatılıyor/kontrol ediliyor,
    - kırmızı + x: Gitea kapalı/yanıt vermiyor.
- Durum kaynağı iki katmanlıdır:
  - backend: `http://127.0.0.1:3001/api/healthz`
  - tarayıcı/SSO proxy yolu: `http://127.0.0.1:3000/api/healthz`
- Backend sağlıklı ama proxy bozuksa ikon yeşil kalmaz; sarı/degraded durum gösterir.
- Kontrol aralığı: 10 saniye.
- Gitea ikonuna çift tıklama: `http://127.0.0.1:3000/` tarayıcıda açılır.
- Sağ tık menüsü:
  - canlı Gitea durumu,
  - `Gitea'yı Aç`,
  - `Gitea'yı Yeniden Başlat`,
  - `Durumu Yenile`.
- Restart kullanıcı/UAC yetkisine bağlı değildir; Tray yerel Talvora MCP'ye bağlanıp LocalSystem bağlamında restart zincirini çalıştırır.
- Caddy raw service kontrol modeli nedeniyle restart zinciri:
  1. Caddy PID force terminate,
  2. SCM'de Caddy `Stopped` bekle,
  3. Gitea restart/start,
  4. Caddy start,
  5. iki servisi `Running` doğrula,
  6. Gitea health endpoint'ini doğrula.
- Headless doğrulama komutları:
  - `Talvora.Tray.exe --gitea-status`
  - `Talvora.Tray.exe --gitea-restart`
- Son doğrulama:
  - source build status -> exit 0
  - source build restart -> exit 0
  - installed binary status -> exit 0
  - installed binary restart -> exit 0
  - Gitea -> Running
  - Caddy -> Running
  - Tray log -> `Gitea tray icon initialized.`
  - pre-commit live SourceCommit -> `c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735`
  - source-published `Talvora.dll`, `Talvora.Shared.dll` ve `Talvora.Tray.exe` SHA256 == installed binaries
  - exact deployed 198-tool MCP smoke -> GREEN
  - installed Tray `--gitea-status` -> exit 0
  - installed Tray `--gitea-restart` -> exit 0
  - restart sonrası Gitea backend/proxy/MCP -> HTTP 200
  - restart sonrası Secure MCP Tunnel -> running/healthy/ready
  - visible user PowerShell window count -> 0.
- Tray artık `ModelContextProtocol` 2.2.0 client paketini kullanır; doğrulama sırasında NuGet'te görünen en güncel sürüm 2.2.0 idi.
### 2026-09-19 ek proje kuralı — sürüm pinleme yasak
- Kullanıcı talimatı: Hiçbir bağımlılık, CLI, SDK, runtime veya araç sürümü pinlenmeyecek.
- Her kurulum/güncellemede güncel `latest` / current stable sürüm kullanılacak.
- Lockfile veya exact-version sabitlemesiyle sürüm dondurulmayacak; Playwright MCP entegrasyonu da bu kurala uyacak.


### 2026-09-19 Playwright MCP bağımsız doğrulama
- Resmî `@playwright/mcp@latest` kullanıldı; hiçbir Playwright/MCP sürümü pinlenmedi, runtime package specifier `latest`, npm lockfile yok.
- Güncel npm latest doğrulaması sırasında `@playwright/mcp` = 0.0.82 idi; bu bilgi envanterdir, pin değildir.
- Chrome 153.0.8010.53, Playwright Extension `mmlmfjhmonkocbjadbfplnigmagldckm` sürüm 0.4.0, profil `Default`.
- Seçilen çalışma modeli: `MSI\\tayla` interactive session içinde hidden Playwright MCP process, Chrome Extension modu, deterministik `--profile-dir-name Default`, localhost Streamable HTTP endpoint `http://127.0.0.1:8931/mcp`.
- Host/DNS-rebinding kontrolü kapatılmadı; allow-list portlu localhost değerleriyle `127.0.0.1:8931,localhost:8931`.
- Full capability için `--allow-unrestricted-file-access` ve ek `vision,pdf,devtools` caps açık.
- Bağımsız smoke GREEN: process/listener -> MCP initialize -> 45 tools/list -> extension browser connect -> `https://example.com/` navigate -> `browser_snapshot`; snapshot `Example Domain` içerdi.
- Dashboard entegrasyonuna geçiş kapısı açıldı.


### Playwright MCP runtime kararı — extension production default değil
- Chrome Default profilinde Playwright Extension 0.4.0 mevcut ve resmi olarak doğrulandı.
- Ancak `--extension` smoke'u kullanıcı Chrome'unda welcome/connection sayfasını tekrar tekrar öne getirerek ChatGPT kullanımını bozdu.
- Bu nedenle managed production default: current-user (`MSI\\tayla`) altında ayrı persistent Playwright Chrome profili, localhost HTTP transport ve headless Chrome. LocalSystem browser profili kullanılmayacak.
- Extension modu silinmedi/kısıtlanmadı; yalnız kullanıcı mevcut oturum/SSO state'ini özellikle kullanmak istediğinde opt-in yol olarak tutulacak.


### 2026-09-19 Playwright MCP güncel çalışma durumu — bu bölüm önceki extension notlarının yerine geçer
- Production default artık extension değildir. Kullanıcı Chrome/ChatGPT sekmesine müdahale etmemek için ayrı current-user persistent headless Chrome profile kullanılır: `C:\Users\tayla\AppData\Local\Talvora\PlaywrightMCP\profile`.
- Canonical Scheduled Task: `Talvora Playwright MCP`; current interactive user `MSI\tayla`, hidden PowerShell launcher, restart policy açık, terminal penceresi yok.
- Runtime manifest `@playwright/mcp: latest` + `@modelcontextprotocol/sdk: latest`; package-lock ve node_modules hidden lock metadata yok.
- Runtime supervisor modeli: backend resmi Playwright MCP `127.0.0.1:8931`; Talvora compatibility/supervisor Streamable HTTP endpoint `127.0.0.1:8932/mcp`. Supervisor parent/child lifecycle ve Host rewrite sağlar; Control Center/tunnel endpoint 8932 olmalıdır.
- Canlı latest paket şu an 0.0.82; bu envanter bilgisidir, pin değildir.
- Local smoke 8932 üzerinden GREEN: MCP initialize, 45 tools/list, `browser_navigate`, `browser_snapshot`, dedicated headless Chrome.
- Current-generation health marker `state\server-start.json` + `state\browser-smoke.json` ile Ready yalnız aynı runtime generation gerçek browser smoke geçtikten sonra verilir.
- Secure MCP Tunnel alias `playwright-business`; kullanıcı tarafından verilen tunnel ID `tunnel_6aaee5a82aec8191b85840c59530d9a7`; local config/current-user DPAPI runtime credential hazır. Son health `/healthz` 200, `/readyz` 200.
- Tunnel startup yarışının kök nedeni bulundu: task başlar başlamaz tunnel connect MCP hazır olmadan çalışabiliyordu. Source lifecycle sırası `task -> local MCP ready -> tunnel connect -> browser smoke` olarak düzeltildi.
- Generic managed-MCP periodic health/recovery, capped backoff, manual-stop suppression, serious incident/event akışı source'ta Playwright dahil generic kayıtlar için mevcut.
- Control Center Playwright registry/detail metadata: endpoint/transport, package version, Chrome channel, profile mode/path, scheduled task, MCP process/protocol, browser runtime, browser-smoke, tunnel, logs.
- Source hedefli durum: service build GREEN 0 warning/0 error; Tray build GREEN 0 warning/0 error; smoke build GREEN; current-user Control Center visual smoke GREEN; Playwright 8932 browser smoke GREEN.
- Runtime/Tray source değiştiği için sıradaki zorunlu adım canonical installer build -> independent SYSTEM deploy -> `talvora_system_info`/PID/version-root -> exact-installed Playwright lifecycle+tunnel+Control Center doğrulaması. Commit/push YOK.

## 2026-09-19 Playwright MCP entegrasyonu — FINAL GREEN
- Talvora canonical live build: `7a24fdc4bb53028d880762db7d8187af153d064b-dirty-32d1aec61d98`.
- Live version-root: `C:\Program Files\Talvora\Versions\7a24fdc4bb53028d880762db7d8187af153d064b-dirty-32d1aec61d98-20260919201437452`.
- Talvora service: Running / LocalSystem / PID 7084; Tray: PID 8168; service ve Tray aynı version-root.
- Kritik installer bug kökten düzeltildi: Talvora upgrade artık servis kaydını `sc delete` ile silmiyor. Normal stop/force-stop sonrası mevcut kayıt `sc config` ile yeni `binPath`'e çevriliyor; kayıt yoksa yalnız fresh/recovery durumda create ediliyor. Rollback service-switch başlar başlamaz arm edilir ve cancellation'dan bağımsız çalışır.
- Gerçek incident kanıtı: 22:56 installer logunda `deleting registration before terminating PID=2188` sonrası `Windows service silinemedi: Talvora`; bu eski akış servis kaydını yok bırakmıştı. Yeni akış iki live deploy'da servis kaybı olmadan doğrulandı.
- Playwright scheduled-task XML native Windows biçimine alındı: UTF-16 declaration + `Encoding.Unicode`; önceki `encoding değiştirilemiyor` hatası kapandı.
- Playwright canonical task: `Talvora Playwright MCP`; eski duplicate `Playwright MCP Server` silindi.
- Task owner/session modeli: `tayla`, Interactive logon, Highest run level, hidden launcher, restart count 3. LocalSystem browser profili kullanılmıyor.
- Playwright runtime sürüm pinlemez: launcher her start'ta `@playwright/mcp@latest` çözer, package-lock bırakmaz. Doğrulanan güncel paket: 0.0.82.
- Browser modeli: ayrı current-user kalıcı headless Chrome profili: `C:\Users\tayla\AppData\Local\Talvora\PlaywrightMCP\profile`. Chrome Default/extension production default değildir; kullanıcı Chrome/ChatGPT sekmesini bozmaz.
- Transport: resmi Playwright backend `http://127.0.0.1:8931/mcp`; Talvora compatibility endpoint `http://127.0.0.1:8932/mcp` (registry endpoint). Compatibility proxy yalnız tunnel istemcisinin `/.well-known/oauth-protected-resource` discovery çağrısını lokal backend'den izole eder; MCP trafiği resmi backend'e proxy edilir.
- Secure MCP Tunnel: `tunnel_6aaee5a82aec8191b85840c59530d9a7`, alias `playwright-business`; runtime credential current-user DPAPI blob, plaintext yok. Final `/healthz=200`, `/readyz=200`.
- Managed registry Count=3; stable ID `playwright`; AutoStart=true; transport `streamable-http`; browserChannel=chrome; profile metadata kayıtlı.
- Final same-generation browser health: runtime generation `c0260f556d214f96829907b41c607816`; smoke generation aynı; passedAt `2026-09-19T23:14:53.8507066+03:00`; toolCount=45. Bu startup auto-start lifecycle içindeki gerçek navigate + accessibility snapshot smoke'tur.
- Exact-installed managed probe daha önce ayrıca GREEN: Ready=True, BrowserSmokePassed=True, ToolCount=45.
- Control Center final exact-installed visual smoke `2026-09-19 23:15:03` GREEN. Önceki false-negative smoke race `_refreshGate` yarışından kaynaklanıyordu; smoke artık 15 sn bounded wait ile gerçek snapshot'ı bekliyor.
- Dashboard generic managed-MCP mimarisi Playwright'ı registry/card/detail/lifecycle/health/recovery zincirine alır. Ready yalnız process ile değil MCP initialize + tools/list + browser runtime + same-generation browser smoke + required component/tunnel health tamamlandığında oluşur.
- Generic recovery timer, capped backoff, manual Stop session suppression, serious incident/events, component health, raw log ve first-run/incomplete setup yolları korunur.
- Final canonical installer build ve live deploy başarılı; temporary `Talvora Live Deploy OneShot` task temizlendi.
- Kullanıcı talimatı gereği COMMIT/PUSH YAPILMADI. Working tree dirty kalmalıdır.


## 2026-09-19 Deep audit checkpoint — Playwright MCP + latest Control Center/installer changes
- Deep audit completed against source, live runtime/process tree, installer logs, tray logs, Windows Event Log, Context7 Microsoft Playwright MCP docs, live `@playwright/mcp --help`, NuGet/npm latest status and exact-installed runtime.
- BUG-AUDIT numbering was normalized; duplicate semantic findings were consolidated. Current new/open audit range for this phase is 63–101 (39 findings total: 1 CRITICAL, 13 HIGH, 2 POLICY, 2 OPTIMIZATION, 3 LOW, 18 standard OPEN). Historical duplicate IDs 36/37 were renumbered to 102/103 without changing their fixed content.
- Highest priority finding: #79 CRITICAL — browser smoke marker is bound to MCP server generation, not the current Chrome browser instance. Live proof: smoke passed at 23:14:53+03, current dedicated Chrome root PID was launched around 23:24:35+03 under the same MCP generation. A new browser instance can therefore become Ready after only `browser_tabs list`, without its own navigate+snapshot smoke.
- Other HIGH findings to fix before calling production-hardening complete: #67 lifecycle hard-depends on Talvora 7676 broker; #68 manual-stop suppression dies with Tray process; #70 no per-MCP operation lock for generic/Playwright lifecycle; #71 smoke cleanup can close another shared-context client's current tab; #73 stale tunnel scope cache; #80 supervisor fatal paths can orphan backend/Chrome; #81 malformed Host can crash compatibility proxy; #82 native installer source regression/CI stale; #83 installer success does not prove Playwright readiness; #85 orphan Chrome descendants on stale cleanup/Stop; #88 Playwright installer mutation not rollback-safe; #89 PS5.1 launcher fallback broken; #96 restart hard-depends on npm registry availability.
- Important standard OPEN gaps: #63 duplicate Playwright payload build blocks; #64 floating dependency provenance; #65 PS5.1 component process-probe fallback; #66 incomplete-setup discovery blind spot; #69 server.log only rotates at task start; #72 smoke generation TOCTOU; #74 DPAPI readiness checks file existence instead of decryptability; #76 profile/state field incomplete; #77 Playwright detail missing recent events; #84 Ready tool contract too weak for enabled caps; #86 tunnel disconnect/restart not fully idempotent; #87 service upgrade failure can leave SCM recovery actions cleared; #92 tunnel health is HTTP-only/not identity-bound; #93 compatibility proxy still produces no-auth discovery/startup-probe warnings; #94 Playwright raw log view omits tunnel log; #97 every Attention state causes full restart; #99 registry backup is written but not consumed on recovery.
- Performance/diagnostic findings: #100 detail view duplicates expensive protocol/component probes every 8s; #101 repeated version failures can spam tray log.
- Latest-version policy check: `@playwright/mcp=0.0.82` is npm latest; npm=12.0.2 is latest; PowerShell=7.6.6 current; Chrome=153.0.8010.53 current Windows Stable. Node=24.21.0 is latest LTS but not latest Current; official latest Current is Node 26.9.0, tracked as #90. Installed Playwright MCP declares `engines.node >=18`.
- NuGet direct wildcard dependencies resolve current direct versions, but `dotnet list package --outdated --include-transitive` reports newer transitive packages; tracked as #95. Do not pin; resolve compatibility before any transitive override.
- Compiler/static gate: Talvora.Shared, Talvora service and Talvora.Tray Release builds all pass with 0 warnings/0 errors under TreatWarningsAsErrors + AnalysisLevel=latest. Direct Installer project build fails only because canonical Payload.zip is intentionally generated by Build-Windows-Installer.ps1; canonical installer build had already succeeded. Windows Event Log after final live deploy shows no Talvora/Tray/node/chrome crash entries.
- Live capability surface remains 45 Playwright tools including vision/PDF/devtools representatives. User Chrome remains a separate PID/profile tree; dedicated Talvora Playwright Chrome uses `...\PlaywrightMCP\profile`.
- No functional fixes from the deep-audit OPEN list were applied in this audit pass; only BUG-AUDIT/HANDOFF bookkeeping was changed. No commit/push performed.

## 2026-09-20 — Playwright dashboard sürekli sarı (#107) kök düzeltme
- Canlı semptom: Playwright kartı sürekli `Browser doğrulaması bekleniyor` Attention durumuna dönüyordu.
- Kök neden: non-smoke `ManagedMcpProtocolProbeService` dashboard health sırasında `browser_tabs` çağırıp demand-launch browser instance üretiyor; smoke marker PID/start-time eski instance'a bağlı olduğundan sonraki browser close/relaunch marker'ı kendi kendine geçersiz kılıyordu.
- Düzeltme: non-smoke health browser tool çağrısı yapmıyor. Successful gerçek browser smoke marker'ı current Playwright runtime generation boyunca geçerli. PID/start-time yalnız smoke execution anında canlı browser kanıtı olarak korunuyor. Generation değişirse marker geçersiz ve recovery gerçek smoke'u yeniden çalıştırıyor.
- Regression: `PlaywrightDashboardSmokeStabilityContract` eklendi; Tray Release 0 warning/0 error; NativeInstallerSourceRegression GREEN.
- Canonical deploy: source fingerprint `7a24fdc4bb53028d880762db7d8187af153d064b-dirty-d504e2d57553`.
- Live acceptance: generation `45ecffa5c5fd4e3c95967722ce10fd15` == smoke marker generation; installer gerçek Playwright smoke GREEN/45 tools; 54+ saniye polling boyunca Chrome health tarafından yeniden açılmadı ve yeni browser-smoke recovery failure oluşmadı.

## 2026-09-20 — Faz 12 final handoff / Playwright MCP production hardening tamamlandı
- Canonical live source fingerprint: `7a24fdc4bb53028d880762db7d8187af153d064b-dirty-d504e2d57553`.
- Live version-root: `C:\Program Files\Talvora\Versions\7a24fdc4bb53028d880762db7d8187af153d064b-dirty-d504e2d57553-20260919220307481`.
- Talvora service Running; Tray aynı version-root altında current interactive user session içinde çalışıyor.
- Playwright runtime: `@playwright/mcp` latest (live 0.0.82, pin değil), Node Current 26.9.0, npm 12.0.2. Installer Chocolatey `nodejs` latest'i dinamik çözer ve npm latest'i Playwright task ile aynı interactive user prefix'inde doğrular.
- Playwright endpoints: backend `http://127.0.0.1:8931/mcp`; Talvora compatibility endpoint `http://127.0.0.1:8932/mcp`.
- Secure MCP Tunnel: alias `playwright-business`; tunnel health `/healthz=live`, `/readyz=200 ready`.
- #93 FIXED: Node compatibility proxy SSE response header'ları `res.flushHeaders()` ile hemen flush ediliyor. Go MCP SDK v1.7.0 standalone SSE açık olarak canlı 8932'ye ~14.6 ms'de bağlandı; yeni tunnel loglarında `mcp probe timed out after 2s` / `failed to connect to mcp` yok.
- Proxy ayrıca backend'i gerçek MCP initialize ile warm/readiness preflight'tan geçirip yalnız hızlı initialize bütçesini sağladıktan sonra 8932 listen açıyor.
- #97 FIXED: Attention remediation reason-aware. Browser smoke/tunnel sorunu healthy MCP/browser zincirini full restart etmiyor. Live fault injection'da smoke marker yeniden üretildi, MCP generation/launcher/supervisor/backend PID'leri korunarak yalnız gerektiği kadar browser doğrulaması yenilendi.
- #106 FIXED: npm latest doğrulaması SYSTEM prefix'i yerine Playwright scheduled task ile aynı interactive user context'inde yapılır. Canonical deploy `npm=12.0.2` ve `npmPrefix=C:\Users\tayla\AppData\Roaming\npm` GREEN.
- #107 FIXED: dashboard/detail non-smoke health artık `browser_tabs` çağırıp demand-launch Chrome başlatmıyor. Successful gerçek browser smoke marker'ı current Playwright runtime generation boyunca geçerli; PID/start-time yalnız smoke execution anında canlı browser kanıtı olarak tutuluyor.
- #107 live acceptance: generation `45ecffa5c5fd4e3c95967722ce10fd15` == smoke marker generation, toolCount=45; 54+ saniye polling boyunca health tarafından Chrome spawn edilmedi ve yeni browser-smoke recovery failure oluşmadı.
- Generic lifecycle per-MCP operation coordinator, session-scoped manual Stop suppression, ownership manifest registry recovery, reason-aware recovery, tunnel identity health, bounded caches/logs, recent events/raw logs, first-run/incomplete-setup ve rollback-safe installer akışları source'ta mevcut.
- Dependency policy: pin yok. Direct/transitive .NET package graph source pass sonunda latest-compatible resolved; canonical installer resolved dependency provenance üretir.
- Regression gates: `NativeInstallerSourceRegression.ps1` GREEN; `PlaywrightDashboardSmokeStabilityContract`, `PlaywrightProxyReadinessGateContract`, installer transaction/latest Node contracts GREEN.
- Build gates: Talvora.Shared, Talvora service, Talvora.Tray Release 0 warning/0 error; canonical `Build-Windows-Installer.ps1` GREEN.
- Exact-installed Playwright smoke: initialize + tools/list + expected capability set + gerçek navigate + accessibility snapshot GREEN, 45 tools.
- Final `git diff --check` GREEN.
- Git branch: `main`. Remotes: `origin` = local Gitea, `github` = GitHub mirror.
- Production-hardening commit'i `9fc9bc87c197b367ced6993d782f299575577b2f` (`feat: production harden Playwright MCP management`) oluşturuldu ve hem local Gitea `origin/main` hem GitHub `github/main` üzerine başarıyla push edildi. İki remote bu commit'te senkronlandı; bu final handoff closeout notu ayrıca docs commit olarak iki remote'a gönderilecektir.
## 2026-09-20 — Production hardening reconciliation + canonical GitHub/deploy rules

- Oturum başlangıcında talvora_system_info canlı MCP bağlantısını doğruladı. Canlı production fingerprint önceki final deploy ile aynı kaldı: 7a24fdc4bb53028d880762db7d8187af153d064b-dirty-d504e2d57553; version-root C:\Program Files\Talvora\Versions\7a24fdc4bb53028d880762db7d8187af153d064b-dirty-d504e2d57553-20260919220307481.
- Başlangıç repo baseline: main, HEAD aad1db4eb38b07ceff38b9ba8915ff1c6e0a6a05, clean tree; live origin/main ve github/main doğrudan ls-remote ile aynı HEAD olarak doğrulandı.
- Faz 12 BUG-AUDIT/TODO reconciliation tamamlandı. Production-hardening commitinde uygulanmış fakat audit'te stale OPEN kalan 41 madde kaynak kodu üzerinden tekrar doğrulanarak kapatıldı; Faz 12 stale checkbox'ları güncellendi.
- Son ayrı madde #34 için scripts\Deploy-Windows-Installer.ps1 eklendi. Canonical installer artık Talvora servis process tree'sinden bağımsız, on-demand hidden SYSTEM Task Scheduler task'ı üzerinden başlatılabilir. Windows PowerShell 5.1 parse GREEN.
- tests\NativeInstallerSourceRegression.ps1 içine CanonicalDeployUsesIndependentSystemTask contract'ı eklendi ve tek hedefli çalıştırmada GREEN; mevcut diğer source contract'lar da aynı koşuda GREEN kaldı.
- GitHub kuralı canonical hale getirildi: github remote push/fetch/sync işlemlerinde yalnız Talvora'nın talvora_git_run GitHub-aware logged-on-user/GCM yolu kullanılacak; LocalSystem PowerShell/CMD içinde ham git/gh GitHub işlemi yapılmayacak.
- Bu oturumda installed runtime/shared/installer/Tray payload kodu değiştirilmedi. Bu nedenle daha önce GREEN olan canonical installer/live deploy/Playwright browser smoke tekrar edilmedi.
- Kullanıcı açıkça istemediği için commit/push yapılmadı. Çalışma ağacındaki bu reconciliation/deploy-helper değişiklikleri sonraki açık commit isteğine kadar yerelde kalacak.
