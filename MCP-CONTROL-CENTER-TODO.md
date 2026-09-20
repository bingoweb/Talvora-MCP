# Talvora Control Center — Uygulama TODO ve Tasarım Kararları

Tarih: 2026-09-19
Durum: PLANLAMA TAMAMLANDI — implementasyon yeni oturumda başlayacak.

## 1. Ürün hedefi

Talvora Control Center, bu Windows bilgisayarına bizim kurduğumuz ve yönettiğimiz yerel MCP sunucularını tek, anlaşılır ve tamamen Türkçe bir arayüzden yönetmek için oluşturulacaktır.

Öncelik sırası:
1. Kullanışlılık / UI-UX.
2. Acemi kullanıcı için anlaşılabilirlik.
3. Menü ve ayar karmaşasını en aza indirmek.
4. Otomatik keşif, otomatik sağlık takibi ve güvenli otomatik toparlama.
5. Gelişmiş teknik ayrıntıları gerektiğinde erişilebilir tutmak; ana ekrana taşımamak.
6. Mevcut Talvora LocalSystem yetki modelini yeniden kullanmak.
7. MCP sayısı arttığında ölçeklenebilir kalmak.

Bu ürün bir MCP Inspector değildir. MCP tool listesini görüntüleme/çalıştırma özelliği eklenmeyecektir.

## 2. Kesinleşen teknoloji ve uygulama mimarisi

- Platform: Windows native.
- Dil/runtime: C# / mevcut .NET 10 Windows hedefi.
- UI: WPF.
- Uygulama modeli: mevcut Talvora Tray ile tek uygulama / tek EXE.
- Tray ve WPF Control Center aynı kullanıcı uygulaması içinde yaşayacak.
- WPF ve mevcut tray/Windows Forms birlikte kullanılabilecek biçimde proje yapılandırılacak.
- Üçüncü taraf UI yaklaşımı: hibrit.
  - Temel mimari saf WPF.
  - Görsel katmanda **WPF-UI 4.3.0** seçici kullanılacak.
  - Fluent kontrol/template altyapısı varsayılan demo görünümüyle bırakılmayacak; Talvora'ya özgü koyu ürün tokenları, spacing/type ramp ve etkileşim ayrıntıları uygulanacak.
  - İş mantığı ve tema mimarisi Wpf.Ui'ye sıkı bağlanmayacak.
- Görsel yön: koyu tema ağırlıklı modern developer dashboard + çok hafif premium control-room dokunuşları.
- Arayüz dili: tamamen Türkçe.
- Teknik terminoloji: Türkçe ana ifade + gerektiğinde teknik terim parantez içinde.
  - Örnek: Tünel (Tunnel), Uç nokta (Endpoint), Sağlık kontrolü (Health Check).
  - MCP, PID, URL gibi yerleşik kısaltmalar korunabilir.
- Canlı loglar çevrilmeyecek; üçüncü taraf ham log metni aynen gösterilecek.

## 3. Tray davranışı

- Tek Talvora tray ikonu olacak.
- Mevcut ayrı Gitea tray ikonu kaldırılacak / Control Center altında birleştirilecek.
- Sol tık: Talvora Control Center penceresini aç / öne getir.
- Sağ tık: MCP bazlı hızlı yönetim menüsü.
- Tray ikonunun durumu tüm yönetilen MCP'lerin genel sağlığını yansıtacak.
- Uygulama penceresindeki X yalnız pencereyi gizleyecek/kapatacak; arka plan tray uygulaması çalışmaya devam edecek.
- Tam çıkış yalnız tray menüsündeki açık bir "Talvora'dan Çık" eylemiyle yapılacak.
- Arka plan servis/process başlatmalarında görünür terminal penceresi bırakılmayacak.

## 4. Ana kullanıcı deneyimi

Ana navigasyon derin olmayacak. Kalıcı karmaşık sol menü / çok seviyeli menüler kullanılmayacak.

Ana yapı:
- Tek Home Dashboard.
- Üstte insan dilinde genel sağlık özeti:
  - "Her şey hazır"
  - "1 MCP dikkat istiyor"
  - "Müdahale gerekiyor"
- Hemen altında kısa teknik özet:
  - örn. "5 MCP • 4 hazır • 1 sorunlu • 5 tünel"
- Alt bölümde MCP kartları.
- Kartlar responsive/adaptive yerleşecek:
  - pencere genişliğine göre sütun sayısı değişecek,
  - dar pencerede tek sütuna inebilecek,
  - okunabilirlik korunacak.
- Sorunlu MCP'ler otomatik olarak üst sıralara taşınacak.
- Arama alanı kolay erişilebilir olacak.
- Filtreler yalnız ihtiyaç olduğunda görünür olacak.
- MCP sayısı büyüdüğünde kullanıcı kart yığını içinde kaybolmayacak.

MCP karta tıklanınca:
- Aynı pencere içinde MCP detay görünümü açılacak.
- Büyük ve belirgin geri dönüş davranışı olacak.
- Sekme ormanı olmayacak.
- Tek ana detay sayfası + gerektiğinde açılan/gizlenen gelişmiş bölümler kullanılacak.
- Normal kullanıcı önce yalnız önemli bilgileri görecek.

## 5. MCP kartı ilk bakış bilgileri

Ana kart sade olacak:
- MCP adı.
- Resmi logo varsa logo; yoksa Talvora varsayılan MCP ikonu.
- Genel sağlık durumu.
- Kısa, insan dilinde durum açıklaması.
- Duruma göre tek bağlamsal ana eylem:
  - Başlat
  - Aç
  - Yeniden bağlan
  - Sorunu düzelt
  - benzeri
- Gerekmedikçe PID, tunnel ID, endpoint, config yolu gibi teknik bilgi ana karta taşınmayacak.

CPU, RAM ve süreç sayısı gibi kaynak kullanım bilgileri Control Center kapsamına alınmayacak.

## 6. MCP detay ekranı

Normal görünümde:
- Sağlık durumu.
- Yerel MCP durumu.
- Tünel (Tunnel) durumu.
- Sürüm bilgisi.
- Gerekli ana kullanıcı eylemleri.
- Son önemli olayların anlaşılır özeti.

Açılabilir gelişmiş bölümlerde:
- Uç nokta (Endpoint).
- Tünel kimliği (Tunnel ID).
- PID / servis adı gibi teşhis bilgileri.
- Bağlı yardımcı bileşenler.
- Ham loglara geçiş.
- Salt okunur config/teknik bilgi gösterimi gerekirse kullanılabilir.

Config düzenleme yapılmayacak.

