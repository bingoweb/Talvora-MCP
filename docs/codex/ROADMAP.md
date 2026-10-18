# Talvora — Codex Roadmap (task cards)

> Source: in-depth review of 2026-10-18 by Claude at HEAD `8b1a611` (`feat: add optional Claude Code engine with user-session jobs`).
> Inspected: all of `src/`, `tests/`, `scripts/`, CI, docs, the **installed** runtime (`C:\Program Files\Talvora`, service `Talvora`, `127.0.0.1:7676`), live `tools/list` on all three surfaces, ProgramData ACLs, tray logs, the latest GitHub Actions run, and the Control Center render.
> Every card is **one Codex task and one commit**. Order = `docs/codex/STATUS.md` → "Agent queue" (same order as this file).
> Card template: **Why** (finding + evidence) · **Files** · **Do** · **Don't** · **Acceptance**.
> Language: this file is English. Everything Talvora shows to people (Control Center, tray, desktop worklog, error messages returned to users) is **ordinary Turkish** (AGENTS.md). Codex reports to the user in **Turkish**.
> The Control Center redesign has its own spec: `docs/codex/DESIGN-CONTROL-CENTER.md` (cards `UI1`–`UI8` below).

---

## Review summary (starting point, HEAD `8b1a611`)

| Area | Finding |
|---|---|
| Build / CI | **HEAD is red.** `dotnet restore Talvora.slnx --locked-mode` fails with `NU1004` (implicit `Microsoft.NET.ILLink.Tasks` moved 10.0.12 → 10.0.13 with SDK 10.0.402). GitHub run `39028784145` failed at the restore step; reproduced locally. HANDOFF admits the installer was built with an env override `RuntimeFrameworkVersion=10.0.12`. |
| Test coverage in CI | `tests/Talvora.SourceEdit.Regression` (the most important suite) is **never run in CI**. 25 of 36 `tests/*.ps1` scripts are not referenced by CI at all. ~850 assertions are regexes over source text ("SourceRegression"), which pin code shape instead of behaviour and will block the UI redesign. |
| MCP transport security | Loopback HTTP endpoint with **LocalSystem** authority, **no authentication**, **no Host validation**: a request with `Host: rebind.evil.example` gets HTTP 200 (verified). This is the classic DNS-rebinding exposure the MCP Streamable HTTP spec requires servers to prevent by validating `Origin`. Any local process of any user (and any web page that wins a DNS-rebinding race) can call `talvora_run_process` as SYSTEM. Cross-origin `text/plain` is rejected (415) and preflight fails (405), so plain CSRF is blocked — rebinding is not. |
| ProgramData ACL | `C:\ProgramData\Talvora` (and every subfolder: `Secrets`, `Toolchains`, `Jobs`, `SourceEdit`, `Tools`, …) inherits `BUILTIN\Users:(CI)(WD,AD)` — standard users can **create** files/folders where SYSTEM later reads journals, executes toolchains (`ast-grep`) and stores secrets. The installer sets no ACL anywhere (`grep SetAccessControl src/Talvora.Installer` → 0). |
| SYSTEM in user-controlled paths | `InteractiveUserProcessRunner` (SYSTEM) creates, writes and **recursively deletes** under the user's `%LOCALAPPDATA%\Talvora\InteractiveRuns` (path comes from the user's own environment block); stale-run cleanup recursively deletes anything older than 24 h there with no reparse-point check. A junction there turns SYSTEM into an arbitrary-delete primitive. A reparse-safe deleter already exists in `TalvoraOwnedTempCleanup` but is not used. |
| Supply chain | `AstGrepToolchainManager` downloads the **latest** `@ast-grep/cli` from npm at runtime and runs it as SYSTEM; integrity comes from the same registry metadata (transport check only, no pin). |
| Secrets | The OpenAI **Admin** API key entered during setup is persisted forever (`%LOCALAPPDATA%\Talvora\ControlCenter\openai-admin-key.dpapi`), never deleted after provisioning. |
| Knowledge tools | `search` scans **every local user's** Documents/Desktop/Downloads/OneDrive as SYSTEM; cloud-only OneDrive placeholders are read (→ hydration/downloads). `fetch` accepts **any absolute path** and is annotated read-only/closed-world, so ChatGPT may call it without confirmation (exfiltration path via prompt injection). |
| Delivery gate | `GitTools.IsPush` stops at the first non-dash token, so `git -C <dir> push` or `git -c k=v push` bypasses the "fail-closed" Context7/Awwwards gate. |
| Agent jobs | Codex/Claude background jobs are in memory only; a service restart (every install!) orphans the user-session process, which keeps editing files with no way to see or cancel it. No concurrency limit, no per-workspace lock (two agents can edit the same tree). ~300 duplicated lines between `CodexBackgroundJobTools` and `ClaudeBackgroundJobTools`. |
| Prompt transport | Claude prompts (allowed up to 100 000 chars) are passed on the command line → `CreateProcess` fails above 32 767 chars, and prompts are visible in the process command line (contradicts the prompt-privacy hardening done elsewhere). |
| Reset script | `scripts/Reset-And-Install.ps1` silently deletes `%ProgramData%\Talvora` (which today holds the user's own SSH deploy keys and API tokens in `Secrets\`, backups, Penpot data), `%LOCALAPPDATA%\Talvora` (tunnel credentials) and **recursively** every file/folder under Desktop/Start Menu whose name contains "Talvora". No confirmation, no backup. |
| Tool surface | Live: **249** tools on `/mcp`, **212** on `/mcp/dev`, **88** on `/mcp/admin`; `tools/list` on `/mcp` is **302 KB** (~75 k tokens) — every client pays this. 36 thin `*_run` wrappers + 32 `*_info` tools; 18 config get/set/delete tools; 18 Stitch tools; "legacy compatibility" mutators still exposed (AGENTS.md forbids legacy aliases). 243/249 tools have no `title`. README claims 236/199 in one place and 234/197 in another. |
| Versioning | `/healthz` says `3.0.0-dev` (hard-coded), MCP `serverInfo.version` says `1.0.0.0`, assemblies have no version. |
| Recovery noise | `tray.log` writes a full stack trace for every automatic-recovery attempt; Dev/Admin tunnels were "repaired" repeatedly when the upstream OpenAI control plane was degraded (not a local fault). |
| Desktop IPC | Service connects to the tray pipe without verifying the server process and with the default impersonation level; notifications only target the console session (`WTSGetActiveConsoleSessionId`), not RDP sessions. |
| Control Center | Visual smoke **fails on the installed build** (`WhiteRatio=68.2%` — the "not blank" heuristic was written for the old dark UI). XAML merges WPF-UI `Theme="Light"` while startup applies `ApplicationTheme.Dark` (mixed control templates). UI is built imperatively in C# (77 font-size literals, no view/viewmodel split). Hover text on primary buttons is `#0D1118` on `#455DD5` (fails contrast). Window title "Talvora Yönetim Merkezi" vs. UI "Kontrol Merkezi" vs. "Control Center". Setup banner shows jargon ("Runtime API key ve tunnel-client"). |
| Docs | `BUG-AUDIT.md` 268 KB, `HANDOFF.md` 139 KB, `MCP-CONTROL-CENTER-TODO.md` 98 KB — enormous token cost, mostly history. `docs/Codex-Autonomy.md` is Turkish without diacritics. README's Claude section is appended after the closing tagline. |
| Context7 gate scope | `Validate-Context7Gate.ps1` ignores `*.xaml`, `tests/*.ps1` and installer manifests — the whole UI redesign would pass without evidence. |
| Desktop worklog | Live card on the installed build: failed step keeps the present-tense start title under a red badge, no reason, history repeats the title, raw tool name shown; sequential calls create a new window per step so the session story and the code terminal disappear within seconds; code previews are built from request arguments (shown even when the edit was rejected) and missing for structural/semantic/config/text edits and agent jobs; Codex cards lack Turkish diacritics; Claude jobs flap to "waiting". Details: `docs/codex/DESIGN-WORKLOG.md`. |
| Tool errors | Agents only get `An error occurred invoking '<tool>'.` for most failures (351 plain exceptions vs 20 `McpException`) → no self-correction (Q6). |
| Source-edit tool on own repo | HANDOFF 2026-10-17: `talvora_apply_patch` "rejected the workspace directory sharing identity with Win32 32" on this repository; the agent fell back to local patching. The primary editor does not work on Talvora itself. |

---

## General rules for every card

- AGENTS.md applies in full (Context7 gate, modern-by-default, delivery gates, desktop progress contract, `talvora_apply_patch` as primary editor).
- **Test first** (red), then code. Prefer **behavioural** tests. Do **not** add new regex-over-source assertions; when you touch a file covered by a `*SourceRegression.ps1`, replace the affected assertions with behaviour where feasible, otherwise update them in the same commit and say so in the Log.
- **Definition of done** (every card):
  1. `dotnet restore Talvora.slnx --locked-mode` and the installer project restore pass **without** env overrides.
  2. `dotnet build Talvora.slnx -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors`).
  3. Targeted tests for the card + the always-on set (`scripts/Invoke-QualityGate.ps1`, introduced in K1).
  4. Runtime/packaging/shared-infrastructure cards: canonical installer (`scripts/Deploy-Windows-Installer.ps1`) + exact-installed smoke; record the installed `sourceCommit` from `/healthz`.
  5. `.context7/verification.json` refreshed when the gate requires it; Awwwards evidence for UI cards.
  6. `docs/codex/STATUS.md` updated (queue tick + one Log entry, English), one commit with trailer `Codex-Card: <ID>`; mirror the evidence to the active Gitea issue.
