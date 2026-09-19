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
- Playwright remains a separate ChatGPT integration and is not part of Talvora.
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

Control Center Faz 0-11 batch'i çalışma ağacında kasıtlı olarak uncommitted durumdadır. Kullanıcı açıkça istemeden commit/push yapılmayacak; gerçek durum her zaman `git status --short --branch` ile doğrulanacak.

Stale dated handoffs and the old transition prompt are deleted in the working tree:
- `HANDOFF-2026-09-18.md`
- `HANDOFF-2026-09-19.md`
- `NEXT-SESSION-PROMPT.txt`

`HANDOFF.md` is the single canonical handoff going forward.
`BUG-AUDIT.md` is the living detailed defect log.

Do not restore the old handoff files or old tool-count assumptions.

## Next action

Control Center Faz 0-11 tamamlandı ve bilinen runtime blocker yok.

Kullanıcı açıkça istemedikçe commit/push yapma. Yeni özellik veya runtime değişikliği istenirse mevcut tamamlanmış Control Center tabanından devam et; runtime kodu değişirse zorunlu source -> targeted test -> canonical installer -> live deploy zincirini uygula. Mevcut dirty-89135c8503e5 runtime için final 198-tool smoke zaten bir kez GREEN çalıştırıldı; runtime source değişmeden tekrar etme.

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