## 7. Bileşen zinciri gösterimi

Bir MCP birden çok yardımcı bileşenden oluşabiliyorsa ana ekranda yine tek MCP kartı gösterilecek.

Örnek Gitea:
- Ana dashboard: tek "Gitea MCP" kartı.
- Detayda sağlık zinciri sade biçimde:
  - Gitea
  - Caddy
  - Gitea MCP sunucusu
  - Tünel (Tunnel)
- Sorunlu halka özellikle vurgulanacak.
- Yardımcı bileşenler ana dashboard'da ayrı kartlara bölünmeyecek.

## 8. Keşif ve sahiplenme modeli

Kapsam:
- Yalnız bu Windows makinesine bizim kurduğumuz ve yönettiğimiz yerel MCP'ler.
- Remote MCP kataloğu / dış sistemlerdeki rastgele MCP'ler kapsam dışı.
- Tunnel kullanmayan üçüncü taraf rastgele MCP'leri genel sistem taramasıyla sahiplenme hedeflenmiyor.

Keşif davranışı:
- Tam otomatik.
- Kullanıcı "MCP Ekle" formuyla uğraşmayacak.
- Keşfedilen yönetilen MCP onay istenmeden Control Center'a alınacak ve yönetilecek.

Keşif altyapısı hibrit olacak:
1. Birincil kaynak: Talvora merkezi MCP yönetim kaydı / registry dosyası.
2. Kurtarma kaynağı: bilinen servis, süreç, tunnel profili ve kurulum izleri.
3. Merkezi kayıt silinmiş/bozulmuşsa otomatik keşif onu yeniden kurabilecek.
4. Yanlış pozitifleri önlemek için "bizim kurduğumuz/yönettiğimiz MCP" imzası/metadata modeli tasarlanacak.

## 9. Yetki modeli

- WPF/Tray kullanıcı arayüzü normal kullanıcı yetkisiyle çalışacak.
- Yükseltilmiş işlemler mevcut Talvora LocalSystem MCP servisi üzerinden yapılacak.
- UI her zaman Administrator olarak çalışmayacak.
- Normal akışta UAC gösterilmeyecek.
- Servis, registry, process ve yükseltilmiş dosya işlemleri için paralel ikinci privileged altyapı yazılmayacak.
- Mevcut Talvora yetenekleri yeniden kullanılacak.

## 10. Başlatma / durdurma / restart davranışı

Windows açılışında:
- Control Center otomatik başlayacak.
- Normalde yalnız tray'de sessiz çalışacak.
- Yönetilen tüm MCP'ler ve gerekli tüneller otomatik ayağa kaldırılacak.
- Ciddi ve kullanıcı müdahalesi gerektiren MCP sorunu varsa Control Center penceresi otomatik açılabilecek.
- Küçük/geçici/kendi kendine düzelen sorunlarda pencere açılmayacak.

Kullanıcı "Durdur" dediğinde:
- Yerel MCP servisi/süreci + ona ait Tünel (Tunnel) birlikte durdurulacak.
- Durdurma öncesi onay istenecek.
- Kullanıcının elle durdurduğu MCP o Windows oturumu boyunca otomatik recovery tarafından yeniden başlatılmayacak.
- Sonraki Windows açılışında tüm MCP'leri otomatik başlatma kuralı tekrar geçerli olacak.

"Başlat":
- Doğru bağımlılık sırasıyla yerel MCP + tünel hazırlanacak.

"Yeniden Başlat":
- Onay istemeden çalışacak.
- İlgili bileşen zinciri doğru sırada yeniden hazırlanacak.
- Sonunda health doğrulaması yapılacak.

## 11. Otomatik sağlık ve recovery

- Bilinen, geri dönüşü güvenli sorunlar otomatik düzeltilecek.
- Örnekler:
  - servis beklenmedik durdu,
  - tunnel koptu,
  - bilinen health zinciri geçici olarak bozuldu.
- Daha riskli/belirsiz bir işlem gerekiyorsa Control Center kullanıcıya anlaşılır öneri sunacak.
- Kullanıcıya teknik hata dökümü yerine önce insan dilinde sorun anlatılacak.
- Recovery döngülerinde backoff uygulanacak; tight retry loop olmayacak.
- Kullanıcı tarafından elle durdurulmuş MCP recovery kapsamından çıkarılacak.

## 12. Bildirim politikası

- Bildirim gürültüsü olmayacak.
- Kısa süreli/kendi kendine düzelen küçük sorunlar sessiz.
- Servis çökmesi, tekrar eden hata, uzun süren bağlantı problemi veya müdahale gereken olay Windows bildirimi üretebilir.
- Başarılı recovery yalnız önemliyse bildirim üretir.
- Durumların ana kaynağı Control Center olacaktır.

## 13. Son olaylar ve loglar

Ana dashboard:
- Küçük bir "Son olaylar" özeti.
- Ayrı bir kalıcı menüye yönlendirmek yerine aynı pencere içinde genişleyen olay paneli.
- Örn. "Son 24 saatte 2 önemli olay".

Olay saklama:
- Akıllı retention.
- Normal bilgi olayları kısa süre tutulur.
- Uyarı ve hatalar daha uzun tutulur.
- Tekrarlanan olaylar gruplanır/deduplicate edilir.
- Eski kayıtlar otomatik temizlenir.
- Kullanıcı log temizliğiyle uğraşmaz.

Canlı log:
- Normal ekranda gösterilmez.
- "Canlı logları aç" gibi gelişmiş eylemle ayrı/expandable teknik panel açılır.
- Arama, filtreleme ve kopyalama desteklenir.
- Kaynak log metni aynen korunur; otomatik çeviri yapılmaz.

## 14. Secure MCP Tunnel otomasyonu

Yeni yönetilen yerel MCP keşfedildiğinde Secure MCP Tunnel yoksa:
- Control Center uygun OpenAI yetkileri mevcut olduğunda otomatik tunnel oluşturacak.
- Kullanıcıdan her MCP için ayrıca onay istenmeyecek.
- Ortak restricted runtime key mevcut modelde yeniden kullanılabilir.
- Runtime key ve Admin key rolleri ayrı tutulacak.

OpenAI anahtarları:
- Runtime key: mevcut DPAPI saklama yaklaşımı korunacak.
- Admin API key:
  - ilk gerektiğinde bir kez alınacak,
  - runtime key'den ayrı saklanacak,
  - Windows DPAPI ile current-user kapsamında korunacak,
  - normal kullanımda tekrar sorulmayacak.
