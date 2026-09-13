# Talvora Core Implementation Plan

> **For agentic workers:** implement task-by-task with tests and fresh verification evidence.

**Goal:** ChatGPT'nin Windows üzerinde dosya, shell ve process işlemlerini çağırabileceği, ileride yeni yetenekler eklenirken yeniden mimari gerektirmeyen Talvora çekirdeğini kurmak.

**Architecture:** Modüler monolit; MCP dış adapter; Windows platform katmanı; ayrı elevated broker süreç sınırı.

**Tech Stack:** .NET SDK 10.0.401, .NET 10.0.12 runtime line, C# 14, MCP C# SDK 2.2.0, MSTest.Sdk 4.4.0.

**Spec:** `docs/superpowers/specs/2026-09-13-talvora-core-design.md`

## Global Constraints

- Windows 11 hedeflenir.
- Core/Abstractions MCP'ye bağımlı değildir.
- Yapay filesystem sandbox veya command deny-list yoktur.
- MCP stateless HTTP kullanır.
- Paket sürümleri merkezî yönetilir.
- Her tamamlanma iddiasından önce restore, build ve test çalıştırılır.

---

### Task 1: Foundation boundaries

- Abstractions/Core sözleşmelerini oluştur.
- Architecture tests ile MCP/Windows ters bağımlılığını engelle.

### Task 2: Initial capabilities

- FileSystem read/write/delete.
- PowerShell 7 ve CMD execution.
- Process list/start/stop.
- Windows platform/system info.

### Task 3: MCP adapter

- MCP C# SDK 2.2.0 stateless HTTP.
- Explicit `WithTools<T>()` registrations.
- Core services constructor DI ile araçlara bağlanır.

### Task 4: Host and broker boundary

- Console + Windows Service aware host.
- `/health` ve `/mcp` endpoints.
- ElevatedBroker ayrı process ve versioned IPC contract scaffold.

### Task 5: Windows verification

- Architecture script.
- `dotnet restore`.
- `dotnet build`.
- `dotnet test`.
- İlk Windows GREEN sonrasında gerçek Named Pipes/gRPC broker fazına geç.
