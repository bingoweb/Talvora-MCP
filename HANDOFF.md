# Talvora Handoff — 2026-09-18

This file is the canonical handoff for continuing Talvora development in a new ChatGPT/Codex session.

The user explicitly wants the next session to **continue automatically without asking for routine confirmation**. Read this file first, refresh the actual GitHub `main` state, then continue from the approved next bounded task below.

---

## 1. Immediate startup procedure for the next session

Do these steps in order:

1. Read this `HANDOFF.md`.
2. Fetch the current `main` branch from `bingoweb/Talvora-MCP`. Do not assume the SHA in this document is still HEAD.
3. Read at minimum:
   - `README.md`
   - `docs/ARCHITECTURE.md`
   - `.github/workflows/windows-ci.yml`
   - `scripts/Reset-And-Install.ps1`
   - `scripts/Install.ps1`
   - `tests/Talvora.Smoke/Program.cs`
   - the files under `src/Talvora/Tools/`
4. Check the latest Windows CI run for the current `main`.
5. If a local MCP namespace/connector named `talvora_local` is actually available in the new session, call `talvora_system_info` before doing local-machine work and compare its `SourceCommit` with current GitHub `main`.
6. If `talvora_local` is **not** available in that session, do not claim access to the user's Windows machine and do not stall. Continue GitHub/Windows-CI development and say only when relevant that physical-PC deployment cannot be executed from that surface.
7. Continue directly with the **approved next bounded task in section 11**. The user's request for automatic continuation is explicit approval of that already-presented bounded design. Do not ask for another routine approval before starting that task.
8. For any genuinely new architectural subsystem not covered by this handoff, follow the normal design/approval rules before implementation.

---

## 2. User intent and non-negotiable rules

Talvora is a Windows-native local MCP/agent system for one owner machine.

The governing philosophy is **full capability**. Security/privacy must not be implemented by artificially removing functionality or by adding hidden command/path/service/process/registry allowlists. Local identity, loopback-only networking, provenance, auditability, explicit structured contracts, deterministic behavior, and tests are acceptable. Capability reduction is not.

Hard rules:

- Windows is the runtime and development target.
- Talvora should run with machine authority. The canonical runtime is a single Windows Service running as `LocalSystem`.
- Chocolatey is the package manager. **WinGet is forbidden.**
- Do not require an OpenAI API account or OpenAI API key for the local runtime.
- A secure tunnel may be considered only if it is useful for ChatGPT Business/web access and an API-key-free path is verified from current official OpenAI documentation. It is never a prerequisite for the local runtime.
- Do not reintroduce the old Gateway + SYSTEM broker + scheduled-task architecture.
- Do not add artificial deny-lists/allow-lists to the primitive capability surface.
- Prefer structured dedicated MCP tools when they materially reduce quoting/parsing fragility, while keeping unrestricted primitives available.
- Use Context7/current official documentation for library/API decisions where freshness matters.
- Use TDD for behavior changes: RED must be observed for the expected reason before GREEN implementation.
- Before claiming a phase is complete, obtain fresh Windows CI evidence.
- Avoid repeated confirmation questions. Continue through ordinary debugging/fixes automatically.
- If the user sends only `devam et`, continue the current approved development thread rather than re-planning from scratch.

---

## 3. Canonical repository/runtime state

Repository:

- GitHub: `bingoweb/Talvora-MCP`
- Canonical branch: `main`
- Last fully verified product/runtime commit before this handoff file:  
  `efd880137d6dfeb912d0e6e388d3a2fdadd9abcb`
- Windows CI run for that commit: **#302 — SUCCESS**
- Open PRs at that point: none.
- Historical branch names existed, but all branch refs were aligned to the same verified commit before this handoff was created.

Important: after this `HANDOFF.md` is committed, `main` will naturally have a newer documentation-only SHA. Always refresh the real branch before modifying anything.

Runtime:

- Service name: `Talvora`
- Service account: `LocalSystem`
- Expected Windows SID: `S-1-5-18`
- MCP endpoint: `http://127.0.0.1:7676/mcp`
- Health endpoint: `http://127.0.0.1:7676/healthz`
- Transport: Streamable HTTP / stateless MCP
- Listener: loopback only
- Product version currently reported by health: `3.0.0-dev`
- No user-session Gateway.
- No named-pipe SYSTEM broker.
- No legacy maintenance shim.

Primary Windows paths:

- Source checkout after reset: `%USERPROFILE%\Talvora-MCP`
- Installed service payload: `%ProgramData%\Talvora\Service`
- Runtime state: `%LOCALAPPDATA%\Talvora\current.json`
- Installed runtime provenance: `%ProgramData%\Talvora\Service\talvora-runtime.json`
- Local client config:
  - `%CODEX_HOME%\config.toml` when `CODEX_HOME` exists, otherwise
  - `%USERPROFILE%\.codex\config.toml`

Canonical MCP client table written by the installer:

~~~toml
[mcp_servers.talvora_local]
url = "http://127.0.0.1:7676/mcp"
enabled = true
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "approve"
~~~

The `approve` tool mode was intentionally kept because current Codex MCP semantics treat that server tool mode as automatic approval/always-allow behavior for the configured MCP, matching the user's no-routine-confirmation preference.

---

## 4. Current MCP tool surface: 23 verified tools

### Full-capability primitives — 6

1. `talvora_system_info`
2. `talvora_read_text`
3. `talvora_write_text`
4. `talvora_delete`
5. `talvora_list`
6. `talvora_run_process`

The filesystem/process primitives are not restricted to the knowledge corpus. They run with the Talvora service account's Windows privileges.

### Structured process inspection/control — 3

7. `talvora_process_list`
8. `talvora_process_get`
9. `talvora_process_kill`

Behavior:

- Structured PID/name/session/start-time/working-set/executable metadata where Windows permits it.
- Protected/unavailable optional metadata does not fail the whole list.
- Kill is PID-based, optionally kills the complete process tree, waits with a timeout, and distinguishes kill request from observed exit.
- Missing/exited PIDs are idempotent not-found.
- No PID/process-name allowlist.

### PowerShell execution — 1

10. `talvora_run_powershell`

Behavior:

- Arbitrary multiline script.
- UTF-16LE Base64 via `-EncodedCommand`.
- `-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass`.
- Captures stdout/stderr, exit code, PID, timeout state, engine and executable.
- Timeout kills the process tree.
- `engine=auto` prefers existing `pwsh.exe`, otherwise built-in Windows PowerShell.
- PowerShell 7 is not a runtime prerequisite.
- No script/command deny-list.

### Windows registry — 6

11. `talvora_registry_create_key`
12. `talvora_registry_get`
13. `talvora_registry_set`
14. `talvora_registry_list`
15. `talvora_registry_delete_value`
16. `talvora_registry_delete_key`

Behavior:

- Hives: HKLM, HKCU, HKCR, HKU, HKCC, HKPD.
- Registry views: default, 32-bit, 64-bit.
- Value kinds: String, ExpandString, MultiString, DWORD, QWORD, Binary, None.
- ExpandString reads are not environment-expanded.
- Deterministic ordinal listings.
- No registry hive/path allowlist.
- Under LocalSystem, HKCU means the LocalSystem profile. User-specific registry work can use HKU with the target SID.

### Windows service control — 5

17. `talvora_service_list`
18. `talvora_service_get`
19. `talvora_service_start`
20. `talvora_service_stop`
21. `talvora_service_restart`

Behavior:

- Structured Service Control Manager access through `System.ServiceProcess.ServiceController`.
- List/get/start/stop/restart.
- Missing service get returns `found=false`.
- State-aware waits with explicit timeout.
- No service-name allowlist.
- Talvora does not block targeting its own service; doing so can terminate the MCP connection. That is an accepted consequence of the full-capability model.

### Business knowledge surface — 2

22. `search`
23. `fetch`

Behavior:

- `search(query)` returns structured `{ results: [{ id, title, text, url }] }`.
- `fetch(id)` returns complete text + URL + metadata.
- Default knowledge roots focus on useful documents instead of blindly traversing the whole disk:
  - Public Documents
  - each Windows user's Documents/Desktop/Downloads/OneDrive*
  - `%ProgramData%\Talvora`
