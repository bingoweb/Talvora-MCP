# Talvora Handoff — 2026-09-18

This file is the canonical handoff for continuing Talvora development in a new ChatGPT/Codex session.

The user explicitly wants the next session to **continue automatically without asking for routine confirmation**. Read this file first, refresh the actual GitHub `main` state, then continue from the current verified state below.

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
7. Read sections 11 through 14 for the completed filesystem-mutation, environment-variable, Event Log query, and ChatGPT Business Secure MCP Tunnel evidence. There is no further bounded subsystem pre-approved by this handoff.
8. For the next genuinely new architectural subsystem, reassess current `main`, present a short bounded design when needed, and follow the normal approval rules before implementation.

---

## 2. User intent and non-negotiable rules

Talvora is a Windows-native local MCP/agent system for one owner machine.

The governing philosophy is **full capability**. Security/privacy must not be implemented by artificially removing functionality or by adding hidden command/path/service/process/registry allowlists. Local identity, loopback-only networking, provenance, auditability, explicit structured contracts, deterministic behavior, and tests are acceptable. Capability reduction is not.

Hard rules:

- Windows is the runtime and development target.
- Talvora should run with machine authority. The canonical runtime is a single Windows Service running as `LocalSystem`.
- Chocolatey is the package manager. **WinGet is forbidden.**
- Do not require an OpenAI API account or OpenAI API key for the local runtime.
- ChatGPT Business/web connectivity may use the current official OpenAI Secure MCP Tunnel. The Talvora core runtime must remain independent of OpenAI Platform credentials; the optional tunnel transport may use the restricted runtime credential required by current official tunnel-client documentation. It is never a prerequisite for the local runtime.
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
- Last fully verified code/workflow commit before this handoff update:  
  `56732fd38904f3491bbc319823e3744b2a7ad24a`
- Windows CI run for that commit: **#353 — SUCCESS**
- That run proved the direct 32-tool MCP smoke, PowerShell parser gate, WinGet rejection, dual-shell ChatGPT Business bootstrap self-tests, and the real elevated LocalSystem installer/service smoke.
- A temporary draft PR was used only to obtain Windows pull-request CI during TDD; close it after the final handoff HEAD is verified and promoted to `main`.
- Historical branch refs must be aligned to the final verified handoff HEAD after promotion.

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

## 4. Current MCP tool surface: 32 verified tools

### Full-capability primitives — 6

1. `talvora_system_info`
2. `talvora_read_text`
3. `talvora_write_text`
4. `talvora_delete`
5. `talvora_list`
6. `talvora_run_process`

The filesystem/process primitives remain unrestricted by the knowledge corpus and run with the Talvora service account's Windows privileges.

### Structured filesystem mutation — 3

7. `talvora_create_directory`
8. `talvora_copy`
9. `talvora_move`

Behavior:

- Create-directory creates missing parents and is idempotent for an existing directory.
- Copy supports files and directories.
- Directory copy requires `recursive=true`; `recursive=false` fails before creating a partial destination.
- `overwrite=false` rejects collisions.
- `overwrite=true` replaces conflicting copied entries while preserving non-conflicting entries already present in a destination directory.
- Directory copy preflights the complete source tree and rejects source reparse points before destination mutation, preventing accidental junction/symlink recursion loops.
- Move supports files and directories; with `overwrite=true` the destination is removed/replaced before moving.
- Same-path operations and directory-into-descendant operations are rejected as invalid filesystem operations.
- No path allowlist or deny-list is applied.

### Environment-variable management — 4

10. `talvora_env_get`
11. `talvora_env_list`
12. `talvora_env_set`
13. `talvora_env_delete`

Behavior:

- Targets: Process, User, Machine.
- Process values live only in the Talvora service process.
- User/Machine values use Windows' persistent environment-variable stores.
- Under the installed LocalSystem service, User refers to the LocalSystem account's user environment.
- List supports an optional case-insensitive name query and deterministic sorting.
- Explicit empty-string values are preserved; delete uses platform removal semantics and is idempotent for missing values.
- No environment-variable name allowlist or deny-list is applied.

### Windows Event Log query — 2

14. `talvora_eventlog_list`
15. `talvora_eventlog_query`

Behavior:

- List enumerates local Windows Event Log names with optional case-insensitive filtering and deterministic ordering.
- Query accepts any local log name plus XPath, max-event count, and newest-first/oldest-first direction.
- Structured records include log/provider name, event ID, record ID, timestamp, level, process/thread IDs, machine/user identity when available, and a best-effort formatted message.
- Provider message-formatting failures do not discard the underlying event record.
- Per-call event count bounds one MCP response; it is not a log/provider/event-ID allowlist.
- No log-name, provider, or event-ID allowlist or deny-list is applied.
- Existing unrestricted process and PowerShell primitives remain available for Event Log operations outside this read-only query layer.

### Structured process inspection/control — 3

16. `talvora_process_list`
17. `talvora_process_get`
18. `talvora_process_kill`

Behavior:

- Structured PID/name/session/start-time/working-set/executable metadata where Windows permits it.
- Protected/unavailable optional metadata does not fail the whole list.
- Kill is PID-based, optionally kills the complete process tree, waits with a timeout, and distinguishes kill request from observed exit.
- Missing/exited PIDs are idempotent not-found.
- No PID/process-name allowlist.

### PowerShell execution — 1

19. `talvora_run_powershell`

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

20. `talvora_registry_create_key`
21. `talvora_registry_get`
22. `talvora_registry_set`
23. `talvora_registry_list`
24. `talvora_registry_delete_value`
25. `talvora_registry_delete_key`

Behavior remains unchanged: HKLM/HKCU/HKCR/HKU/HKCC/HKPD, default/32/64-bit views, structured value kinds, deterministic listings, and no hive/path allowlist.

### Windows service control — 5

26. `talvora_service_list`
27. `talvora_service_get`
28. `talvora_service_start`
29. `talvora_service_stop`
30. `talvora_service_restart`

Behavior remains unchanged: structured SCM access, state-aware waits, missing-service handling, and no service-name allowlist.

### Business knowledge surface — 2

31. `search`
32. `fetch`

The knowledge corpus remains a read-only retrieval surface and does not restrict primitive or structured filesystem/process capabilities.

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
- `current.json.ToolCount == 32`;
- `ToolNames` count is 32;
- core expected tools are present in the manifest.

The filesystem-mutation thread has a fully successful Windows verification before this handoff update:

- Commit: `093bab372ae5ae6bcb727a32882b9697ca5f9ef1`
- Workflow: `windows-ci`
- Run: **#308**
- Result: **SUCCESS**
- Direct MCP smoke: SUCCESS
- Real LocalSystem installer/service smoke: SUCCESS
- Installed manifest: 26 tools

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
15. verify 32-tool manifest;
16. cleanup service and runtime state.

Restore and Build explicitly check `$LASTEXITCODE` after every `dotnet` invocation. This was hardened at `17dde234c2514af78b7a09790732df7ecb49ee45` after Event Log package testing exposed that a later successful smoke-project command could otherwise mask an earlier Talvora restore/build failure inside the same PowerShell step.

A change is not complete until the final canonical HEAD has a fresh successful run of this workflow.

---

## 8. Branch/repository hygiene

Before the filesystem-mutation TDD thread there were no open PRs. That thread used a temporary draft PR to obtain real Windows pull-request CI; finalization requires that PR to be merged/closed and the repository to return to zero open PRs.

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

Do not conflate ChatGPT Business with the OpenAI API or with the Talvora core runtime.

Current project policy:

- Talvora core remains a Windows-local, API-key-free MCP runtime.
- The Talvora service still binds only to `http://127.0.0.1:7676/mcp` and requires no public ingress.
- ChatGPT web cannot directly dial the loopback MCP endpoint.
- Current official ChatGPT Business connectivity for a private/local MCP uses OpenAI Secure MCP Tunnel.
- The optional Business transport is deliberately separate from Talvora core and uses the official `openai/tunnel-client`.
- The verified/pinned tunnel client is `v0.0.14`; upgrade it only with a new explicit Windows CI validation.
- Current official tunnel attach requires an existing `tunnel_id` plus a restricted Runtime API key/principal with **Tunnels Read + Use**.
- That runtime credential authenticates Secure MCP Tunnel transport. Talvora does not use it for model inference, Responses API calls, or its local MCP tools.
- Do not use an organization/admin key when a least-privilege Runtime API key is sufficient.
- The reconnect credential is stored separately using current-user Windows DPAPI; it is not written into command-line arguments or Talvora config.
- No inbound firewall rule, public Talvora listener, secondary Talvora service, Gateway, broker, or scheduled task is added for Business connectivity.
- Reboot recovery is explicit via `TALVORA-BUSINESS-KUR.cmd reconnect` unless future official tunnel-client documentation provides a different supported persistence contract.
- Business Admin/Owner performs the final workspace UI step: Developer mode -> Workspace settings/Apps -> custom MCP app -> Connection: Tunnel -> select/paste the tunnel ID -> review/publish.