- Secret değerler log/UI'da açık gösterilmeyecek.
- Tunnel CRUD admin credential ile; runtime bağlantısı restricted runtime credential ile yapılacak.
- Yeni oturum implementasyona başlamadan önce güncel OpenAI tunnel-client belgeleri Context7 + resmi kaynakla yeniden doğrulanacak.

## 15. İlk kullanım

Klasik uzun kurulum sihirbazı olmayacak.

İlk açılış:
- Dashboard hemen açılır.
- Kritik eksik kurulum varsa üstte tek sade "Kurulumu Tamamla" kartı görünür.
- Kart yalnız gerçekten gereken bilgileri ister.
- Kurulum tamamlanınca normal dashboard'a dönüşür.
- Kullanıcı ayar menüleri içinde dolaştırılmaz.

## 16. Ayarlar / güncelleme / kaldırma kapsamı

Ayarlar:
- Ayrı Ayarlar sayfası olmayacak.
- Sistem mümkün olduğunca otomatik davranacak.

Config:
- Control Center MCP config dosyalarını düzenlemeyecek.

Güncelleme:
- Control Center MCP güncellemesi kurmayacak.
- Sürüm bilgisini gösterebilir.
- Güncelleme işlemi ilgili ayrı kurulum/geliştirme akışında kalacak.

Kaldırma:
- Control Center MCP uninstall/silme yapmayacak.
- Yanlışlıkla veri kaybına yol açabilecek "MCP'yi tamamen kaldır" özelliği eklenmeyecek.

MCP Tools:
- tools/list / tool test / Inspector özelliği eklenmeyecek.

## 17. Pencere davranışı

- İlk kullanımda uygun varsayılan boyutla, ekran ortasında açılacak.
- Son konum, boyut ve maximized durumu hatırlanacak.
- Monitör değişirse veya kayıtlı konum görünür alan dışındaysa otomatik kurtarma uygulanacak.
- Pencere daraldığında layout kırılmayacak.

## 18. Görsel tasarım sistemi

- Koyu tema varsayılan.
- Modern developer aracı hissi.
- Premium control-room etkileri çok hafif:
  - ölçülü durum parıltısı,
  - küçük geçiş animasyonları,
  - net health indicator'lar.
- Gaming dashboard görünümü oluşturulmayacak.
- Büyük boşluklar, net tipografi, tutarlı hiyerarşi.
- Yeşil/sarı/kırmızı renk anlamları uygulama genelinde sabit.
- Renk tek başına bilgi taşımamalı; ikon + metinle desteklenmeli.
- Resmi logo varsa kullan; yoksa Talvora fallback MCP ikonu.
- Tüm logolar aynı kart ölçü sistemi içinde normalize edilmeli.
- Animasyonlar düşük maliyetli ve dikkat dağıtmayacak biçimde kullanılmalı.
- High-DPI / multi-monitor davranışı birinci sınıf olmalı.

## 19. Kullanıcıya gösterilecek örnek durum dili

Tercih edilen:
- "Her şey hazır"
- "Gitea MCP hazır"
- "Tünel (Tunnel) yeniden bağlanıyor"
- "Talvora MCP çalışıyor, bağlantı hazırlanıyor"
- "1 MCP dikkat istiyor"
- "Müdahale gerekiyor"

Kaçınılacak:
- Ana ekranda ham exception mesajları.
- Gereksiz PID/port/config jargon yığını.
- Kullanıcıyı bir sonraki adımı tahmin etmeye zorlayan belirsiz hata metinleri.

## 20. Implementasyon sırası — TODO

### Faz 0 — Başlangıç doğrulaması
- [x] HANDOFF.md oku.
- [x] Bu TODO dosyasını oku.
- [x] Talvora MCP araçlarını keşfet.
- [x] talvora_system_info ile yerel Talvora bağlantısını doğrula.
- [x] main / HEAD / clean-tree durumunu doğrula.
- [x] Runtime değişmediği sürece full 198-tool smoke'u tekrar etme.
- [x] WPF + WPF-UI 4.3.0 güncel API/sürüm durumunu Context7 ve resmi kaynaklarla tekrar doğrula.
- [x] OpenAI tunnel-client admin/runtime credential ve tunnel CRUD akışını güncel resmi belgelerle tekrar doğrula.

### Faz 1 — Mimari iskelet
- [x] Mevcut Talvora.Tray projesinde WPF + WinForms birlikte çalışma stratejisini kesinleştir.
- [x] Tek EXE şartını bozma.
- [x] Uygulama startup/lifetime modelini tray + WPF window için yeniden düzenle.
- [x] Tek-instance davranışını koru.
- [x] Mevcut --reconnect / --self-test / Gitea CLI davranışlarını kırma.
- [x] WPF-UI 4.3.0 bağımlılığını minimum ve seçici yüzeyle ekle; ThemesDictionary + ControlsDictionary + explicit Dark theme doğrulandı.
- [x] UI/iş mantığı ayrımını belirgin tut.

### Faz 2 — MCP yönetim modeli
- [x] Merkezi managed-MCP registry formatını tasarla.
- [x] Atomic write + versioned schema uygula.
- [x] Talvora ve Gitea'yı registry'ye migrate/seed et.
- [x] Recovery discovery adaptörlerini oluştur.
- [x] Yeni managed MCP kayıt sözleşmesini installer/bootstrap akışlarında kullanılabilir hale getir.
- [x] "bizim yönettiğimiz MCP" kimliğini deterministik yap.

### Faz 3 — Tek tray
- [x] Ayrı Gitea NotifyIcon mimarisini kaldır.
- [x] Tek Talvora NotifyIcon üzerinden genel health durumu.
- [x] Sol tık -> Control Center.
- [x] Sağ tık -> MCP hızlı eylemleri.
- [x] Durum icon renkleri + metinleri.
- [x] Çıkış / hide / shutdown lifecycle testleri.

### Faz 4 — WPF ana dashboard
- [x] Koyu tema tasarım tokenları.
- [x] Header / genel sağlık alanı.
- [x] Kısa teknik özet.
- [x] Responsive MCP card grid.
- [x] Search.
- [x] İhtiyaca göre görünür filtreler.
- [x] Sorunluları üste taşıyan sıralama.
- [x] Resmi logo + Talvora fallback icon sistemi.
- [x] Empty/loading/degraded durumları.

### Faz 5 — MCP detay görünümü
- [x] Tek sayfa detail layout.
- [x] Bağlamsal ana eylem.
- [x] Start/Stop/Restart.
- [x] Stop confirmation.
- [x] Yardımcı bileşen health chain.
- [x] Açılır teknik ayrıntılar.
- [x] Endpoint/tunnel/version gibi salt-okunur bilgiler.
- [x] Gitea özel zinciri.
- [x] Talvora özel zinciri.

