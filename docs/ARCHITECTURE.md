# Talvora architecture

Talvora intentionally starts from a clean design.

## Runtime

`Talvora.exe` is a self-contained ASP.NET Core application installed as the Windows service `Talvora` under `LocalSystem`. It listens only on loopback port 7676 and maps MCP at `/mcp` using the official C# MCP SDK's stateless HTTP transport.

There is no user-session gateway, named-pipe broker, compatibility shim, legacy maintenance layer, or secondary privileged process.

## Capability model

The service account is the capability boundary. Talvora does not implement command deny-lists or filesystem allow-lists for its primitive capability surface. Six primitive tools expose system information, filesystem read/write/delete/list, and arbitrary executable execution. Higher-level Windows capabilities are composed from these primitives until a dedicated tool materially improves reliability or ergonomics.

The structured filesystem mutation layer adds create-directory, copy, and move operations without reducing the primitive filesystem surface. It applies no path allowlist. Directory copy is recursive by default, rejects non-recursive partial copies, uses deterministic overwrite behavior, and rejects source reparse points before mutation so traversal cannot loop through junctions/symlinks.

The developer-core layer adds structured source/file discovery, literal or regex text search, raw binary read/write, exact text replacement, hashing, arbitrary HTTP requests, TCP diagnostics/readiness checks, project-manifest discovery, and command resolution. These tools are ergonomic additions for application development; they do not replace or narrow the unrestricted process, PowerShell, filesystem, registry, service, or environment-variable capabilities. Where a response can become large, callers may set the corresponding result/byte limit to `0` for unlimited operation.

The long-running job layer starts unrestricted child processes with redirected stdin/stdout/stderr, persists logs and metadata under ProgramData, and allows later inspection, incremental log reads, stdin writes, and process-tree termination. It adds developer-server/watch/test ergonomics without narrowing the unrestricted process primitives.

The Git layer exposes structured repository identity, porcelain status, diffs, logs, and branch refs plus an unrestricted `talvora_git_run` argument surface. No repository, ref, remote, path, Git subcommand, or option allowlist is introduced.

The config/asset layer adds line-ranged and tail text reads, append semantics, mutable JSON Pointer operations, ZIP listing/creation/extraction, and streaming HTTP downloads to disk. These operations retain Talvora's unrestricted path capability; response-oriented line bounds can be disabled explicitly, and archive extraction exposes an opt-in outside-destination mode when exact archive path semantics are required.

The environment-variable layer exposes structured process/user/machine get/list/set/delete behavior directly through System.Environment. It applies no variable-name allowlist and does not reduce the unrestricted process or PowerShell primitives.

The Event Log layer exposes structured local-log discovery and XPath queries through Windows Eventing APIs. It applies no log/provider/event-ID allowlist. Per-call event limits bound MCP response size only; unrestricted process and PowerShell primitives remain available for Event Log operations outside this read-only layer.

The registry layer is the first dedicated Windows capability built on that rule. It exposes structured create/get/set/list/delete operations directly through Microsoft.Win32 instead of requiring an agent to compose shell commands. It does not introduce a registry hive/path allowlist and does not reduce the unrestricted process primitive.

The process-control layer exposes structured process discovery and PID-based termination while preserving `talvora_run_process` as the unrestricted process-creation primitive. It does not add a PID/name allowlist.

The PowerShell layer applies the same principle to multiline automation. `talvora_run_powershell` is an ergonomic wrapper around a real PowerShell child process; it does not introduce a command deny-list and does not replace `talvora_run_process`.

The Windows service layer exposes structured Service Control Manager inspection and control without a service-name allowlist. It uses `System.ServiceProcess.ServiceController` for list/get/start/stop/restart while keeping `talvora_run_process` available for service creation, deletion, configuration, custom control codes, and any operation not modeled by the dedicated layer.

The read-only knowledge layer is separate from that capability boundary. `search` and `fetch` provide structured document discovery/retrieval for ChatGPT knowledge workflows without reducing the privileges or addressable paths of the primitive tools.

## Filesystem mutation tools

The structured filesystem suite exposes `talvora_create_directory`, `talvora_copy`, and `talvora_move`.

`talvora_create_directory` normalizes the requested path, creates missing parent directories, and reports whether the operation changed filesystem state. Repeating the call for an existing directory is idempotent.

