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

## Developer core tools

Talvora exposes a first-class application-development toolkit in addition to the unrestricted process and PowerShell primitives:

- `talvora_path_info`
- `talvora_file_hash`
- `talvora_find_files`
- `talvora_search_text`
- `talvora_read_bytes`
- `talvora_write_bytes`
- `talvora_replace_text`
- `talvora_http_request`
- `talvora_tcp_connections`
- `talvora_tcp_listeners`
- `talvora_wait_tcp`
- `talvora_project_discover`
- `talvora_resolve_command`

These tools are designed for day-to-day application development: source discovery, literal/regex search, exact text patching, binary asset access, file hashing, local/remote API testing, port ownership diagnostics, readiness checks, project-manifest discovery, and executable resolution. Filesystem tools operate on any path accessible to the LocalSystem service and do not introduce a path allowlist. Search/result limits are response controls and can be set to `0` for unlimited operation where supported. The HTTP tool accepts arbitrary methods and destinations; the process and PowerShell primitives remain available for anything not modeled by this structured layer.

## Long-running development jobs

Talvora can keep development processes alive without blocking one MCP request:

- `talvora_job_start`
- `talvora_job_get`
- `talvora_job_list`
- `talvora_job_read_output`
- `talvora_job_write_stdin`
- `talvora_job_stop`
- `talvora_job_delete`

A job may launch any executable with arbitrary arguments, working directory, and environment overrides under the LocalSystem service. stdout/stderr are persisted under `%ProgramData%\Talvora\Jobs\<jobId>` and can be tailed incrementally. Job metadata keeps PID/start-time identity so running processes remain discoverable across a Talvora service restart; stdin remains available while the originating service instance owns the redirected pipe. Stopping a job can terminate the complete process tree. `talvora_job_delete` removes persisted metadata/stdout/stderr and can optionally stop a still-running job before cleanup. These tools do not replace or restrict the unrestricted process/Powershell controls.

## Structured Git tools

Talvora also exposes repository-aware Git operations:

- `talvora_git_info`
- `talvora_git_status`
- `talvora_git_diff`
- `talvora_git_log`
- `talvora_git_branches`
- `talvora_git_run`

The read-only tools use Git's machine-oriented output where appropriate and return structured repository, branch, history, status, and diff data. `talvora_git_run` accepts arbitrary Git arguments plus environment overrides in any accessible working directory; no subcommand, ref, remote, path, or option allowlist/denylist is applied. This preserves full Git functionality for add/commit/fetch/push/rebase/worktree/submodule and other workflows without requiring Talvora to pre-model every Git command.

## Config, text, archive, and download tools

Talvora includes structured helpers for common development assets and configuration:

- `talvora_read_text_range`
- `talvora_tail_text`
- `talvora_append_text`
- `talvora_json_get`
- `talvora_json_set`
- `talvora_json_delete`
- `talvora_archive_list`
- `talvora_archive_create`
- `talvora_archive_extract`
- `talvora_http_download`

Text range/tail operations avoid loading large logs or source files into one MCP response, while `lineCount=0` preserves an explicit unbounded mode. JSON tools use RFC 6901 JSON Pointer syntax and can create, update, query, or delete arbitrary configuration nodes. ZIP tools expose complete archive listing, directory creation, and extraction; extraction is destination-contained by default but `allowOutsideDestination=true` preserves full filesystem semantics when intentionally required. `talvora_http_download` streams HTTP response bodies directly to any accessible path, supports custom headers, redirects, TLS override, overwrite, and byte-range resume, and returns a SHA-256 of the downloaded file.

## Filesystem change watchers

Talvora exposes live filesystem monitoring for development workflows:

- `talvora_watch_start`
- `talvora_watch_list`
- `talvora_watch_read`
- `talvora_watch_wait`
- `talvora_watch_stop`

Watchers use .NET `FileSystemWatcher` directly against any directory accessible to the LocalSystem service. Recursive monitoring, wildcard filters, `NotifyFilters`, internal buffer size, and queued-event limits are caller-controlled. A queue limit of `0` means unlimited Talvora-side event retention. Watchers are intentionally live service-instance resources rather than persistent configuration; a Talvora service restart clears active watchers.

## Chocolatey developer environment tools

Chocolatey remains Talvora's supported Windows package manager. The MCP surface now includes:

- `talvora_choco_info`
- `talvora_choco_list`
- `talvora_choco_search`
- `talvora_choco_install`
- `talvora_choco_upgrade`
- `talvora_choco_uninstall`
- `talvora_choco_run`

