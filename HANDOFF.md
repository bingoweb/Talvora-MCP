# Talvora — Canonical Session Handoff

Updated: **2026-09-14**

Bu belge Talvora için yeni ChatGPT oturumunda kullanılacak **kanonik devam kaynağıdır**. Eski sohbet özetleri, eski alpha.18/v1 notları veya önceki HANDOFF içeriği bununla çelişirse **bu belgeyi esas al**.

## 1. Proje / çalışma hattı

- Repo: `bingoweb/Talvora-MCP`
- GitHub: `https://github.com/bingoweb/Talvora-MCP`
- Aktif geliştirme branch'i: `feat/elevated-operation-execution`
- Registry recovery öncesi son elevation head: `fde403be39cd04e7aa1e41d5ff0aee961eceb519`
- Handoff güncellemesinden hemen önceki çalışan kod head'i: `9e647b939d7e6fde89d03ab3725ae2b399be0c6b`
- `9e647...` parent: `fde403...`
- Commit: `test: restore registry baseline and RED subkey listing`

Elevation fazı daha önce PR #1 ile `main`e squash-merge edildi. Registry çalışması daha önce ayrı/diverge olmuş bir çizgide ilerlemişti; doğrulanmış Registry baseline'ı `fde403...` üzerine **fast-forward recovery** ile yeniden taşındı. Force-push/reset yapılmadı.

Bu HANDOFF güncellemesi kendi commit'ini oluşturacağı için yeni oturumda branch HEAD'i `9e647...` değerinden daha yeni olacaktır. Yeni oturum önce `HANDOFF.md`yi oku ve branch'in bu handoff commit'i veya onun descendant'ında olduğunu doğrula.

## 2. Çalışma kuralları — değiştirilemez

1. API/library kararlarında **Context7 + güncel resmî kaynakları** kullan. `latest/current` iddiası gerekiyorsa yeniden kontrol et.
2. Davranış değişikliklerinde **focused TDD**: tek RED → minimal GREEN → aynı exact focused test GREEN.
3. Aynı GitHub Actions koşusunu/logunu tekrar tekrar sorgulama. Kullanıcı bunun ciddi zaman kaybı yarattığını özellikle belirtti.
4. Geniş/full test suite'i her küçük değişiklikte çalıştırma; gerçek checkpoint/faz sonunda **bir kez** çalıştır.
5. Analyzer/warning kapatıp sorunu gizleme; kök nedeni düzelt.
6. Güvenlik/gizlilik bahanesiyle istenen yeteneği yapay allowlist/denylist/path/command kısıtlarıyla kırpma. Windows'un gerçek izin modeli neye izin veriyorsa Talvora bunu kullanabilmeli.
7. Main Host normal kullanıcı bağlamında kalır. Yönetici gereken işlemler kurulu LocalSystem Elevated Broker üzerinden yürür; her işlemde UAC istenmez.
8. Kullanıcıya rutin kararlar için sürekli onay sorma. Teknik olarak doğru yolu seçip ilerle.
9. PowerShell scriptleri Windows uyumluluğu için UTF-8 BOM kullanmalı.
10. Mevcut değerli branch durumunu reset/force-push/revert ile geri alma. Özellikle Registry recovery hattını koru.
11. Dokümantasyonu rutin her adımda güncelleme; yalnız gerçek oturum devri veya kalıcı mimari karar için güncelle.
12. Ticari/production kalite hedefi: deterministik davranış, hata modeli, bakım yapılabilirlik, performans, veri bütünlüğü ve güvenilir recovery.

## 3. Güncel mimari

### Temel projeler

- `Talvora.Abstractions`: `TalvoraError`, `TalvoraResult<T>`, `IOperationExecutor`
- `Talvora.Core`: transport-neutral orchestration primitives
- `Talvora.Modules.FileSystem`
- `Talvora.Modules.Shell`
- `Talvora.Modules.Processes`
- `Talvora.Modules.Registry`
- `Talvora.Platform.Windows`
- `Talvora.Application`: application/orchestration katmanı; neutral `IBrokerClient`; MCP/generated gRPC tiplerini bilmez
- `Talvora.Adapter.Mcp`
- `Talvora.Ipc.Contracts`
- `Talvora.Ipc.Client`
- `Talvora.ElevatedBroker`: Windows Service-capable LocalSystem broker
- `Talvora.Host`: normal-user Host / MCP / health

