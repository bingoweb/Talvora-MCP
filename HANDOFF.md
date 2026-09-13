# Talvora — Güncel Oturum Devri

Bu belge Talvora'nın yeni bir ChatGPT oturumunda **kaldığı yerden otomatik devam edebilmesi** için kanonik devir noktasıdır.

## 1. Proje Kimliği

- Proje: **Talvora**
- Amaç: ChatGPT ile Windows arasında güçlü, Windows-native, modüler bir kontrol/otomasyon köprüsü.
- Ana platform: **.NET 10 LTS / C# 14**
- Ana çalışma modeli: normal kullanıcı **Talvora Host** + ayrı **Elevated Broker Windows Service**.
- MCP: **2026-07-28 stateless protocol**.
- Elevated IPC: **gRPC over Windows Named Pipes**, protokol v1.
- GUI şu anda kapsam dışı. Core + MCP + CLI/service altyapısı öncelikli.

## 2. Doğrulanmış Son Durum — alpha.18

Gerçek Windows makinede aşağıdakiler kullanıcı tarafından doğrudan doğrulandı:

### Core / MCP baseline

- Architecture dependency boundaries: GREEN
- Tüm proje build: GREEN
- Unit/integration testler: **15/15 PASS**
- MCP `/health`: GREEN
- MCP `server/discover`: GREEN
- MCP `tools/list`: GREEN — 8 araç
- MCP `tools/call(get_system_info)`: GREEN

MCP araçları:

1. `list_processes`
2. `stop_process`
3. `get_system_info`
4. `delete_file`
5. `write_file`
6. `read_file`
7. `start_process`
8. `run_shell`

### Elevated Broker v1

Gerçek Windows üzerinde şu zincir GREEN:

- `TalvoraElevatedBroker` Windows Service: **Running**
- Service account: **LocalSystem**
- Startup: **Automatic**
- Transport: **gRPC over Windows Named Pipe**
- Protocol: **1**
- Broker elevated: GREEN
- Host non-elevated: GREEN
- Cross-elevation Host → LocalSystem Broker: GREEN

Servis publish kökü:

`%ProgramFiles%\Talvora\Broker`

Varsayılan pipe:

`Talvora.ElevatedBroker.v1`

### Son çözülmüş kritik hata

Windows, LocalSystem servisin oluşturduğu named pipe için varsayılan owner olarak bazı sistemlerde `BUILTIN\Administrators` SID'ini seçebiliyordu. Client owner kontrolü bunu reddediyordu.

**alpha.18 çözümü:** Broker, `PipeSecurity` owner'ını açıkça broker process'in gerçek kullanıcı SID'ine sabitliyor. Client'ın sıkı owner doğrulaması gevşetilmedi.

Gerçek Windows sonucu:

```text
Talvora Elevated Broker Windows Service: GREEN
  service:           Running
  service account:   LocalSystem
  transport:         gRPC over Windows Named Pipe
  protocol:          1
  broker elevated:   GREEN
  host non-elevated: GREEN
  cross-elevation:   GREEN
```

## 3. Mimari

Talvora şu yapıyı kullanır:

```text
ChatGPT / MCP
      ↓
Talvora.Adapter.Mcp
      ↓
Talvora Core + Modules
      ↓
Talvora Host (normal kullanıcı)
      ↓
Talvora.Ipc.Client
      ↓
gRPC / Windows Named Pipe
      ↓
Talvora Elevated Broker (LocalSystem)
      ↓
Yönetici gerektiren Windows işlemleri
```

Temel prensipler:

- `Talvora.Core` MCP, PowerShell, Win32, Registry, ADB, HTTP veya gRPC bilmez.
- MCP yalnız bir adapter/transport katmanıdır.
- Windows bağımlılıkları platform/IPC katmanlarında tutulur.
- Elevated Broker ayrı process/service'dir.
- Yerel privilege IPC için TCP kullanılmaz; Windows Named Pipes kullanılır.
- Native C++ yalnız .NET/Win32 interop yetersiz veya ölçülmüş performans gereksinimi olduğunda eklenir.
- Native AOT şu anda zorlanmaz; kod AOT-friendly tutulur.