- `TALVORA_KNOWLEDGE_ROOTS` can replace the knowledge corpus.
- That environment variable affects only knowledge retrieval; it does **not** restrict the primitive filesystem/process tools.
- Search has a bounded time budget and can return partial results.

---

## 5. Installation/reset behavior

Entry point:

- `TALVORA-KUR.cmd`

Important bootstrap behavior:

- It first copies itself into `%TEMP%`.
- The temporary bootstrap downloads the current `main/scripts/Reset-And-Install.ps1` from GitHub.
- Therefore running an old local `TALVORA-KUR.cmd` still pulls the current reset logic rather than trusting an old local PowerShell installer.

`scripts/Reset-And-Install.ps1` intentionally removes prior Talvora state before cloning fresh `main`:

- Talvora-named Windows services.
- Talvora scheduled tasks.
- Known Talvora processes.
- Talvora firewall rules.
- Talvora startup/Run values.
- `TALVORA*` user/machine environment variables.
- Talvora entries from user/machine PATH.
- Known Talvora registry keys.
- `%LOCALAPPDATA%\Talvora`
- roaming AppData Talvora
- `%ProgramData%\Talvora`
- Program Files Talvora locations.
- Talvora shortcuts.
- Talvora temp directories.
- Talvora MCP tables from the Codex config.
- Existing source checkout at `%USERPROFILE%\Talvora-MCP`.

It then:

1. Ensures Chocolatey.
2. Installs Git and .NET 10 SDK through Chocolatey if missing.
3. Clones clean `main`.
4. Executes `scripts/Install.ps1`.

`scripts/Install.ps1`:

1. Ensures admin rights.
2. Publishes self-contained `win-x64`.
3. Resolves exact source Git commit.
4. Writes `talvora-runtime.json` into the service payload before service start.
5. Removes/recreates the `Talvora` Windows service.
6. Verifies LocalSystem.
7. Configures service restart recovery.
8. Starts Talvora.
9. Verifies health, SID and source provenance.
10. Runs the real MCP smoke project against the installed service.
11. Extracts the live tool manifest from smoke output.
12. Writes `MCP_SMOKE_OK` only after the real smoke succeeds.
13. Writes local MCP client config.
14. Writes `%LOCALAPPDATA%\Talvora\current.json` containing:
    - product/version
    - source commit
    - service/SID/PID
    - MCP URL
    - client config path
    - tool count
    - tool names
    - install timestamp
15. Prints `TALVORA READY`.

---

## 6. Provenance/install-manifest work that was just completed

The latest development thread added install provenance and a live installed-tool manifest.

Why it exists:

- A successful CI build is not enough to prove which source revision is physically installed.
- Talvora now records the exact Git commit used to build the installed service.
- `/healthz` exposes `sourceCommit` and `installedAtUtc`.
- `talvora_system_info` exposes the same runtime provenance.
- `current.json` records the source commit and the actual smoke-observed tool manifest.

The CI service-install gate now proves:

- service runs as LocalSystem;
- SID is `S-1-5-18`;
- it detects Windows Service hosting;
- `health.sourceCommit == git rev-parse HEAD`;
- `current.json.SourceCommit == git rev-parse HEAD`;
- `current.json.ToolCount == 23`;
- `ToolNames` count is 23;
- core expected tools are present in the manifest.

The final verified CI for this thread is:

- Commit: `efd880137d6dfeb912d0e6e388d3a2fdadd9abcb`
- Workflow: `windows-ci`
- Run: **#302**
- Result: **SUCCESS**

