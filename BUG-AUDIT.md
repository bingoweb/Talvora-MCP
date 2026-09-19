# Talvora Deep Bug Audit

Last updated: 2026-09-19
Branch: main
Git HEAD: resolve with `git rev-parse HEAD` after final commit; c8d8e5f602b6cea26c3cb32d698f59b78553721d was the pre-audit baseline
Pre-commit exact deployed runtime: c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735
Canonical tool count: 198
Status: production-hardening source reconciliation completed on 2026-09-20. Previously stale Faz 12 OPEN markers were verified against the hardened source and final live HANDOFF evidence, then closed. #34 is now also closed by the canonical independent SYSTEM Task Scheduler deploy launcher and its source regression contract. No known production-hardening BUG-AUDIT item remains open.

## Fixed findings

1. Stale hard-coded historical tool counts in tests/CI.
2. Regression tests pinned to historical package versions.
3. ProcessRunner could wait indefinitely after timeout/cancellation if termination failed.
4. Batch-file arguments could be corrupted by cmd expansion/metacharacters.
5. Embedded quote in a batch argument could collapse later argument boundaries.
6. ProcessRunner transport variables leaked into captured Visual Studio developer environments.
7. Failed background-job startup could orphan a child process and job directory.
8. Persisted background jobs could target an unrelated process after PID reuse.
9. Background-job output pump flushed every chunk unnecessarily.
10. Background-job exit observer could hide failures.
11. HTTP mock background failures were insufficiently observable.
12. File watcher disposed-state races and redundant sorting.
13. HttpClient handler/client ownership cleanup.
14. WinForms/Tray lifetime cleanup.
15. Culture-dependent machine-data parsing in multiple tool paths.
16. Visual Studio developer-environment capture quoting.
17. Dirty installer provenance was ambiguous.
18. CI referenced a ProcessRunner regression that did not exist.
19. Broad operational catches in multiple paths were narrowed where appropriate.
20. Installer I/O/metadata formatting cleanup.
21. SQLite backup publication/value handling.
22. Installer/Tray graceful-shutdown event mismatch.
23. Installer could update service while Tray stayed on a stale binary.
24. Installer Tray launch could race the old single-instance mutex.
25. CI WinGet scan recursively inspected generated binary output and could stall.
26. CI WinGet scan also matched the regression assertion that verifies WinGet is forbidden.
27. Dev-server smoke launched the apphost as though it were the dotnet host, so the DLL path was misread as the MCP endpoint.
28. Dev-server readiness failure path could leave a background job artifact behind.
29. Gitea tray health checked only backend port 3001, so a broken Caddy/3000 browser path could still show green; status now checks backend + proxy and exposes a degraded state.
30. Gitea tray restart performed an unnecessary MCP `tools/list` round trip and did not explicitly dispose the manually-created `HttpClientTransport`; it now uses direct `CallToolAsync` and async-disposes the transport per current official C# SDK lifecycle guidance.
31. Gitea tray shutdown could race an in-flight async status/restart operation with disposal of its semaphore; lifetime cancellation now lets operations unwind without disposing coordination primitives underneath them.
32. Gitea Secure MCP Tunnel persistence was incomplete: the logon task returned after connect and the managed runtime later stopped. The watchdog now stays alive for the runtime process, fails on unexpected exit so Task Scheduler retries, has unlimited execution time, and runs PowerShell with `-WindowStyle Hidden` so no terminal window remains open.

## Final runtime verification

- Pre-commit SourceCommit: c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735
- Service and Tray run from the same version root.
- current.json ToolCount: 198.
- Current ChatGPT session exposes 198 Talvora tool wrappers.
- Installer log confirmed Tray remained stable after automatic launch.
- Fresh publish from current runtime source matched installed binaries by SHA256:
  - Service/Talvora.dll: match
  - Service/Talvora.Shared.dll: match
  - Tray/Talvora.Tray.exe: match
- Program Files contains only the current Talvora version root after installer cleanup.

## Final verification gates

- Talvora service/shared Release build: GREEN.
- Tray Release build: GREEN.
- Talvora.Smoke Release build: GREEN.
- Canonical native installer build/package: GREEN.
- NATIVE_INSTALLER_SOURCE_GREEN.
- NATIVE_INSTALLER_RUNTIME_GREEN.
- PROCESS_RUNNER_REGRESSION_GREEN.
- FILE_LIST_METADATA_GREEN.
- KNOWLEDGE_SEARCH_SCAN_GREEN.
- RUNTIME_IDENTITY_CACHE_GREEN.
- RUNTIME_METADATA_CACHE_GREEN.
- SMOKE_KNOWLEDGE_ROOTS_ISOLATION_GREEN.
- Business bootstrap self-test on PowerShell 7: GREEN.
- Business bootstrap self-test on Windows PowerShell 5.1: GREEN.
- Final exact deployed 198-tool MCP smoke: GREEN.
- Live batch quote/metacharacter boundary regression: GREEN.
- Live VS developer environment: GREEN with zero internal transport-variable leakage.
- GitHub workflow YAML parse: GREEN.
- git diff --check: GREEN.
- Static TODO/FIXME/HACK/legacy/blocking scan: no material hits.
- NuGet vulnerable-package scan in checked projects: no vulnerable packages reported.
- Gitea tray ModelContextProtocol package: 2.2.0; current package scan clean.
- Context7/official MCP C# SDK lifecycle check: Streamable HTTP client usage, direct tool call and transport disposal aligned with current docs.
- Gitea degraded-path test: Caddy deliberately stopped -> tray status non-green/exit 1 -> tray restart -> backend/proxy healthy again.
- Installed Tray Gitea status/restart integration: GREEN.
- Gitea backend 3001 / proxy 3000 / official MCP 8081: HTTP 200.
- Secure MCP Tunnel: process_running=true, healthy=true, ready=true.
- Hidden watchdog: visible user-session PowerShell window count 0.

## Toolchain verified

- Windows SDK 10.0.28000.0
- Temurin JDK 25.0.4.1 LTS
- Flutter 3.47.5 stable
- Dart 3.13.4 stable
- pnpm 12.4.2
- Yarn Modern 4.18.0
- Bun 1.4.2
- GitHub CLI 2.101.0

## Logging decision

The hourly Talvora-LogMaintenance task bounds tunnel/client and small operational logs and trims completed job stdout/stderr. Live-job stdout/stderr are intentionally left intact until job exit so offsets/full output semantics are preserved.

## Remaining work

Earlier runtime audit is closed. Control Center implementation findings discovered afterward are tracked below.

The historical runtime audit was finalized under an earlier explicit commit/push instruction. The current Control Center batch is intentionally uncommitted because the user has not requested commit/push; verify the authoritative working-tree state with `git status`.

If a future source/runtime change is made, use the mandatory live-update gate:
source -> targeted test -> canonical installer build -> deploy -> system_info/PID/version-root check -> relevant live behavior -> source-vs-installed SHA256 verification when finalizing.

33. [FIXED 2026-09-19 Control Center Faz 1] Ana Talvora tray shutdown yarisi kapatildi. Talvora ve Gitea async operasyonlari lifetime cancellation ile unwind ediyor; koordinasyon semaphore'lari aktif finally bloklarinin altindan dispose edilmiyor. Faz 3 tek-tray refactoru da ayni modeli koruyor.
34. [FIXED 2026-09-20 Canonical Independent Deploy] Canonical installer Talvora LocalSystem MCP servisinin dogrudan child process'i olarak calistirildiginda mevcut Talvora servisini silme/yenileme adimi self-update baglaminda takilabiliyor. 2026-09-19 denemesinde servis Running+Disabled kaldi ve installer `Windows service silinemedi: Talvora` timeout'u verdi. Deploy launcher installer'i Task Scheduler altinda bagimsiz SYSTEM process olarak baslatmali veya installer self-hosted parent/descendant senaryosunu guvenli ele almali. 2026-09-20 resolution: `scripts\Deploy-Windows-Installer.ps1` canonical on-demand hidden SYSTEM Task Scheduler launcher olarak eklendi; installer Talvora service process tree'sinden ayrildi. Windows PowerShell 5.1 parse ve `CanonicalDeployUsesIndependentSystemTask` source regression GREEN.
35. [FIXED 2026-09-19 Control Center Faz 4 hazirligi] Tray startup ve Control Center ayni anda managed-MCP recovery cagirirsa atomic dosya bozulmasa da cift merge/write olusabiliyordu. `ManagedMcpRegistryCoordinator` artik process-wide `RecoveryGate` ile recovery/merge/write zincirini serialize ediyor.

