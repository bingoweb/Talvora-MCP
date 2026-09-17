# Talvora architecture

Talvora intentionally starts from a clean design.

## Runtime

`Talvora.exe` is a self-contained ASP.NET Core application installed as the Windows service `Talvora` under `LocalSystem`. It listens only on loopback port 7676 and maps MCP at `/mcp` using the official C# MCP SDK's stateless HTTP transport.

There is no user-session gateway, named-pipe broker, compatibility shim, legacy maintenance layer, or secondary privileged process.

## Capability model

The service account is the capability boundary. Talvora does not implement command deny-lists or filesystem allow-lists. The six primitive tools expose system information, filesystem read/write/delete/list, and arbitrary executable execution. Higher-level Windows capabilities are composed from these primitives until a dedicated tool materially improves reliability or ergonomics.

## Installation

The reset installer deletes prior Talvora Windows services, scheduled tasks, known Talvora runtime processes, `%LOCALAPPDATA%\Talvora`, `%PROGRAMDATA%\Talvora`, and the previous source checkout. It then clones a clean `main`, publishes a self-contained `win-x64` service, installs it as LocalSystem, verifies health and SID `S-1-5-18`, and writes the `talvora_local` client registration.

Chocolatey is the only package manager used. WinGet is not used.