- User-facing strings: ordinary Turkish, no tool names / exit codes / stack traces / transport terms. Errors say what went wrong, what is protected, what happens next. Example: `"Bu istek Talvora'ya yerel bir uygulamadan gelmediği için reddedildi. Sistem güvende; bağlantınızı Talvora ayarlarından yeniden yapın."`
- Tool **descriptions** stay English (model-facing). Tool **titles** (human-facing in client approval dialogs) are short Turkish.
- Never print, copy or commit secrets. Never read files in `C:\ProgramData\Talvora\Secrets` content; listing names and ACLs is enough.
- Do not push unless STATUS "User queue" says the user authorized pushes for this phase.

---

## Phase K — Build health and working conditions (do first)

### K0 — Make locked restore green again and stop SDK drift

**Why:** HEAD does not restore in locked mode (NU1004 `Microsoft.NET.ILLink.Tasks` 10.0.12 → 10.0.13). CI run `39028784145` is red; HANDOFF used `RuntimeFrameworkVersion=10.0.12` as a workaround. `global.json` uses `rollForward: latestFeature`, so any SDK patch on a runner or the workstation changes implicit packages and breaks the lock.

**Files:** `global.json`, `src/**/packages.lock.json`, `tests/**/packages.lock.json`, `src/Talvora.Installer/packages.lock.json`, `.github/workflows/windows-ci.yml`, `.github/dependabot.yml`, `scripts/Build-Windows-Installer.ps1` (only if it sets the override), `docs/MODERNIZATION-POLICY.md`.

**Do:**
- Query Context7 (`/dotnet/docs`, `/nuget/docs` or the vendor page) for the current guidance on lock files + implicit platform packages (ILLink/Crossgen) and `global.json` roll-forward.
- Set `global.json` to `"version": "10.0.402"` (the SDK installed on the workstation — verify with `dotnet --list-sdks`) and `"rollForward": "disable"`, `"allowPrerelease": false`. Regenerate every lock file with `dotnet restore --force-evaluate` (solution + installer project) and commit them together. If 10.0.402 is not the newest stable SDK installed, stop with a proposal.
- Remove any `RuntimeFrameworkVersion` override from scripts.
- Dependabot `dotnet-sdk` updates must regenerate lock files in the same PR: add a CI step that fails with a clear message when `global.json` changed but lock files did not.

**Don't:** disable `RestoreLockedMode`; pin `ILLink.Tasks` manually; downgrade packages.

**Acceptance:** clean clone → locked restore + Release build pass locally and in CI with no env overrides; CI green on the commit; Log records old/new SDK and the roll-forward decision with the Context7 ID.

### K1 — One quality gate script, all suites in CI, quiet output

**Why:** `Talvora.SourceEdit.Regression` is not in CI; 25 `tests/*.ps1` are orphaned; there is no single local command that matches CI, and full output wastes Codex tokens.

**Files:** new `scripts/Invoke-QualityGate.ps1`, `.github/workflows/windows-ci.yml`, orphaned `tests/*.ps1` (run, fix or delete), new `scripts/Get-CodexContext.ps1`, `tests/` (new test for both scripts).

**Do:**
- `scripts/Invoke-QualityGate.ps1 [-Scope Fast|Full] [-Verbose]`: locked restore → Release build → PowerShell parse → WinGet ban → Context7/Delivery gate validators → every behavioural suite (`Talvora.SourceEdit.Regression`, `Talvora.Notification.Regression`, `Talvora.Smoke --codex-integration-policy-only`, all `tests/*.ps1` that still apply) → prints one summary line `N kontrol, 0 hata` and, as the **very last line**, `Hepsi geçti.` or `Kontrol başarısız: <adım>`. Default output: one line per step; on failure re-print only the failing step's output. `-Verbose` = today's full output.
- CI calls the same script (`-Scope Full`) instead of duplicating steps; keep the installer/installed-runtime steps after it.
- For each orphaned script: if it still passes, add it to the gate; if it fails because the contract is obsolete, delete it and say why in the Log; if it reveals a real bug, open a D-card in STATUS instead of fixing it here.
- `scripts/Get-CodexContext.ps1 [-Card <ID>]` prints: STATUS up to `## Log` + newest Log entry; the card text (default: first unchecked queue item) from this ROADMAP until the next `### `; and the card's `**Files:**` list with current line counts. (Token economy — read this instead of the whole ROADMAP.)