36. [FIXED 2026-09-19 Control Center UI] Canli Control Center penceresi `Wpf.Ui.Controls.FluentWindow` olarak acildiginda gerekli Wpf.Ui control resource/template zinciri uygulama seviyesinde yuklu olmadigi icin kullaniciya beyaz/bos pencere gorunebiliyordu. Urun kararindaki "temel WPF + secici Wpf.Ui" modeline donuldu: ana pencere standart WPF `Window`, Wpf.Ui yalniz secici bagimlilik olarak kaldi. Genisletilmis Control Center smoke dashboard+detail+lifecycle akisini GREEN dogruladi.

102. [FIXED 2026-09-19 Control Center UI] Wpf.Ui FluentWindow acilabiliyor ancak `ThemesDictionary` + `ControlsDictionary` Application resources'a yuklenmedigi icin kullanici oturumunda beyaz/bos pencere gorunuyordu. Wpf.Ui 4.3 Context7 ve paket XML dokumaniyla dogrulandi; `ControlCenterApplication` artik Dark ThemesDictionary ve ControlsDictionary yukluyor. Hedefli visual smoke ile bos/beyaz render regresyonu engellenecek.

37. [FIXED 2026-09-19 Control Center test UX] `--control-center-smoke` gerçek kullanıcı oturumunda görünür WPF pencere açtığı için test sırasında dashboard yaklaşık 2 saniye görünüp kapanıyor ve normal runtime kapanması sanılabiliyordu. Smoke `ControlCenterApplication(smokeTest: true)` ile off-screen, taskbar-disinda çalışacak biçimde ayrıldı; normal Control Center yaşam döngüsü değişmedi.

103. [FIXED 2026-09-19 Control Center Visual Language] Kullanici Fluent tasarimi istemedigini belirtti. `WPF-UI` 4.3.0 bagimliligi tamamen kaldirildi; yerine MIT lisansli `MaterialDesignThemes` 5.3.2 ve Material Design 3 kaynaklari eklendi. Control Center koyu Material 3 control-room tokenlari, outlined arama, ikonlu butonlar ve elevation kartlari kullanacak sekilde guncellendi. Off-screen visual smoke Material kaynaklarini ve render yuzeyini GREEN dogruladi.

38. [FIXED 2026-09-19 Control Center Visual Language] Kullanici Material Design yonunu begenmeyip onceki Wpf.Ui/Fluent tabanina donulmesini istedi. `MaterialDesignThemes` 5.3.2 kaldirildi, `WPF-UI` 4.3.0 geri alindi. Ancak varsayilan demo gorunumu kullanilmadi: Talvora'ya ozgu koyu grafit yuzey, tek soguk mavi vurgu, 8/12/16 spacing sistemi, Segoe UI Variable Text, ince border, dusuk elevation, Wpf.Ui SymbolIcon/TextBox/Button, durum pill'leri ve dengeli kart padding'i uygulandi. Kaynak taramasinda MaterialDesign/PackIcon/ElevationAssist/HintAssist eslesmesi 0; targeted Release build 0 warning/0 error; NuGet vulnerability taramasi temiz; source visual smoke GREEN.

39. [FIXED 2026-09-19 Control Center Wpf.Ui Theme Integration] Wpf.Ui'ye geri donuste iki entegrasyon kusuru hedefli smoke tarafindan yakalandi. (1) `WindowBackdropType.Mica`, `ExtendsContentIntoTitleBar=false` iken InvalidOperationException uretiyordu; Mica zorlamasi kaldirildi, FluentWindow + yuvarlatilmis pencere davranisi korundu. (2) ThemesDictionary/ControlsDictionary yuklu olsa da elle olusturulan Application ilk renderda acik/white theme kalabiliyordu; `ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, updateAccent:false)` eklendi. Son source ve exact-installed visual smoke GREEN.

40. [FIXED 2026-09-19 Control Center Health] Gitea dashboard durumu backend/proxy ile sinirli kaldigi icin Gitea MCP server veya Secure MCP Tunnel bozukken kart yanlislikla Hazir gorunebilirdi. `GiteaTrayClient` artik 3001 backend, 3000 Caddy, 8081 resmi MCP ve tunnel `/healthz` + `/readyz` zincirinin tamamini kontrol ediyor. Exact-installed Gitea restart sonrasinda tum halkalar HTTP 200 / Running olarak dogrulandi.

41. [FIXED 2026-09-19 Control Center Lifecycle] Standard-user Talvora tray, LocalSystem Talvora servisi durduktan sonra MCP broker artik mevcut olmayacagi icin servisi tekrar baslatamiyordu. Installer artik yalniz kurulum kullanicisinin SID'ine Talvora servisi icin query/start/stop/interrogate haklarini veren sinirli service DACL ekliyor; service config/delete/DACL degistirme haklari SYSTEM/Admin'de kaliyor. Canli SDDL ve `MSI\tayla` kullanici tokeniyla stop=0 -> Stopped -> start=0 -> Running -> `/healthz` 200 regresyonu GREEN.

42. [FIXED 2026-09-19 Gitea Lifecycle] Tam zincir Gitea restart ilk uygulamada Caddy `StopPending` durumunda takildi; PowerShell `Stop-Service` kendisi bloklandigi icin PID fallback'ine ulasilamiyordu. Stop istegi `sc.exe stop` ile non-blocking hale getirildi; 4 saniyelik kontrollu graceful pencere sonunda servis durmadiysa ProcessId force terminate ediliyor ve SCM Stopped bekleniyor. Exact-installed `--gitea-restart` GREEN; Gitea/Caddy Running, MCP 8081 HTTP 200, tunnel health/ready HTTP 200.

43. [FIXED 2026-09-19 Talvora Lifecycle] Talvora Windows service host tarihsel olarak `CanStop=false` kullandigi icin Dashboard Durdur eylemi service ACL dogru olsa bile Windows Stop controlunu kabul etmiyordu. `WindowsServiceLifetime.CanStop=true` yapildi; pause kapali, shutdown acik kaldi. Exact deployed service `canStop=true` raporluyor ve standard-user stop/start + health regresyonu GREEN.

44. [FIXED 2026-09-19 Control Center Faz 5] Dashboard/detail lifecycle yuzeyi tamamlandi: kartlarda baglamsal ana eylem, detail'de Start/Stop/Restart, stop confirmation, kullanici-oturumluk manual-stop suppression ve varsayilan kapali Wpf.Ui `CardExpander` teknik ayrintilari eklendi. Talvora tunnel stop/reconnect regresyonu stop=0, reconnect=0, health=true, ready=true; exact-installed Control Center visual smoke GREEN.

45. [FIXED 2026-09-19 Control Center Window Chrome] Control Center `FluentWindow` kullanmasina ragmen icerikte gercek bir `Wpf.Ui.Controls.TitleBar` yoktu; bu nedenle kullanici pencereyi surukleyemiyor ve minimize/maximize/close caption dugmelerini goremiyordu. Explicit Wpf.Ui TitleBar eklendi; close/minimize/maximize acik, ForceShutdown kapali, pencere resize edilebilir ve close yine tray'i kapatmadan pencereyi gizliyor. Visual smoke artik TitleBar renderini, caption control ayarlarini ve kullanilabilir drag surface yuksekligini dogruluyor.

