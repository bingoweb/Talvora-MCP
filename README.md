# Talvora

Talvora is a Windows-local MCP server built for one owner machine with a full-capability philosophy.

## Canonical architecture

- Windows 10/11 runtime and development target.
- One `Talvora` Windows Service running as `LocalSystem`.
- MCP binds only to `http://127.0.0.1:7676/mcp`.
- No artificial command or path allowlist is applied to the full-capability primitive tools.
- Chocolatey is the only package manager used by Talvora setup. WinGet is forbidden.
- No OpenAI API account, API key, public ingress, or tunnel is required by the local runtime.
- Old Talvora services, scheduled tasks, runtime folders, and source checkout are intentionally removed by the reset installer.

## Full-capability primitive tools

- `talvora_system_info`
- `talvora_read_text`
- `talvora_write_text`
- `talvora_delete`
- `talvora_list`
- `talvora_run_process`

Because the MCP host itself runs as LocalSystem, these tools execute with the service account's Windows privileges. The file and process primitives are not restricted to the knowledge-search corpus.

## PowerShell execution

- `talvora_run_powershell`

This tool runs arbitrary multiline PowerShell scripts under the same LocalSystem capability boundary. Scripts are encoded as UTF-16LE Base64 and passed with PowerShell's `-EncodedCommand` option, avoiding nested quoting loss. It runs with `-NoProfile -NonInteractive -ExecutionPolicy Bypass`, captures stdout/stderr, preserves the script exit code, supports a working directory and timeout, and kills the process tree on timeout.

The default `engine = "auto"` uses `pwsh.exe` when PowerShell 7 is already available and otherwise falls back to built-in Windows PowerShell. PowerShell 7 is not a Talvora runtime dependency. No command or script deny-list is applied.

## Windows registry tools

Talvora exposes dedicated local registry tools so agents do not need to compose fragile `reg.exe` or PowerShell quoting for routine registry work:

- `talvora_registry_create_key`
- `talvora_registry_get`
- `talvora_registry_set`
- `talvora_registry_list`
- `talvora_registry_delete_value`
- `talvora_registry_delete_key`

The registry tools support HKLM, HKCU, HKCR, HKU, HKCC, and HKPD together with default, 32-bit, and 64-bit registry views. String, ExpandString, MultiString, DWORD, QWORD, Binary, and None values are supported. Reads preserve ExpandString data without environment expansion, listings are deterministic, and missing keys/values are handled idempotently where appropriate.

No registry hive or path allowlist is applied. These dedicated tools improve structured MCP ergonomics; they do not replace or reduce the unrestricted `talvora_run_process` capability.

## Windows service tools

Talvora exposes structured Service Control Manager operations:

- `talvora_service_list`
- `talvora_service_get`
- `talvora_service_start`
- `talvora_service_stop`
- `talvora_service_restart`

Service listing can be filtered by service/display name and can optionally include driver services. Get returns a structured not-found result instead of relying on localized shell output. Start/stop/restart are state-aware, wait for the requested final state, support explicit timeouts, and preserve idempotent behavior when a service is already running or stopped.

No service-name allowlist is applied. Because the MCP host runs as LocalSystem, these controls use the service account's Service Control Manager privileges. Stopping or restarting the Talvora service itself is intentionally not blocked; doing so can terminate the active MCP connection.

## Business knowledge tools

Talvora also exposes two read-only MCP tools that follow the `search` -> `fetch` retrieval pattern used by ChatGPT custom MCP apps and company knowledge workflows:

- `search(query)` returns structured `{ results: [{ id, title, text, url }] }` data.
- `fetch(id)` returns structured `{ id, title, text, url, metadata }` data for a selected document.

The default search corpus is intentionally optimized for useful local knowledge rather than a blind whole-disk crawl: Public Documents plus each local Windows profile's Documents, Desktop, Downloads, and OneDrive folders, together with `%ProgramData%\Talvora`. Search is time-bounded and can return partial results instead of blocking an MCP request on a very large corpus.

Set the machine environment variable `TALVORA_KNOWLEDGE_ROOTS` to a semicolon-separated list of directories to replace the default search corpus. This setting changes only the read-only knowledge search surface; it does not restrict `talvora_read_text`, `talvora_write_text`, `talvora_delete`, `talvora_list`, or `talvora_run_process`.

As of 2026-09-17, ChatGPT Business supports custom MCP apps with full MCP capabilities, while company knowledge includes custom apps that provide search/fetch access. ChatGPT web does not connect directly to a localhost MCP endpoint, so web publication/remote connectivity remains a separate deployment concern and is not required by Talvora's local runtime.

## Rebuild from zero

Run `TALVORA-KUR.cmd` from an Administrator-capable Windows account. It launches the reset installer, removes prior Talvora runtime state, recreates the source checkout from `main`, builds a fresh self-contained Windows service, installs it, verifies `S-1-5-18`, runs the real MCP smoke suite, and configures the local `talvora_local` MCP registration.

The source of truth is the repository root and `main` is the canonical branch. There is no legacy `v2` product tree. Historical branch names may remain as Git refs for repository compatibility, but they are not independent product lines and are kept aligned with the canonical tree.
