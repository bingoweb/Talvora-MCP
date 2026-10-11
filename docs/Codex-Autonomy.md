# Talvora Codex: Esnek gelistirme modu

## Guvenilir gelistirici profili

Talvora varsayilanda dengeli (balanced) profili kullanir. Isteyen kullanici
talvora_codex_configure aracini defaultProfile=trusted ile bir kez calistirir.
Sonraki Codex cagrilarinda sandbox=auto bu tercihe uyar. Trusted profilde
Codex, oturum acmis Windows kullanicisinin sahip oldugu dosya ve ag
izinlerini dogrudan kullanir. LocalSystem ya da yonetici yetkisi kazanmaz.
talvora_codex_preferences araci aktif profili gosterir; defaultProfile=balanced
ile onceki moda donulebilir. Secim kullanicinin LocalAppData altinda saklanir.

NuGet gercek kabul testinde normal Codex sandbox'inda Schannel
SEC_E_NO_CREDENTIALS goruldu; trusted modda HTTP 200 saglandi. Codex temiz
yerel onbellege Newtonsoft.Json 13.0.3 indirip .NET 10 uygulamasini sifir
hata ve sifir uyariyla derledi. Ayrica ripgrep 15.2.0 yüklendi ve Codex
icinde rg komutunun calistigi goruldu.

## Arka plan gorevleri

talvora_codex_start uzun gorevi baslatip hemen jobId dondurur. Baglanti
kesildiginde, Talvora servisi calismaya devam ettikce gorev surer.
talvora_codex_status ilerlemeyi, sessionId'yi, gecen sureyi, komut ve MCP
araci sayilarini gosterir. Sonuc icin includeResult=true secilir.
talvora_codex_list isleri listeler; talvora_codex_cancel islemi iptal eder.
Saatler suren islerde JSONL olaylari ayri ve sinirli bir ilerleme kanalindan
izlenir, uzun cikti nedeniyle sayaclar donmaz. Masaustu bildirimi ise gorev
baslangicindan gercek bitis/iptal anina kadar tek is kaydi olarak kalir.

Talvora hizmeti yeniden baslarsa arka plan jobId kayitlari kaybolabilir.
Codex oturumlari korundugundan onceki sessionId ile yeni bir is baslatip
resumeSessionId kullanilabilir.

Talvora, Windows'ta oturum acmis kullanicinin Codex CLI kurulumunu ve giris
bilgilerini kullanir. Codex normal kullanici haklariyla calisir.

## Varsayilan

- Model: gpt-6.1-sol; dusunme: medium.
- sandbox=workspace-write: proje icinde kod yazar, test ve derleme yapar.
- approvalMode=automatic: Codex --approve-for-me otomatik onay incelemesi.
- networkAccess=true: workspace-write icin ag erisimi talep edilir.
- preserveSession=true: oturum gecmisi korunur; sessionId yanitta doner.
- allowNonGitWorkspace=true: Git deposu olmayan klasorlerde de calisir.
- Tamamlanan komut ve MCP araci sayilari ile son calisma ozeti sunulur.

## Bir goreve devam

Ilk cevabin sessionId degerini sonraki cagrida resumeSessionId olarak verin.
Ayni workingDirectory kullanin. preserveSession=true olmali.

## Ek klasor ve genis yetki

additionalWritableDirectories listesine mutlak dizinler yazin.
Bu parametre ilk oturumda gecerlidir, exec resume ile desteklenmez.

Yalnizca istege bagli tek bir gorev icin sandbox=danger-full-access ve
allowFullAccess=true secilebilir. Bu durumda Codex sandbox'i kalkar;
Windows kullanicisi SYSTEM olmaz. Varsayilan mod daha kontrolludur.

## Daraltma secenekleri

- sandbox=read-only: yalniz inceleme.
- approvalMode=never: otomatik onayi kapat.
- networkAccess=false: ag erisim istegini kapat.
- preserveSession=false: tek seferlik, kaydedilmeyen oturum.

## Test edilmis gelistirme davranisi

Gorev durumu, canli JSONL ilerlemesi ve iptal icin Talvora araclari
bulunur. Balanced modda bazi Windows cache erisimlerinde sorun varsa
trusted profil normal kullanici izinleriyle calismayi saglar.
