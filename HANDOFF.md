# Talvora MCP Handoff — 2026-09-19

## Oturum kapanış snapshot — final doğrulanmış durum

Bu bölüm **mevcut gerçeği** temsil eder ve aşağıdaki tarihsel/pre-commit notlarından daha önceliklidir.

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