### Faz 6 — Health/recovery engine
- [x] Unified status model.
- [x] Health polling.
- [x] Backoff.
- [x] Manual-stop session suppression.
- [x] Auto-start at Windows startup.
- [x] Safe auto-repair.
- [x] Serious-incident classification.
- [x] Gerekirse Control Center auto-open.
- [x] Bildirim gürültüsü kontrolü.

### Faz 7 — Olaylar ve loglar
- [x] Structured event store.
- [x] Akıllı retention.
- [x] Dedup/grouping.
- [x] Dashboard event summary.
- [x] Same-window expandable event panel.
- [x] Live raw log view.
- [x] Search/filter/copy.
- [x] Log büyüme limitleri.

### Faz 8 — Tunnel provisioning
- [x] Admin API key DPAPI current-user storage.
- [x] Runtime/Admin key separation.
- [x] Missing-tunnel detection.
- [x] Automatic tunnel create/configure.
- [x] Existing runtime key reuse.
- [x] Tunnel profile generation.
- [x] Tunnel ready/health verification.
- [x] Secrets never emitted in log/UI.
- [x] Recovery after reboot.

### Faz 9 — First-run / incomplete setup
- [x] Dashboard-first startup.
- [x] "Kurulumu Tamamla" card.
- [x] Only missing credentials/config asked.
- [x] No Settings page.

### Faz 10 — Window UX / accessibility
- [x] Window size/location persistence.
- [x] Multi-monitor off-screen recovery.
- [x] High-DPI verification.
- [x] Keyboard navigation.
- [x] Focus states.
- [x] Status not color-only.
- [x] Türkçe copy review.
- [x] Beginner usability review.

### Faz 11 — Targeted verification and live deployment
- [x] Only changed-area tests during development.
- [x] Build WPF/Tray targeted.
- [x] Regression: existing tray CLI modes.
- [x] Regression: Gitea restart.
- [x] Regression: Talvora reconnect.
- [x] Regression: Talvora standard-user service stop/start + health.
- [x] Regression: Talvora tunnel stop/reconnect + ready/health.
- [x] Regression: shutdown/mutex/lifetime.
- [x] Native installer source regression.
- [x] Canonical installer build.
- [x] Live deploy.
- [x] talvora_system_info.
- [x] Service/Tray PID + version-root.
- [x] Live Control Center smoke.
- [x] Final exact deployed full MCP smoke at most once if runtime changed.
- [x] BUG-AUDIT.md findings updated as issues are discovered/fixed.

## 21. Kesin kapsam dışı

Aksi özellikle istenmedikçe eklenmeyecek:
- MCP tool inspector / tools/list UI.
- Tool execution console.
- CPU/RAM dashboard.
- MCP uninstall.
- MCP package update installer.
- Raw config editor.
- Large Settings hierarchy.
- Remote/public MCP directory.
- Ayrı Gitea tray icon.
- Çok seviyeli navigation tree.
- Per-MCP ayrı UI process.
- Web UI / Electron / browser-based frontend.

## 22. Yeni oturumda çalışma kuralı

Yeni oturumda kullanıcıdan bu kararlar tekrar sorulmayacak.
Önce HANDOFF + bu TODO okunacak, Talvora bağlantısı ve git durumu doğrulanacak; ardından Faz 0 -> Faz 1 sırasıyla doğrudan implementasyona başlanacak.

Kullanıcı açıkça istemeden commit/push yapılmayacak.
## Faz 12 — Ticari kalite hardening / deep-audit kapatma (2026-09-19)

> 2026-09-20 reconciliation: Faz 12 kutuları production-hardening commitindeki kaynak uygulaması, final canonical live-deploy kanıtı ve HANDOFF kayıtlarıyla yeniden doğrulandı. Bu oturumda runtime kodu değiştirilmedi ve daha önce GREEN olan browser/live smoke testleri gereksiz yere tekrar çalıştırılmadı. #34 de canonical bağımsız SYSTEM Task Scheduler deploy launcher + source regression ile kapatıldı; bilinen açık production-hardening maddesi kalmadı.
### A. Kritik health ve browser instance doğrulaması
- [x] #79 — browser smoke gerçek Chrome PID/start-time ile execution anında doğrulansın ve marker runtime generation'a bağlansın. ✅ 2026-09-20 demand-launch browser lifecycle nedeniyle relaunch sonrası marker invalidate şartı kaldırıldı; #107 ile non-smoke health browser açmadan generation-scoped smoke kanıtını kullanıyor.
- [x] #107 — dashboard non-smoke health browser launch/churn üretmesin; successful smoke marker current runtime generation boyunca geçerli kalsın, PID/start-time yalnız gerçek smoke execution kanıtı olsun. ✅ 2026-09-20 source regression + canonical live deploy + 54s stability GREEN.
- [x] #71 — shared-browser-context smoke cleanup'ında açılan sekmeyi explicit identity/index ile kapat; başka client sekmesini kapatma riskini kaldır.
- [x] #72 — smoke generation'ı başlangıçta capture et; bitişte aynı generation olduğunu doğrula ve marker'ı atomik yaz.
- [x] #84 — Ready tool contract'ını enabled capability metadata'ya göre stable representative tools ile güçlendir; tüm tool listesini sürüme pinleme.
- [x] #78 — production compatibility proxy 8932 için ayrı regression contract ekle.

### B. Lifecycle / recovery / process ownership
- [x] #70 — generic lifecycle'a per-MCP operation lease ekle; UI ve auto-recovery aynı coordinator'ı kullansın.
- [x] #67 — Playwright current-user task lifecycle'ını Talvora 7676 broker'dan bağımsız çalıştır; yalnız privileged bileşenleri broker'a gönder.
- [x] #68 — manual Stop suppression'ı Windows logon session scoped persistent-ephemeral state'e taşı; Tray restart'ta korun, logoff/yeni session'da sıfırla.
- [x] #80/#85 — supervisor/backend/Chrome'u tek owned process-tree domain'i olarak yönet; fatal path ve normal Stop/Restart orphan bırakmasın.
- [x] #86 — tunnel stop/disconnect 'already absent/not known/not running' durumlarını idempotent başarı kabul et.
- [x] #97 — Attention reason/component bazlı remediation uygula; smoke/tunnel sorunlarında full restart yapma. ✅ 2026-09-20 canlı smoke-marker fault injection: generation/launcher/supervisor/backend korunarak smoke yenilendi.