---

## 11. COMPLETED TASK — structured filesystem mutation tools

The approved filesystem mutation task is complete in the verified code/docs commit `093bab372ae5ae6bcb727a32882b9697ca5f9ef1`.

Implemented:

1. `talvora_create_directory`
2. `talvora_copy`
3. `talvora_move`

TDD/CI evidence:

- RED commit: `fdf539a0f34edea264b1b60db81f7cb917cb6d5a`.
- Windows CI **#304** restored and built both projects with zero errors, then failed the real MCP smoke specifically with `Missing MCP tool: talvora_create_directory`.
- Production implementation commit: `583f2d11bda4e12a13ffd3f8ccc31422fee440ab`.
- Windows CI **#305** passed build, direct MCP smoke, installer parsing and WinGet rejection; its real installer wrote `ToolCount=26` and then failed only because the old CI assertion still expected 23. This proved the runtime/tool manifest changed before the gate was updated.
- CI manifest expectation was then changed from 23 to 26.
- README and architecture were updated; the duplicate Process inspection/control architecture section was collapsed to one canonical section.
- Windows CI **#308** on `093bab372ae5ae6bcb727a32882b9697ca5f9ef1` completed fully SUCCESS, including direct MCP smoke and the real elevated LocalSystem installation/service smoke.

Chosen reparse-point contract:

- Directory copy preflights the source tree.
- A source reparse point causes a tool error before destination mutation.
- This avoids accidental infinite traversal while preserving unrestricted path addressability; it is traversal semantics, not an allowlist.

No primitive tool was removed or restricted.

---

## 12. COMPLETED TASK — environment-variable management

The next bounded Windows capability was implemented entirely through GitHub + Windows CI because the user was not on the Windows machine.

Implemented:

1. `talvora_env_get`
2. `talvora_env_list`
3. `talvora_env_set`
4. `talvora_env_delete`

Contract:

- Explicit targets: Process, User, Machine.
- Get returns structured found/not-found data.
- List returns deterministic name-sorted entries and optional case-insensitive name filtering.
- Set creates/replaces values and preserves explicit empty strings.
- Delete is idempotent and removes values using the platform's null-removal semantics.
- No environment-variable name allowlist or deny-list.
- No existing process/PowerShell primitive was removed or restricted.

TDD/CI evidence:

- Initial smoke-only commit: `8bb9849a9d63146d1ce0df48d269d42f3f4915e5`.
- Windows CI **#316** was **not** accepted as RED because the smoke used `smokeId` before declaration and failed at compile time. The test was corrected before any production implementation.
- Corrected RED commit: `2327e715e3ffdd3a491196b9764c66e92d32564d`.
- Windows CI **#317** restored and built both projects with zero errors, then failed the real MCP smoke specifically with `Missing MCP tool: talvora_env_get`.
- Production implementation commit: `4ef585fbc83b44176224bcadf069767cc7167d81`.
- Windows CI **#318** passed build, direct MCP behavior smoke, installer parsing and WinGet rejection. The real LocalSystem installer reached `TALVORA READY`, SID `S-1-5-18`, and wrote a live 30-tool manifest, then failed only because the old CI assertion still expected 26.
- The manifest gate was updated to 30 at `97a1eaef6280511e5a4268ab82877ed849edef6d`.
- Windows CI **#319** completed fully SUCCESS, including direct MCP smoke and the real elevated LocalSystem installation/service smoke with all 30 tools.

API/library basis:

- Current .NET 10 `System.Environment` APIs support Process/User/Machine targets on Windows.
- Passing `null` removes a variable; an explicit empty string is preserved on current .NET.
- Machine-scope writes require administrative authority; Talvora's installed LocalSystem service has that authority.

---

## 13. COMPLETED TASK — Windows Event Log structured queries

This bounded capability was implemented entirely through GitHub + real Windows CI because the user was not on the physical Windows machine.

Implemented:

1. `talvora_eventlog_list`
2. `talvora_eventlog_query`

Contract:

- List discovers local Windows Event Log names with deterministic ordering and optional case-insensitive filtering.
- Query accepts an unrestricted local log name and XPath expression.
- `newestFirst=true` reads newest-to-oldest; false reads oldest-to-newest.
- `maxEvents` bounds a single MCP response rather than restricting addressable logs or providers.
- Structured records preserve core event metadata even when Windows cannot format a provider-specific message.
- No log/provider/event-ID allowlist or deny-list.
- Existing unrestricted process/PowerShell primitives remain available for clear/export/provider/source operations not modeled by this read-only convenience layer.

TDD/CI evidence:

- RED smoke commit: `963e848db3d9be853081bf739b07b84d899587c8`.
- Windows CI **#324** restored and built both projects with zero errors, then failed the real MCP smoke specifically with `Missing MCP tool: talvora_eventlog_list`.
- An initial explicit `System.Diagnostics.EventLog 10.0.12` PackageReference was tested and rejected by Windows CI **#326** with SDK error `NU1510`: the package was already automatically available to this project and the explicit reference had to be removed. The dependency decision was corrected from the log rather than guessed.
- Production Event Log implementation was retained while the redundant package reference was removed at `8c6780beb01d5190fd40ddf9d4686e91d2c902ae`.
- Windows CI **#327** passed restore/build, direct Event Log MCP smoke, installer parsing, and WinGet rejection; the real LocalSystem installer reached `TALVORA READY`, SID `S-1-5-18`, and produced a live **32-tool** manifest, then failed only because the old CI assertion still expected 30.
- The manifest gate was updated to 32 at `ae5f1e3b895a41c1777b6bac8825c8a91971e5f1`.
- Windows CI **#328** completed fully SUCCESS, including direct MCP smoke and the real elevated LocalSystem installation/service smoke with all 32 tools.

API/library basis:

- Windows Eventing is accessed through `System.Diagnostics.Eventing.Reader`.
- `EventLogSession.GetLogNames()` supplies registered local log names.
- `EventLogQuery` + `EventLogReader` perform XPath reads, and `ReverseDirection` controls newest-first traversal.
- Do not re-add an explicit `System.Diagnostics.EventLog` PackageReference unless the project shape changes and CI proves it is required; the current .NET 10 project reports it as automatically available.

---

## 14. COMPLETED TASK — ChatGPT Business Secure MCP Tunnel transport

The user explicitly requested Windows installation to be fully compatible with ChatGPT Business. The transport was added as a separate optional layer without changing Talvora's 32-tool MCP surface or the LocalSystem/loopback core architecture.

Implemented:

1. `scripts/Configure-ChatGPT-Business.ps1`
2. `TALVORA-BUSINESS-KUR.cmd`
3. Windows CI gate for both PowerShell 7 and Windows PowerShell 5.1 bootstrap self-tests.
4. README/architecture/install output describing the separate Business pairing flow.

Contract:

- Talvora remains one `LocalSystem` Windows Service on `127.0.0.1:7676`.
- The Business layer uses official `openai/tunnel-client` and an outbound Secure MCP Tunnel.
- Tunnel client version is pinned to verified `v0.0.14`.
- The Windows archive is downloaded from the official GitHub release and verified against official `SHA256SUMS.txt` before use.
- Supported local tunnel-client architectures are Windows amd64 and arm64.
- Tunnel IDs are validated using the current official shape: `tunnel_` plus 32 lowercase hexadecimal characters.
- Runtime credential is supplied to the native client by `env:CONTROL_PLANE_API_KEY`, not as a literal command-line secret.
- Reconnect copy of the restricted runtime credential is protected with current-user Windows DPAPI.
- Non-secret pairing metadata is stored under `%LOCALAPPDATA%\Talvora\TunnelClient`.
- Pairing uses `runtimes connect` to `http://127.0.0.1:7676/mcp`.
- Success is not reported until `runtimes status <alias> --json` reports `process_running=true`, `healthy=true`, and `ready=true`.
- No public MCP listener or inbound firewall opening is created.
- No hidden second service or scheduled task is created.
- `TALVORA-BUSINESS-KUR.cmd reconnect` is the explicit reboot/reconnect path.
- The Talvora MCP tool manifest remains **32 tools**.

TDD/CI evidence:

- RED commit: `15583176080a873d1d645c4bb1b3cb0f37d16c43`.
- Windows CI **#336** passed Talvora build, real 32-tool MCP smoke, parser and WinGet gates, then failed exactly because `scripts/Configure-ChatGPT-Business.ps1` did not yet exist.
- Windows CI **#337** showed the bootstrap's own self-test GREEN but exposed a CI-gate bug: `$LASTEXITCODE` was incorrectly inspected after invoking a PowerShell script. The gate was corrected and expanded to run Windows PowerShell 5.1 explicitly.
- A JavaScript replacement-string `$'` metacharacter accident later duplicated/corrupted the bootstrap file. Repeated parser failures exposed that the file had grown to 1,344 lines with duplicated major functions. The file was not incrementally patched; it was reconstructed as one canonical 534-line script at `b5266e156a472f9d0040dc86206f46f523f3654f`.
- Windows CI **#352** then passed build, 32-tool MCP smoke, parser and WinGet gates and reached the Business self-test. It exposed that PowerShell `-match` is case-insensitive by default, so uppercase tunnel IDs were incorrectly accepted.
- Final behavior fix: `56732fd38904f3491bbc319823e3744b2a7ad24a` uses case-sensitive `-cmatch`.
- Windows CI **#353** completed fully **SUCCESS** for the final bootstrap behavior, including PowerShell 7 + Windows PowerShell 5.1 self-tests and the real LocalSystem installation gate.
- The Business distribution gate was then strengthened at `dd72b4348bf064700a0707545b2af69e9eba4431` to exercise the official OpenAI Windows distribution itself rather than only helper logic.
- Windows CI **#355** completed fully **SUCCESS**, including:
  - restore/build;
  - direct real 32-tool MCP smoke;
  - all PowerShell installer parsing;
  - WinGet rejection including the Business bootstrap CMD;
  - Business bootstrap self-test under PowerShell 7;
  - Business bootstrap self-test under Windows PowerShell 5.1;
  - real download of official `openai/tunnel-client` v0.0.14 for Windows;
  - SHA256 verification against the official `SHA256SUMS.txt`;
  - successful `tunnel-client.exe --version`;
  - successful `tunnel-client runtimes connect --help` contract checks for `--tunnel-id`, `--runtime-api-key`, and `--mcp-server-url`;
  - real elevated LocalSystem `Install.ps1` service installation and 32-tool installed manifest;
  - cleanup.

Physical-PC caveat:

- This development thread did not expose a `talvora_local` tool namespace in ChatGPT, so GitHub/Windows-runner validation must not be described as physical deployment to the user's own PC.
- On the physical Windows PC, run `TALVORA-KUR.cmd` first, then `TALVORA-BUSINESS-KUR.cmd` for Business pairing.


---

## 15. Next development task

There is no additional bounded subsystem pre-approved by this handoff. Reassess current `main` and choose the next highest-value Windows capability one bounded change at a time.

Likely future candidates, subject to a new short design:

- scheduled task inspection/control;
- Windows package management through Chocolatey only;
- network/port/interface inspection and control;
- Windows account/session/token ergonomics;
- filesystem ACL/ownership tools;
- device/driver inspection;
- update/self-maintenance flow for the installed Talvora service.

Do not add several of these in one commit. Continue one bounded capability at a time with real Windows smoke coverage.

---

## 16. Housekeeping status

The duplicate `Process inspection and control` section in `docs/ARCHITECTURE.md` was removed during the filesystem mutation documentation update. The file now has one canonical process section.

---

## 17. Communication style expected by the user

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

## 18. Compact continuity checklist

Before doing new code:

- [ ] Read this handoff.
- [ ] Refresh `main`.
- [ ] Check latest CI.
- [ ] Check whether `talvora_local` exists in the current session.
- [ ] Preserve full-capability/LocalSystem design.
- [ ] Chocolatey only; reject WinGet.
- [ ] No OpenAI Platform/API-key requirement for Talvora core. Keep the optional Business tunnel runtime credential isolated to the tunnel transport.
- [ ] Confirm the 32-tool manifest is still current.
- [ ] Preserve direct MCP smoke + real LocalSystem installer smoke for every behavior change.
- [ ] Do not reopen the completed filesystem-mutation, environment-variable, Event Log query, or ChatGPT Business tunnel tasks unless fixing a discovered defect.
- [ ] For a new subsystem, keep the change bounded and use RED -> GREEN.
- [ ] Final CI on canonical HEAD.
- [ ] Keep historical branch refs aligned.
- [ ] No open PRs unless intentionally created.

This handoff is intended to let the next session continue without relying on hidden prior-chat context.