List/search use Chocolatey's `--limit-output` format for structured package name/version results. Install/upgrade/uninstall expose common automation options while `talvora_choco_run` accepts an arbitrary Chocolatey argument vector, working directory, and environment overrides. No package, source, subcommand, or Chocolatey-option allowlist/denylist is added. WinGet is still not used by Talvora setup or package-management tooling.

## Filesystem watch tools

Talvora can monitor any accessible directory in real time:

- `talvora_watch_start`
- `talvora_watch_get`
- `talvora_watch_list`
- `talvora_watch_read`
- `talvora_watch_wait`
- `talvora_watch_stop`

The watcher layer uses .NET `FileSystemWatcher` with Created/Changed/Deleted/Renamed/Error events, wildcard filters, optional recursive monitoring, selectable `NotifyFilters`, configurable native buffer size, and bounded or unlimited queued events. Watch IDs are in-memory handles owned by the current Talvora service instance. No watched-path allowlist is applied.

## Chocolatey developer tools

Chocolatey remains Talvora's package manager for Windows development tooling:

- `talvora_choco_info`
- `talvora_choco_list`
- `talvora_choco_search`
- `talvora_choco_install`
- `talvora_choco_upgrade`
- `talvora_choco_uninstall`
- `talvora_choco_run`

List/search use Chocolatey's `--limit-output` form so package name/version rows can be returned as structured MCP data. Install/upgrade/uninstall expose package, version, source, package parameters, native install arguments, prerelease/force/noninteractive controls, plus arbitrary extra arguments. `talvora_choco_run` accepts an unrestricted Chocolatey argument vector and environment overrides, preserving the complete Chocolatey CLI surface. No package, source, subcommand, or option allowlist/denylist is applied. Talvora does not use WinGet.

## .NET and Node build runners

Talvora exposes structured build/runtime helpers for common application stacks:

- `talvora_dotnet_info`
- `talvora_dotnet_restore`
- `talvora_dotnet_build`
- `talvora_dotnet_test`
- `talvora_dotnet_publish`
- `talvora_dotnet_run`
- `talvora_node_info`
- `talvora_npm_install`
- `talvora_npm_ci`
- `talvora_npm_run_script`
- `talvora_npm_run`

The .NET helpers wrap the installed `dotnet` CLI and expose the common restore/build/test/publish controls while keeping `talvora_dotnet_run` as an unrestricted argument-vector surface. The Node helper reports whether Node/npm are installed; npm operations expose install/ci/script ergonomics while `talvora_npm_run` keeps the complete npm CLI surface available. These are convenience APIs, not capability boundaries: unrestricted process/job execution remains available for custom toolchains.

Node.js itself is not installed by Talvora automatically. When needed on Windows, install/upgrade it through the Chocolatey tools rather than WinGet.

## Interactive Windows sessions

Talvora runs as LocalSystem but can intentionally launch application-development processes in a logged-on user's desktop session:

- `talvora_session_list`
- `talvora_session_get`
- `talvora_user_process_start`

Session discovery uses Windows Terminal Services APIs and exposes local console/RDP session ID, station, state, user/domain, and client metadata. `talvora_user_process_start` obtains the selected session's user token, builds that user's environment block, and calls `CreateProcessAsUserW` on `winsta0\default`. Callers can choose any session, executable, arguments, working directory, environment overrides, visibility, and console mode; no executable/path/user/session allowlist is added.

This is the preferred bridge for GUI tools, browser/dev-server helpers, user-profile package managers, and anything that must run as the signed-in developer rather than as LocalSystem.

## Python and Docker runtime tools

Talvora exposes Python/venv/pip and Docker/Compose helpers for application development:

- `talvora_python_info`
- `talvora_python_run`
- `talvora_python_venv_create`
- `talvora_pip_install`
- `talvora_pip_run`
- `talvora_docker_info`
- `talvora_docker_ps`
- `talvora_docker_images`
- `talvora_docker_logs`
- `talvora_docker_exec`
- `talvora_docker_run`
- `talvora_docker_compose_run`

Python tools resolve the machine interpreter or an explicitly supplied Python/py launcher, report virtual-environment and pip state, create venvs, and preserve the complete Python/pip argument surface through `python_run` and `pip_run`. Docker discovery reports missing CLI/engine/Compose structurally; Docker commands remain available without container/image/path/subcommand allowlists through `docker_run` and `docker_compose_run`.

On Windows, installing Python/Docker prerequisites remains a machine-software task and should use Talvora's Chocolatey layer rather than WinGet.

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