### Katman sınırları

- `Talvora.Application` doğrudan MCP, Host, ElevatedBroker veya `Talvora.Platform.Windows` referanslamamalı.
- `Talvora.Adapter.Mcp` doğrudan `Talvora.Ipc.Client`, `Talvora.Ipc.Contracts`, ElevatedBroker, Host veya Platform.Windows referanslamamalı.
- Windows bağımlılıkları platform/IPC katmanlarında tutulmalı.

## 4. Elevated execution — tamamlandı

### Broker protocol v2

- Proto package: `talvora.ipc.v2`
- `BrokerProtocol.CurrentVersion = 2`
- Default pipe: `Talvora.ElevatedBroker.v2`
- Transport: gRPC/HTTP2 over Windows Named Pipe
- Broker: LocalSystem Windows Service

`ProcessExecutionMode`:
- `WaitForExit`
- `StartOnly`

`StartOnly`:
- child process başlatır
- stdout/stderr redirect etmez
- PID'i hemen döndürür
- child'ı sonradan broker tarafından öldürmez

`WaitForExit`:
- stdout/stderr yakalar
- exit code döndürür
- timeout/cancellation destekler
- process tree termination destekler

### Privilege routing

`ExecutionPrivilege`:
- `Auto`
- `Normal`
- `Elevated`

Process `Auto`:
- önce local
- yalnız gerçek elevation/access-denied durumunda broker fallback
- native codes 5 / 740 ve normalize `access_denied` kapsanır

Shell `Auto`:
- önce local
- yalnız process başlangıcı elevation gerektiriyorsa broker fallback
- komutun non-zero exit code'u elevated olarak ikinci kez koşturulmaz; duplicate side effect yaratılmaz

MCP yüzeyi:
- `runAsAdministrator = false` → `Auto`
- `runAsAdministrator = true` → `Elevated`

### Broker transport robustness

- Named-pipe **connect phase** bounded timeout'a sahip.
- Bu timeout çalışan komutun execution süresine uygulanmaz.
- gRPC/IO transport hataları exception olarak dışarı sızmak yerine `TalvoraResult` içine normalize edilir.
- erişilemeyen broker → `broker_unavailable`

## 5. Son tam doğrulanmış elevation checkpoint

Commit: `fde403be39cd04e7aa1e41d5ff0aee961eceb519`

Bu committe final Windows gate:
- Host Release build: GREEN
- ElevatedBroker Release build: GREEN
- tüm o zamanki .NET test projeleri: **31/31 GREEN**

ÖNEMLİ: Bu full **31/31 gate Registry recovery sonrasında yeniden çalıştırılmış değildir**. Yeni oturum Registry checkpoint'ine geldiğinde full Windows gate'i bir kez yeniden çalıştırmalıdır.

## 6. Registry baseline — aktif branch'e recovery edildi

### `Talvora.Modules.Registry`

`IRegistryService` şu anda yalnız şunu taşır:

```csharp
ValueTask<RegistryValueData> ReadValueAsync(
    RegistryHiveId hive,
    string subKeyPath,
    string? valueName = null,
    RegistryViewId view = RegistryViewId.Default,
    bool expandEnvironmentStrings = false,
    CancellationToken cancellationToken = default);
```

Henüz `ListSubKeyNamesAsync` yok. Bu, mevcut expected RED'in sebebidir.

Domain enumları:

`RegistryHiveId`:
- `ClassesRoot`
- `CurrentUser`
- `LocalMachine`
- `Users`
- `CurrentConfig`

`RegistryViewId`:
- `Default`
- `Registry32`
- `Registry64`

`RegistryValueType`:
- `Unknown`
- `None`
- `Text`
- `ExpandableText`
- `Binary`
- `DWord`
- `MultiText`
- `QWord`