### C. Installer transactional correctness
- [x] #34 — canonical installer Talvora servis process tree'sinden bağımsız SYSTEM Task Scheduler launcher ile başlatılsın; MCP üzerinden deploy sırasında self-update parent/descendant riski kaldırılmış olsun.
- [x] #83 — Node/runtime mevcutsa installer Playwright task run kabulünü değil bounded MCP readiness'i doğrulasın.
- [x] #88 — Playwright assets/task mutation transaction + rollback-safe olsun.
- [x] #87 — Talvora service upgrade hata yolunda SCM recovery actions her durumda restore/canonical kalsın.
- [x] #82 — NativeInstallerSourceRegression yeni service upgrade mimarisine göre güncellensin; CI kırmayan ve servis-delete regression'ını gerçekten yakalayan contract ekle.
- [x] #63 — canonical installer Playwright payload hazırlama duplicate bloğunu tekleştir.

### D. Windows / dependency / latest policy
- [x] #65/#89 — tüm PS5.1 fallback scriptlerinde StringComparison Contains yerine PS5.1-compatible IndexOf kullan; fallback regression test ekle.
- [x] #90/#91 — Chocolatey ile latest Node Current çöz/kur; deterministic node/npm resolution ve preflight version check ekle; LTS pinleme yapma.
- [x] #96 — npm latest update başarısızsa mevcut doğrulanmış installed CLI ile degraded-running fallback uygula; sonraki recovery'de update retry.
- [x] #64 — canonical build resolved dependency provenance manifest üretip runtime metadata'ya koysun.
- [x] #95 — newer transitive dependency graph'ı official constraints/compatibility ile değerlendir; güvenli olanları modernize et, kör override yapma.
- [x] #75 — tunnel-client için bounded periodic latest check + atomic update tasarla; pinleme yapma.

### E. Tunnel correctness / secrets / health
- [x] #73 — tunnel-scope cache'i ReferenceTunnelId + TTL ile doğrula, stale ise rediscover.
- [x] #74 — runtime-key.dpapi readiness'i file-exists değil secret sızdırmadan DPAPI decrypt validation ile belirle.
- [x] #92 — tunnel health'i structured runtimes status ile alias+tunnelId+process/profile identity'ye bağla; HTTP probe hızlı sinyal olarak kalsın.
- [x] #93 — compatibility proxy no-auth discovery/startup probe warning'lerini resmi tunnel-client beklentisine göre temizle. ✅ 2026-09-20 SSE response headers flush + backend readiness gate; Go SDK v1.7.0 8932 connect 14.6 ms, tunnel /readyz 200, yeni startup probe timeout yok.

### F. Registry / first-run durability
- [x] #66 — incomplete setup discovery task/tunnel ownership izlerini de hesaba katsın; kart kaybolmasın.
- [x] #99 — primary registry bozulursa validated backup restore -> discovery merge sırası uygula.
- [x] #98 — generic/non-discoverable registry kayıtları için durability/ownership manifest stratejisi ekle.

### G. UI / diagnostics / logs
- [x] #76 — profile path ve runtime/state paths ayrı alanlarda göster.
- [x] #77 — selected MCP detail ekranına son olayları ekle.
- [x] #94 — Playwright raw log görünümüne Secure MCP Tunnel logunu kaynak etiketiyle ekle.
- [x] #69 — server.log runtime boyunca bounded rolling log olsun; yalnız startup rotate'a güvenme.
- [x] #100 — dashboard/detail/recovery protocol probes için kısa ömürlü shared health cache kullan; lifecycle sonrası invalidate et.
- [x] #101 — tekrar eden version/detail-source exception loglarına dedup/rate-limit/backoff ekle.

### H. Final gates
- [x] Değiştirilen her alt sistemi yalnız hedefli test et; aynı testleri tekrarlama.
- [x] Yeni bulgu çıkarsa BUG-AUDIT.md + bu TODO'ya anında ekle ve mümkünse aynı fazda kapat.
- [x] Shared/Service/Tray Release build 0 warning/0 error.
- [x] Canonical installer build.
- [x] Live deploy yalnız tüm targeted source gates GREEN olduktan sonra.
- [x] Exact-installed Talvora system_info + service/tray version-root doğrulaması.
- [x] Exact-installed Playwright initialize/tools/browser instance-bound navigate+snapshot smoke.
- [x] Final Control Center visual smoke bir kez.
- [x] git diff --check temiz.
- [x] Kullanıcı açıkça istemeden commit/push YOK.


## Faz 13 — Playwright MCP kaldırma / Official Playwright CLI geçişi (2026-09-20)

- [x] #108 — Playwright MCP'yi Talvora kaynak/installer/Tray/registry/build payload'ından tamamen kaldır; roadmap'taki "Do not add Playwright to Talvora" kuralını yeniden sağla.
- [x] Current-user bağlamında `@playwright/cli@latest` kur ve gerçek `playwright-cli --version/--help` ile doğrula.
- [x] Eski Playwright MCP scheduled task/process/proxy/tunnel zincirini durdur; browser profile verisini Talvora dışı bağımsız Playwright CLI profile konumuna güvenli taşı.
- [x] CLI capability smoke: persistent named session + navigate/snapshot/find/tab/screenshot/PDF/run-code/console-or-network/tracing temsilci akışı GREEN.
- [x] Eski `Talvora Playwright MCP` task, MCP runtime, supervisor, proxy ve Secure MCP Tunnel live artıklarını kaldır; Playwright CLI profilini koru.
- [x] Native installer regression ve smoke harness'i yeni mimariye göre güncelle; Playwright MCP'ye bağlı eski contract'ları sil.
- [x] Değişen Installer/Tray/Shared alanlarını hedefli Release build/test ile doğrula; gereksiz eski smoke'ları tekrar etme.
- [x] Canonical installer build + bağımsız SYSTEM deploy + exact-installed Talvora doğrulaması.
- [x] BUG-AUDIT/HANDOFF/TODO yeni CLI mimarisiyle güncellensin; git diff --check temiz olsun.
- [x] Kullanıcı açıkça istemeden commit/push YOK.

- [x] #109 — canonical SYSTEM deploy task `Talvora-Setup.exe --silent` çalıştırsın; Session 0 görünmez GUI hang'ini regression ile engelle.


## Faz 14C — Deep Audit Remediation Backlog (CURRENT — 2 OPEN)