46. [FIXED 2026-09-19 Control Center Window UX] Pencere boyut/konum kaliciligi, ekran disina kacmis kayitlar icin virtual-desktop recovery, dar genislikte responsive header, Ctrl+F / Ctrl+R / F5 / Esc / Alt+Left klavye akislari ve DPI dogrulamasi eklendi. Durumlar renk disinda metinle de aktariliyor; gorunen teknik kopya Turkce ana ifade + gerektiginde parantez ici teknik terim modeline cekildi.

47. [FIXED 2026-09-19 Control Center Faz 6 Recovery] Onceki runtime'da Talvora tunnel icin sinirli reconnect backoff vardi ancak Talvora servisi Offline iken aktif auto-start yapilmiyor, Gitea ise yalniz polling ile izleniyordu. Faz 6 tamamlandi: startup'ta Talvora -> Gitea sirali auto-start/recovery, her MCP icin capped exponential backoff (5/10/20/40/60/120/300s), safe auto-repair, manual-stop exclusion, 4 ardisik hata veya 2 dakika sonrasinda serious-incident classification, ciddi olayda bir kez Control Center auto-open + bildirim ve yalniz ciddi olaydan toparlanmada tek recovery bildirimi eklendi. Exact-installed `--self-test` GREEN. Canli fault injection'da `Gitea MCP Tunnel` task'i elle durduruldu; manuel restart verilmeden 26,5 saniyede `Automatic Gitea MCP recovery succeeded.` logu olustu, Gitea MCP Server + Tunnel Running ve 3001/3000/8081 + tunnel health/ready kontrolleri GREEN oldu.

48. [FIXED 2026-09-19 Tray Reconnect Cleanup] Tray uzerinden Talvora yeniden baglama akisi `ManagedMcpSessionState.ClearManualStop("talvora")` ve recovery-state resetini iki kez arka arkaya cagiriyordu. Davranis bozukluguna yol acmiyordu ancak gereksiz tekrar temizlendi; tek reset akisi korundu.

49. [FIXED 2026-09-19 Control Center Faz 7 Events and Logs] Faz 7 tamamlandi. Current-user `events.json` structured event store; Info=3 gun, Warning=14 gun, Error=30 gun retention; 6 saatlik dedup/grouping; 500 kayit hard cap; dashboard Son olaylar ozeti; ayni pencerede expandable event panel; olay arama/severity filtresi; 2 saniyelik canli raw `tray.log` gorunumu; raw arama/kategori filtresi/kopyalama ve son 512 KB/1200 satir okuma limiti eklendi. `tray.log` 3 MiB + tek `.1` arsiv ile bounded. Recovery/lifecycle/startup olaylari structured store'a baglandi. Source `--self-test` retention/dedup/bounds kontratlarini, visual smoke event/raw-log UI'ini dogruluyor.

50. [FIXED 2026-09-19 Control Center Smoke Placement Isolation] Off-screen visual smoke `ControlCenterWindow(smokeMode:true)` kullanmadigi icin test kapanisinda gercek `window-placement.json` dosyasini -32000 koordinatlariyla ezebilirdi. Application artik smokeMode'u pencereye dogrudan geciriyor; smoke modunda restore/save tamamen kapali. Hedefli testte smoke oncesi/sonrasi placement SHA-256 ayni kaldi.

51. [FIXED 2026-09-19 Control Center Faz 8 Tunnel Provisioning] Secure MCP Tunnel provisioning tamamlandi. Current-user DPAPI Protect/Unprotect round-trip, ayri Admin ve Runtime credential dosyalari, mevcut Runtime key reuse, read-only `admin tunnels get` ile org/workspace scope kesfi, Admin key ile remote tunnel create, 30 saniye activation bekleme, business.json + native profile generation, `runtimes connect`, status process_running/healthy/ready dogrulamasi, generic lifecycle ile tunnel stop/start/restart ve startup provisioning eklendi. Admin key runtime profile/daemon'a aktarilmiyor; Runtime connect argumaninda yalniz `env:CONTROL_PLANE_API_KEY` var. Secret-aware diagnostic redaction self-test GREEN; tray/events log taramasinda key-benzeri eslesme 0. Kurulu v0.0.14 read-only preflight: organization scope=1, workspace scope=1, AdminCredentialPresent=False.

52. [FIXED 2026-09-19 Control Center Faz 9 First Run] Uzun setup wizard yerine dashboard-first first-run/incomplete-setup modeli eklendi. Ilk kullanimda (kayitli pencere konumu yoksa) dashboard otomatik acilir; kritik eksik tunnel kurulumu varsa her acilista tek `Kurulumu Tamamla` karti gorunur. Admin API key yalniz gercekten yeni tunnel olusturulacagi zaman istenir ve current-user DPAPI ile saklanir; mevcut iki tunnel hazir oldugu icin bu makinede Admin key istenmez. Ayrica Settings sayfasi eklenmedi.

53. [FIXED 2026-09-19 Gitea Cross-Process Lifecycle Race] Exact-installed `--gitea-restart` sirasinda ana Tray health dongusu ara Degraded durumu gorup ikinci bir automatic recovery baslatabiliyordu; sonuc saglikli olsa da gereksiz cift lifecycle islemi olusuyordu. Gitea Start/Stop/Restart icin session-local named Semaphore tabanli cross-process operation guard eklendi. Auto-recovery guard doluyken failure/backoff yazmak yerine kullanici isleminin bitmesini bekleyecek sekilde davranir. Guard acquire/block/release kontrati `--self-test` icinde GREEN.

54. [FIXED 2026-09-19 Native Installer Runtime Regression] `NativeInstallerRuntimeRegression.ps1` tarihsel `CanStop=false` beklentisini koruyordu ve Control Center lifecycle ile celisiyordu. Regression artik canli Talvora servisinde `ServiceCanStop=True` ve `ServiceCanPauseAndContinue=False` bekliyor. Exact-installed runtime regression `NATIVE_INSTALLER_RUNTIME_GREEN`; ToolCount=198.

55. [VERIFIED 2026-09-19 Control Center Final Gate] Exact-installed `74601595e4727d1cfd09388ae3c8b4d25dc136fa-dirty-89135c8503e5` icin self-test, Gitea restart, Control Center visual smoke, tunnel provisioning preflight, Talvora reconnect, Gitea status ve native installer runtime regression GREEN. Interactive mutex regresyonunda ikinci `--replace` instance 10 saniye sonra dogru sekilde cikti, canli Tray PID 2356 olarak degismedi. Son ve tek final 198-tool MCP smoke `TALVORA MCP SMOKE GREEN` ile exit 0 tamamlandi.

56. [FIXED 2026-09-19 GitHub Push Authentication Context] `talvora_git_run` LocalSystem hesabinda calistigi icin HTTPS GitHub push'lari kullaniciya ait GCM/OAuth credential kasasini goremedi; credential eksikken GCM gorunmeyen browser/GUI prompt'u bekliyor ve MCP tool call timeout'a dusuyordu. Kalici cozum: GitHub HTTPS push'lari otomatik olarak logged-on Windows kullanicisinin primary token/session'i icinde calistiriliyor; stdout/stderr/exit code LocalSystem MCP'ye geri tasiniyor. Interactive runner push sirasinda `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=0`, `GCM_GUI_PROMPT=0` enjekte ediyor; credential yoksa hang yerine hizli fail oluyor. `MSI\tayla` profili GCM OAuth + Windows Credential Manager (`wincredman`) ile `bingoweb` hesabina baglandi. Kullanici-baglamli `git push --dry-run github main` exit 0 ile dogrulandi.

## 2026-09-19 — Playwright MCP sürüm pinleme düzeltmesi
- Risk: İlk runtime hazırlığında `@playwright/mcp` ve `@modelcontextprotocol/sdk` exact sürümle yazılmıştı; bu yeni proje kuralına aykırıydı.
- Düzeltme: dependency specifier'lar `latest` yapıldı ve npm lockfile silindi. Bundan sonra sürüm pinleme yapılmayacak; current stable/latest kullanılacak.
- Durum: KAPALI.


