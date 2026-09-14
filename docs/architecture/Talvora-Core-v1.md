# Talvora Core Architecture v1

## Amaç

Talvora, ChatGPT ile Windows arasında yüksek yetenekli yerel kontrol köprüsüdür. Çekirdek dosya sistemi, PowerShell/CMD, süreç yönetimi, sistem bilgisi ve yükseltilmiş Windows işlemleri için ayrı privilege-boundary sağlar.

## Değişmez mimari kurallar

1. `Talvora.Abstractions` ve `Talvora.Core`, MCP SDK veya Windows platform projesine referans vermez.
2. MCP iş mantığı içermez; protokol girdisini `Talvora.Application` orkestrasyon katmanına ve ilgili capability servislerine çevirir.
3. Modüller birbirini doğrudan çağırmaz. Ortak orkestrasyon `Talvora.Application` katmanında yapılır.
4. Windows'a özgü native entegrasyonlar `Talvora.Platform.Windows` altında tutulur.
5. Yönetici yetkisi isteyen işlemler ana MCP process'ini SYSTEM olarak çalıştırmak yerine `Talvora.ElevatedBroker` süreç sınırından yürütülür.
6. Kullanıcı tarafından istenen işlevlere yapay klasör sandbox'ı, komut deny-list'i veya keyfî işlem limiti eklenmez. Etkin yetki Windows hesabı ve gerektiğinde elevated broker tarafından belirlenir.
7. Yeni özellik eklemek Core'u şişirmek yerine yeni, küçük bir capability/module üzerinden yapılır.
8. Host ve MCP Adapter gRPC veya Named-Pipe ayrıntılarını taşımaz; broker istemci transportu `Talvora.Ipc.Client` içinde kapsüllenir.

## Katmanlar

```text
ChatGPT
  |
  v
Talvora.Adapter.Mcp
  |
  v
Talvora.Application
  |
  +--> Talvora.Modules.Shell
  +--> Talvora.Modules.Processes
  +--> Talvora.Ipc.Client --> Talvora.Ipc.Contracts --> gRPC/Named Pipe --> Talvora.ElevatedBroker
  |
  `--> Talvora.Abstractions

Talvora.Core / Talvora.Abstractions
  |
  +--> Talvora.Modules.FileSystem
  `--> Talvora.Platform.Windows
```

`Talvora.Application`, normal kullanıcı bağlamındaki servislerle transport-neutral `IBrokerClient` arasında execution routing yapar. MCP Adapter generated gRPC/protobuf tiplerini veya named-pipe implementation ayrıntılarını bilmez.

## Çalışma modeli

`Talvora.Host` konsol/debug modunda veya Windows Service yaşam döngüsü altında çalışabilecek şekilde kurulur. Varsayılan HTTP endpoint `127.0.0.1:7676`, MCP endpoint `/mcp`, sağlık endpoint `/health` ve broker sağlık endpoint `/health/broker`'dır.

MCP HTTP transport stateless olarak yapılandırılır. Araç kayıtları reflection tabanlı assembly taraması yerine açık `WithTools<T>()` kayıtlarıyla yapılır.

## Elevated Broker IPC

Broker protokolü `Talvora.Ipc.Contracts` içinde Protocol Buffers ile sürümlüdür. Güncel wire protokolü `talvora.ipc.v2`, `BrokerProtocol.CurrentVersion == 2` ve varsayılan pipe adı `Talvora.ElevatedBroker.v2` değerlerini kullanır.

Windows üzerinde Host ile Broker arasındaki transport:

- gRPC / HTTP/2
- Kestrel Windows Named Pipes transport
- TCP portu yok
- client tarafında `TokenImpersonationLevel.None`
- elevated ve non-elevated süreçlerin konuşabilmesi için `CurrentUserOnly` yerine açık SID ACL
- broker pipe owner doğrulaması: beklenen kullanıcı veya LocalSystem

Named-pipe/gRPC transport bağımsız `Talvora.Ipc.Client` katmanında kapsüllenir. Böylece Host, MCP Adapter ve application orkestrasyonu generated gRPC/pipe implementation ayrıntılarını bilmez.

Elevated Broker kalıcı Windows Service olarak `LocalSystem` hesabında çalışabilir. Kurulum bir kez UAC ister; normal Host yükseltilmeden kalır. `ExecutionRouter`, açık administrator isteğini doğrudan broker'a yönlendirir; `Auto` modu ise desteklenen Windows process-start privilege hatalarında normal yürütmeden broker yürütmesine geçer.

Shell komutu başarıyla başladıktan sonra non-zero exit code alınması aynı komutun otomatik ikinci kez elevated çalıştırılması anlamına gelmez; yan etkili komutların iki kez uygulanmasını önlemek için böyle bir yeniden yürütme yapılmaz. Yönetici hakkı gerektiği önceden bilinen shell komutları açık administrator isteğiyle çalıştırılır.

## Performans yaklaşımı

- Ana uygulama: .NET 10 LTS / C# 14.
- Native kod: yalnız ölçüm veya API gereksinimi .NET/LibraryImport katmanını yetersiz bıraktığında C++23/MSVC.
- Native AOT: başlangıç varsayımı değildir; önce ölçüm yapılır.
- Shell ve process I/O asenkron yürütülür; iptal child process tree'yi sonlandırabilir.
- Yerel privilege-boundary IPC için TCP yerine Named Pipes kullanılır.
- Broker bağlantı süresi yalnız named-pipe bağlantı aşamasında sınırlandırılır; uzun süren gerçek shell/process execution bu bağlantı timeout'u tarafından kesilmez.

## Genişleme modeli

Registry, Services, Network, ADB, UI Automation, screenshots ve benzeri özellikler bağımsız modüller olarak eklenir. MCP tarafında yeni adapter tool sınıfı eklemek dışında mevcut modüllerin yeniden yazılması gerekmez.
