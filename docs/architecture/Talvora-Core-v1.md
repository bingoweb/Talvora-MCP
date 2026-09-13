# Talvora Core Architecture v1

## Amaç

Talvora, ChatGPT ile Windows arasında yüksek yetenekli yerel kontrol köprüsüdür. Çekirdek dosya sistemi, PowerShell/CMD, süreç yönetimi, sistem bilgisi ve yükseltilmiş Windows işlemleri için ayrı privilege-boundary sağlar.

## Değişmez mimari kurallar

1. `Talvora.Abstractions` ve `Talvora.Core`, MCP SDK veya Windows platform projesine referans vermez.
2. MCP iş mantığı içermez; yalnız protokol girdisini Talvora servislerine çevirir.
3. Modüller birbirini doğrudan çağırmaz. Ortak orkestrasyon gerektiğinde Core/application katmanında yapılır.
4. Windows'a özgü native entegrasyonlar `Talvora.Platform.Windows` altında tutulur.
5. Yönetici yetkisi isteyen işlemler ana MCP process'ini SYSTEM olarak çalıştırmak yerine `Talvora.ElevatedBroker` süreç sınırından yürütülür.
6. Kullanıcı tarafından istenen işlevlere yapay klasör sandbox'ı, komut deny-list'i veya keyfî işlem limiti eklenmez. Etkin yetki Windows hesabı ve gerektiğinde elevated broker tarafından belirlenir.
7. Yeni özellik eklemek Core'u şişirmek yerine yeni, küçük bir capability/module üzerinden yapılır.
8. Host gRPC veya Named-Pipe ayrıntılarını doğrudan taşımaz; broker istemci transportu `Talvora.Ipc.Client` içinde kapsüllenir.

## Katmanlar

```text
ChatGPT
  |
  v
Talvora.Adapter.Mcp
  |
  v
Talvora.Core / Talvora.Abstractions
  |
  +--> Modules.FileSystem
  +--> Modules.Shell
  +--> Modules.Processes
  +--> Platform.Windows
  |
  `--> Ipc.Client --> Ipc.Contracts --> gRPC/Named Pipe --> ElevatedBroker
```

## Çalışma modeli

`Talvora.Host` konsol/debug modunda veya Windows Service yaşam döngüsü altında çalışabilecek şekilde kurulur. Varsayılan HTTP endpoint `127.0.0.1:7676`, MCP endpoint `/mcp`, sağlık endpoint `/health` ve broker sağlık endpoint `/health/broker`'dır.

MCP HTTP transport stateless olarak yapılandırılır. Araç kayıtları reflection tabanlı assembly taraması yerine açık `WithTools<T>()` kayıtlarıyla yapılır.

## Elevated Broker IPC

Broker protokolü `Talvora.Ipc.Contracts` içinde Protocol Buffers ile sürümlüdür. V1 protokolü `BrokerProtocol.CurrentVersion == 1` değerini kullanır.

Windows üzerinde Host ile Broker arasındaki transport:

- gRPC / HTTP/2
- Kestrel Windows Named Pipes transport
- TCP portu yok
- client tarafında `TokenImpersonationLevel.None`
- elevated ve non-elevated süreçlerin konuşabilmesi için `CurrentUserOnly` yerine açık SID ACL
- broker pipe owner doğrulaması: beklenen kullanıcı veya LocalSystem

Named-pipe/gRPC transport bağımsız `Talvora.Ipc.Client` katmanında kapsüllenir. Böylece Host, gRPC ve pipe implementation ayrıntılarını bilmez.

Windows Service olarak kalıcı broker kurulumu ve yönetici işlem routing'i sonraki alt fazdır.

## Performans yaklaşımı

- Ana uygulama: .NET 10 LTS / C# 14.
- Native kod: yalnız ölçüm veya API gereksinimi .NET/LibraryImport katmanını yetersiz bıraktığında C++23/MSVC.
- Native AOT: başlangıç varsayımı değildir; önce ölçüm yapılır.
- Shell ve process I/O asenkron yürütülür; iptal child process tree'yi sonlandırabilir.
- Yerel privilege-boundary IPC için TCP yerine Named Pipes kullanılır.

## Genişleme modeli

Registry, Services, Network, ADB, UI Automation, screenshots ve benzeri özellikler bağımsız modüller olarak eklenir. MCP tarafında yeni adapter tool sınıfı eklemek dışında mevcut modüllerin yeniden yazılması gerekmez.