A transient implementation bug occurred while patching `Install.ps1`: a JavaScript replacement string interpreted the PowerShell regex's trailing `$'` as a replacement metacharacter and duplicated/corrupted the installer tail. This was diagnosed from the PowerShell parser output and fixed by reconstructing one canonical 187-line installer while preserving the provenance + manifest behavior. Do not reintroduce that broken 376-line duplicate form.

---

## 7. Windows CI quality gates

`.github/workflows/windows-ci.yml` currently requires:

1. checkout;
2. .NET 10 setup;
3. restore;
4. build Talvora;
5. build smoke project;
6. launch Talvora directly and run the real MCP smoke suite;
7. parse every PowerShell installer script;
8. reject any WinGet usage;
9. run the installer on an elevated Windows runner;
10. verify the real Windows Service is Running as LocalSystem;
11. verify SID `S-1-5-18`;
12. verify Windows Service detection;
13. verify exact source commit provenance;
14. verify installed state file;
15. verify 23-tool manifest;
16. cleanup service and runtime state.

A change is not complete until the final canonical HEAD has a fresh successful run of this workflow.

---

## 8. Branch/repository hygiene

At the last verification before this handoff, there were no open PRs.

Historical branch names included:

- `feat/elevated-operation-execution`
- `feat/windows-registry-management`
- `noop`
- `talvora-2/foundation`
- `talvora-2/foundation-red`
- `talvora-2/legacy-maintenance`
- `talvora-2/system-broker`

They were repeatedly force-aligned to the canonical verified `main` tree so they did not expose divergent old product code.

Important nuance:

- This does **not** guarantee GitHub has physically erased all unreachable historical objects, PR refs, backups, or provider-internal retention.
- An earlier attempt to perform an orphan/history reset was blocked by the environment/security surface.
- Do not claim the Git provider's underlying object history is physically erased.
- The practical requirement is that canonical/current branches expose only the new Talvora product.

After committing this handoff, align historical branch refs to the new handoff HEAD once its CI is verified.

---

## 9. Local Windows access caveat

The previous web ChatGPT session did not expose the user's local `talvora_local` MCP namespace even though the project is designed to install it locally.

Therefore:

- Never pretend a GitHub/CI change was physically deployed to the user's Windows machine unless a real local connector/tool proves it.
- If `talvora_local` becomes available in the new session, immediately verify with `talvora_system_info`.
- A good physical-PC verification sequence is:
  1. `talvora_system_info`
  2. compare `SourceCommit` to GitHub `main`
  3. confirm SID `S-1-5-18`
  4. list tools / verify installed manifest
  5. perform a harmless temp-file roundtrip
  6. only then perform local maintenance/development actions.
- If physical PC is stale, the reset/install entry point is `TALVORA-KUR.cmd`.

---

## 10. OpenAI / ChatGPT Business integration constraints

Do not conflate ChatGPT Business with the OpenAI API.

Current project policy:

- No OpenAI API account/key is required by Talvora local runtime.
- Full MCP write/modify capabilities are intended to be usable where ChatGPT Business supports them.
- The `search`/`fetch` surface exists for company-knowledge-style retrieval compatibility.
- ChatGPT web does not directly reach a loopback-only `localhost` MCP.
- OpenAI has documented a Secure MCP Tunnel concept for private/local MCP connectivity, but do not add a tunnel as a mandatory runtime dependency.
- If pursuing web connectivity, verify current official OpenAI documentation first.
- If there is a genuinely API-key-free secure tunnel path, it is allowed by the user.
- If a proposed tunnel requires an OpenAI API key, do not make it part of the Talvora core installation.

---

## 11. NEXT TASK — already approved for automatic continuation

This is the next bounded change and should start immediately in the new session after refreshing current repository state.

### Goal

Add three structured full-capability filesystem mutation tools:

1. `talvora_create_directory`
2. `talvora_copy`
3. `talvora_move`

These are ergonomic structured tools. They must not reduce or replace the unrestricted filesystem/process primitives.

### Contract

#### `talvora_create_directory(path)`

- Creates the complete directory path, including missing parents.
- Idempotent when the directory already exists.
- Returns structured data with at least normalized/full path and whether creation changed filesystem state.
- No path allowlist.

#### `talvora_copy(source, destination, overwrite = false, recursive = true)`

- Supports files and directories.
- File copy honors `overwrite`.
- Directory copy is recursive when `recursive=true`.
- If a directory source is used with `recursive=false`, fail clearly rather than silently producing a partial tree.
- Existing destination collisions must be deterministic:
  - `overwrite=false` => structured/tool error;
  - `overwrite=true` => replace conflicting file destination and merge/replace conflicting copied entries predictably.
- Preserve unrestricted addressability; no path allowlist.
- Reparse points must not create accidental infinite recursion. Treat them deliberately and test the chosen behavior.

#### `talvora_move(source, destination, overwrite = false)`

- Supports files and directories.
- `overwrite=false` => deterministic error when destination exists.
- `overwrite=true` => remove/replace the destination then move.
- Return structured source/destination/type/change information.
- No path allowlist.

### TDD sequence

1. Read current `FileTools.cs` and current smoke test before editing.
2. Add the three names to the smoke required-tool set.
3. Add real filesystem smoke behavior under a disposable temp/Public Documents tree:
   - create nested directory;
   - create a source file using existing `talvora_write_text`;
   - copy file and verify via existing `talvora_read_text`;
   - construct nested directory tree and copy it recursively;
   - move a file;
   - move a directory;
   - prove `overwrite=false` rejects collision;
   - prove `overwrite=true` replaces as designed;
   - cleanup in `finally`.
4. Commit/run RED and verify failure is specifically due to missing new tool(s), not a typo/build error.
5. Implement the minimal production behavior.
6. Run the full Windows workflow to GREEN.
7. Update installer provenance/tool-manifest CI expectations from **23 to 26**.
8. Run full Windows workflow again. The real installed LocalSystem service must report and smoke all **26** tools.
9. Update README + architecture.
10. Run final canonical HEAD CI again.
11. Align all historical branch refs to that verified HEAD.
12. Confirm open PR count and branch alignment.

Do not ask the user to approve this bounded task again. Their request for the next session to continue automatically is the approval.

---

## 12. After the next filesystem task

Do not automatically invent a large new subsystem. Reassess current repo and choose the next highest-value Windows capability.

Likely future candidates, subject to a new short design:

- scheduled task inspection/control;
- Windows package management through Chocolatey only;
- network/port/interface inspection and control;
- environment-variable management;
- Windows account/session/token ergonomics;
- filesystem ACL/ownership tools;
- device/driver inspection;
- Event Log structured query;
- update/self-maintenance flow for the installed Talvora service;
- API-key-free Secure MCP Tunnel path if current official OpenAI docs support it.

Do not add several of these in one commit. Continue one bounded capability at a time with real Windows smoke coverage.

---

## 13. Known housekeeping item

`docs/ARCHITECTURE.md` currently contains a duplicated `Process inspection and control` section from overlapping documentation commits. This is documentation-only duplication, not a runtime defect.

When updating architecture docs for the next filesystem task, collapse the duplicate process section into one canonical section without changing runtime behavior.

---

## 14. Communication style expected by the user

- Turkish.
- Direct and technically concrete.
- Do not ask repetitive questions when the answer is already known.
- Do not say to wait or promise background work.
- Keep the user updated during long tool sequences with short progress messages.
- If something fails, report the actual root cause when known and continue fixing it.
- Do not call a partial CI state “green”.
- Do not pretend the physical Windows PC was modified when only GitHub/CI was modified.
- When the user says `devam et`, continue.

---

## 15. Compact continuity checklist

Before doing new code:

- [ ] Read this handoff.
- [ ] Refresh `main`.
- [ ] Check latest CI.
- [ ] Check whether `talvora_local` exists in the current session.
- [ ] Preserve full-capability/LocalSystem design.
- [ ] Chocolatey only; reject WinGet.
- [ ] No OpenAI API key requirement.
- [ ] Start the approved filesystem tools task with RED smoke.
- [ ] Update 23 -> 26 manifest expectation only after the tool implementation is real.
- [ ] Verify direct MCP smoke + real LocalSystem installer smoke.
- [ ] Update docs and collapse duplicate process documentation section.
- [ ] Final CI on canonical HEAD.
- [ ] Align historical branch refs.
- [ ] No open PRs unless intentionally created.

This handoff is intended to let the next session continue without relying on hidden prior-chat context.