- [x] #124 — semantic durable replay metadata; adapter receipt persisted in receipt + WAL, forced receipt-loss recovery preserves workspace/symbol/diagnostics, Source Edit 60/60 GREEN.
- [x] #127 — structural YAML control-file self-scan. Targeted RED -> GREEN; Source Edit 56/56 GREEN.
- [x] #128 — semantic resource bounds before/while load/generation; progress/workspace cancellation gate + bounded diagnostics + incremental proposal budgets + emergency ceilings, targeted GREEN, Source Edit 61/61 GREEN.
- [x] #129 — semantic graph concurrency fingerprint/versioning; physical graph revisions + analyzer/source-generator binary inputs + C# membership snapshot + commit guard + SEMANTIC_GRAPH_STALE rollback, targeted GREEN, Source Edit 62/62 GREEN.
- [x] #130 — workspace-scoped MSBuild/toolchain fidelity; request-bazlı semantic worker + WorkingDirectory/global.json çözümlemesi, hedefli testler GREEN, Source Edit 63/63 GREEN.
- [x] #132 — retired Playwright MCP ownership-aware post-commit upgrade cleanup + idempotent migration marker; targeted installer source contracts GREEN, Installer Release compile 0 warning / 0 error.
- [x] #133 — canonical deploy fast-task completion observation; launched instance GUID + LastRunTime transition contract, forced-delay live Task Scheduler regression GREEN.
- [x] #134 — collision-proof managed-MCP persistent ID mapping; shared SHA-256 key + recovery migration fixture GREEN.
- [x] #135 — ordinary C# source syntax validation. Targeted RED -> GREEN; Source Edit 56/56 GREEN.
- [x] #136 — ownership-aware legacy Cloudflared cleanup + post-commit retirement boundary; exact service executable identity + targeted installer source regression GREEN.
- [x] #137 — ast-grep private-cache executable hash revalidation. Targeted RED -> GREEN; 2026-09-20 source audit.
- [x] #138 — ast-grep exact-version provenance snapshot. Targeted RED -> GREEN; 2026-09-20 source audit.
- [x] #139 — installer post-health state/config rollback; exact file/registry snapshot+restore, dedicated rollback regression GREEN, Installer Release 0 warning / 0 error.
- [x] #140 — canonical deploy artifact hash/identity pinning; sidecar build manifest + build mutex + pre-launch SHA-256/size/name gate, mutation regression GREEN.
- [x] #144 — corrupt WAL per-transaction isolation + workspace-scoped durable quarantine; targeted Source Edit 52/52 GREEN.
- [x] #145 — managed-MCP recovery redundancy health/self-heal; ownership corruption fixture GREEN.
- [x] #148 — immutable canonical installer source snapshot / HEAD + index-tree + full runtime input identity; source contract GREEN, real canonical build GREEN.
- [x] #149 — bounded background-job logs + retention/quota; targeted fixture GREEN.
- [x] #150 — bounded job-read response/pagination; continuation/generation regression GREEN.
- [x] #151 — remote tunnel create durable pending/reconciliation; request-marker fail-closed retry contract GREEN, Tray Release 0 warning / 0 error.
- [x] #152 — collision-proof tunnel config/credential/state paths; fallback path probe GREEN.
- [x] #153 — tunnel-client update transaction + rollback; stage-only candidate, durable recovery journal, exact previous-client compensation, crash recovery, rollback regression GREEN, Tray build 0 warning / 0 error.
- [x] #154 — primary managed-MCP registry self-heal after backup recovery; temp-path recovery fixture GREEN.
- [x] #155 — Gitea real MCP protocol readiness gate; initialize + tools/list + required capabilities, 2 s TTL cache ve 10 s absolute probe budget; live regression GREEN.
- [x] #156 — Gitea full-chain stop terminal verification; independent health/readiness shutdown probe + task-action-owned process-tree termination/wait, embedded PowerShell parse regression GREEN, Tray build 0 warning / 0 error.
- [x] #157 — bounded shared process stdout/stderr capture; ProcessRunner regression GREEN.
- [x] #158 — streaming/bounded canonical source read; Source Edit 53/53 GREEN.
- [x] #159 — absolute transport/response budgets + continuation semantics; targeted response-bounds regression GREEN, canonical artifact/live deploy + exact-installed HTTP/TCP bounded metadata GREEN.
- [x] #160 — overall process timeout includes post-parent pipe drain; inherited-pipe regression GREEN.
- [x] #163 — FileSystemWatcher overflow/resync completeness state; targeted runtime-bounds regression GREEN.
- [x] #164 — bounded watcher/HTTP-mock queues, inflight and pending work; global backpressure/accounting regression GREEN.
- [x] #165 — crash-durable AtomicFile staged publication; WriteThrough + Flush(true) + MoveFileExW WriteThrough primary/backup, permanent runtime regression GREEN, Shared build 0 warning / 0 error.
- [x] #169 — immutable dependency provenance tied to publish; build-local isolated --artifacts-path + immutable assets SHA-256 snapshot + --no-restore publish + Service/Tray runtime dependency graph cross-check; dedicated regression GREEN, final clean live gate continues under #173.
- [x] #172 — canonical installer npm metadata envelope compatibility; exactly-one object/array normalization + identity/integrity fail-closed, InstallerAstGrepMetadataRegression 5/5 GREEN.
- [x] #173 — canonical artifacts-path Tray pre-bundle deps lookup; source fix + NativeInstallerSourceRegression GREEN; clean `c54d1d6` canonical build + manifest-bound live deploy + exact-installed source verification GREEN.
- [x] #174 — residual structured/list tools: finite absolute ceilings + deterministic continuation metadata; response-bounds regression GREEN; fix/test commits pushed to Gitea+GitHub; canonical installer/deploy exact-installed `7ea7024` live GREEN.
- [x] #175 — legacy `talvora_read_text` / `talvora_list` finite response budgets: bounded streaming/entry+character ceilings, explicit paginated-alternative guidance, cancellation, targeted response-bounds regression GREEN; fix commit `949272e` pushed to Gitea+GitHub and canonical exact-installed live gate GREEN.
- [x] #176 — `talvora_archive_list`: finite 20,000-entry + 8 MiB response ceiling, archive-order offset continuation, total/truncation metadata; response-bounds regression GREEN, Release build 0 warning / 0 error, both remotes pushed, canonical live acceptance GREEN.
- [x] #177 — Talvora lifecycle concurrency: `ExecuteTalvoraAsync` shared `ManagedMcpOperationCoordinator` lease kullanıyor; Control Center/manual ve automatic recovery start/stop/restart yarışları engellendi; targeted regression GREEN, Tray Release build 0 warning / 0 error, fix `234f26e` iki remote'a push edildi ve exact-installed live gate GREEN.