**Don't:** weaken any existing assertion; make CI slower than 25 minutes (raise `timeout-minutes` only if needed, with reason).

**Acceptance:** `pwsh scripts/Invoke-QualityGate.ps1` ends with `Hepsi geçti.` on a clean tree; deliberately breaking a source-edit regression makes it end with `Kontrol başarısız: SourceEdit`; CI runs the SourceEdit suite (visible in the log).

### K2 — Make `talvora_apply_patch` work on the Talvora repository itself

**Why:** HANDOFF 2026-10-17: both endpoints rejected this workspace with Win32 error 32 (sharing violation) on the "directory sharing identity" check, forcing a fallback editor. AGENTS.md requires `talvora_apply_patch` as the primary editor.

**Files:** `src/Talvora/SourceEditing/SourceEditPathGuard.cs`, `SourceWorkspaceClassifier.cs`, `SourceEditTransactionStore.cs` (only where the identity check lives), `tests/Talvora.SourceEdit.Regression/*`.

**Do:** reproduce with a regression that opens the workspace root with the same handle flags the guard uses while another process (e.g., `git status`, Visual Studio, Defender) holds a handle; fix the identity probe to open directories with `FILE_FLAG_BACKUP_SEMANTICS`, `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE` and no data access (`0` desired access / `FILE_READ_ATTRIBUTES`), then compare `FILE_ID_INFO` (volume serial + 128-bit file id). Retry a sharing violation at most 3× with 50/100/200 ms before failing with a Turkish-safe message.

**Don't:** remove the identity check; follow reparse points.

**Acceptance:** regression reproduces Win32 32 before the fix and passes after; live: `talvora_read_source` + `talvora_apply_patch` on a scratch file in this repo succeed through the installed service.

---

## Phase S — Security (highest product risk)

### S1 — Reject foreign Host and Origin on every HTTP endpoint (DNS-rebinding fix)

**Why:** verified: `POST /mcp` with `Host: rebind.evil.example:7676` → 200. MCP Streamable HTTP spec: servers MUST validate `Origin` to prevent DNS rebinding; loopback binding alone is not enough. Authority behind the endpoint is LocalSystem.

**Files:** `src/Talvora/Program.cs`, new `src/Talvora/LoopbackRequestGuard.cs`, `tests/Talvora.Smoke/SmokeScenarios.PrivacySecurity.cs` (or a new behavioural test project/scenario), `SECURITY.md`.

**Do:**
- Context7: `/modelcontextprotocol/csharp-sdk` (does 2.2.0 ship an Origin/Host validation option? use it if so) and `/dotnet/aspnetcore.docs` (Host filtering).
- Host allow-list: `localhost`, `127.0.0.1`, `[::1]`. Use the built-in host filtering exactly like this (if Context7 shows it does not apply to minimal hosting as written, stop with a proposal — do not write a custom Host middleware):
  ```csharp
  builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
  // WebApplication adds HostFiltering automatically when AllowedHosts is set; verify with Context7 and a test.
  ```
- Origin guard middleware registered **before** `MapMcp` and before the Penpot/health endpoints:
  ```csharp
  internal static class LoopbackRequestGuard
  {
      private static readonly HashSet<string> AllowedOriginHosts =
          new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "[::1]" };

      public static WebApplication UseLoopbackOriginGuard(this WebApplication app)
      {
          app.Use(async (context, next) =>
          {
              var origin = context.Request.Headers.Origin.ToString();
              if (origin.Length > 0 && !IsLoopbackOrigin(origin))
              {
                  context.Response.StatusCode = StatusCodes.Status403Forbidden;
                  await context.Response.WriteAsJsonAsync(new
                  {
                      error = "Bu istek yerel bir Talvora istemcisinden gelmediği için reddedildi.",
                  });
                  return;
              }
              await next(context);
          });
          return app;
      }

      // Uri.Host keeps the brackets for IPv6 ("[::1]"). Any loopback port is local by definition.
      private static bool IsLoopbackOrigin(string origin) =>
          Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
          (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
          AllowedOriginHosts.Contains(uri.Host);
  }
  ```
  Implement the middleware as written (Turkish error text exactly as shown). The Penpot plugin is loaded by the Penpot web app: determine its real origin from `PenpotAiPlugin/manifest.json`, `PenpotAiPluginEndpoints.cs` and the local Penpot URL, and allow that single origin **only** for the static plugin `GET` routes, never for `/mcp*`. If the origin cannot be determined with evidence, stop with a proposal.
- Requests without `Origin` (CLI clients, tunnel-client) stay allowed — S2 adds authentication for them.
- Behavioural tests against a real Kestrel instance (`WebApplicationFactory` or the existing live smoke): foreign Host → 400, foreign Origin → 403, loopback Host without Origin → 200, `Origin: http://127.0.0.1:7676` → 200, Penpot plugin origin → 200 for plugin files.

**Don't:** bind to anything but loopback; add CORS for `/mcp`; break the tunnel-client (it dials `http://127.0.0.1:7676/mcp`).

**Acceptance:** tests above green; installed runtime re-probed with the same PowerShell probe used in the review (foreign Host now rejected); ChatGPT Business Dev/Admin tunnels still report ready in Control Center.

### S2 — Authenticate local MCP callers — **WON'T DO (user decision Q1, 2026-10-18)**

> The user does not want token handling for a personal, single-user workstation. Do **not** implement this card. S1 (Host/Origin validation) stays because it is invisible to every legitimate client and closes the remote DNS-rebinding path. Kept below only as a record of the reviewed option.

**Why (reviewed option):** without authentication any local process (including a standard user or malware in the user session) gets SYSTEM through the MCP endpoint. Loopback is not an identity.

**Files:** `src/Talvora/Program.cs`, new `src/Talvora/LocalClientAuthentication.cs`, `src/Talvora.Installer/InstallerEngine.*.cs` (token provisioning), `scripts/Install.ps1`, client registration code (`CodexMcpConfiguration.cs`, the Claude/Codex client config writers), `src/Talvora.Tray/ManagedMcpTunnelProvisioningService*.cs`, smoke tests, `SECURITY.md`, README.

**Do (after the user answers Q1):**
- Context7: MCP authorization spec + `/modelcontextprotocol/csharp-sdk`; Codex CLI config for HTTP MCP servers (bearer token / headers); Claude Code `.mcp.json` headers; **openai/tunnel-client**: can it send a header or bearer token to the local MCP target? Record the answers.
- Token: 256-bit random, stored at `%ProgramData%\Talvora\Secrets\mcp-local-token` with an explicit ACL (SYSTEM, Administrators full; the interactive user who installed: read). Rotation command in the Control Center ("Bağlantı anahtarını yenile").
- Middleware: constant-time compare of `Authorization: Bearer <token>`; `/healthz` stays anonymous but returns only product/version/status (no PID, user, SID — move those to an authenticated `/healthz/details`).
- Write the token reference into the client configs Talvora already manages (Codex `config.toml` table `talvora_local`, tunnel profiles) using env-var references, never the literal value, where the client supports it.
- If tunnel-client cannot send a header: **stop**, record the evidence in STATUS "Open questions", and leave S2 at `[~]`. Do not invent a bypass.

