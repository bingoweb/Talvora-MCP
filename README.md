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

## Structured filesystem mutation tools

- `talvora_create_directory`
- `talvora_copy`
- `talvora_move`

These tools provide structured create/copy/move behavior without replacing or restricting the primitive filesystem surface. `talvora_create_directory` creates missing parent directories and is idempotent when the requested directory already exists.

`talvora_copy` supports files and recursive directory trees. `overwrite=false` rejects an existing destination deterministically. With `overwrite=true`, file destinations are replaced and directory copies merge non-conflicting destination entries while replacing conflicting copied entries. Directory copies with `recursive=false` fail before creating a partial destination. Source reparse points are rejected before copy mutation so junctions/symlinks cannot create accidental traversal loops.

`talvora_move` supports files and directories. `overwrite=false` rejects a destination collision, while `overwrite=true` removes/replaces the destination and then performs the move. No path allowlist is applied to any of these tools.

## Environment variable tools

- `talvora_env_get`
- `talvora_env_list`
- `talvora_env_set`
- `talvora_env_delete`

These tools provide structured access to Windows environment variables at the Talvora process, current-user, and local-machine scopes. Process values live only for the Talvora process. User and Machine values use Windows' persistent environment-variable stores; because the installed service runs as LocalSystem, the User scope is the LocalSystem account's user environment.

`talvora_env_list` returns deterministic name-sorted data and can filter by variable name. `talvora_env_set` preserves an explicit empty-string value, while `talvora_env_delete` removes a value and is idempotent when it is already missing.

No environment-variable name allowlist or deny-list is applied. These tools do not replace or restrict `talvora_run_process` or `talvora_run_powershell`.

## Windows Event Log query tools

- `talvora_eventlog_list`
- `talvora_eventlog_query`

`talvora_eventlog_list` enumerates local Windows Event Log names, supports an optional case-insensitive name filter, and returns deterministic ordering.

`talvora_eventlog_query` accepts any local log name plus an XPath query, an explicit maximum event count, and newest-first/oldest-first direction. It returns structured event metadata including log/provider identity, event ID, record ID, timestamp, level, process/thread IDs, machine/user identity when available, and a best-effort formatted message. Provider message formatting failures do not discard the underlying event record.

No log-name, provider, or event-ID allowlist is applied. The event-count limit bounds one MCP response rather than reducing which Event Logs can be addressed. The unrestricted process and PowerShell primitives remain available for Event Log operations not modeled by this read-only query layer.

## Process inspection and control

- `talvora_process_list`
- `talvora_process_get`
- `talvora_process_kill`

These tools provide structured PID/name/session/start-time/working-set/executable metadata when Windows permits each field to be read. Inaccessible metadata on protected processes is returned as unavailable rather than causing the complete process list to fail.

`talvora_process_kill` can terminate a PID with or without its entire process tree, waits for exit with an explicit timeout, and distinguishes whether a kill was issued from whether exit was confirmed. Missing/exited PIDs are handled idempotently. No PID or process-name allowlist is applied.

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

Set the machine environment variable `TALVORA_KNOWLEDGE_ROOTS` to a semicolon-separated list of directories to replace the default search corpus. This setting changes only the read-only knowledge search surface; it does not restrict `talvora_read_text`, `talvora_write_text`, `talvora_delete`, `talvora_list`, `talvora_create_directory`, `talvora_copy`, `talvora_move`, or `talvora_run_process`.

As of 2026-09-18, ChatGPT Business supports custom MCP apps with full MCP capabilities. The read-only `search`/`fetch` pair remains useful for company-knowledge workflows, but it does not limit the other Talvora tools.

## ChatGPT Business via Secure MCP Tunnel

ChatGPT web cannot connect directly to Talvora's loopback-only `http://127.0.0.1:7676/mcp` endpoint. The supported private/local connection path is OpenAI Secure MCP Tunnel.

Talvora keeps that transport separate from the core runtime:

- The `Talvora` Windows Service remains the only Talvora service and still listens only on loopback.
- The core local MCP installation does not require an OpenAI Platform account or API key.
- `scripts/Configure-ChatGPT-Business.ps1` downloads the current official `openai/tunnel-client` Windows release, verifies it against the release `SHA256SUMS.txt`, and manages a long-lived tunnel runtime pointed at Talvora's loopback MCP.
- A restricted tunnel runtime credential is passed to `tunnel-client` by environment reference rather than as a literal command-line/config value. The stored reconnect copy is protected with the current Windows user's DPAPI.
- The runtime credential is transport authentication for Secure MCP Tunnel; Talvora does not use it for model inference or Responses API calls.
- No inbound public firewall rule or public Talvora listener is created.

First install/rebuild Talvora with `TALVORA-KUR.cmd`. Then run `TALVORA-BUSINESS-KUR.cmd` (or `scripts\Configure-ChatGPT-Business.ps1`) and provide the existing OpenAI tunnel ID plus a least-privilege runtime key with Tunnels Read + Use. The script does not report success until `tunnel-client runtimes status --json` reports `process_running=true`, `healthy=true`, and `ready=true`.

After a reboot, `TALVORA-BUSINESS-KUR.cmd reconnect` reuses the saved tunnel metadata and DPAPI-protected runtime credential. Talvora deliberately does not install a hidden second Windows service or scheduled task for tunnel startup.

The final one-time ChatGPT Business workspace step is performed by a Business Admin/Owner: enable Developer mode, create a custom MCP app, choose **Connection: Tunnel**, select/paste the tunnel ID, review the Talvora actions, and publish the app to the workspace.

## Rebuild from zero

Run `TALVORA-KUR.cmd` from an Administrator-capable Windows account. It launches the reset installer, removes prior Talvora runtime state, recreates the source checkout from `main`, builds a fresh self-contained Windows service, installs it, verifies `S-1-5-18`, runs the real MCP smoke suite, and configures the local `talvora_local` MCP registration.

The source of truth is the repository root and `main` is the canonical branch. There is no legacy `v2` product tree. Historical branch names may remain as Git refs for repository compatibility, but they are not independent product lines and are kept aligned with the canonical tree.
