# Talvora Roadmap

Talvora'nın tek ana hedefi ChatGPT sohbetini Windows ile güçlü biçimde bütünleştirmektir. Yol haritası bu hedef dışındaki genel amaçlı ajan-platform özelliklerini bilerek kapsam dışı tutar.

## Faz 0 — Çekirdek iskelet ve kalite kapıları

- Modüler solution sınırları
- .NET 10 / C# 14 temel yapı
- FileSystem, Shell, Processes, System Info servisleri
- MCP stateless HTTP adapter
- Console + Windows Service uyumlu host
- Elevated Broker için ayrı process/contract sınırı
- Architecture, unit, build ve test kapıları

**Çıkış ölçütü:** Windows'ta `TALVORA-KUR.bat` restore + build + test işlemlerini hatasız tamamlar ve `/health` cevap verir.

## Faz 1 — ChatGPT gerçek bağlantısı

- Talvora MCP endpoint'inin ChatGPT bağlantısında doğrulanması
- Gerçek `ReadFile`, `WriteFile`, `RunShell`, `ListProcesses`, `StartProcess`, `StopProcess` çağrıları
- Tool sonuçlarının hata sözleşmesi ve cancellation davranışının gerçek makinede doğrulanması

**Çıkış ölçütü:** ChatGPT sohbetinden Talvora'nın `package.json` benzeri bir test dosyasını okuyup yazması ve PowerShell komutu çalıştırması.

## Faz 2 — Elevated Broker

- [x] gRPC over Windows Named Pipes
- [x] Sürümlü IPC contract
- [x] Host ↔ Broker health/routing omurgası
- [ ] Windows Service kalıcı kurulum akışı
- [ ] Yönetici isteyen gerçek Windows işlemlerinin broker üzerinden yürütülmesi

**Çıkış ölçütü:** Normal Talvora Host process'i yükseltilmeden, bir yönetici işlemi Broker üzerinden başarıyla tamamlanır.

## Faz 3 — Çekirdeğin sağlamlaştırılması

- Process output streaming / uzun süren işlem handle'ları
- Yapılandırılmış log rotation
- Activity/metrics gözlemlenebilirliği
- Hata sınıflandırmasının genişletilmesi
- Windows gerçek-makine entegrasyon testleri

## Faz 4 — Gerektikçe Windows yetenek modülleri

Yalnız gerçek ihtiyaç çıktığında eklenir: Registry, Windows Services, Task Scheduler, Event Log, Network, ADB, screenshot ve UI Automation. Her biri bağımsız modül olur; Core veya MCP protokol iş mantığına gömülmez.