**Don't:** log the token; put it in URLs; weaken S1.

**Acceptance:** unauthenticated `tools/list` → 401 with Turkish JSON error; Codex, Claude Code (if configured) and both tunnels keep working; token never appears in logs (search `C:\ProgramData\Talvora`, `%LOCALAPPDATA%\Talvora` logs for the token value in a test).

### S3 — Harden `%ProgramData%\Talvora` ACL and move interactive runs out of user-controlled paths

**Why:** `BUILTIN\Users:(CI)(WD,AD)` inherited on every subfolder (verified with `icacls`). SYSTEM reads/executes from there (journals, toolchains, tools) and stores secrets. `InteractiveUserProcessRunner` writes/deletes as SYSTEM under the user's `%LOCALAPPDATA%` (user-controlled, junction-able) with a recursive 24 h cleanup and no reparse check.

**Files:** `src/Talvora.Installer/InstallerEngine.*.cs` (new `InstallerEngine.Acl.cs`), `src/Talvora.Shared/InteractiveUserProcessRunner.cs`, `src/Talvora.Shared/TalvoraOwnedTempCleanup.cs` (reuse), `src/Talvora/Program.cs` (startup self-check), tests.

**Do:**
1. Inventory every path under `%ProgramData%\Talvora` that a **user-context** component (Tray, interactive runner, user scripts) must read or write: grep `CommonApplicationData` in `src/Talvora.Tray`, `src/Talvora.Shared`, `scripts/`. Write the table into the Log.
2. Installer (and a service startup self-check that only logs) applies a protected ACL on the root:
   ```csharp
   var sec = new DirectorySecurity();
   sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
   var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
   sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
       FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
   sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
       FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
   // Users: ReadAndExecute ONLY on the subfolders the inventory proves are needed; never Write/Append.
   new DirectoryInfo(root).SetAccessControl(sec);
   ```
   Explicit per-file ACEs that already exist (e.g., the user's read on a token in `Secrets`) must be preserved — test this on a fixture folder.
3. Interactive runs: SYSTEM creates `%ProgramData%\Talvora\InteractiveRuns\<guid>` with a protected ACL = SYSTEM full + **only the target user SID** modify; the runner never touches the user's `%LOCALAPPDATA%` again. Cleanup and per-run deletion go through `TalvoraOwnedTempCleanup`'s reparse-safe deletion (never follow junctions). Reject the run root if it is a reparse point.
4. Startup self-check: if the root ACL still grants Users write, log one warning and show one Turkish Control Center notice ("Talvora klasör izinleri güncel değil. Kurulumu yeniden çalıştırın.").

**Don't:** take ownership of user files; change ACLs outside `%ProgramData%\Talvora`; delete anything in `Secrets`.

**Acceptance:** after install `icacls C:\ProgramData\Talvora` shows no Users write/append; a fixture with a junction inside the runs root is not followed (test); Codex/Claude `*_info` and an `exec` still work from the installed service; tray features still read what they need.

### S4 — Pin the ast-grep toolchain (no runtime "latest")

**Why:** the SYSTEM service downloads and executes whatever npm calls `latest`. Integrity = same metadata source.

**Files:** `src/Talvora/SourceEditing/AstGrepToolchainManager.cs`, new `src/Talvora/SourceEditing/AstGrepToolchainPin.cs` (or a JSON manifest under `src/Talvora/`), `tests/InstallerAstGrepMetadataRegression.ps1` → behavioural test, `.github/dependabot.yml` (if a supported ecosystem can track it; otherwise a documented manual bump).

**Do:** store `version`, npm `integrity` (sha512) and the extracted `ast-grep.exe` SHA-256 in source; download only that exact tarball; verify both hashes before first execution and before every reuse from cache; refuse cached binaries whose hash differs. Updating = a reviewed commit (Context7 for release notes).

**Don't:** fetch `/latest`; execute before verification.

**Acceptance:** tests with a tampered cached binary and a tampered tarball both fail closed with a clear message; structural edit regression still passes.

### S5 — Do not keep the OpenAI Admin API key

**Why:** the org-level Admin key is persisted indefinitely after setup (`ControlCenterAdminCredentialStore.Save`, no delete path in `ControlCenterSetupService`).

**Files:** `src/Talvora.Tray/ControlCenterSetupService.cs`, `ControlCenterAdminCredentialStore.cs`, `ControlCenterWindow.Setup.cs`, tests.

**Do:** keep the Admin key only in memory for the provisioning call; zero the buffer afterwards; on upgrade, if `openai-admin-key.dpapi` exists, delete it once and add a Control Center event "Yönetici anahtarı artık saklanmıyor; yalnızca kurulum sırasında kullanılıyor." If a later operation truly needs it, prompt again.

**Don't:** touch the runtime credential (`*runtime*` DPAPI file).

**Acceptance:** after provisioning no admin DPAPI file exists; upgrade test removes an existing one; tunnel provisioning still works end-to-end (user step: re-enter key once if prompted).

### S6 — Knowledge tools: scoped `fetch`, no cloud hydration, only the interactive user's folders

**Why:** `fetch` reads any absolute path as SYSTEM and is marked read-only/closed-world (no confirmation in ChatGPT); `search` scans all users' folders and reads OneDrive cloud-only placeholders (hydration).

**Files:** `src/Talvora/Tools/KnowledgeTools.cs`, `tests/KnowledgeSearchScanRegression.ps1`, `tests/SmokeKnowledgeRootsIsolationRegression.ps1`, smoke scenario.

**Do:**
- Default corpus = the **active interactive user's** Documents/Desktop/Downloads/OneDrive + Public Documents + `%ProgramData%\Talvora\Knowledge` (not the whole ProgramData folder). `TALVORA_KNOWLEDGE_ROOTS` still overrides.
- `fetch` accepts only IDs whose canonical path (after `Path.GetFullPath` and final-path resolution) is under a configured root; otherwise a Turkish error suggesting `talvora_read_text_range` (which remains the general tool).
- Skip files with any of `FileAttributes.Offline`, `RECALL_ON_OPEN (0x00040000)`, `RECALL_ON_DATA_ACCESS (0x00400000)` and cloud reparse tags:
  ```csharp
  const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
  const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
  static bool IsCloudOnly(FileAttributes a) =>
      (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
  ```

**Don't:** narrow `talvora_read_text*` or other filesystem tools (they are the general capability by design).

**Acceptance:** tests: path outside roots rejected; placeholder attributes skipped (use a fixture with attributes set via `File.SetAttributes` where possible, or an injectable attribute provider); another local profile not scanned by default.

### S7 — Desktop IPC: verify the tray end of the pipe, support the real interactive session

**Why:** the service opens `Talvora.DesktopProgress.<session>` without checking who serves it (pipe-squatting by any process in that session receives evidence diffs) and uses the default impersonation level; it only targets the console session, so RDP sessions get nothing.

**Files:** `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `src/Talvora.Shared/WindowsSessionLauncher.Sessions.cs` (reuse its active-session logic), `tests/Talvora.Notification.Regression`.

**Do:** construct the client with `TokenImpersonationLevel.Identification`; after connect call `GetNamedPipeServerProcessId` (LibraryImport) and accept only a process whose image path is the installed `Talvora.Tray.exe` under `C:\Program Files\Talvora\` **and** whose session id matches; otherwise drop the message and log at Debug. Resolve the target session with the same logic as `WindowsSessionLauncher.GetDefaultInteractiveUser()` (active WTS session, console preferred).

**Don't:** block or delay the tool call (delivery stays best-effort); change the frame protocol.

**Acceptance:** regression with a fake pipe server from another executable → message not delivered; normal tray still receives; RDP session test documented as user step if it cannot be automated.

### S8 — Fix the delivery-gate push detection (and the same parser for working-tree mutations)

**Why:** `GitTools.IsPush` returns on the first non-dash token, so `-C <dir>`, `-c k=v`, `--git-dir <d>`, `--work-tree <d>`, `--namespace <n>`, `--config-env <x>` bypass the gate. `SourceMutationPolicy.EnsureGitWorkingTreeMutationAllowed` likely shares the weakness — check it.

**Files:** `src/Talvora/Tools/GitTools.cs`, `src/Talvora/SourceEditing/SourceMutationPolicy.cs`, new `src/Talvora/Tools/GitArgumentParser.cs`, tests.

**Do:** one parser for Git global options:
```csharp
internal static class GitArgumentParser
{
    private static readonly HashSet<string> GlobalOptionsWithValue = new(StringComparer.Ordinal)
    {
        "-C", "-c", "--git-dir", "--work-tree", "--namespace", "--super-prefix", "--config-env", "--exec-path",
    };

    public static string? GetSubcommand(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (string.IsNullOrWhiteSpace(a)) continue;
            if (GlobalOptionsWithValue.Contains(a)) { i++; continue; }      // value is the next token
            if (a.StartsWith('-')) continue;                                  // flag or --opt=value
            return a;
        }
        return null;
    }
}
```
Use it in both places; verify the option list against `git help git` for the installed Git version (record it). Document honestly in `SECURITY.md` that the gate is a guard rail for structured tools; general process/PowerShell tools are not gated by design.

**Acceptance:** table-driven tests (`push`, `-C x push`, `-c a=b push`, `--git-dir=x push`, `--git-dir x push`, `status`) all classified correctly; existing gate tests unchanged.

---

## Phase R — Reliability

### R1 — Agent jobs: one registry, persisted metadata, orphan recovery, concurrency limits

**Why:** Codex and Claude jobs are in-memory `ConcurrentDictionary`s (duplicated code); a service restart loses track of a still-running user-session agent; unlimited concurrent jobs and no per-workspace exclusivity.

**Files:** new `src/Talvora/Tools/AgentJobRegistry.cs`, `CodexBackgroundJobTools.cs`, `ClaudeBackgroundJobTools.cs`, `JobTools.Persistence.cs` (reuse PID+start-time identity), `TalvoraDesktopCodexWorklog.cs`, `TalvoraDesktopClaudeWorklog.cs`, smoke scenarios `SmokeScenarios.Codex.cs` / `.Claude.cs`, README + `docs/Codex-Autonomy.md`.

**Do:**
- `AgentJobRegistry` (engine-agnostic): create/get/list/cancel, state machine `queued → running → completed|failed|timed_out|cancelled|orphaned`, `CancellationTokenSource` disposal on terminal state, prune policy, desktop worklog hooks. Both tools become thin adapters.
- Persist job metadata (no prompt text, no environment values) as JSON under `%ProgramData%\Talvora\AgentJobs\<jobId>.json` (S3 ACL): engine, workingDirectory, created/finished, state, sessionId, root PID + process start time, counters.
- On service start: for each non-terminal record, if the PID still exists **with the same start time**, mark `orphaned` (visible in `*_list`/`*_status`, cancellable by process-tree kill); else `failed` with "Talvora yeniden başlarken görev yarıda kaldı; sessionId ile devam edebilirsiniz."
- Limits: max 4 running agent jobs in total; max 1 per canonical working directory unless the caller passes `allowParallel=true` (default false). Rejections are structured errors in Turkish.

**Don't:** persist prompts or environment values; change the native CLI arguments.

**Acceptance:** smoke: start job → restart the service (test harness) → job listed as `orphaned`, cancel kills the tree; 2nd job on the same directory rejected; 5th concurrent job rejected; desktop worklog ends correctly for each terminal state.

### R2 — Send prompts through stdin, not the command line

**Why:** Claude prompts up to 100 000 chars exceed the 32 767-char Windows command line; prompts are visible to any process that can read command lines (WMI, Process Explorer, 4688 audit events).

**Files:** `src/Talvora.Shared/InteractiveUserProcessRunner.cs` (new `standardInputText` option), `ClaudeCliArguments.cs`, `CodexCliInvocationPolicy.cs`, `ClaudeCliTools.cs`, `CodexCliTools.cs`, tests.

**Do:**
- Context7 `/websites/code_claude` (headless: prompt from stdin with `--print`) and `/openai/codex` (`codex exec -` reads the prompt from stdin) — confirm both for the installed versions.
- Runner: write the prompt to `stdin.txt` inside the ACL'd run folder (S3), set `RedirectStandardInput=$true`, `StandardInputEncoding = UTF8 (no BOM)`, copy the file to stdin, close stdin, delete the file immediately after the copy.
- Arguments end with the engine's stdin marker instead of `-- <prompt>`; keep the `--` separator logic for any positional values that remain.
- Raise or keep the 100 000-char limit only if both CLIs accept it via stdin (test with 120 000 chars of UTF-8 Turkish text).

**Don't:** use a shell; leave `stdin.txt` behind after the child starts.

**Acceptance:** tests: 120 k-char Turkish prompt reaches the child intact (fake child echoing stdin hash); process command line (via `Win32_Process.CommandLine` in the test) does not contain the prompt.

### R3 — Make `Reset-And-Install.ps1` safe

**Why:** silent recursive deletion of `%ProgramData%\Talvora` (user secrets/backups/Penpot data), `%LOCALAPPDATA%\Talvora` (tunnel credentials) and any Desktop/Start-menu item whose name contains "Talvora".

**Files:** `scripts/Reset-And-Install.ps1`, README, `docs/DEVELOPMENT-ENVIRONMENT.md`, new behavioural test using a temporary fake root.

**Do:** require `-ConfirmReset` (or an interactive Turkish confirmation that names what will be removed); **preserve** by default `Secrets`, `Backups`, `Penpot*`, `ssh`, tunnel `state`/DPAPI files (opt-in `-IncludeUserData`); before deleting anything create a timestamped zip in `%ProgramData%\TalvoraBackups\` (outside the reset scope) and print its path; delete only **exact** known shortcut file names (list them), never a recursive wildcard over Desktop/Start Menu; `-WhatIf` support.

**Don't:** change the canonical install path.

**Acceptance:** test with a fake root: `-WhatIf` deletes nothing; default run keeps the preserved folders and the backup zip exists; a Desktop folder "Talvora notlarım" survives.

### R4 — Calmer recovery: upstream degradation is not a local failure; bounded logs

**Why:** `tray.log` contains one full stack trace per automatic recovery attempt (6 attempts × 2 tunnels within minutes on 2026-10-17); repair "fails" when the OpenAI control plane reports degradation — not something a local restart can fix.

**Files:** `src/Talvora.Tray/ControlCenterRepairService.cs`, `ManagedMcpTunnelHealthService.cs`, `TrayApplicationContext.cs` (`MaintainGenericManagedMcpsAsync`), `TrayLog.cs`, tests.

**Do:** classify readiness failures into `LocalFault` (process missing, local MCP unhealthy, response delivery failing locally) vs `UpstreamDegraded` (control-plane polling degraded while local MCP + tunnel process are healthy). For `UpstreamDegraded`: no restart, state "OpenAI tarafı yavaş yanıt veriyor; Talvora bekliyor", probe with backoff (1, 2, 5, 10 min). Log one stack trace per incident (first failure) and then one line per attempt; dedupe identical messages within 10 minutes.

**Acceptance:** unit tests for the classifier with recorded status JSON fixtures (sanitised); log test shows one stack trace for 6 identical failures.

### R5 — One version everywhere

**Why:** `/healthz` 3.0.0-dev (hard-coded), MCP serverInfo 1.0.0.0, assemblies unversioned.

**Files:** `Directory.Build.props`, `src/Talvora/Program.cs`, `src/Talvora/RuntimeMetadata.cs`, Tray About/version service, installer metadata.

**Do:** `<Version>` + `<InformationalVersion>$(Version)+$(SourceRevisionId)</InformationalVersion>` in `Directory.Build.props` (source commit injected by the installer build); `/healthz` and `McpServerOptions.ServerInfo` read `AssemblyInformationalVersionAttribute` (verify the SDK property name with Context7); Control Center shows the same string.

**Acceptance:** `/healthz.version` == `initialize.serverInfo.version` == Control Center version on the installed build.

---

## Phase Q — Quality, usability, surface

### Q1 — Tool metadata diet: titles, shorter descriptions, payload budget

**Why:** `tools/list` on `/mcp` is 302 KB (~75 k tokens) and 265 KB on `/mcp/dev`; 243/249 tools lack `title`; descriptions average 206 chars with repeated policy prose.

**Files:** all `[McpServerTool]` attributes in `src/Talvora/Tools/*.cs`, `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs`, `src/Talvora/McpToolMetadataWirePolicy.cs`, smoke `SmokeScenarios.MetadataPolicy.cs`.

**Order rule:** while Q2 is not `[x]` in STATUS, treat Q1 as blocked (skip it like a `[?]` item) and take the next card; come back to Q1 right after Q2 is done (titles for tools that Q2 removes would be wasted).

**Do:** Titles are user-visible copy, so Claude decides them: Codex first generates `docs/codex/proposals/tool-titles.md` — a table `tool name | one-line English purpose | proposed Turkish title (≤ 4 words, verb-first, e.g. "Kaynak dosya oku")` for every tool on `/mcp` — adds proposal P-n "Q1 tool titles", marks Q1 `[?]` and stops. After Claude approves (edits the table), apply the approved titles verbatim. Descriptions: ≤ 220 chars, English, first sentence = what it does, second = when to use another tool; move repeated policy text into the server instructions once. Add a smoke assertion: `tools/list` JSON for `/mcp/dev` ≤ 120 KB.

**Acceptance:** smoke budget test green; no tool without title; annotations unchanged.

### Q2 — Consolidate the tool surface (approved by the user 2026-10-18: "only if no feature is lost")

**Why:** 36 `*_run` pass-through wrappers + 32 `*_info` probes + 18 config-format CRUD tools + legacy text mutators (`talvora_write_text`, `talvora_append_text`, `talvora_replace_text`) described by the server itself as "compatibility surfaces" — AGENTS.md forbids legacy aliases. Large surfaces measurably hurt tool selection.

**Files:** `src/Talvora/Tools/*`, `src/Talvora.Shared/TalvoraToolManifest.cs`, `TalvoraMcpToolSurfacePolicy.cs`, `SourceEditRoutingContract.cs`, smoke surface tests, README/ARCHITECTURE counts.

**Hard rule (user condition):** zero capability loss. Before removing any tool, add a behavioural test that exercises the same capability through the replacement (same inputs → same effect/output fields). If any capability cannot be mapped 1:1, keep that tool and record why in the Log.

**Do, as three commits Q2a/Q2b/Q2c:**
- **Q2a** `talvora_toolchain_info {tool}` and `talvora_toolchain_run {tool, arguments[], workingDirectory, environment, timeoutSeconds}` replace the single-binary info/run wrappers whose implementation is "resolve executable + run argv" (go, cargo, rustc, rustup, java, javac, maven, gradle, dart, flutter, adb, emulator, android_cli, cmake, ninja, msbuild, signtool, makeappx, makepri, rc, mt, bun, pnpm, yarn, pip, python, gh, modal, choco). Keep dedicated tools that add real structure (dotnet build/test with diagnostics, npm scripts, docker ps/images JSON, git status/diff/log).
- **Q2b** `talvora_config_get|set|delete {path, pointer, format?}` (format auto-detected from extension: json/yaml/toml/xml/ini/dotenv; XML keeps XPath via `pointer` = XPath when format is xml) replace the 18 format-specific tools; keep shared code.
- **Q2c** remove the legacy text mutators from Dev/Full; source-workspace edits go through `talvora_apply_patch`, non-workspace writes through `talvora_write_bytes` / a single `talvora_write_file {path, text, encoding, mode=overwrite|append}`.
- Target after Q2: Full ≤ 150, Dev ≤ 115, Admin ≤ 70, Dev `tools/list` ≤ 120 KB. README counts generated/validated by a test from the manifest (no hand-typed numbers).
- After deploying: refresh ChatGPT Business connector tool lists (user step) and Codex config; list the renamed tools in the Log.

**Don't:** remove any capability (every removed tool must map to a replacement in a table in the Log).

**Acceptance:** surface smoke updated; mapping table complete; live installed `tools/list` counts match README.

### Q6 — Tell the calling agent what went wrong (opaque tool errors)

**Why:** verified live: calling `talvora_search_text` with wrong argument names returns only `An error occurred invoking 'talvora_search_text'.` (`isError: true`). The MCP SDK hides messages of non-`McpException` exceptions; Talvora throws 351 plain exceptions vs 20 `McpException`s, so Codex/ChatGPT cannot self-correct and retries blindly.

**Files:** `src/Talvora/Program.cs` (call-tool filter), new `src/Talvora/ToolErrorTranslator.cs`, smoke scenario, `SECURITY.md` (what is exposed).

**Do:** Context7 `/modelcontextprotocol/csharp-sdk` for the current error-handling contract. In the existing `AddCallToolFilter`, catch exceptions from the tool and return `CallToolResult { IsError = true }` with one English, model-facing, **redacted** (`FileLog.RedactSensitiveData`) line: type category + message, e.g. `InvalidArguments: Missing required argument 'root'. Expected: root, query.` For argument-binding failures list required parameters from the tool's input schema. Map: `ArgumentException` family → `InvalidArguments`; `FileNotFound`/`DirectoryNotFound`/`KeyNotFound` → `NotFound`; `UnauthorizedAccess` → `AccessDenied`; `TimeoutException`/`OperationCanceled` (not client cancel) → `Timeout`; `IOException` → `IO`; others → `Failed` + exception type name only (no stack trace). Client-initiated cancellation still propagates as cancellation. The desktop worklog classifier (DESIGN-WORKLOG §5.3) reuses the same categories.

**Don't:** expose stack traces, environment values or prompt text; change tools' success payloads.

**Acceptance:** smoke: wrong argument names → message names the missing parameter; nonexistent path → `NotFound: …`; a secret-looking value inside an exception message is redacted; cancellation still reported as cancellation.

### Q3 — Documentation diet and correctness

**Why:** 500+ KB of living history in the root costs every agent session; README contradicts itself on counts; Turkish doc without diacritics.

**Files:** `HANDOFF.md`, `BUG-AUDIT.md`, `MCP-CONTROL-CENTER-TODO.md`, `docs/TALVORA-AUDIT-2026-10-09-*.md`, README, `docs/Codex-Autonomy.md`, new `docs/archive/`.

**Do:** move the three big files and the 2026-10-09 audit to `docs/archive/` unchanged (git mv); replace `HANDOFF.md` with a ≤ 150-line "current state" page pointing to `docs/codex/STATUS.md`; README: counts from the manifest (validated by Q2's test), move "Optional Claude Code engine" under Integrations, remove machine-specific paths from examples (`C:\\path\\to\\repo`); rewrite `docs/Codex-Autonomy.md` with correct Turkish diacritics — same sentences and meaning, no added or removed content.

**Acceptance:** root markdown total < 120 KB; links still resolve (simple link checker in the quality gate).

### Q4 — Context7 gate covers UI and test scripts

**Why:** `Validate-Context7Gate.ps1` patterns skip `*.xaml`, `tests/*.ps1`, `src/**/*.json` manifests, `src/Talvora/PenpotAiPlugin/**`.

**Files:** `scripts/Validate-Context7Gate.ps1`, its regression.

**Do:** add `^src/.+\.(xaml|json|js|html)$`, `^tests/.+\.ps1$`; keep docs-only changes out of scope.

**Acceptance:** regression shows a `.xaml`-only change requires evidence.

### Q5 — Replace the broken Control Center visual smoke heuristic

**Why:** installed build: `Control Center render looks blank/light. WhiteRatio=%68.2` — the light Stitch design fails a heuristic written for the old dark UI; the smoke can never pass.

**Files:** `src/Talvora.Tray/ControlCenterSmoke.cs`, CI/local gate.

**Do:** replace pixel-ratio checks with structural checks (UI Automation: rail items, header status, at least one service row, no element with zero size) plus a non-uniformity check (pixel variance > threshold) that works for light and dark themes; always write the PNG when `TALVORA_STITCH_PREVIEW_PNG` is set (rename the variable to `TALVORA_CONTROL_CENTER_PREVIEW_PNG`, keep no alias), and render both themes when asked.

**Acceptance:** `Talvora.Tray.exe --control-center-smoke` exits 0 on the installed build in both themes and writes two PNGs.

---

## Phase E — Token economy (Talvora's cost to every agent)

Measured live on 2026-10-18 (installed build, `/mcp/dev`):

| Measurement | Value |
|---|---|
| Codex registration | `~/.codex/config.toml` → `[mcp_servers.talvora_local] url = "http://127.0.0.1:7676/mcp"` → every Codex session receives **249 tool schemas ≈ 302 KB (~75 k tokens)** before doing anything. |
| Result duplication | **Every** tool result carries the same JSON twice: a `text` content block and `structuredContent` (git_status 2 314 + 2 309 chars, git_log 13 799 + 13 549, find_files 20 729 + 20 729). |
| `talvora_git_status` | `entries` are raw porcelain-v2 lines with two 40-char object ids each, **and** the whole porcelain output is repeated in `raw`. |
| `talvora_git_log` | default 50 commits with full hash, author name, author e-mail, date, subject → 34 KB per call. |
| `talvora_find_files` | one 60-file folder → 52 KB. |
| `talvora_search_text` | defaults `maxMatches=500`, `maxLineChars=4000`. |

Zero-capability-loss rule (user decision Q2) applies: every reduced default stays reachable through an explicit parameter.

### E1 — Codex sees only the Talvora tools it uses

**Why:** ~75 k tokens of tool schemas per Codex session; Codex uses its own shell for everything except Talvora's source-edit family, PowerShell-as-SYSTEM (installer deploy) and the gated `git push`.

**Files:** `src/Talvora.Shared/CodexMcpConfiguration.cs` (the code that writes Talvora's Codex registration), `scripts/Install.ps1`/installer step that calls it, `tests/` (behavioural test on a temp `config.toml`), `README.md` (one line).

**Do:**
- Context7 `/openai/codex`: confirm the `config.toml` keys for a streamable-HTTP MCP server's tool allow-list (`enabled_tools`) for the installed Codex version. If the key does not exist, stop with a proposal.
- Write the registration exactly as:
  ```toml
  [mcp_servers.talvora_local]
  url = "http://127.0.0.1:7676/mcp/dev"
  enabled_tools = [
    "talvora_source_edit_guide", "talvora_read_source", "talvora_apply_patch",
    "talvora_apply_edits", "talvora_structural_edit", "talvora_semantic_edit",
    "talvora_run_powershell", "talvora_git_run",
  ]
  ```
  Preserve every other key/table in the user's `config.toml` byte-for-byte (existing TOML round-trip code; test with a fixture that contains other servers and comments). `talvora_run_powershell` must exist on `/mcp/dev` (it is a shared tool today — verify; if not, stop with a proposal).
- Apply the change to this workstation via the installer path (no manual edit).

**Don't:** change other MCP servers or Codex settings; remove the registration.

**Acceptance:** fixture test; on the workstation `codex mcp list` (or the documented equivalent) shows `talvora_local` with 8 tools; a Codex session can still read + patch a scratch file and run the installer deploy; Log records the measured schema size Codex receives (≈ 8 tools).

### E2 — Stop sending every result twice

**Why:** text block + `structuredContent` with identical JSON doubles the result tokens for clients that forward both.

**Files:** `src/Talvora/Program.cs` (call-tool filter), new `src/Talvora/ToolResultTextPolicy.cs`, smoke scenario.

**Do (deterministic decision, record evidence in the Log):**
1. Context7 + vendor docs: for **Codex CLI**, **ChatGPT custom MCP connectors** and **Claude Code**, find whether the model receives `structuredContent`, the text block, or both, when a result has both.
2. If **all three** use `structuredContent` when present → replace the text block of results that have `structuredContent` with a one-line text: `"Structured result: see structuredContent."` (keep `isError` results' text unchanged).
3. If **any** of them reads only the text block → keep both and do nothing else in this card except recording the evidence (the card is then `[x]` with "no change, evidence recorded").
4. Never touch results without `structuredContent`.

**Acceptance:** evidence table in the Log (client → field used, source); if changed: smoke asserts the one-line text and unchanged `structuredContent`; Codex and ChatGPT Dev still work end-to-end (one call each, logged).

### E3 — Lean defaults for the high-frequency tools

**Why:** see the measurement table. These tools are called many times per session.

**Files:** `src/Talvora/Tools/GitTools.cs`, `DeveloperTools.FileSearch.cs`, `DeveloperTools.Files.cs` (find/list), `ProcessTools.cs`, `PowerShellTools.cs`, `BuildRunnerTools.Dotnet.cs`, smoke scenarios for each, README tool notes.

**Do (exact new defaults; the old behaviour stays reachable via the named parameter):**

| Tool | New default | Opt-in to old/full output |
|---|---|---|
| `talvora_git_status` | `entries` = parsed objects `{ path, origPath?, index, worktree }` (XY codes from porcelain v2; no object ids); no `raw` | `includeRaw=true` |
| `talvora_git_log` | `maxCommits=20`; commit = `{ shortCommit, date (yyyy-MM-dd HH:mm), subject }` | `maxCommits=<n>`, `detail="full"` (today's fields) |
| `talvora_find_files` | `maxResults=200`; entry = `{ path (relative to root), size }` | `detail="full"` (today's fields), `maxResults=0` |
| `talvora_list` | entry = `{ name, type, size }` | `detail="full"` |
| `talvora_search_text` | `maxMatches=100`, `maxLineChars=300` | explicit values (0 = server maximum, as today) |
| `talvora_run_process` / `talvora_run_powershell` | captured stdout/stderr returned as **head 2 000 + tail 14 000 chars** with a marker line `… {n} characters omitted …` between them (model-facing text, English) when longer; totals in `standardOutputTotalCharacters` | `maxOutputCharacters=<n>` |
| `talvora_dotnet_build` / `talvora_dotnet_test` | add `summary` = `{ errors, warnings, testsPassed?, testsFailed?, failedTests[≤10], diagnostics[≤20] {file,line,code,message} }`; `standardOutput` limited like run_process | `fullOutput=true` |

Descriptions of these tools mention the opt-in parameter in ≤ 1 short sentence.

**Don't:** change semantics of any other field; remove fields from `detail="full"`.

**Acceptance:** smoke per tool asserting the new default shape and the opt-in full shape; measured sizes logged before/after on this repo (target: git_status ≤ 1 KB for ≤ 10 entries, git_log default ≤ 3 KB, find_files on `src/Talvora/Tools` ≤ 8 KB).

### E4 — Worklog frames stay small (covered by N3b)

No separate work: N3b's `CodeChangeId` rule (heartbeats ≤ 4 KB) is the token/IPC economy for the desktop worklog. Listed here so the audit checks it.

### E5 — Context helper and gate output are minimal (extends K1)

**Why:** K1 introduced `Get-CodexContext.ps1` and `Invoke-QualityGate.ps1`; the TOKEN ECONOMY section of AGENTS.md relies on them.

**Files:** `scripts/Get-CodexContext.ps1`, `scripts/Invoke-QualityGate.ps1`, their tests.

**Do:**
- `Get-CodexContext.ps1`: support all three card files (ROADMAP `### K0 — …`, DESIGN-WORKLOG `### N1 — …`, DESIGN-CONTROL-CENTER `### UI1 — …`); for a UI/N card also print the spec sections it cites (`§3.1`, `§5.4.1` …) and nothing else; print STATUS up to `## Log` + newest entry; print pending `QUEUE-ADDITIONS.md` entries; print open proposals' titles from `PROPOSALS.md`. Total output target ≤ 250 lines for any card.
- `Invoke-QualityGate.ps1`: default output = one line per step (`✓ Derleme 12 sn`), on failure only the failing step's last 60 lines; final two lines `N kontrol, 0 hata` and `Hepsi geçti.`/`Kontrol başarısız: <adım>`.

**Acceptance:** tests with fixture docs; output line counts asserted (≤ 250 for the largest current card; ≤ 30 lines for a green gate run).

## Phase N — Desktop worklog redesign ("Seyir Defteri")

Full review (defects W1–W19, code-terminal defects C1–C7, live screenshot), information model, copy deck, visual spec and cards: **`docs/codex/DESIGN-WORKLOG.md`**. Order: N1 → N2 → N3 → N3b → N4 → N5 → N6 → N7 (needs UI2 shell) → N8 (needs UI6). The vertical "code terminal" expansion is the user's favourite feature: every N-card must keep it working, and N3b/N5 make it exact (only applied changes), persistent for the session and terminal-like.

## Phase UI — Control Center redesign ("Gözlemevi")

Specification, tokens, wireframes and sample XAML: **`docs/codex/DESIGN-CONTROL-CENTER.md`**. Every UI card: Awwwards evidence (`.talvora/awwwards-verification.json`) with the concrete references you reviewed, Context7 for WPF-UI and WPF APIs used, before/after PNGs of both themes attached to the Gitea issue, and the smoke from Q5 green.

| Card | Scope |
|---|---|
| **UI1** | Design tokens (Gece/Gün), theme manager (follow Windows, manual override), fix Light/Dark WPF-UI mismatch, Mica on Windows 11, typography + spacing resources; no layout change yet. |
| **UI2** | Shell: XAML `ShellWindow` with navigation rail, header (breadcrumb, Nabız status pill, command palette `Ctrl+K`), content host; view/viewmodel split for the shell; keyboard map. |
| **UI3** | Overview: Nabız ring, plain-language status sentence, "Bağlantı zinciri" topology strip, metric tiles from real data, compact recent events. |
| **UI4** | Services: list rows + service detail page (header actions, per-component chain, tabs Durum / Olaylar / Teknik kanıt). |
| **UI5** | Events timeline + raw-log evidence surface (mono, redacted, filter chips). |
| **UI6** | Setup & settings: stepper card in plain Turkish, technical details in an expander; admin-key flow per S5; "Bağlantı anahtarını yenile" (S2). |
| **UI7** | Tray icon set + tray menu, aligned colours for the desktop worklog card (identity kept), About/version (R5). |
| **UI8** | Polish & proof: motion, reduced-motion, high contrast, 125/150/200 % DPI, accessibility audit, delete dead imperative UI code and obsolete UI source-regex tests, README screenshot refresh. |

Card details (Files / Do / Don't / Acceptance) are in the design spec, section "Implementation cards".

---

## Audit fixes (external audits by Claude)

_Empty. When the user asks Claude to check Codex's work, Claude appends an audit table here with D-cards (D1, D2, …). D-cards go to the top of the agent queue._