### PW-MCP-001 — localhost Host allow-list port uyumsuzluğu
- Bulgusu: İlk Streamable HTTP initialize denemesi 403 `Access is only allowed at 127.0.0.1, localhost` döndürdü.
- Etki: MCP initialize/tool-list zinciri başlayamıyor; process/listener tek başına Ready sayılmamalı.
- Neden adayı: HTTP Host header port içeriyor (`127.0.0.1:8931`), allow-list port içermiyordu.
- Plan: Host kontrolünü kapatmadan 127.0.0.1:8931 ve localhost:8931 biçiminde düzelt, yalnız initialize/list testini tekrar et.
- Durum: AÇIK.


### PW-MCP-001 kapanış
- Düzeltme: `--allowed-hosts` değeri `127.0.0.1:8931,localhost:8931` olarak portlu Host header ile uyumlu hale getirildi; kontrol devre dışı bırakılmadı.
- Hedefli doğrulama: initialize GREEN, 45 tools/list GREEN, `browser_navigate` GREEN, `browser_snapshot` GREEN.
- Durum: KAPALI.


57. [FIXED 2026-09-20 Source Reconciliation] Port 8931 zaten Talvora LocalSystem -> WindowsSessionLauncher ile Session 1'de baslatilan extension-mode Playwright MCP tarafindan tutulurken gecici smoke scheduled task ayni portta ikinci instance baslatmayi denedi ve EADDRINUSE ile cikti. Kalici cozum: tek canonical launcher/runtime sahibi belirlenecek, gecici task kaldirilacak ve Control Center lifecycle ayni instance'i yonetecek.
58. [FIXED 2026-09-20 Source Reconciliation] Current-user Playwright runtime package.json `latest` kullaniyor ancak npm kurulumunun `node_modules/.package-lock.json` hidden lock metadata'si mevcut. Yeni proje kuralina uymak icin canonical provisioning `--package-lock=false` kullanacak ve hidden lock metadata temizlenecek; runtime package manifest exact version tasimayacak.

## 2026-09-19 — Playwright Extension welcome-tab interference
- Bulgu: `--extension --profile-dir-name Default` ile bağımsız smoke sırasında resmî Playwright Extension kullanıcı Chrome'unda welcome/connection sayfasını tekrar tekrar öne getirerek aktif ChatGPT sekmesini bozdu.
- Etki: Kullanıcı masaüstü akışı kesildi; bu çalışma biçimi production default için uygun değil.
- Düzeltme kararı: Extension modu ana managed runtime olmaktan çıkarıldı. Extension server + smoke/helper node süreçleri durduruldu. Production/smoke modeli ayrı current-user persistent Chrome profile + localhost HTTP + headless browser olarak değiştirildi. Extension yalnız ileride açıkça mevcut oturuma/SSO'ya ihtiyaç olan isteğe bağlı bağlanma yolu olarak değerlendirilebilir.
- Durum: KAPALI.


### PW-MCP-002 — Extension managed-server restart döngüsü kullanıcı Chrome sekmesini bozuyor
- Bulgusu: `--extension` ile kalıcı Scheduled Task çalıştırıldığında extension bağlantı sayfası kullanıcı Chrome'unda açılıyor; bu bağlantı sayfası kapandığında MCP process `Exit=-1` ile sonlanıyor. Task restart policy tekrar bağlantı/welcome sayfası açarak kullanıcının aktif ChatGPT sekmesini kesintiye uğratıyor.
- Etki: Üretim kalitesinde background managed MCP için kabul edilemez kullanıcı müdahalesi ve görünür browser yan etkisi.
- Anlık önlem: `Talvora Playwright MCP` Scheduled Task durduruldu ve Disabled yapıldı; welcome tekrar döngüsü kesildi.
- Kalıcı karar: Managed/autostart Playwright MCP extension modunda çalışmayacak. `MSI\\tayla` oturumunda ayrı Talvora persistent Chrome user-data-dir ile `--browser chrome --headless` kullanılacak. Mevcut Playwright Extension kurulu kalacak ancak managed health/recovery'nin zorunlu parçası olmayacak.
- Durum: DÜZELTİLİYOR.


59. [FIXED 2026-09-19 Playwright Generic Recovery Compile Break] Kesintiden kalan kaynakta `_genericMcpTimer` `MaintainGenericManagedMcpsAsync()` metodunu çağırıyor ancak metod gövdesi yoktu; targeted Talvora.Tray Release build CS0103 ile kırıldı. Kalıcı çözüm: generic managed-MCP health/recovery döngüsü capped backoff + manual-stop suppression + serious incident/event akışıyla tamamlanacak ve timer startup/dispose yaşam döngüsüne bağlanacak.


### PW-MCP-003 — Scheduled Task stop child process ağacını deterministik kapatmıyor
- Bulgusu: `Stop-ScheduledTask` sonrası launcher `pwsh.exe` ve child `node.exe` yaşamaya devam etti; yeni task 8931 portu dolu olduğu için başlayamadı (`LastTaskResult=1`).
- Etki: Start/Restart işlemlerinde eski MCP instance'ı kalabilir ve yeni instance port çakışması yaşayabilir.
- Anlık düzeltme: Eski launcher process tree PID bazlı kapatılacak; yeni supervisor parent-watch ile child cleanup yapacak.
- Kalıcı düzeltme: Generic lifecycle Stop/Restart scheduled-task zinciri Playwright launcher için task stop sonrası ilgili process/port kapanışını doğrulayacak.
- Durum: DÜZELTİLİYOR.


60. [FIXED 2026-09-19 Talvora Service Registration Loss During Upgrade] Gerçek installer logu 22:56:17'de `Service did not accept STOP; deleting registration before terminating PID=2188.` kaydını, ardından 22:56:32'de `TimeoutException: Windows service silinemedi: Talvora` hatasını gösterdi. Eski akış Talvora servis kaydını upgrade sırasında `sc delete` ile siliyor, fakat rollback yalnız `CreateServiceAsync` başarılı olduktan sonra set edilen `switchedService` koşulunda çalışıyordu. Böylece deletion sonrası timeout/CreateService öncesi hata Windows'ta Talvora servis kaydını tamamen yok bırakabiliyordu. Düzeltme: Talvora upgrade yolu artık servis kaydını hiç silmiyor; recovery actions geçici temizleniyor, normal stop deneniyor, gerekirse process tree force-stop ediliyor, mevcut servis `sc config` ile yeni binPath'e atomik reconfigure ediliyor; servis yoksa yalnız fresh/recovery durumda `sc create` kullanılıyor. Rollback artık service-switch başlar başlamaz arm ediliyor ve kullanıcı cancellation token'ından bağımsız `CancellationToken.None` ile eski binary/service/tray durumunu geri kuruyor. `StopAndDeleteServiceAsync` yalnız gerçek kaldırma/legacy servis temizliği için tutuldu.


61. [FIXED 2026-09-19 Playwright Scheduled Task XML Encoding] İlk güvenli live-deploy regression'ında Talvora servis kaydı korunarak yeni build başarıyla ayağa kalktı; installer 82% Playwright aşamasında `schtasks /Create ... /XML ...` çağrısında `(1,40): encoding değiştirilemiyor` hatası verdi ve yeni rollback yolu eski Talvora binary'sini başarıyla geri yükledi. Kök neden: Task Scheduler XML deklarasyonu/çıktısı UTF-8 idi; Windows native Task Scheduler XML biçimi UTF-16'dır. Düzeltme: XML declaration `UTF-16`, dosya yazımı `Encoding.Unicode` (BOM'lu UTF-16LE) yapıldı. Servis-kaybı yaşanmadı; rollback doğrulandı.


62. [FIXED 2026-09-19 Control Center Smoke Refresh Race] Exact-installed final UI smoke `Control Center dashboard did not load any managed MCP` ile false-negative verdi; aynı ana Tray hemen önce registry Count=3 yüklemişti. Kök neden: pencere açılırken `BringToForeground()` fire-and-forget `RefreshDashboardAsync()` başlatıyor; smoke 2 sn sonra yeniden çağırdığında `_refreshGate.WaitAsync(0)` doluysa ikinci çağrı anında dönüyor ve `_snapshot` henüz null/boş kalabiliyordu. Düzeltme: smoke scenario 15 sn bounded deadline içinde mevcut refresh'in tamamlanmasını/registry snapshot'ın dolmasını bekleyip 250 ms aralıkla yeniden dener. Production dashboard refresh davranışı değiştirilmedi.


