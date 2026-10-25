# Talvora Codex: Esnek geliştirme modu

## Güvenilir geliştirici profili

Talvora varsayılanda dengeli (balanced) profili kullanır. İsteyen kullanıcı
talvora_codex_configure aracını defaultProfile=trusted ile bir kez çalıştırır.
Sonraki Codex çağrılarında sandbox=auto bu tercihe uyar. Trusted profilde
Codex, oturum açmış Windows kullanıcısının sahip olduğu dosya ve ağ
izinlerini dogrudan kullanır. LocalSystem ya da yönetici yetkisi kazanmaz.
talvora_codex_preferences aracı aktif profili gösterir; defaultProfile=balanced
ile önceki moda dönülebilir. Seçim kullanıcının LocalAppData altinda saklanır.

NuGet gerçek kabul testinde normal Codex sandbox'ında Schannel
SEC_E_NO_CREDENTIALS görüldü; trusted modda HTTP 200 sağlandı. Codex temiz
yerel önbelleğe Newtonsoft.Json 13.0.3 indirip .NET 10 uygulamasini sıfır
hata ve sıfır uyarıyla derledi. Ayrıca ripgrep 15.2.0 yüklendi ve Codex
içinde rg komutunun çalıştığı görüldü.

## Arka plan görevleri

talvora_codex_start uzun görevi başlatıp hemen jobId döndürür. Bağlantı
kesildiğinde, Talvora servisi çalışmaya devam ettikçe görev sürer.
talvora_codex_status ilerlemeyi, sessionId'yi, geçen süreyi, komut ve MCP
aracı sayılarını gösterir. Sonuç için includeResult=true seçilir.
talvora_codex_list işleri listeler; talvora_codex_cancel işlemi iptal eder.
Saatler süren işlerde JSONL olayları ayrı ve sınırlı bir ilerleme kanalından
izlenir, uzun çıktı nedeniyle sayaçlar donmaz. Masaüstü bildirimi ise görev
başlangıcından gerçek bitiş/iptal anına kadar tek iş kaydı olarak kalır.

Talvora hizmeti yeniden başlasa da görev kimlikleri, durumları ve sayaçları
korunur. Aynı PID ve işlem başlangıç zamanı doğrulanan görevler `orphaned`
olarak görüntülenir; doğrulanamayanlar `failed` olur. `orphaned` görevler
işlem ağacı güvenle sonlandırılarak iptal edilebilir. İki motor birlikte en çok
4 etkin görevi destekler; aynı klasörde paralel çalışma için `allowParallel=true`
gereklidir. Codex oturumuna `resumeSessionId` ile devam edilebilir.

Talvora, Windows'ta oturum açmış kullanıcının Codex CLI kurulumunu ve giriş
bilgilerini kullanır. Codex normal kullanıcı haklarıyla çalışır.

## Varsayılan

- Model: gpt-6.1-sol; düşünme: medium.
- sandbox=workspace-write: proje içinde kod yazar, test ve derleme yapar.
- approvalMode=automatic: Codex --approve-for-me otomatik onay incelemesi.
- networkAccess=true: workspace-write için ağ erişimi talep edilir.
- preserveSession=true: oturum geçmişi korunur; sessionId yanıtta döner.
- allowNonGitWorkspace=true: Git deposu olmayan klasörlerde de çalışır.
- Tamamlanan komut ve MCP aracı sayilari ile son çalışma özeti sunulur.

## Bir göreve devam

İlk cevabın sessionId değerini sonraki çağrıda resumeSessionId olarak verin.
Aynı workingDirectory kullanın. preserveSession=true olmalı.

## Ek klasör ve geniş yetki

additionalWritableDirectories listesine mutlak dizinler yazın.
Bu parametre ilk oturumda geçerlidir, exec resume ile desteklenmez.

Yalnızca isteğe bağlı tek bir görev için sandbox=danger-full-access ve
allowFullAccess=true seçilebilir. Bu durumda Codex sandbox'i kalkar;
Windows kullanıcısı SYSTEM olmaz. Varsayılan mod daha kontrollüdür.

## Daraltma seçenekleri

- sandbox=read-only: yalnız inceleme.
- approvalMode=never: otomatik onayı kapat.
- networkAccess=false: ağ erişim isteğini kapat.
- preserveSession=false: tek seferlik, kaydedilmeyen oturum.

## Test edilmiş geliştirme davranışı

Görev durumu, canlı JSONL ilerlemesi ve iptal için Talvora araçları
bulunur. Balanced modda bazı Windows cache erişimlerinde sorun varsa
trusted profil normal kullanıcı izinleriyle çalışmayı sağlar.