`talvora_copy` supports file and directory sources. Files honor `overwrite`; directories require `recursive=true`. With `overwrite=false`, an existing destination is rejected before mutation. With `overwrite=true`, conflicting copied entries are replaced while non-conflicting entries already present in a destination directory are preserved. Before any directory-copy destination is created, Talvora walks the source tree and rejects reparse points. This deliberately prevents accidental recursion through junctions or symbolic links rather than silently following them.

`talvora_move` supports files and directories. Destination collisions fail deterministically unless `overwrite=true`, in which case the destination entry is removed/replaced before the source is moved. Same-path and directory-into-descendant moves are rejected as invalid filesystem operations, not as capability restrictions.

These tools use the same LocalSystem filesystem authority as the primitive read/write/delete/list tools. They do not introduce a path allowlist or deny-list.

## Developer core

The developer-core suite exposes `talvora_path_info`, `talvora_file_hash`, `talvora_find_files`, `talvora_search_text`, `talvora_read_bytes`, `talvora_write_bytes`, `talvora_replace_text`, `talvora_http_request`, `talvora_tcp_connections`, `talvora_tcp_listeners`, `talvora_wait_tcp`, `talvora_project_discover`, and `talvora_resolve_command`.

File discovery walks the requested tree directly and can optionally follow reparse points. Text search supports literal or .NET regular-expression matching plus include/exclude wildcards. Binary tools expose byte ranges and writes as Base64 so images, archives, compiled assets, and other non-text files can be handled without shell encoding workarounds. Exact text replacement supports literal or regex patches, expected-match assertions, first/all replacement modes, and optional backup creation.

The HTTP tool is an arbitrary `HttpClient.SendAsync` surface supporting custom methods, headers, text/Base64 bodies, redirect control, optional TLS validation bypass, and text/Base64/no-body response modes. TCP inspection returns `netstat -ano` data as structured local/remote endpoint, connection state, PID, and process-name records; readiness polling uses direct `TcpClient` connections.

Project discovery recognizes common .NET, Node, Python, Rust, Go, Maven, Gradle, CMake, Docker, and Git markers. Command resolution follows the service process PATH/PATHEXT environment. None of these tools add command, path, host, port, project-type, or executable allowlists.

## Long-running development jobs

The job suite exposes `talvora_job_start`, `talvora_job_get`, `talvora_job_list`, `talvora_job_read_output`, `talvora_job_write_stdin`, and `talvora_job_stop`.

Start uses `ProcessStartInfo.ArgumentList` with shell execution disabled and supports arbitrary child environment overrides. It redirects all three standard streams. stdout/stderr are pumped continuously into UTF-8 log files under `%ProgramData%\Talvora\Jobs\<jobId>`; callers tail them by byte offset without waiting for the child to exit. Metadata records the original PID and UTC start time so Talvora can distinguish a restarted process ID from PID reuse after the service itself restarts.

The live service instance owns the redirected stdin pipe, so `talvora_job_write_stdin` works while that same Talvora process remains attached. Job get/list still recover persisted process state after a service restart. Stop defaults to complete process-tree termination. Arbitrary PID control remains available through `talvora_process_kill`.

## Git

The Git suite exposes `talvora_git_info`, `talvora_git_status`, `talvora_git_diff`, `talvora_git_log`, `talvora_git_branches`, and `talvora_git_run`.

Info resolves repository root, Git directory, HEAD, branch/detached state, dirty state, and remotes. Status uses porcelain v2 branch output. Branch enumeration uses `for-each-ref` with field separators. Log uses an explicit field-separated format. Diff supports staging, arbitrary revision ranges, context sizes, and path filters.

`talvora_git_run` launches the installed Git executable with exactly the argument vector supplied by the caller, optional environment overrides, and an explicit timeout. It intentionally provides the complete Git command surface rather than a command allowlist. Dedicated read-only tools are convenience APIs, not capability boundaries.

## Config and asset workflows

The config/asset suite exposes `talvora_read_text_range`, `talvora_tail_text`, `talvora_append_text`, `talvora_json_get`, `talvora_json_set`, `talvora_json_delete`, `talvora_archive_list`, `talvora_archive_create`, `talvora_archive_extract`, and `talvora_http_download`.

Range reads use one-based line positions and support `lineCount=0` for the remainder of the file. Tail reads maintain only the requested trailing lines in memory unless `lineCount=0` requests the complete file. Append writes UTF-8 without adding a BOM and can optionally add a platform newline.

JSON operations are based on the mutable `System.Text.Json.Nodes` DOM and RFC 6901 JSON Pointer paths. Set can create missing object/array containers and supports the array `-` append token. Delete is idempotent for missing targets. No JSON property or file-path allowlist is applied.

