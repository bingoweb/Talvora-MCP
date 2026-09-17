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

## Business knowledge tools

Talvora also exposes two read-only MCP tools that follow the `search` -> `fetch` retrieval pattern used by ChatGPT custom MCP apps and company knowledge workflows:

- `search(query)` returns structured `{ results: [{ id, title, text, url }] }` data.
- `fetch(id)` returns structured `{ id, title, text, url, metadata }` data for a selected document.

The default search corpus is intentionally optimized for useful local knowledge rather than a blind whole-disk crawl: Public Documents plus each local Windows profile's Documents, Desktop, Downloads, and OneDrive folders, together with `%ProgramData%\Talvora`. Search is time-bounded and can return partial results instead of blocking an MCP request on a very large corpus.

Set the machine environment variable `TALVORA_KNOWLEDGE_ROOTS` to a semicolon-separated list of directories to replace the default search corpus. This setting changes only the read-only knowledge search surface; it does not restrict `talvora_read_text`, `talvora_write_text`, `talvora_delete`, `talvora_list`, or `talvora_run_process`.

As of 2026-09-17, ChatGPT Business supports custom MCP apps with full MCP capabilities, while company knowledge includes custom apps that provide search/fetch access. ChatGPT web does not connect directly to a localhost MCP endpoint, so web publication/remote connectivity remains a separate deployment concern and is not required by Talvora's local runtime.

## Rebuild from zero

Run `TALVORA-KUR.cmd` from an Administrator-capable Windows account. It launches the reset installer, removes prior Talvora runtime state, recreates the source checkout from `main`, builds a fresh self-contained Windows service, installs it, verifies `S-1-5-18`, runs the real MCP smoke suite, and configures the local `talvora_local` MCP registration.

The source of truth is the repository root. There is no legacy `v2` product tree.