63. [FIXED 2026-09-20 Source Reconciliation] `scripts/Build-Windows-Installer.ps1` içinde `$PlaywrightPayload` ataması, payload klasörü oluşturma ve Playwright asset copy/manifest/assert akışı yinelenmiş. Bugün idempotent görünüyor fakat ileride asset listesi/validation ayrışırsa canonical installer payload'ının tutarsız üretilmesine ve gereksiz IO'ya yol açabilir. Tek canonical Playwright payload hazırlama bloğuna indirilmeli.


64. [FIXED 2026-09-20 Source Reconciliation] Kullanıcı kuralına uygun olarak Talvora/Tray NuGet bağımlılıkları `Version="*"` ile her build'de en güncel stable sürümü çözümlüyor. Bu pinleme değildir ve korunmalıdır; ancak installer/runtime metadata yalnız source commit/fingerprint kaydediyor. Aynı commit farklı tarihlerde farklı resolved NuGet sürümleriyle farklı binary üretebilir ve olay incelemesinde hangi dependency setinin canlı olduğu kanıtlanamaz. Çözüm pinlemek değil; canonical build sırasında resolved package name/version listesini artifact/runtime metadata'ya yazmak ve dashboard/version detayında gerektiğinde göstermek.


65. [FIXED 2026-09-20 Source Reconciliation] `ControlCenterComponentHealthService.ProbeProcessAsync` PowerShell 7 yoksa Windows PowerShell 5.1'e düşüyor; üretilen script `string.Contains($marker,[StringComparison]::OrdinalIgnoreCase)` overload'unu çağırıyor. Bu overload .NET Framework/Windows PowerShell 5.1'de yoktur ve fallback health probe MethodException ile bozulur. Mevcut makinede pwsh 7 bulunduğu için canlı sistem etkilenmiyor; ancak fallback sözleşmesi gerçekte çalışmıyor. Çözüm: PS5.1 uyumlu `IndexOf($marker,[StringComparison]::OrdinalIgnoreCase) -ge 0` kullanmak.


66. [FIXED 2026-09-20 Source Reconciliation] `PlaywrightManagedMcpRecoveryDiscovery` yalnız launcher dosyası, package.json veya profile klasöründen en az biri varsa kayıt döndürüyor. Scheduled task/tunnel config/state mevcut fakat bu üç artifact kayıpsa managed registry Playwright'ı tamamen düşürebilir; bu da istenen first-run/incomplete-setup kartını görünmez yapar. Discovery koşulu canonical task ve `McpTunnel\business.json` gibi sahiplik kanıtlarını da hesaba katmalı; eksik artifact'ler card state'i Attention/Incomplete yapmalı, kaydı yok etmemeli.


67. [FIXED 2026-09-20 Source Reconciliation] Generic `ControlCenterLifecycleService` bütün Playwright Start/Stop/Restart scriptlerini `RunPrivilegedPowerShellAsync` ile `http://127.0.0.1:7676/mcp` Talvora LocalSystem broker üzerinden yürütüyor. Oysa `Talvora Playwright MCP` task'ı current-user/Interactive/Highest ve kullanıcı bağlamından doğrudan yönetilebilir. Talvora servisi offline olduğunda Playwright kartı bağımsız olarak Start/Stop/Restart edilemiyor; gerçek tray logu 22:57:46'da `MCP=playwright; Operation=Restart ... connection refused (127.0.0.1:7676)` ile bunu kanıtlıyor. Çözüm generic lifecycle executor'a component ownership/required-privilege bazlı current-user task yolu eklemek; yalnız gerçekten privileged Windows-service işlemlerini Talvora broker'a göndermek. Böylece Playwright recovery Talvora servisinden bağımsız kalır.


68. [FIXED 2026-09-20 Source Reconciliation] `ManagedMcpSessionState` manual-stop bilgisini yalnız static in-memory `ConcurrentDictionary` içinde tutuyor. Tray process replace/crash/restart olduğunda aynı Windows logon session devam etse bile suppression sıfırlanır; `InitializeManagedRegistryAndRecoveryAsync` AutoStart kayıtlarını yeniden başlatabilir. Bu, istenen `manual Stop session suppression` sözleşmesini bozar ve kullanıcı elle durdurduğu Playwright/Talvora/Gitea'nın Tray yenilenince geri açılmasına yol açabilir. Çözüm: current interactive SessionId/logon identity ile scope edilmiş ephemeral session-state dosyası veya named kernel object kullanmak; logoff/yeni session'da otomatik sıfırlamak.


69. [FIXED 2026-09-20 Source Reconciliation] `Start-PlaywrightMcp.ps1` `server.log` için 5 MiB rotate kontrolünü yalnız task başlangıcında yapıyor, ardından supervisor stdout/stderr `*>> server.log` ile task ömrü boyunca aynı dosyaya ekleniyor. Uzun süre çalışan Playwright MCP'de log restart olmadan sınırsız büyüyebilir. `--output-max-size` yalnız Playwright output artifacts içindir, launcher/server logunu sınırlamaz. Çözüm: supervisor/launcher logging'i size-aware rolling writer'a taşımak veya bounded log sink kullanmak; raw-log viewer aynı rolling dosyaları okumalı.


70. [FIXED 2026-09-20 Source Reconciliation] `ManagedMcpOperationCoordinator` yalnız `GiteaTrayClient` tarafından kullanılıyor; `ControlCenterLifecycleService.ExecuteGenericAsync` Playwright için hiçbir named semaphore/operation lease almıyor. Generic recovery timer kendi `_genericMcpOperationGate`'iyle yalnız recovery döngüsünü serialize ediyor, Control Center kullanıcı Start/Stop/Restart işlemlerini serialize etmiyor. Sonuç: auto-recovery ve kullanıcı Restart/Stop aynı anda scheduled task, process tree, tunnel ve smoke state üzerinde yarışabilir. `MaintainGenericManagedMcpsAsync` içindeki `catch (ManagedMcpOperationInProgressException)` Playwright tarafında bugün fiilen erişilemez. Çözüm: `ExecuteGenericAsync` girişinde registration.Id bazlı `ManagedMcpOperationCoordinator.TryAcquire` zorunlu olmalı; UI/recovery aynı lease'i paylaşmalı.


71. [FIXED 2026-09-20 Source Reconciliation] Playwright `--shared-browser-context` ile tüm HTTP clients aynı browser context'i paylaşıyor (Microsoft resmi dokümanı). Health smoke `browser_tabs action=new` ile sekme açıyor fakat cleanup `browser_tabs action=close` çağrısında index vermiyor; resmi tool semantiğinde bu `current tab`ı kapatır. Tunnel bağlandıktan sonra ChatGPT eşzamanlı tab seçerse smoke yanlış sekmeyi kapatabilir ve aktif otomasyonu bozabilir. Çözüm: `new` sonucundan oluşturulan tab index/identity'yi güvenilir biçimde yakalayıp explicit index ile kapatmak ve tercihen gerçek browser smoke'u tunnel connect'ten önce, external clients erişmeden çalıştırmak.


72. [FIXED 2026-09-20 Source Reconciliation] `RecordBrowserSmokeAsync` generation değerini smoke tamamlandıktan sonra `server-start.json`dan yeniden okuyor; smoke başında hangi generation üzerinde çalıştığı capture edilmiyor. Eşzamanlı restart olursa teorik olarak eski client/smoke yeni generation marker'ına yazılabilir. Bug 70'teki generic operation-lock eksikliği bu yarışı mümkün kılıyor. Çözüm: generation'ı smoke başlamadan capture etmek, tool calls sonunda generation'ın değişmediğini doğrulamak ve marker'a yalnız aynı captured generation için atomik yazmak.


