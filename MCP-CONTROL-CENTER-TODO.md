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