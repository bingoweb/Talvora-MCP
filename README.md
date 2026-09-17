# Talvora

Talvora is a Windows-local MCP server built for one owner machine with a full-capability philosophy.

## Canonical architecture

- Windows 10/11 runtime and development target.
- One `Talvora` Windows Service running as `LocalSystem`.
- MCP binds only to `http://127.0.0.1:7676/mcp`.
- No artificial command or path allowlist.
- Chocolatey is the only package manager used by Talvora setup. WinGet is forbidden.
- No OpenAI API account, API key, public ingress, or tunnel is required by the local runtime.
- Old Talvora services, scheduled tasks, runtime folders, and source checkout are intentionally removed by the reset installer.

## Core tools

- `talvora_system_info`
- `talvora_read_text`
- `talvora_write_text`
- `talvora_delete`
- `talvora_list`
- `talvora_run_process`

Because the MCP host itself runs as LocalSystem, these tools execute with the service account's Windows privileges.

## Rebuild from zero

Run `TALVORA-KUR.cmd` from an Administrator-capable Windows account. It launches the reset installer, removes prior Talvora runtime state, recreates the source checkout from `main`, builds a fresh self-contained Windows service, installs it, verifies `S-1-5-18`, and configures the local `talvora_local` MCP registration.

The source of truth is the repository root. There is no legacy `v2` product tree.