73. [FIXED 2026-09-20 Source Reconciliation] `TunnelProvisioningScope` `ReferenceTunnelId` ve `UpdatedAtUtc` saklıyor fakat `GetOrDiscoverScopeAsync` cache'i kullanırken bu alanları hiç kontrol etmiyor. Reusable runtime source farklı bir tunnel'a dönerse veya organization/workspace scope zamanla değişirse eski `tunnel-scope.json` yeni tunnel creation için körlemesine kullanılabilir; yanlış workspace/org scope ile remote tunnel oluşturma ya da creation failure riski var. Çözüm: cache yalnız `ReferenceTunnelId == reference.Config.TunnelId` ve bounded TTL içinde ise kullanılmalı; aksi durumda `tunnels get` ile yeniden keşfedilmeli.


74. [FIXED 2026-09-20 Source Reconciliation] `Assess` ve `FindReusableRuntimeSource` runtime credential hazır kararını `runtime-key.dpapi` dosyasının varlığına göre veriyor; current-user DPAPI decrypt/read doğrulanmıyor. Bozuk/kopyalanmış/başka kullanıcıya ait blob setup'ı tamamlanmış gösterebilir fakat Connect/Provision sırasında patlar. Çözüm: secret içeriğini loglamadan bounded DPAPI decrypt validation eklemek; `HasRuntimeCredential` semantiğini gerçekten kullanılabilir credential olarak tanımlamak.


75. [FIXED 2026-09-20 Source Reconciliation] `Configure-ChatGPT-Business.ps1` artık `latest` release'i çözüyor ve 2026-09-19 itibarıyla resmi latest v0.0.14 ile canlı sistem eşleşiyor. Ancak Control Center startup/provisioning mevcut `BusinessConfig.TunnelClientVersion` ve binary path'ini yeniden kullanıyor; yeni tunnel-client release çıktığında çalışan kurulum otomatik latest-check/upgrade yapmıyor. Kullanıcının sürekli-latest kuralı için periodic/bounded release check + atomic client update mekanizması eksik; pinleme yapılmamalı.


76. [FIXED 2026-09-20 Source Reconciliation] Ayrıntı ekranında alan etiketi `Profil / durum yolu` fakat değer yalnız `registration.ProfilePath` (`...\PlaywrightMCP\profile`). `RuntimeGenerationStatePath`, `BrowserSmokeStatePath` veya Playwright state root gösterilmiyor. Kullanıcının istediği profile/state path görünürlüğü tam değil ve etiket mevcut içeriği olduğundan fazla gösteriyor. Çözüm: profile path ve runtime/state paths ayrı teknik alanlar olarak gösterilmeli.


77. [FIXED 2026-09-20 Source Reconciliation] `BuildDetailView` durum/sürüm, bileşenler, lifecycle ve teknik ayrıntıları gösteriyor ancak selected MCP'ye ait `son olaylar` bölümü yok. Olay listesi dashboard seviyesindeki ayrı expander'da; kullanıcı Playwright ayrıntı ekranında son olayları istemişti. Çözüm: detail view'e selected registration.Id ile filtrelenmiş son N structured event + severity/time özeti eklemek; global dashboard event paneli korunmalı.


78. [FIXED 2026-09-20 Source Reconciliation] `tests/Talvora.Smoke --playwright-mcp` default endpoint'i `http://127.0.0.1:8931/mcp`; production managed registry endpoint'i `8932/mcp` compatibility proxy'dir. Bu test default haliyle proxy'nin OAuth discovery 404 davranışını, header forwarding'ini ve streamable HTTP passthrough'unu regress etmez. Exact-installed managed probe 8932'yi test ediyor ama unit/smoke harness'ta proxy-specific contract testi eksik. Çözüm: production endpoint default 8932 veya ayrı `--playwright-proxy` contract scenario eklemek; backend smoke ayrıca korunabilir.


79. [FIXED 2026-09-20 Source Reconciliation] Canlı kanıt: `browser-smoke.json` passedAt `2026-09-19T23:14:53.8507066+03:00`, fakat mevcut Chrome ana process PID 3704 `2026-09-19T20:24:35.4649172Z` (=23:24:35+03) tarihinde, yani smoke'tan yaklaşık 10 dakika sonra açıldı. MCP backend generation değişmediği için `HasCurrentBrowserSmoke` eski marker'ı hâlâ geçerli sayıyor. Browser demand-launch/crash/relaunch aynı MCP generation içinde gerçekleştiğinde Ready durumu mevcut browser instance için navigate/snapshot kanıtına sahip olmayabilir. Çözüm: browser instance identity/epoch takibi veya bounded smoke freshness + browser relaunch detection; yeni browser instance görüldüğünde marker invalidate edilip güvenli smoke yeniden yapılmalı.


80. [FIXED 2026-09-20 Source Reconciliation] `supervisor.mjs` backend Node child'ı hemen `spawn` ediyor; ardından `writeProcessState()` ve `server.listen()` geliyor. `fs.writeFileSync/renameSync`, child `error` veya proxy listen `error` gibi supervisor-fatal durumlarda central failure handler yok. Windows'ta parent Node exit olması child process tree'yi garantiyle sonlandırmaz; backend 8931'i ve Chrome persistent profile'ı orphan bırakabilir. Scheduled task restart'ı sonra port/profile conflict ile döngüye girebilir. Çözüm: child `error`, server `error`, uncaughtException/unhandledRejection için tek idempotent shutdown; Windows process tree/job-object veya `taskkill /T /F` benzeri deterministic descendant termination; state cleanup yalnız gerçekten tree kapandıktan sonra.


81. [FIXED 2026-09-20 Source Reconciliation] `supervisor.mjs` request handler incoming `req.headers.host` ile doğrudan `new URL(req.url, 'http://'+host)` kuruyor ve handler/global exception guard yok. İzole Node testi `Host: [` isteğinin HTTP parser'dan geçtiğini ve callback içinde `UNCAUGHT:ERR_INVALID_URL` ürettiğini doğruladı. Secure tunnel üzerinden böyle bir malformed Host ulaşırsa Playwright supervisor düşebilir; Bug 80 nedeniyle backend orphan da kalabilir. Çözüm: URL parse try/catch + fixed local base/yalnız pathname extraction, invalid Host için 400; request handler hiçbir girdide process-level exception üretmemeli.


82. [FIXED 2026-09-20 Source Reconciliation] `tests/NativeInstallerSourceRegression.ps1` hedefli çalıştırmada exit 1 verdi; GitHub `windows-ci.yml` bunu zorunlu `Validate source contracts` gate olarak çalıştırıyor. Daha önemlisi `InstallerCanReplaceNonStoppableService` hâlâ `deleting registration before terminating PID` ifadesini başarı kriteri sayıyor ve bugün TRUE dönüyor, çünkü tehlikeli generic `StopAndDeleteServiceAsync` kodu dosyada Cloudflared/gerçek delete senaryoları için hâlâ mevcut; test Talvora upgrade yolunun artık bu metodu çağırmadığını doğrulamıyor. Böylece az önce kökten düzeltilen servis-kaybı bug'ı için regression koruması gerçekte yok. Aynı test yeni mimariye taşınan reconnect/recovery sözleşmelerini eski kaynak-patternlerle aradığı için `TrayReconnectIsNative=False` ve `TrayAutoReconnectsAfterStartup=False` verip CI'ı da gereksiz kırıyor. Çözüm: Talvora upgrade path'inde `StopAndDeleteServiceAsync(ServiceName)` yasaklayan negatif contract + `StopServiceForUpgradeAsync`/`sc config`/early rollback şartlarını doğrulayan test; Tray contract'larını yeni sınıflara göre güncellemek.


