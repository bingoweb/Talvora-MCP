# Talvora

Talvora, **ChatGPT ile Windows arasında yerel sistem kontrol köprüsü** olarak geliştirilen bağımsız bir Windows projesidir. Mac'teki DevSpace kod tabanını kullanmaz ve resmî DevSpace projesinin fork'u değildir.

## Mevcut çekirdek

- .NET 10 LTS / C# 14
- MCP C# SDK 2.2.0, stateless HTTP
- FileSystem: metin dosyası oku / yaz / sil
- Shell: PowerShell 7 ve CMD
- Processes: listele / başlat / durdur
- Windows sistem bilgisi
- Windows Service + console/debug uyumlu host
- Elevated Broker için sürümlü gRPC protokolü
- Host ↔ Broker IPC: Windows Named Pipes + HTTP/2 gRPC
- Broker bağlantısı için ayrı `Talvora.Ipc.Client` adapter katmanı

Talvora yapay bir klasör sandbox'ı veya komut deny-list'i uygulamaz. Normal işlemler Windows hesabının gerçek yetkileriyle yürür. Yönetici işlemlerinin ana Host'u yükseltmeden çalıştırılması için ayrı Elevated Broker süreç sınırı kullanılır.

## Windows'ta çalışma

ZIP'i bir klasöre çıkar ve ilk kurulum/doğrulama için:

```text
TALVORA-KUR.bat
```

GREEN sonrasında Talvora'yı başlatmak için:

```text
TALVORA-BASLAT.bat
```

Tüm kalite kapılarını tekrar çalıştırmak için:

```text
TALVORA-TEST.bat
```

Elevated Broker'ı kalıcı Windows Service olarak kurmak için:

```text
TALVORA-BROKER-KUR.bat
```

Bu işlem bir kez UAC onayı ister, broker'ı `%ProgramFiles%\Talvora\Broker` altına yayınlar, `TalvoraElevatedBroker` servisini `LocalSystem` hesabında otomatik başlatır ve ardından normal kullanıcı Host → elevated service named-pipe bağlantısını doğrular. Servisi kaldırmak için `TALVORA-BROKER-KALDIR.bat` kullanılır.

`TALVORA-TEST.bat` şu zinciri doğrular:

1. architecture boundaries
2. NuGet restore
3. analyzer'lı build
4. unit tests
5. MCP 2026-07-28 smoke testi (`server/discover` → `tools/list` → `tools/call`)
6. Elevated Broker gRPC-over-Named-Pipes smoke testi

Varsayılan endpointler:

- Health: `http://127.0.0.1:7676/health`
- Broker health: `http://127.0.0.1:7676/health/broker`
- MCP: `http://127.0.0.1:7676/mcp`

Varsayılan yerel broker pipe adı: `Talvora.ElevatedBroker.v2`.

## Mimari

Ayrıntılar: `docs/architecture/Talvora-Core-v1.md`