## 4. Önemli Projeler

Kaynak projeler:

- `src/Talvora.Abstractions`
- `src/Talvora.Core`
- `src/Talvora.Platform.Windows`
- `src/Talvora.Modules.FileSystem`
- `src/Talvora.Modules.Shell`
- `src/Talvora.Modules.Processes`
- `src/Talvora.Adapter.Mcp`
- `src/Talvora.Ipc.Contracts`
- `src/Talvora.Ipc.Client`
- `src/Talvora.ElevatedBroker`
- `src/Talvora.Host`

Test projeleri:

- `tests/Talvora.Core.Tests`
- `tests/Talvora.FileSystem.Tests`
- `tests/Talvora.Shell.Tests`
- `tests/Talvora.Architecture.Tests`
- `tests/Talvora.Ipc.Tests`
- `tests/bootstrap-regressions`

Önemli scriptler:

- `TALVORA-TEST.bat`
- `TALVORA-BASLAT.bat`
- `TALVORA-KUR.bat`
- `TALVORA-BROKER-KUR.bat`
- `TALVORA-BROKER-KALDIR.bat`
- `scripts/Test-McpSmoke.ps1`
- `scripts/Test-BrokerSmoke.ps1`
- `scripts/Test-InstalledBrokerService.ps1`
- `scripts/Install-BrokerService.ps1`
- `scripts/Uninstall-BrokerService.ps1`
- `scripts/verify.ps1`

## 5. Sürüm / Paket Bilgileri

- Current version suffix: **alpha.18**
- .NET SDK baseline: **10.0.401**
- Runtime testte görülen .NET: **10.0.12**
- PowerShell test ortamı: **7.6.6**
- Git test ortamı: **2.55.0.windows.3**
- `ModelContextProtocol.AspNetCore`: **2.2.0**
- `Grpc.AspNetCore`: **2.83.0**
- `Grpc.Net.Client`: **2.83.0**
- `Grpc.Tools`: **2.83.0**
- `Google.Protobuf`: **3.36.1**

## 6. Kalite Kuralları

Bunlar yeni oturumda korunacak:

1. API/library kararlarında **Context7 + güncel resmî internet kaynağı** kullan.
2. Yeni özellik/bugfixlerde TDD: önce RED, sonra GREEN, sonra refactor.
3. Analyzer, warning veya kalite kapısını bastırarak hata çözme. Kök nedeni düzelt.
4. Gerçek Windows çıktısı olmadan Windows build/runtime için GREEN iddiasında bulunma.
5. Kullanıcıdan gereksiz sürekli onay isteme; teknik kararı verip ilerle.
6. Güvenlik bahanesiyle istenen yeteneği yapay biçimde kırpma. Gerçek teknik/politika zorunluluğu varsa açıkla; mümkünse mimari olarak çöz.
7. Türkçe, açık ve doğrudan iletişim kullan.
8. Core'u dev bir agent platformuna çevirmeden ChatGPT ↔ Windows amacında tut.
9. Repo kalite hedefi ticari/production düzeyidir: deterministik davranış, hata modeli, bakım yapılabilirlik, performans, veri bütünlüğü ve geri dönüş güvenilirliği.

## 7. Kodlama / Windows Uyumluluğu Notları

- `.bat` / `.cmd`: **UTF-8 BOM'suz + CRLF**
- `.ps1`: Windows uyumluluğu için **UTF-8 BOM**
- Windows PowerShell 5.1 fallback'ı desteklenir; ana tercih PowerShell 7'dir.
- `Invoke-WebRequest` 5.1 fallback'ta `-UseBasicParsing` ile kullanılır.
- `ConvertFrom-Json -Depth` Windows PowerShell 5.1 için kullanılmaz.