## Faz 14 — Canonical Source Edit Transaction Engine (2026-09-20)

> Ayrıntılı teknik sözleşme: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`. Core + Routing Contract v2 + ast-grep structural adapter exact-installed live doğrulandı; yalnız Roslyn semantic adapter ayrı sonraki subphase olarak deferred.

- [x] #110 — Agent-first Source Edit Transaction Engine: normalized changeset modeli, exact-by-default preconditions, revision/hash optimistic concurrency, idempotent transactionId + durable receipt/journal, multi-file all-or-nothing semantics, rollback/recovery.
- [x] `talvora_apply_patch`: PRIMARY/default source editor; Git'e bağımlı olmayan agent-friendly custom patch DSL; add/update/delete/move + multi-file/multi-hunk; Git unified diff aynı tool üzerinde compatibility input/backend olarak kalır.
- [x] `talvora_apply_edits`: exact zero-based UTF-16 range/revision koordinatları zaten bilinen/üretilen durumlar için specialist deterministic structured edit yüzeyi. Multi-file olması tek başına bu aracı seçme nedeni değildir.
- [x] `talvora_source_edit_guide`: tool seçimi belirsizse mutator deneme-yanılmasına girmeden canonical routing contract'ı döndüren read-only MCP tool.
- [x] Atomic commit altyapısı: same-volume staging, durable flush, replace/move strategy, encoding/BOM/newline preservation, ACL/read-only/reparse-point davranışı ve failure recovery.
- [x] Stable source-edit error taxonomy ve structured diagnostics/receipts; conflict/parse/context/recovery domain kodları ve MCP-visible policy errors.
- [x] Fuzzy matching yalnız candidate/diagnostic amaçlı; fuzzy-only otomatik mutation kesinlikle yasak.
- [x] SourceMutationPolicy: recognized development workspace içindeki legacy text mutations (`write_text`, `replace_text`, `append_text`, text-like `write_bytes`), typed config mutator'ları, source-file delete ve same-workspace source-file move server-side `SOURCE_EDIT_POLICY_VIOLATION` ile canonical source-edit rotasına yönlendirilir. Directory/generated/binary/non-workspace ve cross-workspace capability korunur.
- [x] Tool-routing contract MCP `ServerInstructions` + tool Title/Description + `talvora_source_edit_guide` + server policy + docs + regressions ile tek kaynaktan sabitlendi.
- [x] Unrestricted PowerShell/process capability korunuyor; tool açıklamalarında normal source editing için canonical olmadığı açık.
- [x] Structural backend #114: ast-grep proposal-only adapter implemented; verified latest official Windows platform package is vendored with installer provenance and no live-workspace direct write.
- [x] Semantic backend kararı: Roslyn preferred C# backend; generic transaction core tamamlandıktan sonraki ayrı subphase.
- [x] Conflict/rebase preview kararı: DiffPlex 1.9.0 preferred advisory candidate; three-way/fuzzy sonuç otomatik commit edilmez.
- [x] Polyglot syntax validation kararı: core JSON/XML/YAML/TOML maintained parsers; Tree-sitter yalnız gelecekte gerçek polyglot ihtiyaç olduğunda ayrı adapter.
- [x] Exact-installed routing/structural tool surface GREEN; raw MCP tools/list = 203, guide/primary/structural/exact-range metadata live doğrulandı.
- [x] Targeted tests: 27/27 Source Edit regression GREEN.
- [x] Release build + yalnız ilgili regression/smoke; gereksiz geniş test tekrarı yok.
- [x] Routing Contract v2 canonical installer build/deploy + exact-installed 202-tool discovery + guide/primary-specialist metadata/lifecycle policy live verification GREEN.
- [x] BUG-AUDIT/HANDOFF/TODO closeout; git diff --check clean; kullanıcı açıkça istemeden commit/push YOK.

### Faz 14 locked implementation plan — 2026-09-20 research gate

- [x] Context7 + current official research revalidated: LSP WorkspaceEdit, AHP Changesets, ast-grep, Roslyn, DiffPlex, Tree-sitter, Git compatibility, Windows durability/TxF alternatives.
- [x] Exact source-edit routing surface locked: `talvora_read_source`, PRIMARY `talvora_apply_patch`, specialist `talvora_apply_edits`, read-only `talvora_source_edit_guide`.
- [x] Talvora Patch DSL v1 grammar locked; existing-file operations require SHA-256 revision; exact unique hunk matching only.
- [x] Normalized changeset and structured result/error model locked.
- [x] Durable append-only WAL, receipt retention, idempotency and crash/lost-response recovery semantics locked.
- [x] Same-volume staging + ReplaceFileW/rename + durable flush + reverse rollback algorithm locked; no TxF.
- [x] Workspace/source classifier + SourceMutationPolicy behavior locked; required legacy text writers guarded, general shell/process stays unrestricted.
- [x] Encoding/BOM/newline/read-only/ACL/reparse/large-file semantics locked.
- [x] Core syntax validation scope locked: JSON/XML/YAML/TOML now; Roslyn/Tree-sitter later where they add real value.
- [x] Structural decision: ast-grep remains preferred proposal backend but is not a core dependency; Chocolatey currently has no ast-grep package.
- [x] Semantic decision: Roslyn 5.9.0 is preferred C# backend after core transaction gates.
- [x] Conflict-preview decision: DiffPlex 1.9.0 preferred future in-process three-way preview; never commit preview directly.
- [x] Failure-injection matrix and deployment gate sequence locked.
- [x] Implement core model/parser/codec/classifier.
- [x] Implement WAL/idempotency/commit/rollback/recovery.
- [x] Add three core MCP tools and manifest entries.
- [x] Wire SourceMutationPolicy into legacy write_text/replace_text/append_text/text-like write_bytes and update execution/file-tool descriptions.
- [x] Add and run dedicated targeted Source Edit regression project.
- [x] Release build + only affected regressions.
- [x] Canonical installer build/deploy + exact-installed source-edit discovery/policy smoke.
- [x] Close #110 with final evidence; no commit/push unless explicitly requested.


- [x] #115 — lost-response retry contract: external request hash + receipt/journal-first lookup; dedicated regression ve live replay GREEN.


- [x] #116 — attempted/applied ownership + safe rollback; targeted concurrent-writer regression GREEN.


- [x] #117: typed config mutators (JSON/dotenv/INI/XML/YAML/TOML set/delete) recognized-workspace SourceMutationPolicy guard + routing descriptions + regression/live smoke GREEN.
- [x] #118: SourceEditDomainException -> McpException; exact-installed live policy smoke'ta SOURCE_EDIT_POLICY_VIOLATION client'a görünür.
- [x] #114 structural backend implementation: canonical installer resolves latest official Windows platform ast-grep package, uses `--ignore-scripts`, vendors `ast-grep.exe` + version/license/SRI/executable-SHA provenance; runtime prefers and verifies vendored payload.
- [x] `talvora_structural_edit`: pattern/kind + rewrite and YAML rule/fix modes; ast-grep `--json=stream` proposal-only isolated mirror -> scalar/UTF-8 byte/text cross-check -> exact revision/range Source Edit transaction.
- [x] Structural resource/diagnostic contract: bounded output/matches/files/bytes, cancellation/timeout, stable no-match/proposal/toolchain/tool-failed codes, overlap/stale protection inherited by normalized transaction, no direct workspace mutation.
- [x] Routing contract extended: repetitive AST/syntax-shaped transformation -> structural specialist; ordinary source work remains PRIMARY `talvora_apply_patch`; exact-range-only work remains `talvora_apply_edits`.
- [x] Structural targeted evidence: generated-edit durable idempotency + Unicode scalar->UTF-16 regression; full Source Edit 27/27 GREEN; NativeInstaller structural payload/provenance contract GREEN.
- [x] Canonical installer/exact-installed gate: runtime `...-dirty-b55f88d972ca`, raw tools/list 203, ast-grep 0.45.3 provenance/hash verified, live emoji pattern/rewrite + YAML rule/fix + same-transaction `replayed=true` GREEN.
- [x] #119: Agent Source-Edit Routing Determinism / File Lifecycle Coverage Gap — apply_patch PRIMARY/default, apply_edits exact-range-only, MCP server instructions + guide + centralized descriptions, direct workspace source delete/same-workspace move guard; targeted 25/25 + exact-installed 202-tool live gate GREEN. Live two-file ordinary change tek `talvora_apply_patch` transaction'ında commit oldu ve aynı transaction retry `replayed=true` döndü.

### Faz 14B — Roslyn C# Semantic Edit (2026-09-20)

> Scope is deliberately semantic-only. `talvora_apply_patch` remains PRIMARY/default for ordinary C# source changes; `talvora_structural_edit` remains the repetitive AST/syntax specialist; `talvora_apply_edits` remains the exact generated-range specialist.

- [x] Research gate: Context7 + current Microsoft/Roslyn/MSBuild docs revalidated. Current stable Roslyn workspace line is 5.9.0; `Microsoft.Build.Locator` registration occurs before MSBuild workspace creation; obsolete `WorkspaceFailed` event is not used.
- [x] Roslyn/MSBuild dependencies integrated with canonical restore provenance: `Microsoft.CodeAnalysis.CSharp.Workspaces 5.9.0`, `Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0`, `Microsoft.Build.Locator 1.11.2`; `Microsoft.Build.Framework 17.14.28` is compile-only/private with runtime assets excluded per Locator requirements.
- [x] Narrow `talvora_semantic_edit` specialist added. Current operation is C# symbol-aware rename only; ordinary C# changes remain on PRIMARY/default `talvora_apply_patch`.
- [x] Semantic request contract implemented: explicit workspace + solution/project path, transactionId, revisioned C# document, zero-based UTF-16 line/character anchor, new name, optional project/symbol disambiguation, conservative rename options and caller-visible resource/time limits.
- [x] Deterministic MSBuild bootstrap implemented: one-time Locator selection/registration before workspace creation, per-call disposable `MSBuildWorkspace`, `SkipUnrecognizedProjects=false`, project references loaded as projects, cancellation-aware loading and actual MSBuild identity in receipt.
- [x] Workspace diagnostics use current `RegisterWorkspaceFailedHandler` plus workspace diagnostics, bounded without hiding failure count; load failures reject before mutation with structured domain diagnostics and no text/regex fallback.
- [x] Symbol identity resolves every eligible anchored Roslyn document with SemanticModel/SymbolFinder. No-symbol and divergent linked/multi-project identities reject with zero mutation; explicit project selector disambiguates.
- [x] Rename proposal remains in memory through current `Renamer.RenameSymbolAsync(..., SymbolRenameOptions, ...)`; `RenameFile=false`; no `Workspace.TryApplyChanges`; Roslyn never writes the live workspace.
- [x] Minimal-change conversion uses changed `Solution` -> changed documents -> exact UTF-16 `TextChange` ranges with `ExpectedText`; linked physical files are deduplicated and contradictory proposals reject.
- [x] Live-file fidelity is preserved: Roslyn old text must equal the revisioned Talvora disk snapshot. Existing codec remains encoding/BOM/newline authority; semantic rename does not run Formatter/Simplifier automatically and targeted/live evidence shows no unrelated formatting churn.
- [x] Semantic proposals commit only through `SourceEditEngine.ApplyGeneratedEditsAsync`: durable request-hash replay precedes Roslyn generation, then SHA-256 preconditions, WAL, commit barrier, rollback/recovery and all-or-nothing transaction semantics remain canonical.
- [x] Stable semantic domain errors, caller cancellation, timeout and bounded project/document/change/diagnostic state implemented; hidden partial success is rejected.
- [x] Structured semantic receipt includes actual workspace/MSBuild identity, symbol identity/display/kind, bounded diagnostics and the underlying Source Edit transaction receipt without logging source bodies.
- [x] Routing Contract v3 implemented across ServerInstructions, tool Title/Description, `talvora_source_edit_guide`, manifest, server policy wording and regressions. Agent routing is deterministic before mutation.
- [x] Targeted Source Edit/Semantic regression 33/33 GREEN: cross-document rename, durable replay, stale anchor, no-symbol, linked-context ambiguity, load failure, cancellation, UTF-16 BOM/newline preservation, no formatting churn and routing contract.
- [x] Final gates GREEN: Talvora/targeted Release build 0 warning / 0 error; NativeInstaller source regression GREEN including semantic contract and #109 explicit `--silent`; `git diff --check` exit 0; current canonical installer SHA-256 `28171A926EA96D78B05724201BCAFA30E3351208DB56EAB517384FFB0AFC991A`; canonical deploy/live snapshot `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-8726cde50619`; raw exact-installed `tools/list` 204/204 unique; routing smoke GREEN; real two-project semantic rename GREEN with UTF-16 LE BOM/CRLF/deliberate-spacing preservation; same semantic transaction retry `replayed=true`. Living-doc closeout follows this deploy snapshot without a self-referential docs-only rebuild/fingerprint loop.