83. [FIXED 2026-09-20 Source Reconciliation] `InstallPlaywrightManagedMcpAsync` task XML'ini kurup `/Run` çağrısının yalnız exit code'unu kontrol ediyor; task kabul edilirse npm `@playwright/mcp@latest` resolution, supervisor start, 8931/8932 listen, MCP initialize/tools veya browser smoke beklenmiyor. Node yoksa installer bunu yalnız loglayıp başarıyla devam ediyor; Node mevcut fakat npm/network/CLI/proxy startup bozuksa da installer sonradan exit 0 verebilir. Production hedefi Playwright'ın gerçek çalışır kurulumu olduğundan installer success semantiği eksik. Çözüm: first-run incomplete mode yalnız dependency gerçekten yoksa bilinçli Attention olarak işaretlenmeli; dependency mevcutsa bounded post-task readiness (task running + ports + MCP initialize/tools, browser smoke tercihen Tray startup recovery ile) installer gate olmalı ve başarısızlık rollback/clear diagnostic üretmeli.


84. [FIXED 2026-09-20 Source Reconciliation] `PlaywrightManagedMcpRecoveryDiscovery.ProtocolProbe.RequiredTools` yalnız `browser_tabs`, `browser_navigate`, `browser_snapshot` kontrol ediyor. Canlı 0.0.82 yüzeyi click/type/fill/evaluate/network/screenshot ve etkin `vision,pdf,devtools` yeteneklerine ait çok daha geniş araç seti sunuyor. Core veya özellikle kullanıcı tarafından etkinleştirilen capability araçları kaybolsa/misconfigure olsa ToolCount yüksek kalabilir ve dashboard yine Ready olabilir. Çözüm sürümdeki tüm tool listesini pinlemek değil; stable capability contracts tanımlamak (ör. core interaction + file/upload + screenshot + pdf + devtools temsilci araçları) ve enabled caps metadata ile beklenen seti dinamik doğrulamak.


85. [FIXED 2026-09-20 Source Reconciliation] Launcher startup stale cleanup `supervisorPid` ve `backendPid` için sırayla `Stop-Process -Force` kullanıyor; supervisor önce zorla öldürüldüğü için kendi graceful child cleanup'ını çalıştıramaz, backend zorla öldürüldüğünde de Windows Chrome descendants'ı otomatik sonlandırmaz. Generic Stop da önce `Stop-ScheduledTask` ile launcher'ı bitirip sonra yalnız state'teki launcher PID tree'sini öldürmeye çalışıyor; launcher çoktan yoksa descendants kaçabilir. Persistent profile kullandığımız için orphan Chrome profile lock ve port/restart sorununa yol açabilir. Çözüm: process-tree ownership'i Windows Job Object veya deterministic descendant traversal ile yönetmek; supervisor/backend/Chrome tek atomik kill-tree domain'i olmalı, supervisor graceful stop önce denenmeli.


86. [FIXED 2026-09-20 Source Reconciliation] `ExecuteGenericAsync(Restart)` configured tunnel için önce `DisconnectExistingAsync` çağırıyor ve `tunnel-client runtimes stop <alias>` nonzero ise işlemi kesiyor. İzole v0.0.14 testi bilinmeyen alias için exit 1 / `alias ... is not known; run create or connect first` döndürdü. Auto-recovery Offline durumda Start seçtiği için çoğu kez kaçınıyor, ancak kullanıcı Restart veya stale config+silinmiş runtime state senaryosunda connect aşamasına hiç ulaşmayabilir. Çözüm: stop/disconnect `already absent/not known/not running` durumlarını idempotent başarı kabul etmeli; diğer hatalar korunmalı.


87. [FIXED 2026-09-20 Source Reconciliation] `StopServiceForUpgradeAsync` upgrade öncesi service failure actions'ı `reset=0 actions=` ile geçici temizliyor. Fonksiyon force-stop sonrasında yine STOPPED olamazsa exception atıyor; outer rollback aynı StopServiceForUpgradeAsync'i tekrar çağırıyor ve o da başarısız olursa `CreateServiceAsync`'e ulaşıp restart recovery policy'yi restore edemiyor. Servis kaydı korunur ama automatic SCM recovery actions boş kalabilir. Çözüm: eski failure configuration'ı capture/restore eden finally veya stop başarısız olsa bile recovery policy restore guarantee; transaction sonucu ne olursa olsun servis recovery ayarları canonical durumda kalmalı.


88. [FIXED 2026-09-20 Source Reconciliation] `InstallPlaywrightManagedMcpAsync` mevcut `Start-PlaywrightMcp.ps1` ve `runtime\supervisor.mjs` dosyalarını overwrite ediyor, task'ı `/End` ile durduruyor ve runtime processlerini öldürüyor; sonra yeni task XML oluşturuyor. Bu aşamadan sonra failure olursa outer installer rollback yalnız Talvora service + önceki Tray'i geri yüklüyor; önceki Playwright assets/task definition/runtime state restore edilmiyor. Yeni Playwright payload/task bozuksa başarılı eski Playwright kurulumu kaybedilebilir. Çözüm: Playwright asset/task snapshot + staged files + task XML export; yeni task/readiness GREEN olduktan sonra commit, failure'da eski assets/task ve runtime'ı restore eden transaction.


89. [FIXED 2026-09-20 Source Reconciliation] Installer `ResolvePlaywrightPowerShell()` pwsh 7 yoksa Windows PowerShell 5.1 seçiyor; ancak `Start-PlaywrightMcp.ps1` stale process-state cleanup içinde `([string]$process.CommandLine).Contains(marker,[StringComparison]::OrdinalIgnoreCase)` kullanıyor. PS5.1/.NET Framework bu 2-arg Contains overload'unu desteklemiyor (Bug 65 için izole test MethodException ile kanıtlandı). İlk start'ta process-state yoksa geçebilir, sonraki restart/recovery'de state varsa launcher kırılır. Çözüm: launcher dahil tüm fallback scriptleri PS5.1-compatible `IndexOf(...,[StringComparison]::OrdinalIgnoreCase) -ge 0` kullanmalı ve fallback için ayrı regression test eklenmeli.


90. [FIXED 2026-09-20 Source Reconciliation] Canlı runtime Node `v24.21.0`; resmi Node release/download sayfasında 2026-09-16 itibarıyla en yeni Current `v26.9.0`, v24.21.0 ise LTS'dir. Chrome 153.0.8010.53 ise 2026-09-17 Windows Stable ile eşleşiyor ve `@playwright/mcp` npm latest 0.0.82 ile canlı package eşleşiyor. Kullanıcının `her zaman en son en modern versiyon` kuralı LTS yerine latest Current gerektiriyor; Node bu kurala uymuyor. Çözüm pinlemek değil, canonical dependency bootstrap'ta latest Node Current çözmek/yükseltmek ve runtime preflight'ta latest-check yapmak.


91. [FIXED 2026-09-20 Source Reconciliation] Installer ve launcher Node/npm'i yalnız `%ProgramFiles%\nodejs\node.exe/npm.cmd` altında arıyor. PATH/current-user/custom Chocolatey location'da geçerli latest Node olsa bile kullanmıyor; gerçekten Node yoksa installer task'ı kurup yalnız `first-run incomplete` logluyor, Chocolatey ile latest Current Node sağlamıyor. Bu hem taşınabilirliği hem üretim-grade first-run'ı zayıflatıyor. Çözüm: deterministic command resolution (Talvora resolve/registry/PATH + canonical preferred location), sürüm/preflight kontrolü ve gerektiğinde kullanıcı kuralına uygun Chocolatey `nodejs` latest kurulumu; `nodejs-lts` kullanılmamalı çünkü latest Current politikası var.


92. [FIXED 2026-09-20 Source Reconciliation] `ControlCenterComponentHealthService.GetTunnelStateAsync` state root'taki `<alias>.url` dosyasını okuyup yalnız `/healthz` ve `/readyz` HTTP success status kontrol ediyor. v0.0.14 `runtimes status <alias> --json` ise alias, tunnel_id, runtime_state, process_running/PID, profile path, healthy/ready ve issues bilgisini veriyor. Stale health URL portu başka local process tarafından yeniden kullanılırsa 2xx yanlış Ready üretebilir; ayrıca doğru runtime/tunnel kimliği doğrulanmıyor. Çözüm: bounded structured status probe ile expected alias+tunnelId+process/profile identity doğrulamak; HTTP endpointleri hızlı sinyal olarak korunabilir.