ZIP creation stages through a temporary archive before replacing the requested destination, so the output path may intentionally reside inside the source directory without recursively archiving itself. Extraction normally resolves entries under the requested destination to prevent accidental traversal; `allowOutsideDestination=true` explicitly enables entries whose normalized path resolves elsewhere, matching Talvora's unrestricted filesystem authority.

HTTP download uses response-header streaming rather than buffering an MCP body. Resume requests use a Range header and append only when the server answers with HTTP 206; a normal 200 response restarts the destination file. A final SHA-256 is returned after the stream is persisted.

## Environment variables

The environment suite exposes `talvora_env_get`, `talvora_env_list`, `talvora_env_set`, and `talvora_env_delete`.

Targets are explicit and case-insensitive: Process, User, or Machine. Process values belong only to the running Talvora service process. On Windows, User and Machine values use the operating system's persistent environment-variable stores. Because the installed Talvora service runs as LocalSystem, the User target refers to the LocalSystem account's user environment rather than an interactive desktop user's environment.

Get returns a structured found/not-found result. List returns deterministic name-sorted entries and supports an optional case-insensitive name query. Set preserves an explicit empty-string value. Delete uses the platform removal semantics and is idempotent for a missing variable.

No environment-variable name allowlist or deny-list is applied. Machine-scope writes therefore use Talvora's LocalSystem authority, and callers can modify any machine environment variable Windows permits that account to change.

## Windows Event Log queries

The Event Log suite exposes `talvora_eventlog_list` and `talvora_eventlog_query` using `System.Diagnostics.Eventing.Reader`.

List enumerates local log names through the Windows Event Log session, applies an optional case-insensitive name filter, and returns deterministic ordering.

Query accepts any local log name and XPath expression. `newestFirst=true` uses reverse-direction Event Log reading; `maxEvents` bounds one response so a broad XPath cannot create an unbounded MCP payload. Returned records include the log/provider identity, event ID, record ID, timestamp, level, process/thread IDs, machine/user identity when available, and a best-effort formatted message. If Windows cannot resolve provider message metadata, Talvora keeps the record and returns a null message rather than dropping the event.

No log-name, provider, or event-ID allowlist or deny-list is applied. The dedicated tools are intentionally read-only ergonomics; callers retain the unrestricted process and PowerShell primitives for clearing logs, exporting logs, provider/source management, or other Event Log operations not modeled here.

## Windows registry

The registry suite exposes:

- `talvora_registry_create_key`
- `talvora_registry_get`
- `talvora_registry_set`
- `talvora_registry_list`
- `talvora_registry_delete_value`
- `talvora_registry_delete_key`

Supported roots are HKLM, HKCU, HKCR, HKU, HKCC, and HKPD. Callers may explicitly select the default, 32-bit, or 64-bit registry view. Structured value handling covers String, ExpandString, MultiString, DWORD, QWORD, Binary, and None.

ExpandString reads use `RegistryValueOptions.DoNotExpandEnvironmentNames`, so the stored representation is returned rather than a service-environment expansion. Subkey/value listings use ordinal sorting for deterministic agent behavior. Missing keys and values return structured not-found/deleted-false results where that makes the operation naturally idempotent.

Because Talvora runs as LocalSystem, HKCU refers to the LocalSystem profile when the installed service executes these tools. Per-user registry work can use HKU with the target user's SID, or the unrestricted process primitive can be used when a different Windows execution context is intentionally required.

## Process inspection and control

The process suite exposes `talvora_process_list`, `talvora_process_get`, and `talvora_process_kill`.

List/get use `System.Diagnostics.Process` and return deterministic structured process data. PID and process name are always the primary identity fields; session ID, start time, working set, and executable path are included when Windows permits those fields to be inspected. Access-denied metadata on protected processes is represented as unavailable rather than aborting the full enumeration.

Kill is PID-based, optionally terminates the complete process tree, and waits for exit up to an explicit timeout. The response distinguishes `killed` (a kill request was successfully issued) from `exited` (process termination was observed). A missing/already-exited PID returns an idempotent not-found result.

No PID or process-name allowlist is applied. Therefore the API can target any process accessible to Talvora's LocalSystem service, including processes whose termination may destabilize Windows or Talvora itself.

## PowerShell execution

`talvora_run_powershell` accepts a complete script string and converts it to UTF-16LE Base64 before launching PowerShell with `-EncodedCommand`. This avoids command-line escaping and nested-quote corruption for multiline agent-generated scripts.

