# Talvora Deep Bug Audit

Last updated: 2026-09-19
Branch: main
Git HEAD: resolve with `git rev-parse HEAD` after final commit; c8d8e5f602b6cea26c3cb32d698f59b78553721d was the pre-audit baseline
Pre-commit exact deployed runtime: c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735
Canonical tool count: 198
Status: earlier runtime audit closed; Control Center findings are tracked below. Deploy self-update item #34 remains open; current independent SYSTEM launcher workaround is verified.

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
34. [OPEN 2026-09-19 Deploy] Canonical installer Talvora LocalSystem MCP servisinin dogrudan child process'i olarak calistirildiginda mevcut Talvora servisini silme/yenileme adimi self-update baglaminda takilabiliyor. 2026-09-19 denemesinde servis Running+Disabled kaldi ve installer `Windows service silinemedi: Talvora` timeout'u verdi. Deploy launcher installer'i Task Scheduler altinda bagimsiz SYSTEM process olarak baslatmali veya installer self-hosted parent/descendant senaryosunu guvenli ele almali.
35. [FIXED 2026-09-19 Control Center Faz 4 hazirligi] Tray startup ve Control Center ayni anda managed-MCP recovery cagirirsa atomic dosya bozulmasa da cift merge/write olusabiliyordu. `ManagedMcpRegistryCoordinator` artik process-wide `RecoveryGate` ile recovery/merge/write zincirini serialize ediyor.

36. [FIXED 2026-09-19 Control Center UI] Canli Control Center penceresi `Wpf.Ui.Controls.FluentWindow` olarak acildiginda gerekli Wpf.Ui control resource/template zinciri uygulama seviyesinde yuklu olmadigi icin kullaniciya beyaz/bos pencere gorunebiliyordu. Urun kararindaki "temel WPF + secici Wpf.Ui" modeline donuldu: ana pencere standart WPF `Window`, Wpf.Ui yalniz secici bagimlilik olarak kaldi. Genisletilmis Control Center smoke dashboard+detail+lifecycle akisini GREEN dogruladi.

36. [FIXED 2026-09-19 Control Center UI] Wpf.Ui FluentWindow acilabiliyor ancak `ThemesDictionary` + `ControlsDictionary` Application resources'a yuklenmedigi icin kullanici oturumunda beyaz/bos pencere gorunuyordu. Wpf.Ui 4.3 Context7 ve paket XML dokumaniyla dogrulandi; `ControlCenterApplication` artik Dark ThemesDictionary ve ControlsDictionary yukluyor. Hedefli visual smoke ile bos/beyaz render regresyonu engellenecek.

37. [FIXED 2026-09-19 Control Center test UX] `--control-center-smoke` gerçek kullanıcı oturumunda görünür WPF pencere açtığı için test sırasında dashboard yaklaşık 2 saniye görünüp kapanıyor ve normal runtime kapanması sanılabiliyordu. Smoke `ControlCenterApplication(smokeTest: true)` ile off-screen, taskbar-disinda çalışacak biçimde ayrıldı; normal Control Center yaşam döngüsü değişmedi.

37. [FIXED 2026-09-19 Control Center Visual Language] Kullanici Fluent tasarimi istemedigini belirtti. `WPF-UI` 4.3.0 bagimliligi tamamen kaldirildi; yerine MIT lisansli `MaterialDesignThemes` 5.3.2 ve Material Design 3 kaynaklari eklendi. Control Center koyu Material 3 control-room tokenlari, outlined arama, ikonlu butonlar ve elevation kartlari kullanacak sekilde guncellendi. Off-screen visual smoke Material kaynaklarini ve render yuzeyini GREEN dogruladi.

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