`String` benzeri adlar analyzer CA1720 nedeniyle kullanılmadı; analyzer bastırılmadı.

### `WindowsRegistryService`

Mevcut `ReadValueAsync`:
- cancellation kontrolü
- path validation
- `RegistryKey.OpenBaseKey(MapHive(...), MapView(...))`
- read-only `OpenSubKey`
- missing key → `KeyNotFoundException`
- `GetValueKind`
- default olarak `RegistryValueOptions.DoNotExpandEnvironmentNames`
- caller isterse expandable string expansion
- String / ExpandString / Binary / DWord / MultiString / QWord / None → domain model mapping

### DI

`WindowsRegistryServiceCollectionExtensions.AddWindowsRegistry(IServiceCollection)`:
- `IRegistryService → WindowsRegistryService`
- singleton

Host `Program.cs` içinde `AddWindowsRegistry()` çağrılır.

### MCP

`Talvora.Adapter.Mcp/RegistryTools.cs` içinde:
- `ReadRegistryValue`
- `IRegistryService + IOperationExecutor`
- operation name: `registry.read`

### Hata modeli

`DefaultErrorMapper`:
- `KeyNotFoundException` → `not_found`

## 7. Registry API kararları — güncel kaynaklarla doğrulandı

Context7 + güncel .NET dokümantasyonu ile doğrulanan kararlar:

- `RegistryKey.OpenBaseKey(hive, view)` ile Default / Registry32 / Registry64 görünümü açıkça seçilir.
- `RegistryValueOptions.DoNotExpandEnvironmentNames`, `REG_EXPAND_SZ` için ham `%VAR%` içeriğini korur.
- Talvora default olarak ham expandable string döndürür; expansion opt-in'dir.
- `GetValueKind` String / ExpandString / Binary / DWord / MultiString / QWord / None ayrımını sağlar.
- unnamed `(Default)` value için `null` veya empty value name kullanılabilir.
- missing `OpenSubKey` → `null`.
- `GetSubKeyNames()` tüm alt anahtar isimlerini verir; sıralama garantisine güvenilmez.
- Talvora deterministik output için `StringComparer.Ordinal` sıralaması uygular.
- Güncel MCP C# SDK kalıbı: `[McpServerToolType]`, `[McpServerTool]`, `Description`, assembly discovery.
- 2026-09-14 kontrolünde `ModelContextProtocol.AspNetCore 2.2.0` current stable idi. Yeni oturum `latest` diyecekse tekrar kontrol et.
- DI kalıbı: `Add{Feature}` extension + `AddSingleton<TService,TImplementation>()`.

## 8. Registry doğrulama geçmişi ve önemli dürüstlük notu

Registry'nin önceki diverged geliştirme çizgisinde focused GREEN görülen davranışlar:
- ilk `REG_EXPAND_SZ` read: 1/1 GREEN, build 0 warning / 0 error
- MCP `ReadRegistryValue` routing: GREEN
- `AddWindowsRegistry` singleton DI: GREEN
- Host Release composition build: GREEN
- missing Registry key → MCP `not_found`: GREEN

Bu kodlar aktif branch'e recovery edildi.

**Ancak recovery sonrasında yukarıdaki bütün Registry testleri current HEAD üzerinde yeniden topluca/fresh koşulmadı.** Eski GREEN sonuçlarını current HEAD full validation olarak sunma.

## 9. CURRENT EXACT RED — buradan devam et

Focused test:

`Talvora.Registry.Tests.RegistryListSubKeysTests.ListSubKeyNamesReturnsOrdinalSortedNames`

GitHub Actions:
- run: `34807709810`
- job: `103862721902`
- status: FAILURE
- checkout/setup/restore: GREEN
- exact Registry list-subkeys target: FAILED

Bu failure **beklenen RED**. Current source'ta `IRegistryService.ListSubKeyNamesAsync` yok.

Test davranışı:
- geçici `HKCU\Software\Talvora\Tests\ListSubKeys\<guid>` oluşturur
- alt anahtarları insertion order ile oluşturur: `Zulu`, `alpha`, `Beta`
- reflection ile şu interface metodunu bekler:

```csharp
ListSubKeyNamesAsync(
    RegistryHiveId hive,
    string subKeyPath,
    RegistryViewId view,
    CancellationToken cancellationToken)
```

Expected return:

```csharp
ValueTask<IReadOnlyList<string>>
```

Expected deterministic output:

```text
Beta
Zulu
alpha
```

Sıralama: `StringComparer.Ordinal`.

Test cleanup `finally` içinde `DeleteSubKeyTree(..., throwOnMissingSubKey: false)` ile yapılır.

## 10. EXACT NEXT GREEN

Yalnız iki production dosyasını değiştir.

### `IRegistryService`

Ekle:

```csharp
ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
    RegistryHiveId hive,
    string subKeyPath,
    RegistryViewId view = RegistryViewId.Default,
    CancellationToken cancellationToken = default);
```

### `WindowsRegistryService`

Ekle:

```csharp
public ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
    RegistryHiveId hive,
    string subKeyPath,
    RegistryViewId view = RegistryViewId.Default,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);

    using var baseKey = RegistryKey.OpenBaseKey(MapHive(hive), MapView(view));
    using var key = baseKey.OpenSubKey(subKeyPath, writable: false)
        ?? throw new KeyNotFoundException($"Registry key was not found: {hive}\\{subKeyPath}");

    var names = key.GetSubKeyNames();
    Array.Sort(names, StringComparer.Ordinal);
    return ValueTask.FromResult<IReadOnlyList<string>>(names);
}
```

Ardından **yalnız aynı exact focused test**i çalıştır:

`Talvora.Registry.Tests.RegistryListSubKeysTests.ListSubKeyNamesReturnsOrdinalSortedNames`

Bu loop içinde MCP list tool ekleme. Full suite çalıştırma.

## 11. List-subkeys GREEN sonrasında

Yeni ve ayrı TDD loop:
1. MCP `ListRegistrySubKeys` için tek RED.
2. Minimal `RegistryTools.ListRegistrySubKeys`.
3. `IOperationExecutor` üzerinden çalıştır.
4. Operation name için testte açık sözleşme kur; uygun isim `registry.list_subkeys` olabilir.
5. Aynı focused test GREEN.

Sonraki Registry yeteneklerini de ayrı davranışlar halinde ekle:
- value-name listing
- value write/update
- value delete
- key create/delete
- gerektiğinde HKLM/elevated write path

Yönetici gereken Registry yazmaları ileride `Application → broker` mimarisi üzerinden route edilmeli; ad-hoc UAC veya ayrı gizli execution yolu oluşturma.

## 12. CI stratejisi

Current workflow Registry list-subkeys TDD için bilinçli olarak focused durumda:
- Setup .NET
- Registry target restore
- yalnız exact list-subkeys RED test

Focused döngü sırasında bunu geniş suite'e çevirme.

Registry anlamlı checkpoint'e ulaştığında workflow'u yeniden final Windows gate'e getir ve **bir kez**:
- Host Release build
- ElevatedBroker Release build
- tüm .NET test projeleri
- architecture tests

çalıştır.

Registry recovery sonrasında bu full gate yapılana kadar “full project GREEN” deme.

Daha önce preinstalled .NET SDK ile CI hızlandırma denemesi yapılmıştı; live branch'teki current workflow tekrar Setup .NET/global-json yolunu kullanıyor. Yeniden doğrulamadan bu optimizasyon aktifmiş gibi davranma.

---

## EXACT ONE-LINE RESUME POINT

**On active branch `feat/elevated-operation-execution`, the Registry baseline has been fast-forward recovered onto the final elevation head; the current expected RED is `RegistryListSubKeysTests.ListSubKeyNamesReturnsOrdinalSortedNames` because `IRegistryService`/`WindowsRegistryService` lack `ListSubKeyNamesAsync`; next action is the minimal interface + Windows implementation with deterministic `StringComparer.Ordinal` sorting, then run only that one focused test.**