93. [FIXED 2026-09-20 Playwright Compatibility Proxy SSE Header Flush / Tunnel Startup Probe] Kök neden kesinleştirildi: tunnel-client v0.0.14'ün kullandığı modelcontextprotocol/go-sdk v1.7.0 doğrudan 8931 backend'e birkaç ms içinde bağlanırken 8932 Node compatibility proxy üzerinde standalone SSE GET /mcp response header'ları body gelene kadar istemciye flush edilmediği için Client.Connect 2 saniyelik startup probe deadline'ını aşıyordu. Proxy upstream response yolunda res.flushHeaders() uygulanıp backend MCP readiness preflight ile 8932 yalnız gerçek initialize hazır olduktan sonra expose edildi. Exact-installed doğrulama: Go MCP SDK v1.7.0 + standalone SSE açık bağlantı 8932'de 14.6 ms, tunnel /readyz HTTP 200 ready, production smoke 45 tool GREEN ve yeni tunnel loglarında mcp probe timed out / failed to connect to mcp yok. OAuth discovery 404 WARN upstream tunnel-client'ın düz/no-auth MCP için optional discovery davranışıdır ve readiness'i bloklamıyor.


94. [FIXED 2026-09-20 Source Reconciliation] `PlaywrightManagedMcpRecoveryDiscovery.DiscoveryHints` raw-log için yalnız `logs\server.log` ve `logs\bootstrap.log` ekliyor; `McpTunnel\state\logs\playwright-business.log` dahil değil. Control Center Raw Log viewer yalnız TrayLog + `log-file` hints okuduğu için tunnel OAuth discovery, dispatcher, startup probe ve remote-forwarding hataları Playwright ekranında görünmez. Derin audit'te bu log dışarıdan okunarak kritik warnings bulundu. Çözüm: tunnel state root/alias'tan canonical tunnel log hint üretmek ve raw-log viewer'da kaynak etiketini ayırmak.

95. [FIXED 2026-09-20 Source Reconciliation] dotnet list package --outdated --include-transitive doğrudan wildcard PackageReference kayıtlarının güncel olduğunu, fakat resolved transitive graph içinde daha yeni sürümler bulunduğunu gösterdi: Microsoft.Extensions.AI.Abstractions 10.8.3 -> 10.10.0; SQLitePCLRaw 2.1.12 -> 3.x; bazı Microsoft.Extensions abstractions 10.0.10 -> 10.0.12. Bunlar Talvora tarafından pinlenmiyor, parent package constraint/restore graph sonucu. Kullanıcının her şey latest kuralı transitive graph düzeyine de uygulanacaksa compatibility-aware override stratejisi gerekir.

96. [FIXED 2026-09-20 Source Reconciliation] Launcher her task başlangıcında npm install ile @playwright/mcp latest çözümünü zorunlu yapıyor; npm veya network geçici erişilemezse daha önce kurulmuş ve çalışan CLI mevcut olsa bile launcher exit edip Playwright MCP yi tamamen offline bırakıyor. Sürüm pinlemeden çözüm: her startta latest update attempt; registry erişimi başarısızsa mevcut doğrulanmış CLI ile degraded-but-running fallback, Attention/event kaydı ve sonraki recovery döngüsünde retry.

97. [FIXED 2026-09-20 Generic Attention Recovery Preserves Healthy MCP/Browser Chain] Generic Attention remediation reason-aware hale getirildi. Browser smoke eksikse çalışan MCP zinciri üzerinde smoke yenileniyor; tunnel sorunu varsa yalnız mevcut tunnel yeniden bağlanıyor; bu işlemler başarısız olduğunda full restart fallback'ına düşmek yerine zincir korunup retry/backoff uygulanıyor. Canlı fault injection'da browser-smoke marker silinip yeniden üretildi; generation, launcher, supervisor ve backend PID'leri değişmedi.

98. [FIXED 2026-09-20 Source Reconciliation] LoadOrRecoverCoreAsync bozuk veya okunamayan registry durumunda yalnız hard-coded Talvora, Gitea ve Playwright recovery discovery sonuçlarını yeniden yazar. Gelecekte dedicated discovery implementasyonu olmayan generic MCP kayıtları registry corruption halinde kaybolabilir. Mevcut üç canonical MCP etkilenmiyor.

99. [FIXED 2026-09-20 Source Reconciliation] Registry createBackup true ile backup üretebiliyor, fakat primary JSON parse veya validation hatasında backup restore denenmeden hard-coded discovery rebuild yapılıyor. Doğru sıra primary fail, validated backup read/restore, discovery merge; backup da geçersizse son çare discovery rebuild olmalı.

100. [FIXED 2026-09-20 Source Reconciliation] Control Center refresh interval 8 saniye. RefreshDashboardAsync Playwright için MCP initialize, tools list ve browser_tabs list yapıyor; detail açıksa aynı turda RefreshDetailAsync ve GetStatesAsync tekrar protocol probe yapıyor ve task/process component health için PowerShell processleri spawn ediyor. Generic tray recovery de 15 saniyede ayrı probe yapıyor. Kısa ömürlü per-MCP health cache veya dashboard probe sonucunu detail renderer a taşıma gerekli.

101. [FIXED 2026-09-20 Source Reconciliation] Canlı tray.log Gitea endpoint offline iken Gitea version could not be read hatasını yaklaşık her 8 saniyelik Control Center refresh te tekrar yazmış. FileLog bounded olsa da diagnostics gürültüleniyor. Sürekli detail-source failure kayıtları state transition veya bounded interval bazlı dedup/rate-limit/backoff ile loglanmalı.

104. [FIXED 2026-09-20 Source Reconciliation] Faz 12 sırasında daha önce taranmamış `ParseExpectedSha256` kodu compiler gate'te CS1009 verdi; C# normal string içinde regex `\s`/`\*` escape'i yanlış yazılmış. Tunnel-client latest updater bu haliyle kaynak build'i kırıyordu. Raw/verbatim regex ile düzeltilecek ve targeted Tray build ile doğrulanacak.


105. [FIXED 2026-09-20 Source Reconciliation] Faz 12 transaction reordering sırasında `CleanupObsoleteInstallationsAsync` Playwright final readiness öncesinde kalırsa önceki version-root rollback binary'si silinebilir; ardından Playwright failure outer rollback'i yapamaz. Düzeltme: Playwright transactional commit önce, obsolete cleanup sonrasında best-effort/non-fatal çalışacak; önceki version rollback kaynağı tüm failure-prone aşamalar bitene kadar korunacak.


106. [FIXED HIGH 2026-09-20 npm Latest Verification Uses Interactive Playwright User Context] npm@latest install/view/version/prefix işlemleri installer SYSTEM context'inden çıkarılıp Playwright scheduled task ile aynı interactive install-user context'inde çalıştırılıyor. Gate, npm prefix -g değerini installUser.RoamingAppData\npm ile doğruluyor. Exact-installed canonical deploy npm=12.0.2 ve npmPrefix=C:\Users\tayla\AppData\Roaming\npm ile GREEN; server-start.json da npmVersion=12.0.2 raporluyor.

107. [FIXED HIGH 2026-09-20 Dashboard Non-Smoke Probe No Longer Self-Invalidates Browser Smoke] Non-smoke dashboard/detail health artık browser_tabs çağırmıyor ve browser process başlatmıyor. Gerçek navigate+ARIA smoke sırasında canlı Chrome PID/start-time hâlâ doğrulanıp marker'a yazılıyor; marker geçerliliği ise current Playwright runtime generation'a bağlandı. Canonical deploy dirty-d504e2d57553 sonrası generation=45ecffa5c5fd4e3c95967722ce10fd15 ile smoke marker generation birebir eşleşti; 54+ saniyelik polling boyunca Chrome gereksiz yere açılmadı ve yeni Automatic managed MCP recovery failed / Browser doğrulaması hatası oluşmadı.