## 8. Elevated Broker Güvenlik/IPC Gerçekleri

Bunlar özellik kısıtı değil, IPC doğruluğu içindir:

- Main Host normal kullanıcı olarak kalır.
- Broker LocalSystem Windows Service olarak çalışır.
- Pipe ACL kurulum yapan kullanıcı + gerekli sistem kimliği için açıkça oluşturulur.
- Pipe owner broker process SID'ine deterministik olarak sabitlenir.
- Client impersonation kullanılmaz (`TokenImpersonationLevel.None`).
- Client pipe owner'ı doğrular.
- Broker config/service asset yollarında `Directory.GetCurrentDirectory()` kullanılmaz; service modunda content root `AppContext.BaseDirectory` olmalıdır.

## 9. Bir Sonraki Faz — EXACT RESUME POINT

**İlk iş:** mevcut alpha.18 durumunu değiştirmeden Elevated Broker'ı gerçek yönetici operasyonlarını yürütebilen bir execution layer'a dönüştür.

Önerilen sıra:

1. `ElevatedOperation` / `OperationEnvelope` sözleşmesini tasarla.
2. Her istek için `OperationId`, protocol version, cancellation/timeout ve normalize `TalvoraError` taşı.
3. Broker tarafında ilk gerçek elevated operasyonu TDD ile ekle.
4. İlk operasyon olarak elevated shell/process execution seçilebilir:
   - PowerShell 7 / cmd
   - stdout/stderr
   - exit code
   - working directory
   - env vars
   - timeout/cancellation
   - process tree termination
5. Host normal/elevated execution yolunu otomatik seçsin; MCP kullanıcıya ayrı altyapı ayrıntısı yüklemesin.
6. Sonra sırayla Windows Service management, Registry ve diğer Windows yönetim modüllerine geç.
7. Her fazda unit test + architecture test + gerçek Windows smoke test ekle.

**Henüz yapılmaması gerekenler:** GUI, hot-plugin sistemi, erken Native AOT zorlaması, geniş kapsamlı registry/service/driver modüllerini IPC execution contract oturmadan eklemek.

## 10. Git / GitHub Durumu

GitHub kanonik uzak depo:

`https://github.com/bingoweb/Talvora-MCP`

GitHub varsayılan dalı:

`main`

Alpha.18 doğrulanmış kaynak ağacının GitHub import snapshot commit'i:

`9743f4fce601ce2c7350ff38c757a10ffb61b8e3` — `import: Talvora alpha.18 verified Windows baseline`

Önemli tarihçe notu: yeni GitHub deposu connector üzerinden oluşturulduğu için alpha.18 kaynakları **tam dosya snapshot'ı** olarak aktarıldı; yerel geliştirme reposundaki önceki commit zinciri GitHub'a birebir replay edilmedi. Ayrıntılı yerel provenance için önemli commitler:

- `a7ad9a3 chore: establish Talvora core baseline`
- `26c0ef6 feat: add elevated broker named-pipe grpc transport`
- `a4da250 feat: install elevated broker as Windows service`
- `676bfad fix: pin elevated broker pipe owner identity`
- `ec4ff7e docs: add canonical Talvora handoff`

Yeni geliştirme GitHub `main` üzerinden devam ettirilebilir. Kaynak snapshot bütünlüğü kökteki `SOURCE-SHA256.txt` ile izlenir.

---

## EXACT ONE-LINE RESUME POINT

**Talvora alpha.18 gerçek Windows üzerinde Core+MCP+15/15 test ve LocalSystem Elevated Broker gRPC/Named-Pipe cross-elevation hattı tamamen GREEN; sıradaki iş TDD ile sürümlü elevated operation contract ve ilk gerçek yönetici shell/process execution operasyonunu eklemektir.**