The child process also receives `-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass`. Standard output, standard error, exit code, process ID, selected engine, executable path, and timeout state are returned as structured MCP content. A timeout terminates the entire child process tree.

Engine selection supports `auto`, `pwsh`, and `windows-powershell`. Auto prefers an existing PowerShell 7 `pwsh.exe` and falls back to the built-in Windows PowerShell executable under the Windows system directory. Talvora does not install PowerShell 7 solely for this tool.

Like `talvora_run_process`, this tool executes as the Talvora LocalSystem service and applies no script or command deny-list.

## Windows services

The service suite exposes `talvora_service_list`, `talvora_service_get`, `talvora_service_start`, `talvora_service_stop`, and `talvora_service_restart`.

List/get return structured service name, display name, current status, start type, service type, and control capabilities. Missing services return `found=false` rather than depending on localized shell text. Start/stop/restart are state-aware and wait for the final Running/Stopped state within an explicit timeout.

The dedicated API intentionally applies no service-name allowlist. Therefore it can also target the Talvora service itself; stopping or restarting Talvora can terminate the MCP request that initiated the operation. That is an operational consequence of full capability, not a hidden restriction.

The underlying `ServiceController` package is already supplied by Talvora's Windows service hosting dependency, so this layer adds no new Windows runtime prerequisite.

## Knowledge retrieval

`search(query)` returns structured search results containing `id`, `title`, `text`, and `url`. `fetch(id)` accepts a search result ID and returns the complete text plus metadata.

By default, search walks knowledge-oriented locations rather than crawling the whole machine: Public Documents, each local user's Documents/Desktop/Downloads/OneDrive folders, and `%ProgramData%\Talvora`. The machine environment variable `TALVORA_KNOWLEDGE_ROOTS` replaces those roots when an explicit corpus is desired.

The local scan skips reparse-point directory recursion, ignores inaccessible files/directories, searches text-oriented files up to 4 MiB each, caps a response at 20 results, and uses an internal time budget so an oversized corpus returns partial results rather than holding the MCP request indefinitely. These are retrieval-engine constraints only; they are not filesystem or execution restrictions on Talvora's primitive tools.

## ChatGPT Business transport

The privileged MCP runtime and the ChatGPT Business transport are deliberately separate layers.

Talvora itself remains a single LocalSystem Windows Service bound only to `127.0.0.1:7676`. ChatGPT web cannot dial that loopback address directly. For a Business custom MCP app, Talvora uses OpenAI Secure MCP Tunnel as an optional outbound-only transport.

`scripts/Configure-ChatGPT-Business.ps1` manages that transport in the interactive Windows user's context. It downloads the current official `openai/tunnel-client` Windows archive directly from the project's GitHub release, verifies the archive against the release SHA-256 manifest, and places the client under `%LOCALAPPDATA%\Talvora\TunnelClient`.

The script attaches an existing tunnel with `tunnel-client runtimes connect`, points it at `http://127.0.0.1:7676/mcp`, and passes the restricted runtime key through an environment-variable secret reference. Reconnect metadata is non-secret JSON; the runtime key is stored separately using current-user Windows DPAPI. The key authenticates the tunnel runtime and is not consumed by Talvora or used to invoke OpenAI models.

A Business connection is considered ready only after native `runtimes status --json` reports the managed runtime as running, healthy, and ready. There is no inbound Talvora firewall exposure, public MCP listener, user-session gateway, second Talvora Windows Service, or scheduled-task broker.

Automatic reboot persistence is not invented beyond what the official tunnel client documents. The explicit `-Reconnect` flow restores the managed runtime from the saved tunnel metadata and DPAPI-protected credential.

## Installation

The reset installer deletes prior Talvora Windows services, scheduled tasks, known Talvora runtime processes, `%LOCALAPPDATA%\Talvora`, `%PROGRAMDATA%\Talvora`, and the previous source checkout. It then clones a clean `main`, publishes a self-contained `win-x64` service, installs it as LocalSystem, verifies health and SID `S-1-5-18`, runs the real MCP smoke suite, and writes the `talvora_local` client registration.

Chocolatey is the only package manager used. WinGet is not used.

The local Talvora runtime does not require an OpenAI Platform account, API key, public ingress, or tunnel. ChatGPT Business web connectivity is an optional deployment layer and, under the current official Secure MCP Tunnel design, requires a tunnel ID plus a restricted tunnel runtime credential.