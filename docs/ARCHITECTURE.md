# Talvora architecture

Talvora intentionally starts from a clean design.

## Runtime

`Talvora.exe` is a self-contained ASP.NET Core application installed as the Windows service `Talvora` under `LocalSystem`. It listens only on loopback port 7676 and maps MCP at `/mcp` using the official C# MCP SDK's stateless HTTP transport.

There is no user-session gateway, named-pipe broker, compatibility shim, legacy maintenance layer, or secondary privileged process.

## Capability model

The service account is the capability boundary. Talvora does not implement command deny-lists or filesystem allow-lists for its primitive capability surface. Six primitive tools expose system information, filesystem read/write/delete/list, and arbitrary executable execution. Higher-level Windows capabilities are composed from these primitives until a dedicated tool materially improves reliability or ergonomics.

The registry layer is the first dedicated Windows capability built on that rule. It exposes structured create/get/set/list/delete operations directly through Microsoft.Win32 instead of requiring an agent to compose shell commands. It does not introduce a registry hive/path allowlist and does not reduce the unrestricted process primitive.

The PowerShell layer applies the same principle to multiline automation. `talvora_run_powershell` is an ergonomic wrapper around a real PowerShell child process; it does not introduce a command deny-list and does not replace `talvora_run_process`.

The Windows service layer exposes structured Service Control Manager inspection and control without a service-name allowlist. It uses `System.ServiceProcess.ServiceController` for list/get/start/stop/restart while keeping `talvora_run_process` available for service creation, deletion, configuration, custom control codes, and any operation not modeled by the dedicated layer.

The read-only knowledge layer is separate from that capability boundary. `search` and `fetch` provide structured document discovery/retrieval for ChatGPT knowledge workflows without reducing the privileges or addressable paths of the primitive tools.

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

## Installation

The reset installer deletes prior Talvora Windows services, scheduled tasks, known Talvora runtime processes, `%LOCALAPPDATA%\Talvora`, `%PROGRAMDATA%\Talvora`, and the previous source checkout. It then clones a clean `main`, publishes a self-contained `win-x64` service, installs it as LocalSystem, verifies health and SID `S-1-5-18`, runs the real MCP smoke suite, and writes the `talvora_local` client registration.

Chocolatey is the only package manager used. WinGet is not used.

The local runtime does not require an OpenAI API account, API key, public ingress, or tunnel. Remote publication to ChatGPT web is a separate deployment concern from the Windows-local Talvora runtime.
