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
  6. `docs/codex/STATUS.md` updated (queue tick + one Log entry, English), one commit with trailer `Card: <ID>`; mirror the evidence to the active Gitea issue.
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
- Origin guard middleware registered **before** `MapMcp` and before the health endpoints. **Exception (decision P-2):** the four static Penpot plugin `GET` routes in `PenpotAiPluginEndpoints.cs` are exempt from the Origin guard and keep `Access-Control-Allow-Origin: *`; host filtering still applies to them. Acceptance addition: plugin `GET` with `Origin: https://design.example` → 200; `POST /mcp` with the same Origin → 403.
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

> **Amended by P-12 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

> **Amended by P-13 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

> **Amended by P-14 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

> **Amended by P-15 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

**Order rule:** while Q2 is not `[x]` in STATUS, treat Q1 as blocked (skip it like a `[?]` item) and take the next card; come back to Q1 right after Q2 is done (titles for tools that Q2 removes would be wasted). **Amended by P-32 (Claude, 2026-10-24):** Q2b is withdrawn, so Q2 is done when Q2c is `[x]`. Q1 also deletes from `src/Talvora.Shared/TalvoraToolTitles.cs` every title whose tool no longer exists (today: `talvora_bun_info`, `talvora_cmake_info`, `talvora_cmake_run`, `talvora_ninja_info`, `talvora_ninja_run`, `talvora_pnpm_info`, `talvora_yarn_info`, plus whatever Q2c removes) and adds to `Program.N9Subjects.cs` the check `TalvoraToolTitles.All.Keys` ⊆ `TalvoraToolManifest.Names` (no orphan titles).

**Do:** Titles are user-visible copy and **Claude has decided them** (2026-10-23): `src/Talvora.Shared/TalvoraToolTitles.cs` (added by N9, extended by N10/RM1) is the approved table. Set every tool's protocol `title` from it — in the existing list-tools wire policy, not by editing 250 attributes; where an attribute already has a `Title`, the table value wins. No proposal table. Tools that Q2 removes simply drop out of the table in the same Q2 card. Descriptions: ≤ 220 chars, English, first sentence = what it does, second = when to use another tool; move repeated policy text into the server instructions once. Add a smoke assertion: `tools/list` JSON for `/mcp/dev` ≤ 120 KB.

**Acceptance:** smoke budget test green; no tool without title; annotations unchanged.

### Q2 — Consolidate the tool surface (approved by the user 2026-10-18: "only if no feature is lost")

> **Amended by P-16 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

### Q2a — Toolchain info/run consolidation (split of Q2, decision P-16)

Implement only the **Q2a** bullet of card Q2 above, with Q2's hard rule (zero capability loss, behavioural test per removed tool before removal), its Don't list, and its mapping table in the Log. Acceptance: Q2 Acceptance restricted to the Q2a tools + installed `tools/list` counts recorded. One commit.

### Q2b — Config get/set/delete consolidation (split of Q2, decision P-16)

> **Withdrawn by P-32 (Claude, 2026-10-24).** The 18 format-specific config tools stay unchanged: INI/dotenv/XML need their native inputs and results, and merging them would add new public behaviour or lose options. Do not implement. The orphan-title clean-up that was added here moves to Q1.

### Q2c — Remove legacy text mutators; manifest-driven counts (split of Q2, decision P-16)

Implement only the **Q2c** bullet of card Q2 above, same rules as Q2a, plus the README/ARCHITECTURE count test generated from the manifest. User step U4 follows. One commit.

> **Amended by P-34 (Claude, 2026-10-24):** no numeric surface ceilings in Q2c — the Q2 targets (Full ≤ 150, Dev ≤ 115, Admin ≤ 70) are void after P-32. Remove each of `talvora_write_text`, `talvora_append_text`, `talvora_replace_text` only where its behaviour maps 1:1 to `talvora_write_file {path, text, encoding, mode=overwrite|append}` (new) or an existing tool, with Q2's behavioural test per removed tool; keep any that does not map and say why in the Log. Acceptance = mapping table, manifest-driven README/ARCHITECTURE count test, installed `tools/list` counts equal README. The Dev `tools/list` ≤ 120 KB budget belongs to Q1 only.

### Q6c — Translate tool exceptions into redacted, categorised errors (decision P-9)

**Files:** `src/Talvora/Program.cs` (tool registration), new `src/Talvora/RedactingToolFunction.cs` (`DelegatingAIFunction` subclass), new `src/Talvora/ToolErrorTranslator.cs`, `tests/Talvora.Smoke/*` (catalog snapshot + error scenarios), new `tests/Talvora.Smoke/Snapshots/tools-list-{full,dev,admin}.json`.

**Do:** exactly the mechanism proven in P-9: wrap every assembly tool's `AIFunction` in `RedactingToolFunction` (override `InvokeCoreAsync`; catch per the original Q6 category table; throw `McpException("<Category>: " + FileLog.RedactSensitiveData(message))`; rethrow `OperationCanceledException` when the request token is cancelled) and register it with `McpServerTool.Create(AIFunction, McpServerToolCreateOptions)` carrying the attribute's name, title, description and annotations. **First** commit nothing: capture the three `tools/list` snapshots from the current build, then implement, then compare byte-for-byte.

**Don't:** change any tool's metadata; partial rollout (if one tool cannot be byte-identical, stop with a proposal).

**Acceptance:** byte-identical snapshots for all three surfaces; smoke: missing file → `NotFound: …`, bad argument → `InvalidArguments: …`, secret-looking text in a message redacted, client cancel → cancellation; full gate green as the signed-in user.

### Q6 — Tell the calling agent what went wrong (opaque tool errors)

> **Amended by P-3, P-8 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

> **Re-scoped by decision P-3 (2026-10-18).** Implement only the schema-validation part described in PROPOSALS.md → P-3 (Q6), and the research card Q6b. The original text below is kept as background; where it conflicts with P-3, P-3 wins.

**Why:** verified live: calling `talvora_search_text` with wrong argument names returns only `An error occurred invoking 'talvora_search_text'.` (`isError: true`). The MCP SDK hides messages of non-`McpException` exceptions; Talvora throws 351 plain exceptions vs 20 `McpException`s, so Codex/ChatGPT cannot self-correct and retries blindly.

**Files:** `src/Talvora/Program.cs` (call-tool filter), new `src/Talvora/ToolErrorTranslator.cs`, smoke scenario, `SECURITY.md` (what is exposed).

**Do:** Context7 `/modelcontextprotocol/csharp-sdk` for the current error-handling contract. In the existing `AddCallToolFilter`, catch exceptions from the tool and return `CallToolResult { IsError = true }` with one English, model-facing, **redacted** (`FileLog.RedactSensitiveData`) line: type category + message, e.g. `InvalidArguments: Missing required argument 'root'. Expected: root, query.` For argument-binding failures list required parameters from the tool's input schema. Map: `ArgumentException` family → `InvalidArguments`; `FileNotFound`/`DirectoryNotFound`/`KeyNotFound` → `NotFound`; `UnauthorizedAccess` → `AccessDenied`; `TimeoutException`/`OperationCanceled` (not client cancel) → `Timeout`; `IOException` → `IO`; others → `Failed` + exception type name only (no stack trace). Client-initiated cancellation still propagates as cancellation. The desktop worklog classifier (DESIGN-WORKLOG §5.3) reuses the same categories.

**Don't:** expose stack traces, environment values or prompt text; change tools' success payloads.

**Acceptance:** smoke: wrong argument names → message names the missing parameter; nonexistent path → `NotFound: …`; a secret-looking value inside an exception message is redacted; cancellation still reported as cancellation.

#### Exact implementation (scope = decision P-3/P-8) — copy this, do not reinterpret

API names verified by Claude in the installed ModelContextProtocol 2.2.0 XML docs: `McpServer.ServerOptions`, `McpServerOptions.ToolCollection`, `McpServerPrimitiveCollection<T>.TryGetPrimitive(string, out T)`.

Step 1 — new file `src/Talvora/ToolArgumentValidator.cs`, exactly:

```csharp
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Talvora;

internal static class ToolArgumentValidator
{
    private const int MaximumListedProperties = 12;

    internal static CallToolResult? TryCreateError(
        RequestContext<CallToolRequestParams> request)
    {
        var name = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(name) ||
            request.Server?.ServerOptions?.ToolCollection is not { } tools ||
            !tools.TryGetPrimitive(name, out var tool))
        {
            return null; // Unknown tool: keep the SDK's own answer.
        }

        var message = Validate(tool.ProtocolTool.InputSchema, request.Params!.Arguments);
        return message is null
            ? null
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = message }],
            };
    }

    internal static string? Validate(
        JsonElement schema,
        IDictionary<string, JsonElement>? arguments)
    {
        var args = arguments ?? new Dictionary<string, JsonElement>();
        var properties = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var p) &&
            p.ValueKind == JsonValueKind.Object
                ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var required = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("required", out var r) &&
            r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToArray()
                : [];
        var allowsAdditional = schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("additionalProperties", out var ap) &&
            ap.ValueKind is not JsonValueKind.False;

        var missing = required.Where(n => !args.ContainsKey(n)).ToArray();
        var unknown = allowsAdditional
            ? []
            : args.Keys.Where(k => !properties.ContainsKey(k)).ToArray();
        var wrongType = args
            .Where(a => properties.TryGetValue(a.Key, out var ps) && !TypeMatches(ps, a.Value))
            .Select(a => $"'{a.Key}' (expected {TypeLabel(properties[a.Key])})")
            .ToArray();

        if (missing.Length == 0 && unknown.Length == 0 && wrongType.Length == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (missing.Length > 0) parts.Add("missing " + string.Join(", ", missing.Select(m => $"'{m}'")));
        if (unknown.Length > 0) parts.Add("unknown " + string.Join(", ", unknown.Select(u => $"'{u}'")));
        if (wrongType.Length > 0) parts.Add("wrong type " + string.Join(", ", wrongType));

        var expected = properties.Keys
            .OrderBy(k => Array.IndexOf(required, k) is var i && i >= 0 ? i : int.MaxValue)
            .Take(MaximumListedProperties)
            .Select(k => $"{k} ({TypeLabel(properties[k])}{(required.Contains(k) ? ", required" : string.Empty)})");
        var more = properties.Count > MaximumListedProperties ? ", …" : string.Empty;

        return $"InvalidArguments: {string.Join("; ", parts)}. Expected: {string.Join(", ", expected)}{more}";
    }

    private static IReadOnlyList<string> Types(JsonElement propertySchema) =>
        !propertySchema.TryGetProperty("type", out var type)
            ? []
            : type.ValueKind switch
            {
                JsonValueKind.String => [type.GetString()!],
                JsonValueKind.Array => type.EnumerateArray()
                    .Where(t => t.ValueKind == JsonValueKind.String)
                    .Select(t => t.GetString()!)
                    .ToArray(),
                _ => [],
            };

    private static string TypeLabel(JsonElement propertySchema)
    {
        var types = Types(propertySchema).Where(t => t != "null").ToArray();
        return types.Length == 0 ? "any" : string.Join("|", types);
    }

    private static bool TypeMatches(JsonElement propertySchema, JsonElement value)
    {
        var types = Types(propertySchema);
        if (types.Count == 0)
        {
            return true;
        }

        return types.Any(t => t switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        });
    }
}
```

Step 2 — `src/Talvora/Program.cs`, inside `filters.AddCallToolFilter(next => async (request, cancellationToken) => { … })`, insert as the **first** statements of the lambda (before `var notifier = …`), so no desktop card is created for invalid calls (P-8):

```csharp
if (ToolArgumentValidator.TryCreateError(request) is { } invalidArguments)
{
    return invalidArguments;
}
```

Step 3 — new smoke scenario `tests/Talvora.Smoke/SmokeScenarios.ArgumentValidation.cs` (registered in `tests/Talvora.Smoke/Program.cs` like the other scenarios; link `ToolArgumentValidator.cs` into the smoke project the same way as `ToolResultTextPolicy.cs` if the scenario is in-process):
- unit part: a schema `{"type":"object","properties":{"root":{"type":"string"},"query":{"type":"string"},"regex":{"type":"boolean"}},"required":["root","query"]}` with arguments `{"path":"x","pattern":"y"}` must give exactly
  `InvalidArguments: missing 'root', 'query'; unknown 'path', 'pattern'. Expected: root (string, required), query (string, required), regex (boolean)`;
- `{"root":"x","query":"y","regex":"yes"}` → message contains `wrong type 'regex' (expected boolean)`;
- valid arguments → `null`;
- live part (installed service, `/mcp/dev`): `talvora_search_text {"path":"x","pattern":"y"}` → `isError: true` and the text starts with `InvalidArguments: missing 'root', 'query'`.

Step 4 — deploy; run the live part; Log the exact returned line.

### Q3 — Documentation diet and correctness

> **Amended by P-17 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

> **Amended by P-18 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

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

## Phase RM — Animations with Remotion (user request 2026-10-23)

> User: "Talvora'ya Remotion eklensin; animasyon istediğimde otomatik devreye girsin — nasıl olacağını sen düşün, ChatGPT eklesin, sen denetle."

**Design (Claude).** Remotion (https://www.remotion.dev, v4.0.538, React → video) runs entirely as the **signed-in user**, never as LocalSystem: it downloads a 120 MB Chrome Headless Shell into the user profile, uses the user's Node/npm cache and must open the result in the user's player.

| Piece | What it does |
|---|---|
| `talvora_remotion_guide` (read-only) | The playbook every agent follows: 6-step workflow, authoring rules (React/remotion APIs, determinism, `staticFile`, Turkish fonts, 1920×1080@30 default), default projects folder `%USERPROFILE%\Videos\Talvora`, license note. |
| `talvora_remotion_run` | Starts **one** step detached as the user — `create` (`npx create-video@4.0.538 --yes --blank <name>`), `compositions`, `still` (PNG), `render` (mp4 = h264, webm = vp9, gif) — and returns a `runId` at once. Never blocks the caller (lesson of P-26). One running step per project. Props go to a JSON file (Remotion cannot take inline JSON on Windows shells). |
| runner (`Invoke-TalvoraRemotion.ps1`, written by the service into `%LOCALAPPDATA%\Talvora\Remotion`) | Runs `npx`, mirrors every line into `runs\<runId>.log`, writes `runs\<runId>.json` (`running` → `succeeded`/`failed`), opens the finished file when `openWhenDone`. |
| `talvora_remotion_status` | Reads those files: state, real frame counter (`Rendered/Encoded n/m`), output path and size, composition ids, the compiler error block (`diagnostics`, stack frames removed) and the last log lines. |
| desktop worklog | The run gets one live card: "Animasyon oluşturuluyor · TalvoraIntro" with `45/90 kare`, then "Animasyon hazır · TalvoraIntro-…mp4 · 2.3 MB" or a plain failure reason. The tool calls themselves stay silent. |
| automatic routing | `RemotionTools.ServerInstructions` is appended to the MCP server instructions every client receives: any request for an animation, motion graphic, intro/logo, explainer/promo video or GIF (also in Turkish) → call `talvora_remotion_guide` first. The guide's tool description repeats the trigger words. |

**Verified by Claude on this machine (2026-10-23, as `MSI	ayla`)** with the exact runner script: `create` 83 s → project with `src/index.ts`/`Root.tsx`; a Turkish intro composition written per the guide; `compositions` 43 s (first run downloads Headless Shell) → `["TalvoraIntro"]`; `render` 10 s → 1920×1080 MP4 222 kB; `still` frame 100 → PNG with correct `çğıöşü`; a deliberately broken `Root.tsx` → `failed` + `diagnostics` starting with `Error: × Module build failed … Syntax Error`. The replayed card on a clean clone: build 0 errors, regression GREEN, full gate (Log).

License: free for individuals, non-profits and companies up to 3 employees (Remotion LICENSE.md); the guide states it.

### RM1 — Remotion animations: guide, detached runs, live card, automatic routing

> After N10 (uses `WorklogSubject`, the N10 session fields and the title table).

**Files:** new `src/Talvora/Tools/RemotionTools.cs`, new `src/Talvora/TalvoraDesktopRemotionWorklog.cs`, `src/Talvora/Program.cs`, `src/Talvora/WorklogCopy.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `src/Talvora/TalvoraDesktopWorklogState.cs`, tool manifest / title table / metadata (+1 open-world) and surface counts, the two source-regression scripts, smoke surface/metadata scenarios, `README.md`, new `tests/Talvora.Notification.Regression/Program.RemotionWorklog.cs`, `tests/Talvora.Notification.Regression/Program.cs`.

#### Exact implementation — copy this, do not reinterpret

Context7 first: `/remotion-dev/remotion` (CLI `render`/`still`/`compositions`, `create-video --yes --blank`, `--props` file on Windows); record it in `.context7/verification.json`. Do not change the pinned version `4.0.538` (a later DEP card updates it).

Step 1 — copy `docs/codex/reference/remotion/RemotionTools.cs` → `src/Talvora/Tools/RemotionTools.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `ef6ca148e756b2801c806de013fe3128bcb3d0a8a8fd5730de2c96adfcd35834`).

Step 2 — copy `docs/codex/reference/remotion/TalvoraDesktopRemotionWorklog.cs` → `src/Talvora/TalvoraDesktopRemotionWorklog.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `8b0ed2bd79f5daa930ae952c98f0479430d81f3de2cb6772537a24b6c4cf2e27`).

Step 3 — `src/Talvora/Program.cs` — replace

```csharp
        options.ServerInstructions =
            SourceEditRoutingContract.ServerInstructions;
```

with

```csharp
        options.ServerInstructions =
            SourceEditRoutingContract.ServerInstructions + " " +
            RemotionTools.ServerInstructions;
```

Step 4 — `src/Talvora/Program.cs` — replace

```csharp
CodexBackgroundJobTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
```

with

```csharp
CodexBackgroundJobTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
RemotionTools.DesktopNotifier =
    app.Services.GetRequiredService<TalvoraDesktopProgressNotifier>();
```

Step 5 — `src/Talvora/WorklogCopy.cs` — replace

```csharp
    internal static readonly WorklogTitles Generic =
```

with

```csharp
    internal static readonly WorklogTitles RemotionCreate = new("Animasyon projesi hazırlanıyor", "Animasyon projesi hazır", "Animasyon projesi hazırlanamadı");
    internal static readonly WorklogTitles RemotionCheck = new("Animasyon denetleniyor", "Animasyon denetlendi", "Animasyon denetlenemedi");
    internal static readonly WorklogTitles RemotionStill = new("Önizleme karesi hazırlanıyor", "Önizleme karesi hazır", "Önizleme karesi hazırlanamadı");
    internal static readonly WorklogTitles RemotionRender = new("Animasyon oluşturuluyor", "Animasyon hazır", "Animasyon oluşturulamadı");
    internal static readonly WorklogTitles Generic =
```

Step 6 — `src/Talvora/TalvoraDesktopProgressNotifier.cs` — replace

```csharp
            "worklog_note" => false,
```

with

```csharp
            "worklog_note" => false,
            "remotion_guide" => false,
            "remotion_run" => false,
            "remotion_status" => false,
```

The background watch (RM2 part of this card) reports the run; the tool calls stay silent so the user sees one card, not two.

Step 7 — `src/Talvora/TalvoraDesktopWorklogState.cs` — replace

```csharp
        public IReadOnlyList<WorklogFileChange>? Changes { get; set; }
    }
```

with

```csharp
        public IReadOnlyList<WorklogFileChange>? Changes { get; set; }
        /// <summary>Real progress counter of a running background step ("45/90 kare"); RM1 only.</summary>
        public string? ProgressFact { get; set; }
    }
```

Step 8 — `src/Talvora/TalvoraDesktopWorklogState.cs` — replace

```csharp
        session.Purpose = purpose;
        session.Changes = changes;
```

with

```csharp
        session.Purpose = purpose;
        session.Changes = changes;
        session.ProgressFact = null;
```

Step 9 — `src/Talvora/TalvoraDesktopWorklogState.cs` — replace

```csharp
                    ToWorklogState(kind), title, isTerminal ? evidence?.Summary : null, outcome,
```

with

```csharp
                    ToWorklogState(kind), title, isTerminal ? evidence?.Summary : session.ProgressFact, outcome,
```

Step 10 — `src/Talvora.Shared/TalvoraToolManifest.cs` — replace

```csharp
        "talvora_worklog_note",
```

with

```csharp
        "talvora_worklog_note",
        "talvora_remotion_guide",
        "talvora_remotion_run",
        "talvora_remotion_status",
```

Step 11 — `src/Talvora.Shared/TalvoraToolTitles.cs` — replace

```csharp
        ["talvora_registry_create_key"] =
```

with

```csharp
        ["talvora_remotion_guide"] = "Animasyon rehberini getir",
        ["talvora_remotion_run"] = "Animasyon adımı başlat",
        ["talvora_remotion_status"] = "Animasyon durumunu oku",
        ["talvora_registry_create_key"] =
```

Step 12 — `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs` — replace

```csharp
public const int ExpectedReviewedToolCount = 252;
```

with

```csharp
public const int ExpectedReviewedToolCount = 255;
```

Step 13 — `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs` — replace

```csharp
public const int ExpectedOpenWorldToolCount = 85;
```

with

```csharp
public const int ExpectedOpenWorldToolCount = 86;
```

Step 14 — `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs` — replace

```csharp
            "talvora_python_venv_create",
            "talvora_run_powershell",
```

with

```csharp
            "talvora_python_venv_create",
            "talvora_remotion_run",
            "talvora_run_powershell",
```

Step 15 — `src/Talvora.Shared/TalvoraMcpToolSurfacePolicy.cs` — replace

```csharp
    public const int ExpectedFullToolCount = 252;
    public const int ExpectedDevelopmentToolCount = 215;
```

with

```csharp
    public const int ExpectedFullToolCount = 255;
    public const int ExpectedDevelopmentToolCount = 218;
```

Step 16 — `tests/CodexCliIntegrationSourceRegression.ps1` — replace

```powershell
ExpectedFullToolCount = 252') -and
```

with

```powershell
ExpectedFullToolCount = 255') -and
```

Step 17 — `tests/CodexCliIntegrationSourceRegression.ps1` — replace

```powershell
ExpectedDevelopmentToolCount = 215') -and
```

with

```powershell
ExpectedDevelopmentToolCount = 218') -and
```

Step 18 — `tests/FocusedMcpSurfaceSourceRegression.ps1` — replace

```powershell
ExpectedFullToolCount = 252') -and
```

with

```powershell
ExpectedFullToolCount = 255') -and
```

Step 19 — `tests/FocusedMcpSurfaceSourceRegression.ps1` — replace

```powershell
ExpectedDevelopmentToolCount = 215') -and
```

with

```powershell
ExpectedDevelopmentToolCount = 218') -and
```

Step 20 — `tests/Talvora.Smoke/SmokeScenarios.SurfacePolicy.cs` — replace

```csharp
        AssertSurface(
            "talvora_worklog_note",
            development: true,
            administration: false);
```

with

```csharp
        AssertSurface(
            "talvora_worklog_note",
            development: true,
            administration: false);
        foreach (var remotionTool in new[] { "talvora_remotion_guide", "talvora_remotion_run", "talvora_remotion_status" })
        {
            AssertSurface(remotionTool, development: true, administration: false);
        }
```

Step 21 — `tests/Talvora.Smoke/SmokeScenarios.MetadataPolicy.cs` — replace

```csharp
        AssertWorldScope("talvora_user_run", true);
```

with

```csharp
        AssertWorldScope("talvora_user_run", true);
        AssertWorldScope("talvora_remotion_run", true);
        AssertWorldScope("talvora_remotion_guide", false);
        AssertWorldScope("talvora_remotion_status", false);
```

Step 22 — `README.md` — replace

```markdown
- **Desktop worklog task line:**
```

with

```markdown
- **Animations with Remotion:** `talvora_remotion_guide`, `talvora_remotion_run` and `talvora_remotion_status` create Remotion projects, check compositions and render stills, MP4/WebM videos and GIFs as the signed-in user, detached, with live progress on the desktop worklog card. The server instructions route every animation request to the guide first.
- **Desktop worklog task line:**
```

Step 23 — copy `docs/codex/reference/remotion/Program.RemotionWorklog.cs` → `tests/Talvora.Notification.Regression/Program.RemotionWorklog.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `424c1e255f06e122dc08c394aa1f02c8e99167f5b2a3ae2584146f2a4b5044db`).

Step 24 — `tests/Talvora.Notification.Regression/Program.cs` — replace

```csharp
        await AssertN10ChangesAsync();
```

with

```csharp
        await AssertN10ChangesAsync();
        await AssertRemotionWorklogAsync();
```

**Don't:** run Remotion as LocalSystem or through `talvora_run_powershell`; block a tool call until a render ends; add a Remotion MCP server (deprecated upstream — the guide replaces it); install global npm packages.

**Acceptance:** `DESKTOP_PROGRESS_BEHAVIOR_GREEN` (includes the RM test); smoke surface/metadata green; Full gate `Hepsi geçti.`; installer + deploy; live through the installed service (signed-in session): `talvora_remotion_guide` → `defaultProjectsRoot` ends with `\Videos\Talvora`; `talvora_remotion_run {action:"create", name:"talvora-rm1-deneme"}` → poll status to `succeeded`; `compositions` → `["MyComp"]`; `render {compositionId:"MyComp", format:"gif", scale:0.25, openWhenDone:false}` → `succeeded`, output size > 0 (the blank template renders an empty 2 s clip); paste runIds, states and the output size into the Log; the desktop showed one card per step; GitHub CI green (10b).

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-10-25 (18) · no new card commits since `4c94a77` · scope: the uncommitted UI5 work (applied to a clean clone, not committed) · result: redaction defect fixed by Claude; UI5 amended before delivery

**UI5 (working tree, built and rendered by Claude on a clean clone of `4c94a77`).** Builds with 0 errors; both-theme smoke renders the day-grouped timeline (Bugün / Dün), chips, search, service filter, the `Ham günlüğü göster` toggle and the sunken raw-log panel per §5.4; titles, details and the raw log pass `FileLog.RedactSensitiveData`; the 2 s tail runs only while the panel is visible and the window is not minimised. Four gaps, written into the UI5 card as an amendment so the agent fixes them before committing (no D-card):
1. The empty text says "Son 24 saatte kaydedilmiş olay yok." although the view reads **7 days**, shows it while the first read is still loading, and says it when only the filters exclude everything.
2. An event without a service draws an empty service pill.
3. "Yaşam döngüsü" matches only category `operation`; the store also writes `startup` lifecycle events (8 of 88 on this PC), which then appear only under "Tümü".
4. The raw-log fixture rendered "Authorization: [REDACTED]] [REDACTED]" — see the redaction fix below.

**Redaction fixed by Claude (security, shared).** `FileLog.RedactSensitiveData` was not idempotent and cut values at the first `]`:
- re-redacting already redacted text produced `api_key=[REDACTED]]` / `Authorization: [REDACTED]] [REDACTED]` (text is redacted twice wherever the protocol and a view both redact);
- **a secret containing `]` leaked its tail**: `password=Ab]c9xyz` → `password=[REDACTED]]c9xyz`.
The named-secret value now (a) skips a value that is exactly the placeholder followed by a delimiter, and (b) keeps a `]` inside the value when more value characters follow; a closing bracket after the value stays outside (`[password=x] ok` → `[password=[REDACTED]] ok`). New gate scenario `redaction-is-idempotent` (twice = once, never `[REDACTED]]`, a value that only starts with the placeholder is still hidden, a `]` inside a secret is hidden to its end, a structural `]` is kept): RED before, GREEN after. Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` on a clean clone.

### Audit 2026-10-24 (17, system-wide) · no new card commits since `e5d4738` (UI5 still uncommitted, P-35 answered) · result: one robustness card (D22), two small fixes by Claude, UI8 amended

The user asked for a line-by-line audit ("derin derin denetle, didik didik et"). With nothing new delivered, Claude audited the running system instead: the installed runtime (`355af6d`) and its logs, secrets in the public repository and its history, build hygiene, the open findings list, and the uncommitted UI5 work.

| # | Severity | Finding | Evidence | Action |
|---|---|---|---|---|
| 1 | **MEDIUM** | `AtomicFile.PublishDurable` races when two writers publish the same file at the same moment: the existence check and the move are not one step, so the second writer fails ("Durable file publication failed"); a simultaneous `File.Replace` collides too. Hit in real life: `tray.log` 19:50 — the Control Center dashboard refresh failed writing `managed-mcp-recovery\v2-….json` while a second `Talvora.Tray.exe` (the gate's Control Center smoke) wrote the same manifest. | New regression `atomic-publish-concurrent-first-writers` (4 writers × 150 files) is RED on `e5d4738` in 3/3 runs, GREEN 8/8 with the fix. | **D22** (shared infrastructure → card with exact code) |
| 2 | LOW | "UI3 component health unavailable :: TaskCanceledException" logged 6× — a newer refresh cancels the older one, but the filter checked the *new* token. | `ShellWindow.UI3.cs` | Fixed by Claude: the refresh's own token is captured before the `try`. |
| 3 | LOW | A frameless desktop-progress message would be accepted and then throw `NullReferenceException` in `window.Update(message.WorklogFrame!, …)` (STATUS finding of N5, still open). No frameless sender exists today. | `DesktopProgressNotificationPresenter.cs` | Fixed by Claude: frameless messages are ignored after evaluation; finding resolved. |
| 4 | LOW | Control Center smoke reports overview idle CPU 7–10 % of the machine (≈ one core); §10 asks < 1 %. Not the chain animation (same at 1 fps), not rendering (same minimised, same on other pages); the installed tray with the window closed uses 0.1 %. The smoke process's own start-up work dominates the sample, so the number does not yet measure the real window. | per-thread sampling, `tray.log` | UI8 amended: measure the real window (installed tray, window open, 30 s after load) and fix the cause if it is ≥ 1 %. |
| 5 | info | 68 "Desktop progress IPC client timed out" lines in `tray.log` come from the notification regression's own listener (fake session ids) writing to the shared `TrayLog`, not from the real tray. | reproduced by running the suite | none (log noise only) |
| 6 | ok | No secret in the tree or history (`sk-proj-`, `github_pat_`, `AKIA`, private keys: only fixtures/patterns); `Directory.Build.props` keeps `TreatWarningsAsErrors`, rebuild has 0 warnings, no TODO/FIXME/HACK in `src`. UI5 (uncommitted) redacts titles, details and the raw log and stops its 2 s timer when hidden/minimised. | — | — |
| 7 | — | Findings list: N5 null frame (fixed above), P-29 ast-grep state (D17), RM1 destructive count (audit 14 counts verified), detached user process (P-26/D14), `git pull` as LocalSystem (P-24/D12) — all resolved. | STATUS | marked resolved |
| 8 | — | UI5 deletes the replaced legacy Events builder (`ControlCenterWindow.Events.cs`) although §8 says "only in UI8". | working tree | §8 amended: a card may delete the legacy builder of the screen it replaces once nothing references it. |

**Research follow-up (user: "araştır", same day).** Finding 4 traced on a scratch build that keeps the smoke window open 40 s and samples every second: the first ~7 s after load cost ≈ one core on every page (start-up work — this is what the smoke's 3 s sample measured). After that the history page idles at ~0 ms/s, but Genel bakış kept **≈ 380 ms/s** in-process and started **26 child processes in 37 s** (two `pwsh Get-ScheduledTask` and three `tunnel-client runtimes status` per 8 s refresh), because every dashboard snapshot re-ran the UI3 component probes; the chain dash animation ran at 60 fps. **Fixed by Claude:** component probes run at most once per 60 s, or at once after "Yenile" / "Sorunu onar", and the last probed components (with their real check time) stay on screen between probes; the flow animation runs at 20 fps. Measured after: **6** child processes in 37 s (no PowerShell), ≈ **180 ms/s** in-process. The rest is the legacy 8 s dashboard refresh (tunnel status + rebuild), left to UI8's measurement on the real window. Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` on a clean clone.

### D22 — Atomic publish survives a simultaneous writer

> Audit 2026-10-24 (17), finding 1. After UI5. Shared infrastructure: every JSON store (registry, recovery manifests, preferences, history) uses this path.

**Files:** `src/Talvora.Shared/AtomicFile.cs`, new `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.AtomicRace.cs` (verbatim copy), `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs` (one list entry), `.context7/verification.json` (.NET `File.Replace`, Win32 `MoveFileEx`).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `src/Talvora.Shared/AtomicFile.cs` — replace

```csharp
    private static string? PublishDurable(
        string sourcePath,
        string destinationPath,
        string? backupPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(
                sourcePath,
                destinationPath,
                backupPath,
                ignoreMetadataErrors: false);
            return backupPath;
        }

        MoveDurable(
            sourcePath,
            destinationPath,
            replaceExisting: false);
        return null;
    }
```

with

```csharp
    // Bounded retries when another writer publishes or replaces the same file at the same moment (two
    // Talvora.Tray processes, or parallel refreshes): the first attempt that wins publishes (D22).
    private const int PublishAttempts = 8;

    private static string? PublishDurable(
        string sourcePath,
        string destinationPath,
        string? backupPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Replace(
                        sourcePath,
                        destinationPath,
                        backupPath,
                        ignoreMetadataErrors: false);
                    return backupPath;
                }

                MoveDurable(
                    sourcePath,
                    destinationPath,
                    replaceExisting: false);
                return null;
            }
            catch (IOException) when (attempt < PublishAttempts && File.Exists(sourcePath))
            {
                Thread.Sleep(10 * attempt);
            }
        }
    }
```

Step 2 — copy `docs/codex/reference/d22/SourceEditRegressionRunner.AtomicRace.cs` → `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.AtomicRace.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `0037718d34bed436921431844d8b9887072d15e8b50eb510b0729d70985aff18`).

Step 3 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs` — replace

```csharp
            ("exact-patch-success", ExactPatchSuccessAsync),
```

with

```csharp
            ("exact-patch-success", ExactPatchSuccessAsync),
            ("atomic-publish-concurrent-first-writers", AtomicPublishConcurrentFirstWritersAsync),
```

**Don't:** change `MoveDurable`, `WriteDurableTextAsync`, the temp-file naming or the backup behaviour; retry anything other than `IOException`; add asynchronous delays or a lock file.

**Acceptance:** the new scenario is RED before Step 1 (run `--without-semantic` three times; at least one `FAIL atomic-publish-concurrent-first-writers`) and GREEN after in five consecutive runs; `TALVORA SOURCE EDIT CORE REGRESSION GREEN`; Full gate `Hepsi geçti.`; installer + deploy (shared runtime library); GitHub CI green.

Claude's verification (clean clone of `e5d4738` with this audit's two small fixes): RED 3/3 before, GREEN 8/8 after; Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

### Audit 2026-10-24 (16, deep) · range `audit/2026-10-24-15 (fce75d5)..355af6d` · 7 card commits · result: exact where literal; Claude fixed six small defects directly (user: "çok küçük düzeltmeleri sen yap"); P-34 and P-35 decided

Delivered: D21 `5b57960`, TI1 `d3925fb`, UI2 `f4fab29`, N7 `94e4079`, UI3 `6d12e65`, UI4 `355af6d`; proposals P-34 (Q2c) and P-35 (UI5, work left uncommitted in the working tree, not audited). Installed `355af6d`; CI green for every commit.

**Fidelity.** D21: `WorklogCardView.xaml` identical to Claude's verified clone, reference promoted, `-next` removed. TI1: `TrayIconArt.cs`, `TrayIconFactory.cs`, `Program.TrayIcon.cs` hash-identical to the reference; the four edits match the card line for line. UI2/N7/UI3/UI4 (spec cards, no literal code): reviewed on the agent's installed both-theme PNGs and on Claude's own renders — shell, rail, palette, theme switch, Nabız ring with the §9 sentence, history list/detail, services list with pills and role chips, detail with tabs and action buttons follow the spec.

**CI.** Two first attempts failed (`94e4079`, `355af6d`) with the same flake — `toolchain-q2a-zero-capability-loss: yarn info changed found` — and passed on re-run. Cause: the Q2a test reads the legacy and the new yarn probe once each; on the runner the first Corepack shim call can provision or time out, so `found` differs between the two calls.

**Fixed by Claude in this commit (small, verified on a clean clone of `355af6d`):**

| # | Where | Defect | Fix |
|---|---|---|---|
| 1 | `Controls/ChainView.xaml(.cs)` | Links were anchored at `X+66, Y+7`, but the node button grew with padding and centred its content, so every link ran ≈ 9 px above the dots and diagonals missed them. | Node = fixed 136 × 56 box, `Padding 0`, content top-centred; links anchored at `X+68, Y+7` (the dot centre). |
| 2 | same | Branch links ran through the labels "Talvora çekirdeği" / "Güvenli tüneller"; a link showed inside the hollow optional ring. | Label hugs its text on a `Tv.Surface` background (`MaxWidth 136`, padding 4); optional ring filled with `Tv.Surface`. |
| 3 | same | The §5.2 legend shows ▲ dikkat, ✕ ulaşılamıyor, ↻ onarılıyor, but every node was a dot (state by colour only, against §3 "never colour-only"). | Nodes draw the legend shapes: triangle (`Tv.Attention`), cross (`Tv.Offline`), Fluent `ArrowSync` (`Tv.Repairing`); dot otherwise, hollow when optional. Overview strip and full page both use it. |
| 4 | `ViewModels/ServiceDetailViewModel.cs` | "Ne oldu?" said "Yanıt gecikiyor. — Yanıt gecikiyor." when the detail repeats the status. | Say it once when the detail is empty or equal. |
| 5 | `Views/ServiceDetailView.xaml` | "Son kontrol 19:29:38 ·" — a dangling separator when there is no last-success time. | The separator collapses when `LastSuccess` is empty. |
| 6 | tests: `Program.N7.cs`, `SourceEditRegressionRunner.ToolchainConsolidation.cs` | P-35 (N7 300 ms bound measured one load under the parallel gate's CPU load: 309–340 ms) and the Q2a CI flake above. | N7: best of three loads, same 300 ms bound (`LoadRecent` only reads). Q2a: on a mismatch, compare one fresh back-to-back pair; any real difference still fails. |

Verification: Release build 0 errors; `DESKTOP_PROGRESS_BEHAVIOR_GREEN` (N7 113 ms, UI3/UI4 suites green), `TALVORA TOOLCHAIN Q2A GREEN`; Control Center smoke both themes exit 0 with the chain and detail renders checked by eye; Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

**Status bookkeeping.** D21 → `[x]` (installed, live capture and CI verified by the agent; the Gitea attachment was blocked only by the connector's approval policy). UI2 visual approved by Claude; only the user's mixed-DPI check U5 remains. TI1 waits for the user's look (U9).

### Audit 2026-10-24 (15, deep) · range `audit/2026-10-24-14 (830bb67)..fce75d5` · 8 card commits · result: all exact; worklog polish card D21; P-32 and P-33 decided

Delivered: D19 `b17c6f7`, D20 `fbe7542`, T1 `5877647`, T2 `b559640`, T3 `3844440`, T4 `47f8613`, UI1 `49e1421`. Stopped correctly with proposals: Q2b (P-32), UI2 (P-33).

**Fidelity.** D20 and T1–T4: every file identical to Claude's verified clones (WorklogTheme, gate, three new regressions, workflow, SourceEdit runner). D19: identical except one character — the regex `[\\/\"]` instead of the card's `[\\/"]`; in a single-quoted PowerShell string both mean the same character class (the regression proves the cases), so no fix card (LOW, noted for literalness).

**Phase T is live.** GitHub CI for the two docs-only pushes (`3c309bf`, `fce75d5`) took **21 s and 27 s** instead of 5–6 min; code pushes ≈ 4 min. Local signed-in Full ≈ 1 min 50 s, `-Scope Changed` 4–40 s.

**UI1 review (installed 49e1421).** All 17 colour tokens per theme equal §3.1/§3.2 including the P-31 `TextFaint` values; rendered surfaces sample exactly `Tv.Bg`/`Tv.Surface`/`Tv.SurfaceRaised` in both themes; WPF-UI theme follows the palette (no Light/Dark mismatch); live switch and persisted preference are proven in the smoke; layout untouched, worklog untouched. Observation (LOW): on a cold start the smoke's `*-loaded.png` was captured while the summary still said "Durum kontrol ediliyor…" (2 of 6 runs) — `RunSmokeScenarioAsync` does not wait for the first status. Added to UI8.

| # | Severity | Finding | Fix |
|---|---|---|---|
| 1 | design (Claude's own) | Worklog card: a wrapped title and a wrapped `!` notice restart at the left edge instead of hanging under their first word; the title text sits ≈ 2.7 DIP right of the answer column; the code-terminal header imitates macOS window buttons (three coloured dots) — decorative chrome that looks clickable and is foreign on Windows. | **D21** |
| 2 | — | P-32: the 18 format-specific config tools cannot be merged into three without new public behaviour or lost options. | **Q2b withdrawn** (`[-]`); the orphan-title clean-up moves to Q1 |
| 3 | — | P-33: UI2 needs content for "Çalışma günlüğü" and "Bağlantı zinciri" before N7 / the chain page exist. | P-33 decided (placeholder contract below); UI3 amended to build the §5.2 page |
| 4 | LOW | UI1 smoke loaded-snapshot race (above). | UI8 amended |

### D21 — Worklog card: hanging indents, one text edge, no imitation window buttons

> Audit 2026-10-24 (15). User request: "masaüstü bildirim kartını kozmetik profesyonellik açısından yeniden ele al". The updated view and all ten previews are in `docs/codex/reference/worklog-phosphor-next/`.

**What changes (visible):** the `>` prompt and the `!` mark each sit in a 16 DIP gutter, so a long title (`Kod değişikliği uygulanıyor · kiosk-routes.js, app.js ve 1 dosya`) and a long reason wrap under their first word, and the title text now starts on the same 16 DIP edge as `ne / nerede / fark / sonuç` and the notice; the code-terminal header loses the three red/amber/green dots and reads `diff · 14:02:11` from its left padding. Colours, sizes, copy, state line, spinner, cursor, code terminal and details panel are unchanged; high contrast unchanged.

**Files:** `src/Talvora.Tray/Worklog/WorklogCardView.xaml`, the reference promotion, `.context7/verification.json`, `.talvora/awwwards-verification.json` (`designDecisions`: "DESIGN-WORKLOG §3 Phosphor card — audit 15 polish: hanging indents, single text edge, no imitation window buttons").

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogCardView.xaml` → `src/Talvora.Tray/Worklog/WorklogCardView.xaml` (AGENTS.md rule 5 "Reference copies"; the only changed file: the title prompt and the `!` notice each get a 16 DIP gutter so wrapped lines hang at the shared text edge, and the three imitation window dots leave the code-terminal header).

Step 2 — promote the reference in the same commit: replace `docs/codex/reference/worklog-phosphor/src/WorklogCardView.xaml`, all ten `previews/*.png` and `SHA256SUMS.txt` with the files of `docs/codex/reference/worklog-phosphor-next/`, then delete that folder (`talvora_apply_patch` for text, `talvora_copy`/`talvora_delete` with `explicitAdmin: true` for PNGs). `ModernizationPolicyRegression.ps1` is green only after this step.

**Don't:** change anything else in the card; re-add decorative window chrome; change `WorklogCardView.xaml.cs` (it still finds `TitleText`, `PromptMark` and `TypingCursor` by name — `TitleText` is now the title grid, its glow effect applies to both parts).

**Acceptance:** `MODERNIZATION_POLICY_GREEN` (after Step 2); `DESKTOP_PROGRESS_BEHAVIOR_GREEN`; `Talvora.Tray.exe --worklog-preview <tmp>` → all ten PNGs byte-identical to the promoted reference; Full gate `Hepsi geçti.`; installer + deploy; live: one real `talvora_apply_patch` with a long `purpose`, capture the desktop card as the signed-in user (D18 capture command, `artifacts/d21-live.png`), attach to Gitea #9; GitHub CI green.

Claude's verification (clean clone of `fce75d5`): build 0 errors; policy green after promotion; the built tray renders 10/10 previews byte-identical to `worklog-phosphor-next`; Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

### Audit 2026-10-24 (14, deep) · range `audit/2026-10-24-13 (78fce40)..830bb67` · 6 card commits · result: TWO FIX CARDS (D19 reset safety, D20 worklog contrast), P-31 decided

Delivered: N6 `b27cee0`, R1 `7aac8ce`, R2 `ef4c959`, R3 `7cf3d33`, Q5 `6f533fe`, Q2a `830bb67` (agent marked `[~]`; Claude verified the open items: GitHub CI green, installed counts `/mcp` 250, `/mcp/dev` 213, `/mcp/admin` 88 = code). Blocked: UI1 (P-31) — decided now.

**Fidelity and deep review.**
- N6: all 17 files identical to Claude's verified clone.
- R3 (`Reset-And-Install.ps1`): `Secrets` is skipped **before** the walk enters it at any depth, reparse points are skipped, directories are removed only when empty (non-recursive) so `Secrets` and its parents survive; backup zip before any deletion; `-TestRoot` only under `%TEMP%` (P-15). Correct.
- R2: the prompt goes through `stdin.txt` inside the protected per-run folder and is deleted with `[IO.File]::Delete` right after reading (D17-safe); no task text in argv.
- R1: persisted job records hold only operational metadata (no prompt, arguments or environment); stored under the service-only `ProgramData\Talvora\AgentJobs`.
- Q5: CI and the Full gate run the installed/built Control Center smoke and check both PNGs.
- Q2a: 7 wrappers → `toolchain_info`/`toolchain_run`, each case calls the unchanged original method (lossless). Live: `toolchain_info {tool:"cmake"}` returns the same fields; the removed tool is gone.

**Live (installed 830bb67):** `talvora_user_run whoami` → `msi\tayla`; tool lists: `/mcp` 250 tools **338 KB**, `/mcp/dev` 213 tools **302 KB**, 242/250 without protocol `title` (Q1 will apply `TalvoraToolTitles` and the 120 KB budget).

| # | Severity | Where | Finding | Fix |
|---|---|---|---|---|
| 1 | **MEDIUM** (pre-existing, not R3's scope) | `scripts/Reset-And-Install.ps1` process stop | Stops **every** process whose command line contains `Talvora-MCP` or whose path contains `\Talvora-MCP\` — the user's editor, shells, Claude/ChatGPT/Codex sessions working in the repository, test runners. Test mode skips this block, so no test saw it. | **D19** |
| 2 | **MEDIUM** (Claude's design) | `Worklog/WorklogTheme.xaml` `Wl.Faint #557363` | Faint text (labels ne/nerede/fark, times, line numbers, footer) has only **3.5–3.9 : 1** on the card surfaces — below WCAG 1.4.3 (4.5 : 1, normal text). The same class as P-31. | **D20**: `#688B78` (4.86–5.41 : 1 on every surface incl. diff rows; still distinct from Muted) |
| 3 | LOW | `TalvoraToolTitles.cs` | 7 titles of tools Q2a removed remain (orphans). | Q2b amended (remove + test) |
| 4 | — | P-31 | Control Center `Tv.TextFaint` fails 4.5 : 1 on some surfaces in both themes (agent computed correctly). | P-31: Gece `#7C899E`, Gün `#5D6A81` (≥ 4.6 : 1 on all four surfaces) |

Claude verified D19 and D20 on a clean clone of `830bb67`: `ResetProcessFilterRegression.ps1` RED before D19 (`must select processes with Test-TalvoraProcess`), `RESET_PROCESS_FILTER_GREEN` after (editor, pwsh, Claude, repository test runner not matched; installed Talvora/Tray and `dotnet Talvora.dll` matched); `R3_SAFE_RESET_GREEN` unchanged; the built tray renders 10/10 previews byte-identical to the updated reference; Full gate `61 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

### D19 — Reset stops Talvora's own programs only

> Audit 2026-10-24 (14), finding 1. **Run first.**

**Files:** `scripts/Reset-And-Install.ps1`, new `tests/ResetProcessFilterRegression.ps1` (verbatim copy).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `scripts/Reset-And-Install.ps1` — replace

```powershell
function Test-ResetProtectedItem {
```

with

```powershell
# Only Talvora's own programs are stopped (audit 2026-10-24 (14)): never an editor, shell or agent whose
# command line merely mentions the repository folder.
function Test-TalvoraProcess {
    param([string] $ExecutablePath, [string] $CommandLine)
    $name = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) { '' } else { [IO.Path]::GetFileName($ExecutablePath) }
    if (@('Talvora.exe', 'Talvora.Tray.exe', 'Talvora.Gateway.exe', 'Talvora.SystemBroker.exe', 'Talvora.Service.exe') -contains $name) {
        return $true
    }
    return ($name -ieq 'dotnet.exe') -and ($CommandLine -match '(?i)[\\/"]Talvora(?:\.Tray)?\.dll\b')
}

function Test-ResetProtectedItem {
```

Step 2 — `scripts/Reset-And-Install.ps1` — replace

```powershell
    Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and (
            ([string]$_.ExecutablePath -match '(?i)\\Talvora(?:-MCP)?\\') -or
            ([string]$_.CommandLine -match '(?i)Talvora(?:\.Gateway|\.SystemBroker|\.Service|\.dll|\.exe|-MCP)')
        )
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
```

with

```powershell
    Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and
        (Test-TalvoraProcess -ExecutablePath ([string]$_.ExecutablePath) -CommandLine ([string]$_.CommandLine))
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
```

Step 3 — copy `docs/codex/reference/ci/ResetProcessFilterRegression.ps1` → `tests/ResetProcessFilterRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `d042c78b63fbd12bcfd3d3bd88f57595d4d9421a09b8360ff361a74347fe75aa`).

**Acceptance:** `RESET_PROCESS_FILTER_GREEN` (RED before Step 1), `R3_SAFE_RESET_GREEN`; Full gate `Hepsi geçti.`; GitHub CI green. Script-only: no deploy, never run a real reset.

### D20 — Worklog faint text meets 4.5 : 1

> Audit 2026-10-24 (14), finding 2. After D19. The updated theme and all previews are in `docs/codex/reference/worklog-phosphor-next/`.

**Files:** `src/Talvora.Tray/Worklog/WorklogTheme.xaml`, the reference promotion, `.context7`/`.talvora` evidence.

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogTheme.xaml` → `src/Talvora.Tray/Worklog/WorklogTheme.xaml` (AGENTS.md rule 5 "Reference copies"; the only changed file: `Wl.Faint.Color` `#557363` → `#688B78`, with its comment).

Step 2 — promote the reference in the same commit: replace `docs/codex/reference/worklog-phosphor/src/WorklogTheme.xaml`, all ten `previews/*.png` and `SHA256SUMS.txt` with the files of `docs/codex/reference/worklog-phosphor-next/`, then delete that folder (`talvora_apply_patch` for text, `talvora_copy`/`talvora_delete` with `explicitAdmin: true` for PNGs). `ModernizationPolicyRegression.ps1` is green only after this step.

**Acceptance:** `MODERNIZATION_POLICY_GREEN`; `Talvora.Tray.exe --worklog-preview <tmp>` → all ten PNGs byte-identical to the promoted reference; Full gate `Hepsi geçti.`; installer + deploy; live card capture (D18 command, `artifacts/d20-live.png`) attached to Gitea #9; GitHub CI green.

### Audit 2026-10-24 (13) · range `audit/2026-10-23-12 (b8fd17a)..78fce40` · 1 card commit · executor: ChatGPT/Codex via Talvora · result: D18 delivered exactly and verified live; P-30 decided (N6 designed)

Delivered: D18 `78fce40` (also N5's CI fix; N5 closed). Blocked: N6 (P-30) — decided now.

**Fidelity: exact.** All eleven D18 files are identical to Claude's verified clone; the reference was promoted and `worklog-phosphor-next` removed in the same commit; CI green (`78fce40`), installed `78fce40`.

**Live design check (installed 78fce40):** a real `talvora_apply_patch` with a task note and `purpose` → the card shows the code terminal with the diff, `nerede kiosk.js › pair`, the `60 → 70` word highlight, the change time in the diff header, the quiet green state line, and no repeated one-row timeline — exactly `previews/09-one-step-receipt.png`.

**P-30** was a correct stop: N6's visual design was not in the reference and the checksum guard pins the card files. Claude designed and verified it (card N6 rewritten); no finding against the agent.

### Audit 2026-10-23 (12) · range `audit/2026-10-23-10 (8dfb855)..b8fd17a` · 4 card commits · executors: ChatGPT/Codex via Talvora · result: D18 (code pane lost on the Phosphor card + design polish), CI red

Delivered: D17 `866ec2a`, N4 `e49151a`, N5 `e6a3391` + CI follow-up `b8fd17a`.

**Fidelity.** D17: every file identical to Claude's verified replay. N5: all eight Phosphor files identical to the reference (normalised hashes); the old window code was removed. The rewrite of `tests/ModernizationPolicyRegression.ps1` in `b8fd17a` translates every retired assertion to the Phosphor card and adds the eight reference-hash checks — no check was weakened. N4: CI green, no live Codex (P-28).

**CI is red** for `e6a3391` (ModernizationPolicy still asserted the removed window — the Full gate was not run before the commit; STATUS says so) and for `b8fd17a` (`Context7`: the follow-up changed a test without refreshing `.context7/verification.json`, `Validate-Context7Gate.ps1:53`). D18 refreshes the evidence and is N5's CI fix.

**Design audit on the installed card (`e6a3391`), live:** Claude set a task note and made a real `talvora_apply_patch` with `purpose` on a scratch workspace and captured the desktop. The card itself reads well — `# görev`, title with subject, ne / nerede / fark, footer — but:

| # | Severity | Where | Finding | Fix |
|---|---|---|---|---|
| 1 | **HIGH** (favourite feature) | `DesktopProgressNotificationPresenter.cs` → `window.Update(message.WorklogFrame!)`; `TalvoraDesktopWorklogState.CreateWorklogFrame` sends `evidence with { CodePreview = null }` | **The code terminal never opens.** N3b sends the preview once in `message.Evidence` and only the id in the frame; the old window restored it with `DesktopProgressCodeState`. The Phosphor window (Claude's reference, written before N3b) reads `frame.Evidence`, and nothing calls `DesktopProgressCodeState` any more. Proven: the committed receipt carries a full unified diff; `completed.WorklogFrame.Evidence.CodePreview` is empty (probe in the N3b test). No test looked at the frame. | D18 |
| 2 | MEDIUM | `TextDiffBuilder.cs` hunk context | "nerede" shows `kiosk.js` without `› pair`: the declaration is searched above the hunk's **context** start, so small files and edits near the top never get a place. | D18: search above the first **changed** line |
| 3 | LOW | `UnifiedDiffViewParser` | N3b previews carry `@@ FILE x` **and** `--- a/x` / `+++ b/x`; the parser made two file headers and two tabs (`x +0 −0`, `x +1 −1`). | D18 |
| 4 | LOW | mapper | the diff header showed the **frame** time, which drifts with every later frame — not the time of the change (no fabricated telemetry). | D18: `DesktopProgressCodeState.CodeChangedUtc` |
| 5 | polish | card | a one-step session repeated its title as the only timeline row; a single file showed both a tab and a file header; finished cards had no state cue at the top; long hunk headers wrapped one "…" onto a new line. | D18: hide single repeated row, tabs only for ≥ 2 files, quiet 2 px state line (green/red/amber, 55 %, off in high contrast), hunk header ≤ 48 chars |

Claude verified D18 before writing it, on a clean clone of `b8fd17a`: Release build 0 errors; `DESKTOP_PROGRESS_BEHAVIOR_GREEN` (the N3b test now also asserts the frame keeps the pane and the change time); `MODERNIZATION_POLICY_GREEN`; the built `Talvora.Tray.exe --worklog-preview` renders **all nine PNGs byte-identical** to `docs/codex/reference/worklog-phosphor/previews/` (new `09-one-step-receipt.png` = the live case above, fixed); Full gate `57 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

### D18 — The code terminal is back on the Phosphor card + audit-12 polish

> Audit 2026-10-23 (12). **Run first;** it is also N5's CI fix (Context7 evidence) — commit body line `CI follow-up: 40018913570`.

**Files:** the seven updated Phosphor files (`WorklogCardViewModel.cs`, `UnifiedDiffViewParser.cs`, `WorklogCardMapper.cs`, `WorklogCardView.xaml`, `WorklogCardView.xaml.cs`, `WorklogPreviewScenarios.cs`, `DesktopProgressNotificationWindow.cs`), `src/Talvora.Shared/DesktopProgressCodeState.cs`, `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs`, `src/Talvora/WorklogDiff/TextDiffBuilder.cs`, `tests/Talvora.Notification.Regression/Program.N3bCode.cs`, `.context7/verification.json`.

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogCardViewModel.cs` → `src/Talvora.Tray/Worklog/WorklogCardViewModel.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 2 — copy `docs/codex/reference/worklog-phosphor-next/src/UnifiedDiffViewParser.cs` → `src/Talvora.Tray/Worklog/UnifiedDiffViewParser.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 3 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogCardMapper.cs` → `src/Talvora.Tray/Worklog/WorklogCardMapper.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 4 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogCardView.xaml` → `src/Talvora.Tray/Worklog/WorklogCardView.xaml` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 5 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogCardView.xaml.cs` → `src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 6 — copy `docs/codex/reference/worklog-phosphor-next/src/WorklogPreviewScenarios.cs` → `src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 7 — copy `docs/codex/reference/worklog-phosphor-next/src/DesktopProgressNotificationWindow.cs` → `src/Talvora.Tray/DesktopProgressNotificationWindow.cs` (AGENTS.md rule 5 "Reference copies"; normalised hash in `docs/codex/reference/worklog-phosphor-next/SHA256SUMS.txt`).

Step 8 — `src/Talvora.Shared/DesktopProgressCodeState.cs` — replace

```csharp
    public string? Title { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }
```

with

```csharp
    public string? Title { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    /// <summary>Time of the real code change shown in the pane, or null when the session has no code.</summary>
    public DateTimeOffset? CodeChangedUtc => _latest is null ? null : TimestampUtc;
```

Step 9 — `src/Talvora.Shared/DesktopProgressCodeState.cs` — replace

```csharp
        return message with { Evidence = evidence };
```

with

```csharp
        // The Phosphor card reads the frame: give it the same restored evidence (audit 2026-10-23 (12)).
        return message with
        {
            Evidence = evidence,
            WorklogFrame = message.WorklogFrame is null ? null : message.WorklogFrame with { Evidence = evidence },
        };
```

Step 10 — `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs` — replace

```csharp
    private readonly DesktopProgressPresentationState _presentationState =
        new();
```

with

```csharp
    private readonly DesktopProgressPresentationState _presentationState =
        new();
    // N3b sends a code preview once per change; later frames carry only its id.
    private readonly DesktopProgressCodeState _codeState = new();
```

Step 11 — `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs` — replace

```csharp
    private void PublishCore(DesktopProgressMessage message)
    {
        if (_presentationState.Evaluate(message) !=
```

with

```csharp
    private void PublishCore(DesktopProgressMessage message)
    {
        message = _codeState.Restore(message);
        if (_presentationState.Evaluate(message) !=
```

Step 12 — `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs` — replace

```csharp
        window.Update(message.WorklogFrame!);
```

with

```csharp
        window.Update(message.WorklogFrame!, _codeState.CodeChangedUtc);
```

Step 13 — `src/Talvora/WorklogDiff/TextDiffBuilder.cs` — replace

```csharp
            for (var line = startOld - 1; line >= 0; line--)
```

with

```csharp
            // Nearest declaration above the first changed line (the hunk's own context lines count).
            for (var line = blocks[first].DeleteStartA - 1; line >= 0; line--)
```

Step 14 — `tests/Talvora.Notification.Regression/Program.N3bCode.cs` — replace

```csharp
                if (next.Evidence?.CodeChangeId != codeId || next.Evidence.CodePreview is not null ||
                    pane.Restore(next).Evidence?.CodePreview != preview)
```

with

```csharp
                var restored = pane.Restore(next);
                if (next.Evidence?.CodeChangeId != codeId || next.Evidence.CodePreview is not null ||
                    restored.Evidence?.CodePreview != preview ||
                    (next.WorklogFrame is not null && restored.WorklogFrame?.Evidence?.CodePreview != preview))
```

Step 15 — `tests/Talvora.Notification.Regression/Program.N3bCode.cs` — replace

```csharp
            var pane = new DesktopProgressCodeState();
            pane.Restore(completed);
```

with

```csharp
            var pane = new DesktopProgressCodeState();
            if (pane.Restore(completed).WorklogFrame?.Evidence?.CodePreview != preview || pane.CodeChangedUtc != completed.TimestampUtc)
                throw new InvalidOperationException("N3b: the card frame lost the committed code pane or its change time.");
```

Step 16 — promote the reference in the same commit: replace `docs/codex/reference/worklog-phosphor/src/*`, `previews/*` and `SHA256SUMS.txt` with the files of `docs/codex/reference/worklog-phosphor-next/` (same names, adds `previews/09-one-step-receipt.png`), then delete the `worklog-phosphor-next` folder. Use `talvora_apply_patch` (Update/Add/Delete File) for the text files and `talvora_copy`/`talvora_delete` with `explicitAdmin: true` for the PNGs. `ModernizationPolicyRegression.ps1` checks the tray files against this reference, so it is green only after this step.

**Don't:** change any colour, size or text beyond these files; send the preview twice from the service; touch `WorklogTheme.xaml` (unchanged).

**Acceptance:** `DESKTOP_PROGRESS_BEHAVIOR_GREEN`; `MODERNIZATION_POLICY_GREEN`; `Talvora.Tray.exe --worklog-preview <tmp>` → all nine PNGs byte-identical to `docs/codex/reference/worklog-phosphor/previews/` after Step 16 (`Get-FileHash`); Full gate `Hepsi geçti.` (`talvora_user_run`); build installer + deploy; **live:** `talvora_worklog_note` + one `talvora_apply_patch` with `purpose` on a scratch file, then capture the card as the signed-in user with `talvora_user_run` → `pwsh -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; $b=[Windows.Forms.SystemInformation]::VirtualScreen; $i=New-Object Drawing.Bitmap $b.Width,$b.Height; [Drawing.Graphics]::FromImage($i).CopyFromScreen($b.X,$b.Y,0,0,$b.Size); $i.Save('artifacts/d18-live.png')"` — the card must show the code terminal with the diff, `nerede <file> › <method>` and no duplicated file tab; attach the PNG to Gitea #9; GitHub CI green (10b). Then N5 may be `[x]` (its live screenshot is this one).

### Audit 2026-10-23 (10) · range `audit/2026-10-22-09 (f40e402)..8dfb855` · 9 card commits · executors: ChatGPT/Codex via Talvora · result: ONE HIGH REGRESSION (D17), P-28 decided

Delivered: D15 `4b080f5`, D16 `14a43eb`, N2 `c56c7ee`, N3 `504fa7b`, N9 `4b02102`, N10 `dff4627`, RM1 `319fa88`, N3b `8f379ad`, S3 `8dfb855`. Blocked: N4 (P-28, decided now), N5 (waits for N4).

**Fidelity: exact where a reference exists.** N9, N10 and RM1: every changed file is identical to Claude's verified replay of the cards (clean clone of `504fa7b`), only the mandatory `.context7`/`.talvora` evidence differs. D15 and D16: identical to Claude's verified scratch files. N3b uses `DeclarationLine.PlaceName` for hunk-header context as amended; S3's startup ACL check is log-only as P-12 decided. GitHub `windows-ci` green for all nine commits.

Verified live by Claude on installed `8dfb855` (the agent ticked N9/N10/RM1/N3b with "live evidence pending in Gitea #9"; Claude ran it instead):
- D15: `push --dry-run` in a repository without evidence → `TALVORA_DELIVERY_GATE_BLOCKED: Context7 evidence is mandatory and missing: …` (was an opaque message).
- RM1: `talvora_remotion_guide` → root `C:\Users\tayla\Videos\Talvora`, 4.0.538; `create` → succeeded as `MSI\tayla`; `compositions` → `MyComp`; `render gif scale 0.25` → `60/60` frames, 2 699 bytes; the server instructions carry the Remotion routing sentence.
- N10: `talvora_worklog_note` → `accepted: true`; `talvora_apply_patch` schema has `purpose`; `/mcp/dev` lists 218 tools.
- S3: `C:\ProgramData\Talvora` = SYSTEM + Administrators only; `Penpot` adds Users RX.

| # | Severity | Type | Where | Finding | Fix |
|---|---|---|---|---|---|
| 1 | **HIGH** | regression (S3) | `src/Talvora.Shared/InteractiveUserProcessRunner.cs` `RunnerScript`: `Remove-Item -LiteralPath $RequestPath -Force -ErrorAction SilentlyContinue` | Since S3 the run folder's parents (`C:\ProgramData\Talvora`, `…\InteractiveRuns`) are closed to the user. PowerShell's `Remove-Item` opens the parent folder, throws a terminating `Access to the path 'C:\ProgramData\Talvora\InteractiveRuns' is denied`, and the helper returns exit 1 before starting the program. **Every `talvora_user_run` fails on the installed runtime** (live: `whoami` → exit 1, that error) — the agent's quality-gate tool, and the same runner carries Codex/Claude jobs. The S3 privacy fixture never ran the real helper as a user, so the gate stayed green. | **D17** |
| 2 | MEDIUM | process | STATUS N9/N10/RM1/N3b `[x]` | Cards were ticked while their live acceptance was still "pending in Gitea #9"; finding 1 slipped through exactly that gap (S3 also deferred installer/live checks). | D17 acceptance + AGENTS step 8 clarification below |
| 3 | — | decision | P-28 | Live Codex run contradicts U7/U8 ("do not use Codex"). | P-28 option 1 |

Claude verified D17 before writing it: the real `RunnerScript` executed as `MSI\tayla` in a run folder with exactly S3's permissions (prepared by the service) → current: `Error: Access to the path 'C:\ProgramData\Talvora\InteractiveRuns' is denied`, request file left behind; fixed: exit 0, stdout `msi\tayla`, request file deleted. The new `tests/InteractiveRunnerParentAccessRegression.ps1` (two closed parent levels in `%TEMP%`, no admin needed) reproduces the production error with the old line and prints `INTERACTIVE_RUNNER_PARENT_ACCESS_GREEN` with the fix; its static check also rejects any `Remove-Item` in the helper.

### D17 — `talvora_user_run` and the signed-in gate work under S3's protected ProgramData

> Audit 2026-10-23 (10), finding 1; **extended by P-29 (Claude, 2026-10-23)**. **Run first.**

**Why:** S3 closed `C:\ProgramData\Talvora` to the signed-in user (correct). Two things still assumed access: (1) the interactive helper deleted its request with `Remove-Item`, which opens the closed parent, so every `talvora_user_run` fails; (2) five SourceEdit regressions used the service's ProgramData state (`SourceEdit`, `Toolchains`), so the signed-in gate is red (P-29). The service keeps its protected ProgramData paths; only the helper and the regression runner change.

**Files:** `src/Talvora.Shared/InteractiveUserProcessRunner.cs`, `src/Talvora/SourceEditing/SourceEditEngine.cs`, `src/Talvora/SourceEditing/AstGrepToolchainManager.cs`, `tests/Talvora.SourceEdit.Regression/Program.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Semantic.cs`, `tests/PrivacySecuritySourceRegression.ps1`, `tests/Talvora.Smoke/SmokeScenarios.PrivacySecurity.cs`, new `tests/InteractiveRunnerParentAccessRegression.ps1`.

#### Exact implementation — copy this, do not reinterpret

Claude applied exactly these steps to a clean clone of `6cbb126` with S3's ACL installed and ran the Full gate **as `MSI\tayla`**: `57 kontrol, 0 hata` / `Hepsi geçti.` (47 PowerShell tests, the new one included). Without them the same gate fails at SourceEdit (P-29). Apply in order.

Step 1 — `src/Talvora.Shared/InteractiveUserProcessRunner.cs` — replace

```csharp
    Remove-Item -LiteralPath $RequestPath -Force -ErrorAction SilentlyContinue
```

with

```csharp
    # .NET delete: Remove-Item also opens the parent folder, which the user may not read (S3).
    try { [IO.File]::Delete($RequestPath) } catch { }
```

Step 2 — `tests/PrivacySecuritySourceRegression.ps1` — replace

```powershell
Assert-Contains $interactive 'Remove-Item -LiteralPath $RequestPath -Force' 'interactive request document is removed after deserialization'
```

with

```powershell
Assert-Contains $interactive '[IO.File]::Delete($RequestPath)' 'interactive request document is removed after deserialization'
```

Step 3 — `tests/Talvora.Smoke/SmokeScenarios.PrivacySecurity.cs` — replace

```csharp
        var requestDeleteIndex = runnerScript.IndexOf(
            "Remove-Item -LiteralPath $RequestPath",
            StringComparison.Ordinal);
```

with

```csharp
        var requestDeleteIndex = runnerScript.IndexOf(
            "[IO.File]::Delete($RequestPath)",
            StringComparison.Ordinal);
```

Step 4 — `src/Talvora/SourceEditing/SourceEditEngine.cs` — replace

```csharp
internal static class SourceEditRuntime
{
    public static SourceEditEngine Engine { get; } = new();
}
```

with

```csharp
internal static class SourceEditRuntime
{
    /// <summary>
    /// The service's engine (state under ProgramData\Talvora\SourceEdit). Regression runners call
    /// <see cref="UseStateRootForTests"/> first, because S3 closes ProgramData to the signed-in user (P-29).
    /// </summary>
    public static SourceEditEngine Engine { get; private set; } = new();

    internal static void UseStateRootForTests(string stateRoot) =>
        Engine = new SourceEditEngine(new SourceEditEngineOptions { StateRoot = stateRoot });
}
```

Step 5 — `src/Talvora/SourceEditing/SourceEditEngine.cs` — replace

```csharp
    public IReadOnlyList<SourceEditJournalQuarantine> LastRecoveryQuarantines
```

with

```csharp
    /// <summary>Folder of this engine's journal and receipts (ProgramData for the service, a test root in regressions).</summary>
    internal string StateRoot => store.StateRoot;

    public IReadOnlyList<SourceEditJournalQuarantine> LastRecoveryQuarantines
```

Step 6 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Semantic.cs` — replace

```csharp
            new SourceEditTransactionStore();
```

with

```csharp
            new SourceEditTransactionStore(SourceEditRuntime.Engine.StateRoot);
```

Step 7 — `src/Talvora/SourceEditing/AstGrepToolchainManager.cs` — replace

```csharp
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "Toolchains", "ast-grep", ResolveArchitectureKey());
```

with

```csharp
        var root = Path.Combine(CacheRoot, "ast-grep", ResolveArchitectureKey());
```

Step 8 — `src/Talvora/SourceEditing/AstGrepToolchainManager.cs` — replace

```csharp
    public static async Task<AstGrepToolchain> ResolveAsync(
```

with

```csharp
    /// <summary>
    /// Toolchain cache root. The service uses ProgramData\Talvora\Toolchains; regression runners set a
    /// user-owned root because S3 closes ProgramData to the signed-in user (P-29).
    /// </summary>
    internal static string CacheRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora", "Toolchains");

    public static async Task<AstGrepToolchain> ResolveAsync(
```

Step 9 — `tests/Talvora.SourceEdit.Regression/Program.cs` — replace

```csharp
if (args.Contains("--receipt-counts-only", StringComparer.OrdinalIgnoreCase))
```

with

```csharp
// S3 (P-29): the signed-in gate cannot write ProgramData\Talvora. Engine state and the ast-grep cache live in
// a user-owned folder that persists between runs (so the pinned ast-grep download is cached, as before).
var regressionState = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talvora", "RegressionState");
Talvora.SourceEditing.SourceEditRuntime.UseStateRootForTests(Path.Combine(regressionState, "SourceEdit"));
Talvora.SourceEditing.AstGrepToolchainManager.CacheRoot = Path.Combine(regressionState, "Toolchains");

if (args.Contains("--receipt-counts-only", StringComparer.OrdinalIgnoreCase))
```

Step 10 — copy `docs/codex/reference/ci/InteractiveRunnerParentAccessRegression.ps1` → `tests/InteractiveRunnerParentAccessRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `636eba36c7a58ad89f64c4e2621d75a20c110cac15422eecbb4f9d2caeb2e6ee`).

**Don't:** open ProgramData folders to users, change S3's ACLs, or point the **service** at any other state folder; the overrides are only called by the regression runner.

**Bootstrap for this card only (because `talvora_user_run` is what is broken):** run the Full gate and the installer build as the signed-in user **detached**, then read the log:
`talvora_user_process_start {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-Command","& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/d17-gate.log; exit $LASTEXITCODE"], workingDirectory: "C:\Users\tayla\Talvora-MCP", visible: false}`, then `talvora_tail_text` on `artifacts/d17-gate.log` every 60 s until it ends with `Hepsi geçti.` or `Kontrol başarısız`. Same pattern for `scripts/Build-Windows-Installer.ps1` (log `artifacts/d17-build.log`). Claude verified this start path on the installed runtime (`msi\tayla`, repository as working directory). Never fall back to LocalSystem for these.

**Acceptance:** test-first — the new regression is RED before Step 1 and `INTERACTIVE_RUNNER_PARENT_ACCESS_GREEN` after; Full gate `Hepsi geçti.` (bootstrap); build installer + deploy; then **live through the installed service**: `talvora_user_run {executable:"whoami", arguments:[], workingDirectory:<repo>}` → exit 0 and `msi\tayla`, then a normal `talvora_user_run` Fast gate → `Hepsi geçti.`; paste both into the Log; GitHub CI green (10b). Only then `[x]` (AGENTS step 8). Then backfill the pending live evidence of N9/N10/RM1/N3b in the Log (Claude verified RM1/N10 live in audit 10 — reference it).

### Audit 2026-10-22 (9) · range `audit/2026-10-22-08 (a28f77b)..f40e402` · 3 card commits · executor: ChatGPT via Talvora · result: N1 delivered by Claude after unblocking; TWO FIXES (D15, D16)

Delivered: D14 (`522e52b`), DEP17 (`c3f03ca`, PR #17 closed, installed), N1 (committed locally by the agent as `5fc56ed`, **not pushed** — the agent was stuck there).

**Fidelity: exact.** D14 equals the reference script; DEP17 changed exactly the prescribed files (one notice line, five locks, props); N1: `WorklogCopy.cs` and `WorklogFailureClassifier.cs` are identical to the card blocks, every Step 2/5/6/7 string is present, every removal is done (`_recentWorklogUpdates`, `Son yaptıklarım`, `Amaç:`, `BuildPlainCompletion`, `DescribeEvidenceForUser`, `gitArgs[0]`), all 15 inspection tools are silent. `tests/ModernizationPolicyRegression.ps1` changes only replace assertions that N1 supersedes (logged). CI green: 522e52b, c3f03ca.

**Why the agent was stuck (verified by Claude):** N1's `.context7/verification.json` had two trailing commas. `scripts/Validate-Context7Gate.ps1` and `scripts/Validate-ProjectDeliveryGate.ps1` read it with `ConvertFrom-Json`, which accepts them, so the Full gate was green (55/0). `talvora_git_run push` runs the C# `ProjectDeliveryGate` (System.Text.Json, strict) and refused — but it throws `InvalidOperationException`, whose text the MCP SDK hides: the agent only saw `An error occurred invoking 'talvora_git_run'.` and had nothing to act on. Live proof: `push --dry-run` returned exactly that text; `fetch` of a path outside the knowledge roots (a tool that throws `McpException`) returns its full reason.

**Unblocked by Claude (user's explicit request "sen çöz", 2026-10-22):** removed the two trailing commas and folded the fix into the unpushed N1 commit (no second N1 commit; nothing pushed had to be rewritten) → `f40e402`; pushed to both remotes **through `talvora_git_run`** (delivery gate passed); GitHub windows-ci 39800505820 green; built the installer as `MSI\tayla` and deployed with `scripts/Deploy-Windows-Installer.ps1` via `talvora_run_powershell`; `/healthz.sourceCommit` = `f40e402`; `NATIVE_INSTALLER_RUNTIME_GREEN`.

| # | Severity | Type | Where | Finding | Fix |
|---|---|---|---|---|---|
| 1 | HIGH | silent failure | `src/Talvora/SourceEditing/ProjectDeliveryGate.cs:211` | `Blocked()` returns `InvalidOperationException`; the SDK replaces its text, so every delivery refusal (push, publish) is opaque to the agent. | D15 |
| 2 | MEDIUM | gate parity | `scripts/Validate-Context7Gate.ps1:58`, `scripts/Validate-ProjectDeliveryGate.ps1:8` | Local gates parse evidence leniently; the push gate parses strictly. Local green, push red. | D16 |
| 3 | LOW | docs (Claude) | `AGENTS.md` mechanics table, "Deploy" row | Did not say the installer must be **built** first; `Deploy-Windows-Installer.ps1` alone reinstalls the last built artifact (Claude's first deploy reinstalled `c3f03ca`). Fixed by Claude in this audit. | — |

Claude verified D15 + D16 before writing them, in a fresh clone of `f40e402` as `MSI\tayla`: `tests/StrictEvidenceJsonRegression.ps1` RED without D16 (`scripts/Validate-Context7Gate.ps1 must parse evidence strictly…`), `STRICT_EVIDENCE_JSON_GREEN` with it; with D15 + D16 applied the Full gate passes: `56 kontrol, 0 hata` (46 PowerShell tests, the new one included) / `Hepsi geçti.`; the gate-probe repo reproduces the opaque refusal on the installed `f40e402`, and `fetch` (which already throws `McpException`) shows that the SDK passes that exception's text.

### D15 — Delivery-gate refusals reach the agent

> Audit 2026-10-22 (9), finding 1. Run first.

**Files:** `src/Talvora/SourceEditing/ProjectDeliveryGate.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.QualityAudit.cs`.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `ProjectDeliveryGate.cs`, replace

```csharp
    private static InvalidOperationException Blocked(string reason) =>
        new($"TALVORA_DELIVERY_GATE_BLOCKED: {reason}");
```

with

```csharp
    // McpException: the MCP SDK passes its message to the client. Any other exception type is
    // hidden behind "An error occurred invoking ...", so agents could not see why delivery stopped.
    private static ModelContextProtocol.McpException Blocked(string reason) =>
        new($"TALVORA_DELIVERY_GATE_BLOCKED: {reason}");
```

Step 2 — `SourceEditRegressionRunner.QualityAudit.cs`, in `ExpectBlocked` replace `catch (InvalidOperationException ex)` with `catch (ModelContextProtocol.McpException ex)` (the `when` filter stays). Then, directly above the line `            await WriteContextAsync(vendor: new string('X', 300 * 1024));` insert:

```csharp
            await WriteContextAsync();
            await File.WriteAllTextAsync(
                contextPath,
                (await File.ReadAllTextAsync(contextPath)).TrimEnd()[..^1] + ",}");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "trailing comma");

```

**Acceptance:** SourceEdit regression GREEN; Full gate `Hepsi geçti.` as the signed-in user; build installer + deploy (see AGENTS.md "Deploy" row); live, before and after deploy: create a throw-away git repo under `%TEMP%` (one commit, `origin` = `https://example.invalid/x.git`, no `.context7`) and call `talvora_git_run {repositoryPath: <that repo>, arguments: ["push","--dry-run","origin","main"]}`. Before D15 the text is only `An error occurred invoking 'talvora_git_run'.` (Claude reproduced this on `f40e402`); after D15 it must contain `TALVORA_DELIVERY_GATE_BLOCKED: Context7 evidence is mandatory and missing`. Paste both lines into the Log. GitHub CI green (10b).

### D16 — Local evidence gates parse as strictly as the push gate

> Audit 2026-10-22 (9), finding 2. Run after D15.

**Files:** `scripts/Validate-Context7Gate.ps1`, `scripts/Validate-ProjectDeliveryGate.ps1`, `tests/StrictEvidenceJsonRegression.ps1` (new, verbatim copy).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `scripts/Validate-Context7Gate.ps1`, replace the line

```powershell
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
```

with

```powershell
    $evidenceText = Get-Content -LiteralPath $evidencePath -Raw
    # Same strict parser as ProjectDeliveryGate (System.Text.Json): ConvertFrom-Json accepts trailing
    # commas, so this gate passed evidence that talvora_git_run push then refused (audit 2026-10-22 (9)).
    try { [System.Text.Json.JsonDocument]::Parse($evidenceText).Dispose() }
    catch { throw "Context7 evidence is not valid JSON (strict): $($_.Exception.InnerException.Message)" }
    $evidence = $evidenceText | ConvertFrom-Json
```

Step 2 — `scripts/Validate-ProjectDeliveryGate.ps1`, in `Read-Evidence` replace the line

```powershell
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
```

with

```powershell
    $text = Get-Content -LiteralPath $Path -Raw
    # Same strict parser as ProjectDeliveryGate (System.Text.Json); ConvertFrom-Json accepts trailing commas.
    try { [System.Text.Json.JsonDocument]::Parse($text).Dispose() }
    catch { throw "$Name delivery evidence is not valid JSON (strict): $($_.Exception.InnerException.Message)" }
    return $text | ConvertFrom-Json
```

Step 3 — copy `docs/codex/reference/ci/StrictEvidenceJsonRegression.ps1` → `tests/StrictEvidenceJsonRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised hash `e2897d0b48df809e5cb8163aa6302131c691db3dccc0d8c332bf73fbc99a6d32`). The gate runs it automatically.

**Acceptance:** test-first: the new regression is RED before Steps 1–2 and prints `STRICT_EVIDENCE_JSON_GREEN` after; Full gate `Hepsi geçti.` as the signed-in user; GitHub CI green (10b). Script-only: no deploy.

### Audit 2026-10-22 (8) · range `audit/2026-10-21-07 (eff0f85)..a28f77b` · 2 card commits · executor: ChatGPT via Talvora · result: ONE FIX (D14, caused by Claude's script design)

Delivered: D12 (`56bce29`), D13 (`a28f77b`). Blocked: DEP17 (P-26) — decided now.

**Fidelity: exact.** D12 equals Claude's verified scratch diff line for line, including the renamed regression message; `scripts/Apply-DependabotPr.ps1` equals the D13 reference (normalised hash `681cd1cd…`); the policy paragraph equals the card text. GitHub `windows-ci` green for both commits. On DEP17 the agent stopped after three failures instead of weakening the guard, reverted completely and asked (P-26) — correct behaviour.

| # | Severity | Type | Where | Finding | Fix |
|---|---|---|---|---|---|
| 1 | MEDIUM | process (Claude's script design) | `scripts/Apply-DependabotPr.ps1` run through `talvora_user_run` | The script needs ~6 minutes (two forced restores, Release build, Full gate). Run as one blocking tool call, it outlives the chat client's tool-call time limit; the client gives up or retries while the first run keeps working in the repository. The retry then sees the first run's files and fails in under a second — exactly P-26's symptom (dirty at 0.8 s, PR changes appearing afterwards, "another writer"). Two more gaps made it undiagnosable: the dirty-tree error did not name the files, and two runs could overlap. | D14 (P-26 option 1) |

Claude verified D14 before writing it, in a fresh clone of `a28f77b` as `MSI\tayla`: dirty tree → `FAILED: … Unexpected: ?? stray.txt`; real run `-Pr 17` → `55 kontrol, 0 hata`, `READY DEP17` in `pr-17.result`, 38-line log; a **second start 20 s into that run** → exit 3, `this run changed nothing`, result still `RUNNING pid …`; finally the exact DEP17 path through the installed service: `talvora_user_process_start` (`MSI	ayla`, `visible: false`) with `-DryRun`, polled `pr-17.result` → `DRY RUN OK`, tree clean again.

### D14 — Dependabot runs detached, single-instance and observable (decision P-26)

> Audit 2026-10-22 (8), finding 1. Run before DEP17.

**Files:** `scripts/Apply-DependabotPr.ps1` (verbatim copy).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/ci/Apply-DependabotPr.ps1` → `scripts/Apply-DependabotPr.ps1` (AGENTS.md rule 5 "Reference copies"; normalised hash `465380c5aeb18187af54a6e19374a53e521da5e190c00d5eb923bc62b9f7b33d`).

What changed (for the Log; do not re-implement): an exclusive lock `artifacts/dependabot/apply.lock` (a second start prints `Another Apply-DependabotPr.ps1 run is already active; this run changed nothing.` and exits 3 without touching anything); `artifacts/dependabot/pr-<n>.log` (every command and its output) and `artifacts/dependabot/pr-<n>.result` (`RUNNING pid <pid>` → `READY DEP<n>: …` / `DRY RUN OK` / `FAILED: <reason>`); the dirty-tree error lists the unexpected files. `artifacts/` is git-ignored.

**Acceptance:** normalised hash matches; PowerShell parse 0 errors; Full gate green as the signed-in user; GitHub CI green.

### Audit 2026-10-21 (7) · range `audit/2026-10-21-06 (5048232)..eff0f85` · 4 card commits · executor: ChatGPT via Talvora · result: TWO FIXES (D12, D13) — one caused by Claude

Delivered: D9 (`5899995`), D11 (`a7bf0dd`), D5 (`61da387`), DEP1 (`eff0f85`); A1 ticked with the D9 signed-in gate. Blocked with proposals: P-24 (git pull identity), P-25 (DEP17) — both decided now.

**Fidelity: exact.** D9 and D11 diffs equal the card blocks line for line (D11 also equals Claude's verified scratch diff); `tests/IdentityPortabilityRegression.ps1` and `scripts/Apply-DependabotPr.ps1` equal the references by normalised hash; D5 equals the block (the smoke uses the scenario's existing `context`/`sections` variables, as allowed). GitHub `windows-ci` green for all four commits (39602635240, 39605559155, 39609582483, 39614105176). The agent stopped correctly twice instead of improvising (P-24, P-25) and reverted DEP17 cleanly; PR #17 is still open, `origin/main` = `github/main` = `eff0f85`.

Verified live by Claude on installed `61da387`:
- D11: `talvora_apply_patch` on a fresh workspace **while that root was this shell's current directory** → `committed`, warnings `[]` (before D11: `DIRECTORY_RENAME_UNPROTECTED`).
- D5: `talvora_agent_brief {card: "N5"}` → `agentsSections` starts with `## PERMANENT USER-APPROVED AGENT PROMPT`; `ğ`, `—`, `§` intact. (`ç` simply does not occur in the N5 context — the local helper output has none either; the smoke test without a card checks all four. Claude's P-20 wording overstated this; no action.)

| # | Severity | Type | Where | Finding | Fix |
|---|---|---|---|---|---|
| 1 | MEDIUM | identity (user-reported) | `src/Talvora/Tools/GitTools.cs:777-799` `ShouldUseInteractiveUserForRemoteGitAsync` | Only `push`/`fetch` are routed to the signed-in user. `git pull --ff-only origin main` (CONTINUE PROTOCOL step 2!) runs as LocalSystem and fails on the user's SSH key (`UNPROTECTED PRIVATE KEY FILE`). The key ACL is correct and must stay. | D12 (P-24 option 1) |
| 2 | MEDIUM | process (caused by Claude) | `scripts/Apply-DependabotPr.ps1:26-27, 63-69` | The script refused a pre-edited `.context7/verification.json` and never staged it, so its own Full gate always fails `Context7` for a package change; it also committed/pushed with raw `git`, bypassing `talvora_git_run`'s delivery gate. Claude's re-run then hit the next gap: `tests/ThirdPartyNoticesSourceRegression.ps1` requires `THIRD-PARTY-NOTICES.md` to carry the new version (`Microsoft.Extensions.AI.Abstractions 10.11.0`), and nothing updated it. | D13 (P-25 option 2, adjusted) |

Claude verified both before writing them, in a scratch clone of `eff0f85` as `MSI\tayla`:
- D12: without the change `tests/GitSafeDirectoryRegression.ps1` is RED (`Git SSH fetch/push does not route…`); with it `GIT_CREDENTIAL_CONTEXT_SOURCE_GREEN`; `src/Talvora` Release build 0 errors.
- D13: revised script + refreshed `.context7/verification.json`, real run `-Pr 17` → Full gate `55 kontrol, 0 hata` / `Hepsi geçti.` and `READY DEP17`; changed files: `Directory.Packages.props`, `THIRD-PARTY-NOTICES.md` (one line, 10.10.1 → 10.11.0), five `packages.lock.json`, `.context7/verification.json`. Nothing committed or pushed by the script; the clone was discarded.

### D12 — `git pull` uses the signed-in user's credentials (decision P-24)

> Audit 2026-10-21 (7), finding 1.

**Files:** `src/Talvora/Tools/GitTools.cs`, `tests/GitSafeDirectoryRegression.ps1`, `AGENTS.md` (Claude already added the invariant — do not edit it).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `GitTools.cs`, in `ShouldUseInteractiveUserForRemoteGitAsync`, replace

```csharp
                string.Equals(
                    arguments[index],
                    "fetch",
                    StringComparison.OrdinalIgnoreCase))
            {
                remoteCommandIndex = index;
```

with

```csharp
                string.Equals(
                    arguments[index],
                    "fetch",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    arguments[index],
                    "pull",
                    StringComparison.OrdinalIgnoreCase))
            {
                remoteCommandIndex = index;
```

(`pull` resolves the remote like `fetch`: `remote get-url <remote>`, not `--push`. Nothing else changes.)

Step 2 — `tests/GitSafeDirectoryRegression.ps1`: replace the line

```powershell
    $text -match '"push"[\s\S]*?"fetch"' -and
```

with

```powershell
    $text -match '"push"[\s\S]*?"fetch"[\s\S]*?"pull"' -and
```

and replace `throw 'Git SSH fetch/push does not route through the logged-on user credential context.'` with `throw 'Git SSH fetch/pull/push does not route through the logged-on user credential context.'`.

**Don't:** touch the SSH key, its ACL, `~/.ssh`, git config, or any other git verb.

**Acceptance:** test-first: the regression is RED before Step 1 and prints `GIT_CREDENTIAL_CONTEXT_SOURCE_GREEN` after; Full gate `Hepsi geçti.` as the signed-in user; deploy; live `talvora_git_run {args: ["pull","--ff-only","origin","main"]}` → exit code 0 (paste its first output line into the Log); GitHub CI green (10b).

### D13 — The Dependabot script prepares and verifies; Talvora tools commit (decision P-25)

> Audit 2026-10-21 (7), finding 2. Replaces DEP1 Step 4 for every future `DEP<n>`.

**Files:** `scripts/Apply-DependabotPr.ps1` (verbatim copy), `docs/MODERNIZATION-POLICY.md` (one paragraph).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/ci/Apply-DependabotPr.ps1` → `scripts/Apply-DependabotPr.ps1` (AGENTS.md rule 5 "Reference copies"; normalised hash `681cd1cd7b08c4096027b67f479f2e3739949325c4c0965b039d2b86c3879d3e`).

Step 2 — `docs/MODERNIZATION-POLICY.md`: in the paragraph that starts with `> **Dependabot PRs.**`, replace the text from `Apply it with` to the end of the paragraph with exactly:

> Apply it as the signed-in user in this order: refresh `.context7/verification.json` (NuGet + the updated package), then `pwsh scripts/Apply-DependabotPr.ps1 -Pr <n>` — it takes only `Directory.Packages.props`/`global.json` from the PR, regenerates every lock file, runs the lockstep test, locked restores and Release build, updates the versions in `THIRD-PARTY-NOTICES.md` (it stops if a new runtime package has no notice line — that needs a license review), runs the Full gate and prints `READY DEP<n>`. It never commits. Then commit once with `talvora_git_run` (`Card: DEP<n>`), push both remotes, close the PR with `talvora_gh_run pr close <n> --comment "Applied on main as <sha>"`, and check CI (10b).

**Acceptance:** normalised hash matches; PowerShell parse 0 errors; Full gate green as the signed-in user; GitHub CI green. Then DEP17 runs as rewritten below.

### Audit 2026-10-21 (6) · range `audit/2026-10-21-05 (3bb0bd6)..5048232` · 2 card commits · executor: ChatGPT via Talvora · result: ONE FIX (D11) + one Claude-caused block (P-23)

Delivered: D10 (`11dc434`), A1 (`5048232`). Blocked: D9 (P-23, caused by Claude's reference file — decided now), D5 (waits for D9).

**Fidelity: exact.** `tests/NativeInstallerRuntimeRegression.ps1` matches the D10 block line for line; `UserRunTools.cs` is identical to the A1 block; `FileOwnership.cs` equals the A1 block plus the static layout check the card asked for. GitHub CI green for both commits (10b followed).

Verified live by Claude on installed `5048232`: `talvora_user_run` → `whoami` = `MSI\tayla`; the six repository paths (`obj/`, `bin/`, `.git/index`, …) are owned by `MSI\tayla`; a `talvora_apply_patch` edit keeps the file owner; Full gate as `MSI\tayla` with D9 applied in a scratch clone: `55 kontrol, 0 hata` / `Hepsi geçti.`.

| # | Severity | Type | Where | Finding | Fix |
|---|---|---|---|---|---|
| 1 | MEDIUM | false warning on every edit | `src/Talvora/SourceEditing/SourceEditPathGuard.cs` `HoldExisting` → `SourceEditEngine.cs:723` | Every `talvora_apply_patch`/commit reports `DIRECTORY_RENAME_UNPROTECTED` for the **workspace root**. The root is the current directory of shells/editors, which never share DELETE, so deny-delete can never succeed there. A warning that is always present teaches agents to ignore the real one (a protected subdirectory that could not be locked). The root's identity is already checked (FILE_ID_INFO) before and after the edit. | D11 |
| 2 | LOW | reference file (Claude) | `docs/codex/reference/ci/*.ps1` | Mixed newlines in Claude's reference copies made the "byte-for-byte" check impossible with `talvora_apply_patch`. Fixed by Claude in this audit: all reference files are pure LF, `SHA256SUMS.txt` lists normalised-content hashes, AGENTS.md rule 5 "Reference copies" says how to copy and verify. | P-23 decided |
| 3 | INFO | A1 status | STATUS | A1 stays `[~]` only until D9 makes the signed-in gate green; then tick it with the D9 run as evidence (no extra work). | — |

Claude verified D11 before writing it: scratch clone of `5048232` with the D11 edits → full `Talvora.SourceEdit.Regression` GREEN, including the five parent-rename protection tests and `DirectoryIdentitySharingWithExternalProcessAsync`.

### D11 — Do not rename-lock the workspace root (it is identity-checked instead)

> Audit 2026-10-21 (6), finding 1. Run directly after D9.

**Files:** `src/Talvora/SourceEditing/SourceEditPathGuard.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.PathGuard.cs`.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `SourceEditPathGuard.cs`: directly below the line
`    public System.Security.Principal.SecurityIdentifier? WorkspaceRootOwner { get; private set; }`
insert:

```csharp

    // The workspace root is usually some process's current directory (shells, editors), which never
    // shares DELETE; it is identity-checked but not rename-protected (audit 2026-10-21 (6)).
    private string? workspaceRoot;
```

Step 2 — same file, three one-line insertions:

- below `        guard.WorkspaceRootOwner = FileOwnership.TryGetOwner(prepared.ChangeSet.WorkspaceRoot);` insert `        guard.workspaceRoot = prepared.ChangeSet.WorkspaceRoot;`
- below `        guard.WorkspaceRootOwner = FileOwnership.TryGetOwner(plan.WorkspaceRoot);` insert `        guard.workspaceRoot = plan.WorkspaceRoot;`
- in `AcquireFileSystemTargets`, below its `        var guard = new SourceEditPathGuard();` insert `        guard.workspaceRoot = existingRoot;`

Step 3 — same file, directly above `    private void HoldExisting(` insert:

```csharp
    private bool IsWorkspaceRoot(string path) =>
        workspaceRoot is not null &&
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot)),
            StringComparison.OrdinalIgnoreCase);

```

Step 4 — same file, inside `HoldExisting`, replace

```csharp
        var handle =
            OpenDirectory(
                path,
                denyDeleteShare: true,
                out var renameProtected);
        if (!renameProtected)
```

with

```csharp
        var isWorkspaceRoot = IsWorkspaceRoot(path);
        var handle =
            OpenDirectory(
                path,
                denyDeleteShare: !isWorkspaceRoot,
                out var renameProtected);
        if (!renameProtected && !isWorkspaceRoot)
```

Step 5 — `SourceEditRegressionRunner.PathGuard.cs`, in `DirectoryIdentitySharingWithExternalProcessAsync` (the external holder must now hold a subdirectory, because the root is no longer opened with deny-delete):

- `"k2-probe.txt"` → `"held/k2-probe.txt"` in the `WriteUtf8Async`, `PathInWorkspace` and `Update(...)` calls (3 places);
- in the process argument list replace `scriptPath, fixture.Root, readyPath, releasePath,` with `scriptPath, Path.Combine(fixture.Root, "held"), readyPath, releasePath,`.

**Don't:** change `OpenDirectory`, the identity checks, the warning code or text, or any other directory's protection.

**Acceptance:** `Talvora.SourceEdit.Regression` GREEN (all five `PathGuardBlocks*ParentRename` tests pass); Full gate `Hepsi geçti.` as the signed-in user; deploy; a `talvora_apply_patch` edit of one file in this repository returns **no** `DIRECTORY_RENAME_UNPROTECTED` warning (paste the result's warning list, empty, into the Log); GitHub CI green (10b).

### Audit 2026-10-21 (5) · range `audit/2026-10-20-03 (36fe923)..3bb0bd6` · 9 card commits · executor: ChatGPT via Talvora · result: FIXES REQUIRED (test identity, Dependabot)

Delivered: D8, D7, CI1, CI3, CI2, D6, D1, D3, Q6. Blocked with proposals: D5 (P-20), A1 (P-21), Q6c (P-22) — all decided now.

**Fidelity to the exact-implementation blocks: excellent.** Claude compared byte-for-byte: `ToolArgumentValidator.cs` and `StructuredResultFailure.cs` are identical to the card code; `Get-CiStatus.ps1`, `PlatformPackageLockstepRegression.ps1`, `Update-DotnetPlatformPackages.ps1` identical to `docs/codex/reference/ci`; D1, D7, D8 diffs match the guides line for line; supporting changes (one smoke assertion in D7, one modernization assertion in D3, notices/locks in CI2) are minimal and logged. CI rule 10b was followed for every card (each `[x]` cites its green run).

Verified live by Claude on installed `3bb0bd6`: GitHub CI green for all 9 commits since D7; `talvora_search_text {path,pattern}` → `isError: true`, `InvalidArguments: missing 'root', 'query'; unknown 'pattern', 'path'. Expected: …` (Q6 ✓); WorkContract refusal now keeps its text (D3 ✓); installed `current.json` ToolCount 250 = live `/mcp` 250 = manifest 250.

| # | Severity | Type | Where | Finding | Fix card |
|---|---|---|---|---|---|
| 1 | **HIGH** | test identity (same class as D7) | `tests/Talvora.Notification.Regression/Program.cs:826-827` (S7 test `AssertInstalledTrayPipeIsTrustedWhenRunningAsync`) | Claude ran the Full gate **as `MSI\tayla`**: `FAIL Notification … Win32Exception (1314) WTSQueryUserToken failed`. The test calls a LocalSystem-only API whenever a tray is running. GitHub CI has no tray (test returns early) and ChatGPT's gate runs as LocalSystem — so both stay green while Codex and the user get a red gate. `tests/Talvora.Smoke/SmokeScenarios.Claude.cs:63` has the same unguarded call (not in the gate today). | D9 |
| 2 | MEDIUM | process | — | The same identity bug class has now escaped twice. A static guard is needed so it cannot come back. | D9 (IdentityPortabilityRegression) |
| 3 | MEDIUM | CI / deps | Dependabot PR #17 (`Microsoft.Extensions.AI.Abstractions` 10.10.1 → 10.11.0), run `39495063592` | `FAIL Restore … NU1004`: Dependabot regenerates only the lock files of projects that reference the package directly; the three test projects pin it transitively and stay stale. Every future NuGet PR will fail the same way, and merging on GitHub would also fork GitHub from Gitea. | DEP1 |
| 4 | LOW | acceptance (Claude) | D5 step 4 | `-Card N5` context keeps parent §3 and drops child §3.1 by design, so "contains `### 3.1 "Kod terminali"`" could not hold. | P-20 decided (acceptance amended) |
| 5 | LOW | ordering (Claude) | A1 vs `tests/NativeInstallerRuntimeRegression.ps1:6-7` | The installed-runtime test compares installed `current.json` with the **working-tree** manifest, so any card adding a tool is red before its own deployment. | D10 (P-21) |

Claude verified D9 and D10 before writing them: in a scratch clone of `3bb0bd6` with D9 applied, `IDENTITY_PORTABILITY_GREEN`, `DESKTOP_PROGRESS_BEHAVIOR_GREEN` and `TALVORA CLAUDE INTEGRATION GREEN` (with the SKIP line) as `MSI\tayla`; the D10 version of `NativeInstallerRuntimeRegression.ps1` prints `NATIVE_INSTALLER_RUNTIME_GREEN` as `MSI\tayla`. With D9 applied, the complete Full gate as `MSI\tayla` on that clone: `55 kontrol, 0 hata` / `Hepsi geçti.` (HealthHandle and NativeInstallerRuntime included) — no other identity-dependent failure is hiding behind the Notification one. `scripts/Apply-DependabotPr.ps1 -Pr 17 -DryRun` on a scratch clone regenerated all five affected lock files (including the three Dependabot missed), locked restores and Release build passed, and the tree was left clean.

### D9 — Identity-portable tests + a guard so the class cannot return

> Audit 2026-10-21 (5), findings 1–2.

**Files:** `tests/Talvora.Notification.Regression/Program.cs`, `tests/Talvora.Smoke/SmokeScenarios.Claude.cs`, `tests/IdentityPortabilityRegression.ps1` (new, verbatim copy).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `tests/Talvora.Notification.Regression/Program.cs`, in `AssertInstalledTrayPipeIsTrustedWhenRunningAsync`, replace

```csharp
            var sessionId = WindowsSessionLauncher
                .GetDefaultInteractiveUser().SessionId;
```

with

```csharp
            // LocalSystem resolves the interactive session; any other identity already runs in it.
            var sessionId = IsRunningAsLocalSystem()
                ? WindowsSessionLauncher.GetDefaultInteractiveUser().SessionId
                : Process.GetCurrentProcess().SessionId;
```

and insert directly above `private static async Task AssertPipeReadTimeoutRecoveryAsync()`:

```csharp
    private static bool IsRunningAsLocalSystem()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid) == true;
    }

```

Step 2 — `tests/Talvora.Smoke/SmokeScenarios.Claude.cs`: insert directly **above** the line `            // Exercise the actual user-session launcher, PowerShell helper and six-argument pump binding.`

```csharp
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (identity.User?.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid) != true)
            {
                // WTSQueryUserToken needs LocalSystem; CI and the signed-in user skip this live part.
                Console.WriteLine("SKIP Claude user-session runner: requires LocalSystem.");
            }
            else
            {
```

and insert a closing line `            }` directly **below** the line `            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }`.

Step 3 — copy `docs/codex/reference/ci/IdentityPortabilityRegression.ps1` → `tests/IdentityPortabilityRegression.ps1` (AGENTS.md rule 5 "Reference copies"; verify the normalised hash with `SHA256SUMS.txt`, decision P-23). The gate picks it up automatically.

**Acceptance:** run as the **signed-in user** (Codex shell; ChatGPT: say "gate run as LocalSystem; Claude re-checks" until A1): `IDENTITY_PORTABILITY_GREEN`, `DESKTOP_PROGRESS_BEHAVIOR_GREEN`, Full gate `Hepsi geçti.`; GitHub CI green (10b).

### D10 — Installed-runtime test compares installed with installed (decision P-21)

> Audit 2026-10-21 (5), finding 5. Unblocks A1.

**Files:** `tests/NativeInstallerRuntimeRegression.ps1` only.

#### Exact implementation — copy this, do not reinterpret

Replace exactly these two lines:

```powershell
$toolManifest = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\Talvora.Shared\TalvoraToolManifest.cs'))
$expectedToolCount = ([regex]::Matches($toolManifest, '"(?:talvora_[a-z0-9_]+|search|fetch)"')).Count
```

with exactly:

```powershell
# Installed state must match the INSTALLED runtime. The working-tree manifest may already be
# newer (a card adding a tool runs this gate before its own deployment); source-side counts are
# covered by the surface smoke tests.
$listResponse = Invoke-WebRequest 'http://127.0.0.1:7676/mcp' -Method Post -UseBasicParsing -TimeoutSec 15 `
    -ContentType 'application/json' -Headers @{ Accept = 'application/json, text/event-stream' } `
    -Body '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
$listText = $listResponse.Content
if ($listText -match '^event:') {
    $listText = (($listText -split "`n") | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5) }) -join ''
}
$expectedToolCount = @(($listText | ConvertFrom-Json).result.tools).Count
if ($expectedToolCount -lt 1) { throw 'The installed Talvora runtime returned no tools.' }
```

(`$repoRoot` stays defined; it is no longer used for the count.)

**Acceptance:** `pwsh -NoProfile -File tests/NativeInstallerRuntimeRegression.ps1` → `NATIVE_INSTALLER_RUNTIME_GREEN`; Full gate green; CI green. Then A1 runs unchanged (its Full gate no longer depends on deployment order).

### DEP1 — Dependabot PRs are applied on main, never merged on GitHub

> Audit 2026-10-21 (5), finding 3. Run **after A1** (the script calls `gh`, which only works as the signed-in user).

**Files:** `scripts/Apply-DependabotPr.ps1` (new, verbatim copy), `docs/MODERNIZATION-POLICY.md` (one paragraph), `docs/codex/STATUS.md` (fixed facts line).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/ci/Apply-DependabotPr.ps1` → `scripts/Apply-DependabotPr.ps1` (AGENTS.md rule 5 "Reference copies"; verify the normalised hash with `SHA256SUMS.txt`).

Step 2 — append to `docs/MODERNIZATION-POLICY.md` exactly:

> **Dependabot PRs.** Never merge a Dependabot PR on GitHub (it would fork GitHub from Gitea and leaves transitive `packages.lock.json` files stale → NU1004). Apply it with `pwsh scripts/Apply-DependabotPr.ps1 -Pr <n>` as the signed-in user: it takes only `Directory.Packages.props`/`global.json` from the PR, regenerates every lock file, runs the lockstep test, locked restores, Release build and the Full gate, commits once (`Card: DEP<n>`), pushes to both remotes, closes the PR and checks CI.

Step 3 — `docs/codex/STATUS.md` → Fixed facts → Branch row: replace `Do not touch \`dependabot/*\`.` with `Dependabot PRs: apply with scripts/Apply-DependabotPr.ps1 (DEP1), never merge on GitHub.`

Step 4 — apply PR #17: first add the queue/Log line for `DEP17` to STATUS (the script commits STATUS together with the update), then run `talvora_user_run` → `pwsh -NoProfile -File scripts/Apply-DependabotPr.ps1 -Pr 17`. Before that, run it once with `-DryRun` and paste its "Changed files" line into the Log.

**Acceptance:** DEP1 commit (script + policy + STATUS) green on CI; DEP17 commit green on CI; PR #17 closed with the comment the script writes; `origin/main` = `github/main`.

**Standing procedure (also for future PRs):** every new Dependabot NuGet PR becomes one queue item `DEP<n>` (no hyphen; the context helper reads letter+digit ids) handled exactly like the rewritten DEP17 card (P-25; replaces Step 4). SDK PRs (`global.json`): run the same script, then `scripts/Update-DotnetPlatformPackages.ps1 -Version <runtime patch of that SDK>` if the lockstep test fails, inside the same card.


### DEP17 — Apply Dependabot PR #17 (`Microsoft.Extensions.AI.Abstractions` 10.10.1 → 10.11.0)

> **Rewritten by P-25 (Claude, audit 2026-10-21 (7)).** Runs after D13. Replaces the earlier "script commits" text.

#### Exact implementation — copy this, do not reinterpret

1. `docs/codex/STATUS.md`: write `In progress: DEP17`. Refresh `.context7/verification.json` with `talvora_apply_patch` (scope `DEP17: Microsoft.Extensions.AI.Abstractions 10.10.1 -> 10.11.0`, Context7 `/websites/learn_microsoft_en-us_nuget` for lock-file regeneration and the package's own docs; `verifiedAtUtc` = now).
2. Start the script **once, detached** (amended by P-26, runs after D14): `talvora_user_process_start {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-File","scripts/Apply-DependabotPr.ps1","-Pr","17"], workingDirectory: "C:\Users\tayla\Talvora-MCP", visible: false}`. Then about every 60 s read line 1 of `artifacts/dependabot/pr-17.result` (`talvora_read_text_range`) until it no longer starts with `RUNNING` (limit 20 minutes; at the limit stop and report — never start a second run). Progress: `talvora_tail_text` on `artifacts/dependabot/pr-17.log`. `READY DEP17: …` → step 3. `FAILED: …` → paste that line into the Log, restore with `talvora_git_run {args:["restore","--staged","--worktree","--",".",":!docs/codex/STATUS.md",":!.context7/verification.json"]}` and stop. Exit 3 / "already active" means a run is still going: keep polling, do not restore.
3. STATUS: tick DEP17 and add the Log entry (changed files line from step 2, gate last line).
4. `talvora_git_run {args:["add","--","Directory.Packages.props","global.json","*packages.lock.json","THIRD-PARTY-NOTICES.md",".context7/verification.json","docs/codex/STATUS.md"]}`, then commit with subject `build(deps): apply Dependabot PR #17`, body `Microsoft.Extensions.AI.Abstractions 10.10.1 -> 10.11.0; all lock files regenerated with --force-evaluate.` and trailer `Card: DEP17`.
5. `talvora_git_run` push `origin main`, then `github main`.
6. `talvora_gh_run {args:["pr","close","17","--repo","bingoweb/Talvora-MCP","--comment","Applied on main as <short sha> with all lock files regenerated (scripts/Apply-DependabotPr.ps1)."]}`.
7. CI check (10b). Deploy is required (runtime dependency): `scripts/Deploy-Windows-Installer.ps1`, `/healthz.sourceCommit` = the DEP17 commit.

**Acceptance:** exactly one DEP17 commit; CI green; PR #17 closed (not merged); `origin/main` = `github/main`; installed commit = DEP17.

### Audit 2026-10-20 (4) · range `429591f..429591f` (no agent commits) · result: FIX REQUIRED (caused by Claude)

The agent made **no commits**: it started D7, found the gate red on a clean HEAD, reverted everything and raised P-19; D6 hit the same red baseline and was also reverted. That is exactly the required behaviour.

Root cause (verified by Claude): `tests/QualityGateRegression.ps1:207-213` enforces ≤ 250 context lines for every N/UI card; Claude's `#### Exact implementation` blocks (commits `3d176bf`, `429591f`) made `-Card N1` 343 lines and `-Card N5` 299 lines. The guard was right for prose, wrong for literal implementation blocks, which must reach the agent complete.

| # | Severity | Type | Where | Finding | Fix card |
|---|---|---|---|---|---|
| 1 | HIGH | gate (caused by Claude) | `tests/QualityGateRegression.ps1:207-213` | Gate red on clean HEAD: real N1 context 343 > 250 lines (N5 299). Blocks every card. | D8 (decision P-19) |
| 2 | INFO | environment | protocol step 2 as LocalSystem | `git pull` through Talvora failed ("loose SSH key ACL"): LocalSystem uses its own SSH setup. Harmless today (local = origin). After A1, run `git fetch`/`git pull` with `talvora_user_run` (signed-in user's SSH). | note in A1 Part D |

### D8 — Context budget ignores literal implementation blocks (decision P-19)

> Audit 2026-10-20 (4), finding 1. Do this card first; it turns the gate green again.

**Files:** `tests/QualityGateRegression.ps1` only.

#### Exact implementation — copy this, do not reinterpret

Replace exactly this block (lines 207–213 today):

```powershell
    foreach ($card in @('UI1','UI2','UI3','UI4','UI5','UI6','UI7','UI8',
            'N1','N2','N3','N3b','N4','N5','N6','N7','N8')) {
        $actual = @(& $contextPath -Card $card | Out-String) -join ''
        if (($actual -split '\r?\n').Count -gt 250) {
            throw "Real context for $card exceeds 250 lines."
        }
    }
```

with exactly:

```powershell
    foreach ($card in @('UI1','UI2','UI3','UI4','UI5','UI6','UI7','UI8',
            'N1','N2','N3','N3b','N4','N5','N6','N7','N8')) {
        $actual = @(& $contextPath -Card $card | Out-String) -join ''
        # Literal "#### Exact implementation" blocks must reach the agent complete, and the
        # STATUS header is required reading anyway; the 250-line economy budget applies to
        # the card prose, queue/proposal summaries, file counts and cited spec sections.
        $counted = 0
        $insideExact = $false
        $insideStatus = $false
        foreach ($line in ($actual -split '\r?\n')) {
            if ($line -like '=== *') {
                $insideStatus = $line -like '=== STATUS*'
                $insideExact = $false
            }
            if ($line -like '#### Exact implementation*') { $insideExact = $true; continue }
            if (-not $insideExact -and -not $insideStatus) { $counted++ }
        }
        if ($counted -gt 250) {
            throw "Real context for $card exceeds 250 lines outside STATUS and exact-implementation blocks."
        }
    }
```

**Acceptance:** `pwsh -NoProfile -File tests/QualityGateRegression.ps1` passes; the Fast gate ends with `Hepsi geçti.`; one commit `test(quality): exempt exact-implementation blocks from the context budget` with trailer `Card: D8`. Then continue with D7 (unchanged card) — P-19 is decided.

## Phase T — Faster feedback (user request 2026-10-24)

**Why.** The user: GitHub CI takes very long and every small change re-runs the full test set. Claude measured (installed `b17c6f7`, signed-in, 12 cores):

| What | Before | After T1–T4 (Claude's clean-clone replay) |
|---|---|---|
| Local Full gate | ≈ 3 min 30 s, every suite one after another (SourceEdit 69 s, of which the 13 Roslyn `semantic-*` scenarios 50 s; 48 PowerShell regressions 56 s, mostly process start-up; Notification 25 s) | **1 min 41 s** (`ParallelPhase` 70 s, bounded by the semantic scenarios); green three times in a row |
| `-Scope Fast` (baseline, inner loop) | ≈ 2 min 30 s — it still ran SourceEdit, Notification and IntegrationPolicy | `-Scope Changed`: docs-only **4 s**, a script change **5 s**, a tray change **39 s** (Notification + IntegrationPolicy only) |
| GitHub CI, docs-only push (every Claude audit, STATUS-only commits) | 5–6 min of runner time (full gate, installer, install) | the changed-scope gate only (≈ 1 min including checkout) |
| GitHub CI, code push | 4–5 min runner time (gate 2.8–3.9 min) + queue wait up to 10 min | gate shortened by T1/T2; queue wait is GitHub's and cannot be changed by the repository |

Nothing is weakened: every code push still runs the complete Full gate, installer, install and installed-runtime checks in CI; the agent still runs Full once per card before committing; dependency/SDK changes select everything in `-Scope Changed`; anything CI cannot classify runs the full pipeline.

**Order:** T1 → T2 → T3 → T4 (each builds on the previous one). One commit per card. All four are scripts/tests only: no deploy; acceptance = the named regressions + Full gate `Hepsi geçti.` as the signed-in user + GitHub CI green (10b).

### T1 — SourceEdit regression: Roslyn scenarios selectable on their own

> Phase T. Lets T2 run the 13 `semantic-*` scenarios (≈ 50 s) in a process of their own next to the other 86. Each process keeps its own engine state folder (`RegressionState\SourceEdit` / `RegressionState\SourceEdit-Semantic`), so they never share a WAL. Without an argument the runner behaves exactly as before.

**Files:** `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs`, `tests/Talvora.SourceEdit.Regression/Program.cs`, `.context7/verification.json`.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs` — replace

```csharp
    public static async Task RunAllAsync()
    {
```

with

```csharp
    // T1: the Roslyn workspace scenarios are the slowest part of the suite; the gate runs them in their own
    // process next to the other scenarios.
    public static bool IsSemanticScenario(string name) =>
        name.StartsWith("semantic-", StringComparison.Ordinal);

    public static async Task RunAllAsync(Func<string, bool>? include = null)
    {
```

Step 2 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs` — replace

```csharp
        foreach (var test in tests)
        {
```

with

```csharp
        foreach (var test in tests.Where(test => include?.Invoke(test.Name) ?? true))
        {
```

Step 3 — `tests/Talvora.SourceEdit.Regression/Program.cs` — replace

```csharp
Talvora.SourceEditing.SourceEditRuntime.UseStateRootForTests(Path.Combine(regressionState, "SourceEdit"));
```

with

```csharp
// T1: the semantic scenarios run in a parallel process of their own, so they keep their own engine state.
var semanticOnly = args.Contains("--semantic-only", StringComparer.OrdinalIgnoreCase);
Talvora.SourceEditing.SourceEditRuntime.UseStateRootForTests(
    Path.Combine(regressionState, semanticOnly ? "SourceEdit-Semantic" : "SourceEdit"));
```

Step 4 — `tests/Talvora.SourceEdit.Regression/Program.cs` — replace

```csharp
await SourceEditRegressionRunner.RunAllAsync();
Console.WriteLine("TALVORA SOURCE EDIT REGRESSION GREEN");
```

with

```csharp
if (semanticOnly)
{
    await SourceEditRegressionRunner.RunAllAsync(SourceEditRegressionRunner.IsSemanticScenario);
    Console.WriteLine("TALVORA SOURCE EDIT SEMANTIC REGRESSION GREEN");
    return;
}
if (args.Contains("--without-semantic", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunAllAsync(name => !SourceEditRegressionRunner.IsSemanticScenario(name));
    Console.WriteLine("TALVORA SOURCE EDIT CORE REGRESSION GREEN");
    return;
}
await SourceEditRegressionRunner.RunAllAsync();
Console.WriteLine("TALVORA SOURCE EDIT REGRESSION GREEN");
```

**Acceptance:** run both at the same time — `dotnet run --project tests/Talvora.SourceEdit.Regression/Talvora.SourceEdit.Regression.csproj -c Release --no-build -- --semantic-only` prints `TALVORA SOURCE EDIT SEMANTIC REGRESSION GREEN` (13 PASS) and `... -- --without-semantic` prints `TALVORA SOURCE EDIT CORE REGRESSION GREEN` (86 PASS; signed in, the same 4 SKIPs as before); without arguments still `TALVORA SOURCE EDIT REGRESSION GREEN`; Full gate `Hepsi geçti.`; CI green.

### T2 — Quality gate runs independent suites in parallel

> Phase T, after T1. The cheap checks (Restore, Build, PowerShellParse, PackageManager, Context7, Delivery) stay first and serial. Then `Invoke-ParallelQualityChecks` starts SourceEditSemantic, SourceEdit, Notification, IntegrationPolicy and every PowerShell regression as thread jobs (`Start-ThreadJob`, PowerShell 7's `Microsoft.PowerShell.ThreadJob`, throttle = processor count, minimum 2); each keeps its own numbered log, and the results are printed in the listed order with the 60-line failure tail. The three regressions that rebuild the tray or restore/publish fixtures (`GiteaProtocolReadiness`, `GiteaStopChain`, `InstallerDependencyProvenance`) and the two installed-service checks run serially afterwards, then ControlCenter and BusinessBootstrap as before. A failure in the parallel phase reports **every** failing check (count in `N kontrol, M hata`). `Invoke-QualityCheck` and `Get-InstalledRuntimeAvailability` are unchanged (`QualityGateRegression.ps1` extracts them).

**Files:** `scripts/Invoke-QualityGate.ps1` (whole file from the reference), new `tests/QualityGateParallelRegression.ps1`, `.context7/verification.json` (Context7: PowerShell `Start-ThreadJob`).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/t2/Invoke-QualityGate.ps1` → `scripts/Invoke-QualityGate.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `7ddf8f8b54da7342c2281cc1290d9e3c46c8b38b0d8ee59db79ca9e7bb28cc77`). Replaces the whole file (it was 233 lines): the cheap serial checks stay first, then the independent suites run in parallel thread jobs, then the checks that rebuild the tray or need the installed service run serially.

Step 2 — copy `docs/codex/reference/t2/QualityGateParallelRegression.ps1` → `tests/QualityGateParallelRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `158cc78e213bdd41b286d039520bdba4c081fbfa3b30f6e0a6f30a6d5c5e8ee6`).

**Acceptance:** `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_PARALLEL_GREEN` (RED before Step 1: `must run independent suites with Invoke-ParallelQualityChecks`; the test also proves overlap, listed-order reporting, a missing program counted as a failure, and all three failures counted); `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_REGRESSION_GREEN`; Full gate as the signed-in user shows `PASS ParallelPhase (50 checks)` and `Hepsi geçti.` — run it **twice** (stability) and write both durations into the Log; CI green.

### T3 — `-Scope Changed`: run only what the change can affect

> Phase T, after T2. `-Scope Changed [-Base <rev>]` lists the files that differ from `-Base` (default `HEAD`, i.e. the uncommitted card work) plus untracked files, then: Restore/Build only when something under `src/` or a test project changed; SourceEditSemantic for `src/Talvora/SourceEditing/**`; SourceEdit for `src/Talvora/**`, `src/Talvora.Shared/**`; Notification for those plus `src/Talvora.Tray/**`; IntegrationPolicy whenever it builds; a dependency/SDK file (`Directory.*.props|targets`, `global.json`, `NuGet.config`, `Talvora.slnx`, any `packages.lock.json`) selects everything. PowerShell regressions: the four always-on policy tests, any changed test, and every test whose text names a changed file. The always-cheap checks (PowerShellParse, PackageManager, Context7, Delivery) always run. It prints one line: `Değişen dosya: N · derleme: evet/hayır · paketler: … · PS testleri: M`. `-Scope Fast` and `-Scope Full` are unchanged.

**Files:** `scripts/Invoke-QualityGate.ps1` (whole file from the reference), new `tests/QualityGateChangedRegression.ps1`, `.context7/verification.json`. `AGENTS.md` already tells the agent to use `-Scope Changed` for the baseline and inner loop once T3 is in `main` (Claude, 2026-10-24).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/t3/Invoke-QualityGate.ps1` → `scripts/Invoke-QualityGate.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `15c896d0ed434af6ae440a01c4885852d7022274a18bec52727fd448c0d7640f`). Replaces the whole file again (T2 version + `-Scope Changed` and `-Base`).

Step 2 — copy `docs/codex/reference/t3/QualityGateChangedRegression.ps1` → `tests/QualityGateChangedRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `bd1084356bbaa679e8385e078bca7a0bbbe675b8c96947ca6cbb4d89a13cdfb9`).

**Acceptance:** `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_CHANGED_GREEN` (RED before Step 1); `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_PARALLEL_GREEN`, `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_REGRESSION_GREEN`; with this card's own uncommitted files `-Scope Changed` selects no build and only the gate regressions and finishes `Hepsi geçti.`; Full gate `Hepsi geçti.`; CI green. Log the `-Scope Changed` duration.

### T4 — CI: documentation-only pushes run the changed-scope gate

> Phase T, after T3. A new step `Classify the change` (id `changes`) reads the push/PR range; when **every** changed file is Markdown under `docs/` and none is under `docs/codex/reference/`, the job runs `Invoke-QualityGate.ps1 -Scope Changed -Base <previous commit>` and skips setup-dotnet, the full gate, the installer, the install and the installed-runtime checks. Everything else — including a new branch or an unreadable range, `README.md`, `AGENTS.md` and any reference copy — runs the unchanged full pipeline. The SDK-lock step is untouched (`ModernizationPolicyRegression.ps1` executes it with fixtures).

**Files:** `.github/workflows/windows-ci.yml`, new `tests/CiDocsOnlyRegression.ps1`, `.context7/verification.json` (Context7/vendor: GitHub Actions `steps.<id>.outputs`, `GITHUB_OUTPUT`, step `if`).

#### Exact implementation — copy this, do not reinterpret

Step 1 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
```

with

```yaml
      - name: Classify the change
        id: changes
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
          if ($env:GITHUB_EVENT_NAME -eq 'pull_request') {
            $headRef = [string]$event.pull_request.head.sha
            $baseRef = [string](& git merge-base ([string]$event.pull_request.base.sha) $headRef)
          }
          else {
            $headRef = [string]$event.after
            $baseRef = [string]$event.before
          }
          # T4: a change made only of Markdown documents under docs/ (reference copies excluded) cannot change
          # the build or the runtime; it runs the changed-scope gate instead of the full build and install.
          # Anything that cannot be classified (a new branch, an unreadable range) runs the full pipeline.
          $docsOnly = $false
          if ($baseRef -match '^[0-9a-f]{40}$' -and $baseRef -notmatch '^0{40}$') {
            $changedFiles = @(& git diff --name-only --no-renames $baseRef $headRef)
            if ($LASTEXITCODE -ne 0) { throw 'CI could not read the change set.' }
            $docsOnly = $changedFiles.Count -gt 0 -and
              @($changedFiles | Where-Object { $_ -notmatch '^docs/.+\.md$' -or $_ -match '^docs/codex/reference/' }).Count -eq 0
          }
          "docs_only=$($docsOnly.ToString().ToLowerInvariant())" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
          "base_ref=$baseRef" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8

      - name: Validate documentation-only change
        if: steps.changes.outputs.docs_only == 'true'
        shell: pwsh
        env:
          TALVORA_CI_BASE: ${{ steps.changes.outputs.base_ref }}
        run: |
          & ./scripts/Invoke-QualityGate.ps1 -Scope Changed -Base $env:TALVORA_CI_BASE
          if ($LASTEXITCODE -ne 0) { throw 'Changed-scope quality gate failed.' }

      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        if: steps.changes.outputs.docs_only != 'true'
        with:
```

Step 2 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Validate build and all test suites (including SourceEdit)
        shell: pwsh
```

with

```yaml
      - name: Validate build and all test suites (including SourceEdit)
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 3 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Start and verify current MCP surface
        shell: pwsh
```

with

```yaml
      - name: Start and verify current MCP surface
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 4 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Build canonical native installer
        shell: pwsh
```

with

```yaml
      - name: Build canonical native installer
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 5 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Install and verify canonical runtime
        shell: pwsh
```

with

```yaml
      - name: Install and verify canonical runtime
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 6 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Validate installed health handle stability
        shell: pwsh
```

with

```yaml
      - name: Validate installed health handle stability
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 7 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Validate optional signed-in desktop installation
        shell: pwsh
```

with

```yaml
      - name: Validate optional signed-in desktop installation
        if: steps.changes.outputs.docs_only != 'true'
        shell: pwsh
```

Step 8 — `.github/workflows/windows-ci.yml` — replace

```yaml
      - name: Clean service test
        if: always()
```

with

```yaml
      - name: Clean service test
        if: always() && steps.changes.outputs.docs_only != 'true'
```

Step 9 — copy `docs/codex/reference/t4/CiDocsOnlyRegression.ps1` → `tests/CiDocsOnlyRegression.ps1` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `b419dfd38ac7abdbec9f5e2d1a019582f9daeb8c2d03be155d7a23ba9f623885`).

**Acceptance:** `CI_DOCS_ONLY_GREEN` (RED before Step 1), `MODERNIZATION_POLICY_GREEN`, `QUALITY_`65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`_REGRESSION_GREEN`; Full gate `Hepsi geçti.`; this card's own push runs the **full** CI (it changes the workflow) and is green; the next docs-only push (e.g. the STATUS update of the next card, or Claude's next audit) shows `Validate documentation-only change` run and the build/install steps skipped — record that run id in the Log (if the next push is not docs-only, mark `[~] live pending: docs-only CI run` and check it at the next docs-only push).

**Claude's verification (all four, clean clone of `b17c6f7`, signed-in `MSI\tayla`):** T1 halves green concurrently (55 s instead of 69 s); T2 test RED before / green after; parallel Full gate green three times (1 min 41 s, `ParallelPhase` 69–71 s; before ≈ 3 min 30 s); T3 test RED before / green after; `-Scope Changed`: workflow change 4.7 s, docs-only `-Base HEAD~1` 4.4 s, tray change 39 s and it correctly failed on the tray file's pinned reference checksum; T4 test RED on the old workflow, green after; final Full gate with T1–T4: `65 kontrol, 0 hata` / `Hepsi geçti.` in 1 min 49 s as `MSI\tayla`.

## Tray icon (user request 2026-10-24)

### TI1 — Tray icon "Altıgen-T" (Claude's design, user request 2026-10-24)

> User: "talvora tray ikon için bir kozmetik yaratıcılık sergile". This card is **complete and literal**: every pixel decision is in the reference code. It replaces the tray-icon part of UI7 (DESIGN-CONTROL-CENTER UI7 amended). Executor rule: apply the steps exactly; there is **nothing** to design, choose, tune or improve.

**Why.** Today the tray shows the 32 px executable icon (dark rounded plate, gradient hexagon, T) shrunk by Windows to 16 px — soft, heavy on a dark taskbar, a dark square on a light one — with a 10.5 px status disc covering a third of the mark, and a green disc even when everything is fine.

**Design (what the user will see).**
- The brand mark alone, no plate: the **notched hexagon** (the brand's gap at the top vertex) as a 1.55-unit ring and the **T** with bevelled bar ends, drawn as vector art at the tray's **real pixel size** (16 px at 100 %, 20 / 24 / 32 at 125 / 150 / 200 %), so it is crisp at every scale.
- Colours are the Control Center tokens of the **taskbar** theme: dark taskbar → ring `#5BD8FF`, T `#E9EEF6`; light taskbar → ring `#0779A8`, T `#0E1625`. High contrast → ring and T pure white (dark) / black (light).
- **Hazır** = the quiet mark, **no badge** (state is in the tooltip and menu). **Dikkat** (LocalOnly) = an amber triangle (`#F5B84B` / `#9A5B00`), **Ulaşılamıyor** (Offline) = a red disc (`#FF6B81` / `#C2324A`), 5.4 units, bottom-right, inside a transparent 7.2-unit cut-out that cuts the ring but never the T. Shape + colour, never colour alone.
- It follows the taskbar live: a 2 s UI-thread timer compares `TrayIconAppearance.Current()` (size, light taskbar, high contrast) and redraws only when it changed.
- Proof sheet: `docs/codex/reference/tray-icon/previews/tray-sheet.png` (3 states × 4 sizes × both taskbars, 8×) and the 24 single PNGs.

**Files:** new `src/Talvora.Tray/TrayIconArt.cs`; `src/Talvora.Tray/TrayIconFactory.cs` (whole file from the reference); `src/Talvora.Tray/Program.cs` (`--tray-icon-preview`); `src/Talvora.Tray/TrayApplicationContext.cs` (field, timer, icon creation, stop/dispose); new `tests/Talvora.Notification.Regression/Program.TrayIcon.cs`; `tests/Talvora.Notification.Regression/Program.cs` (one call); `tests/NativeInstallerSourceRegression.ps1` (the old "shrink the exe icon" contract becomes the TI1 contract); `.context7/verification.json` (System.Drawing `Graphics`/`Bitmap.GetHicon`, `SystemInformation.SmallIconSize`, registry `SystemUsesLightTheme`); `.talvora/awwwards-verification.json` (`designDecisions`: "DESIGN-CONTROL-CENTER §11 TI1 Altıgen-T tray mark").

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/tray-icon/TrayIconArt.cs` → `src/Talvora.Tray/TrayIconArt.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `fe8dfe96eaed822b0a552499875c847474f30610f05b3e6f4bc7d513545b55ed`). New file: the whole drawing (geometry on a 16-unit grid, token colours, badge shapes, PNG preview writer).

Step 2 — copy `docs/codex/reference/tray-icon/TrayIconFactory.cs` → `src/Talvora.Tray/TrayIconFactory.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `3ba4462117229b1dfeb06f942cdcf1ac120215f24bb85de54122c766096e35b8`). Replaces the whole file: `CreateStatusIcon` now takes a `TrayIconAppearance` and draws with `TrayIconArt`; `DrawBrandFallback` and `DrawStatusBadge` are gone; `CreateGiteaStatusIcon` is unchanged.

Step 3 — `src/Talvora.Tray/Program.cs` — replace

```csharp
    private static int Main(string[] args)
    {
```

with

```csharp
    private static int Main(string[] args)
    {
        if (args.Length >= 2 &&
            string.Equals(args[0], "--tray-icon-preview", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var path in Talvora.Tray.TrayIconArt.RenderPreviews(args[1]))
            {
                Console.WriteLine(path);
            }

            return 0;
        }
```

Step 4 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
    private Icon? _statusIcon;
```

with

```csharp
    private Icon? _statusIcon;
    private TrayIconAppearance _iconAppearance;
    private readonly System.Windows.Forms.Timer _iconAppearanceTimer;
```

Step 5 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
        UpdateAggregateTrayState();

        _talvoraTimer = new System.Windows.Forms.Timer
```

with

```csharp
        UpdateAggregateTrayState();

        // Redraw the tray mark when the taskbar theme, high contrast or the tray icon size changes.
        _iconAppearanceTimer = new System.Windows.Forms.Timer
        {
            Interval = 2_000,
            Enabled = true,
        };
        _iconAppearanceTimer.Tick += (_, _) =>
        {
            if (TrayIconAppearance.Current() != _iconAppearance)
            {
                UpdateAggregateTrayState();
            }
        };

        _talvoraTimer = new System.Windows.Forms.Timer
```

Step 6 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
        var color = aggregateState switch
        {
            TalvoraConnectionState.Ready => Color.FromArgb(49, 196, 112),
            TalvoraConnectionState.LocalOnly => Color.FromArgb(240, 178, 52),
            _ => Color.FromArgb(220, 72, 72),
        };

        var previous = _statusIcon;
        _statusIcon = TrayIconFactory.CreateStatusIcon(
            aggregateState,
            color);
```

with

```csharp
        var previous = _statusIcon;
        _iconAppearance = TrayIconAppearance.Current();
        _statusIcon = TrayIconFactory.CreateStatusIcon(
            aggregateState,
            _iconAppearance);
```

Step 7 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
        _shutdownTimer.Stop();
        _lifetimeCts.Cancel();
        _notifyIcon.Visible = false;
```

with

```csharp
        _shutdownTimer.Stop();
        _iconAppearanceTimer.Stop();
        _lifetimeCts.Cancel();
        _notifyIcon.Visible = false;
```

Step 8 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
            _shutdownTimer.Stop();
            _talvoraTimer.Dispose();
```

with

```csharp
            _shutdownTimer.Stop();
            _iconAppearanceTimer.Stop();
            _talvoraTimer.Dispose();
```

Step 9 — `src/Talvora.Tray/TrayApplicationContext.cs` — replace

```csharp
            _shutdownTimer.Dispose();
```

with

```csharp
            _shutdownTimer.Dispose();
            _iconAppearanceTimer.Dispose();
```

Step 10 — `tests/NativeInstallerSourceRegression.ps1` — replace

```powershell
    TrayUsesEmbeddedBrandIcon = (
        $trayProgram -match 'Icon\.ExtractAssociatedIcon\(Application\.ExecutablePath\)' -and
        $trayProgram -match 'DrawStatusBadge' -and
        $trayProgram -match 'TalvoraConnectionState\.Ready'
    )
```

with

```powershell
    # TI1: the tray draws the brand mark as vector art at the real tray size instead of shrinking the exe icon.
    TrayUsesBrandMarkArt = (
        $trayProgram -match 'TrayIconArt\.Render\(' -and
        $trayProgram -match 'TrayIconAppearance\.Current\(\)' -and
        $trayProgram -match 'TalvoraConnectionState\.Ready' -and
        $trayProgram -notmatch 'Icon\.ExtractAssociatedIcon\(Application\.ExecutablePath\)'
    )
```

Step 11 — copy `docs/codex/reference/tray-icon/Program.TrayIcon.cs` → `tests/Talvora.Notification.Regression/Program.TrayIcon.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `3808bb28f43c644f4c2d4a969fed80e757ce0a44d42c6c39d89386458222c70b`).

Step 12 — `tests/Talvora.Notification.Regression/Program.cs` — replace

```csharp
        AssertN6Details();
```

with

```csharp
        AssertN6Details();
        AssertTrayIconArt();
```

**Don't (each of these is a violation → D-card):**
- change any coordinate, size, stroke width, notch fraction, colour, badge shape, badge position, timer interval, identifier, comment or string in the reference code;
- add a background plate, gradient, glow, shadow, outline, animation, blinking, extra badge, or a badge for Ready;
- draw the tray icon from `Talvora.ico`, `Talvora.png` or `Icon.ExtractAssociatedIcon`;
- use `SystemEvents` or another notification mechanism instead of the 2 s timer;
- change `CreateGiteaStatusIcon`, the tray menu, the tooltip text, the state evaluation or any file not listed in **Files**;
- "fix" a preview pixel by hand — if a PNG differs, stop with a proposal that attaches both images.

**Acceptance:** `DESKTOP_PROGRESS_BEHAVIOR_GREEN` (includes `AssertTrayIconArt`; RED before the steps = compile error, the types do not exist); `Talvora.Tray.exe --tray-icon-preview <tmp>` writes 25 PNGs, **all byte-identical** to `docs/codex/reference/tray-icon/previews/`; Full gate `Hepsi geçti.`; installer + deploy; installed `Talvora.Tray.exe --tray-icon-preview` also byte-identical; GitHub CI green. Then add the User queue line "U9 — Tepsideki yeni Talvora ikonuna bakın (açık ve koyu görev çubuğunda); beğenmezseniz Claude'a söyleyin." and mark TI1 `[~]` until the user answers.

Claude's verification (clean clone of `64c95b4`): solution build 0 errors; `DESKTOP_PROGRESS_BEHAVIOR_GREEN`; the built tray writes 25/25 PNGs byte-identical to the reference; Full gate `66 kontrol, 0 hata` / `Hepsi geçti.` as `MSI\tayla`.

## Phase CI — Stop the recurring GitHub failures (root cause, Claude 2026-10-20)

**Verified by Claude from the GitHub logs:** every `windows-ci` run since `edcef4f` (S6, 20 Oct) failed at the same place — `FAIL SourceEdit … knowledge-s6-scoped-fetch: Win32Exception (1314) WTSQueryUserToken failed`. The test only passes as LocalSystem; the runner is a normal administrator. Locally the agent ran the gate through `talvora_run_powershell` **as LocalSystem**, so it never saw the failure, and nothing in the protocol made it look at CI after pushing. Two more independent failure classes exist: a timing-flaky test (`directory-identity-external-reader: External directory handle never became ready`, run `39239571794`) and Dependabot PR #16 (`NU1109 Detected package downgrade … from 10.0.13 to centrally defined 10.0.12`: Dependabot bumped some .NET platform packages, central transitive pinning kept the rest at 10.0.12).

Root-cause rules (now also in AGENTS.md):
1. **CI is part of "done".** A card is `[x]` only after the GitHub run for its commit is green, checked with `scripts/Get-CiStatus.ps1` (CI1). A red run makes the next message's task the CI fix (one `CI follow-up: <run id>` commit, EXECUTION MANDATE rule 4).
2. **A LocalSystem gate is not CI-equivalent.** Behaviour that depends on the identity (interactive session, user profile, SSH keys) must be tested in both identities (D7 does this for S6).
3. **.NET platform packages move together** (CI2).
4. **No fixed-iteration waits in tests** for external processes; use a deadline (CI3).

Order: D8 → D7 → CI1 → CI3 → CI2 (D7 removes the current red; CI1 makes every later push self-checking).

### CI1 — Every push checks its own GitHub run

**Files:** `scripts/Get-CiStatus.ps1` (new, copied verbatim), `AGENTS.md` is already updated by Claude (do not edit it).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy `docs/codex/reference/ci/Get-CiStatus.ps1` → `scripts/Get-CiStatus.ps1` byte-for-byte (Codex: `Copy-Item`; ChatGPT: `talvora_copy` with `explicitAdmin: true` — approved exception for files under `docs/codex/reference/`). Verify with `Get-FileHash` against `docs/codex/reference/ci/SHA256SUMS.txt`.

Claude ran this script against the failing commit `429591f`; it printed exactly the 7 lines that matter (`FAIL SourceEdit`, the S6 Win32 1314 line, `Kontrol başarısız: SourceEdit`, …) and exited 1.

Step 2 — how to run it after every push (CONTINUE PROTOCOL step 10b):
- Codex: `pwsh -NoProfile -File scripts/Get-CiStatus.ps1` (default: HEAD, waits up to 25 min, polls every 30 s).
- ChatGPT before A1: call `talvora_gh_run` (runs as the signed-in user) with `["run","list","--repo","bingoweb/Talvora-MCP","--workflow","windows-ci.yml","--commit","<full sha>","--json","databaseId,status,conclusion,url","--limit","1"]` every ~60 s until `status` is `completed`; on `failure` call `["run","view","<databaseId>","--repo","bingoweb/Talvora-MCP","--log-failed"]` and read only lines containing `FAIL `, `Kontrol başarısız`, `Reason:`, `regression failures`, `Exception (`. ChatGPT after A1: `talvora_user_run` with `executable: "pwsh"`, `arguments: ["-NoProfile","-File","scripts/Get-CiStatus.ps1"]`.
- Exit 0 → mark the card `[x]`. Exit 1 → do not start another card; fix CI (one `CI follow-up` commit). Exit 2 (no run / timeout) → mark `[~]` with "CI pending" and check again at the start of the next message before anything else.

**Acceptance:** script copied identical; this card's own push is checked with it and the Log contains its output line (`CI başarılı: …` or the failure lines).

### CI3 — Deadline instead of a fixed loop in the directory-holder test

**Files:** `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.PathGuard.cs` only.

#### Exact implementation — copy this, do not reinterpret

In `DirectoryIdentitySharingWithExternalProcessAsync`, replace

```csharp
for (var attempt = 0; attempt < 200 && !File.Exists(readyPath); attempt++)
{
    if (child.HasExited)
    {
        throw new InvalidOperationException(
            "External directory handle setup failed: " +
            await child.StandardError.ReadToEndAsync());
    }

    await Task.Delay(40);
}
```

with

```csharp
// A cold CI runner can need well over 8 s to start pwsh and compile the Add-Type helper.
var readyDeadline = System.Diagnostics.Stopwatch.StartNew();
while (!File.Exists(readyPath) && readyDeadline.Elapsed < TimeSpan.FromSeconds(90))
{
    if (child.HasExited)
    {
        throw new InvalidOperationException(
            "External directory handle setup failed: " +
            await child.StandardError.ReadToEndAsync());
    }

    await Task.Delay(50);
}
```

**Acceptance:** SourceEdit regression green locally and in CI (checked with CI1).

### CI2 — .NET platform packages in lockstep; Dependabot can no longer split them

**Files:** `tests/PlatformPackageLockstepRegression.ps1` (new, verbatim), `scripts/Update-DotnetPlatformPackages.ps1` (new, verbatim), `Directory.Packages.props` (changed only by the script), all `packages.lock.json` (regenerated only by the script), `.github/dependabot.yml`, `docs/MODERNIZATION-POLICY.md` (one paragraph).

#### Exact implementation — copy this, do not reinterpret

Step 1 — copy verbatim and verify against `docs/codex/reference/ci/SHA256SUMS.txt`:
- `docs/codex/reference/ci/PlatformPackageLockstepRegression.ps1` → `tests/PlatformPackageLockstepRegression.ps1` (the quality gate picks up every `tests/*.ps1` automatically);
- `docs/codex/reference/ci/Update-DotnetPlatformPackages.ps1` → `scripts/Update-DotnetPlatformPackages.ps1`.

Step 2 — run, as the signed-in user (Codex shell; ChatGPT via `talvora_user_run` after A1, otherwise `talvora_run_powershell` and note "LocalSystem" in the Log):

```powershell
pwsh -NoProfile -File scripts/Update-DotnetPlatformPackages.ps1 -Version 10.0.13
```

Verified by Claude on a clean copy of HEAD `bf010aa`: 17 packages moved 10.0.12 → 10.0.13 (`Microsoft.Extensions.AI.Abstractions` 10.10.1 and `Microsoft.Build.Framework` untouched), both `--locked-mode` restores pass, Release build 0 errors. All 10.0.13 versions exist on nuget.org.

Step 3 — `.github/dependabot.yml`: in the `nuget` entry, replace the `ignore:` list with exactly (keep everything else):

```yaml
    ignore:
      # Microsoft.Build.Framework 18.x pulls Microsoft.NET.StringTools 18.x,
      # which is not compatible with Talvora's current net10.0-windows toolchain.
      # Revisit this guard when the repository moves to the .NET 11 toolchain.
      - dependency-name: "Microsoft.Build.Framework"
        versions:
          - ">=18.0.0"
      # .NET platform packages ship with the runtime and must move together.
      # Bump them only with scripts/Update-DotnetPlatformPackages.ps1 (CI2);
      # single-package bumps cause NU1109 downgrade errors under central transitive pinning.
      - dependency-name: "Microsoft.Data.Sqlite"
      - dependency-name: "Microsoft.Extensions.Caching.Abstractions"
      - dependency-name: "Microsoft.Extensions.Configuration.Abstractions"
      - dependency-name: "Microsoft.Extensions.DependencyInjection.Abstractions"
      - dependency-name: "Microsoft.Extensions.Diagnostics.Abstractions"
      - dependency-name: "Microsoft.Extensions.FileProviders.Abstractions"
      - dependency-name: "Microsoft.Extensions.Hosting.Abstractions"
      - dependency-name: "Microsoft.Extensions.Hosting.WindowsServices"
      - dependency-name: "Microsoft.Extensions.Logging.Abstractions"
      - dependency-name: "Microsoft.Extensions.Options"
      - dependency-name: "Microsoft.Extensions.Primitives"
      - dependency-name: "System.Composition*"
```

Step 4 — `docs/MODERNIZATION-POLICY.md`: append exactly this paragraph:

> **.NET platform packages.** `Microsoft.Extensions.*` (except `Microsoft.Extensions.AI.Abstractions`), `Microsoft.Data.Sqlite` and `System.Composition*` are bumped together with `scripts/Update-DotnetPlatformPackages.ps1 -Version <runtime patch>` — normally in the same change as a .NET SDK bump. `tests/PlatformPackageLockstepRegression.ps1` fails the gate if they diverge. Dependabot ignores them on purpose.

Step 5 — close Dependabot PR #16 with `gh pr close 16 --repo bingoweb/Talvora-MCP --comment "Superseded by CI2: .NET platform packages are bumped together with scripts/Update-DotnetPlatformPackages.ps1."` (ChatGPT: `talvora_gh_run`; this PR closure is authorised by the user's request to fix CI at the root).

**Acceptance:** lockstep regression prints `PLATFORM_PACKAGE_LOCKSTEP_GREEN 10.0.13 (17 packages)`; locked restores + full gate green; CI green for the commit (CI1); PR #16 closed. Deploy (runtime packages changed) and verify `/healthz`.

### Audit 2026-10-20 (3) · range `audit/2026-10-18-02 (dd4ee5b)..36fe923` · 17 commits · executor: ChatGPT via Talvora · result: FIXES REQUIRED

Cards delivered: D2, D4, A2, A3, E3, S1, Q6b (research), S4, S5 (`[~]`), S6, S7, S8, R4, R5, Q4; user-requested AGENTS.md section `8714266` (permanent prompt). 14 proposals P-5…P-18 raised — **all decided now** (PROPOSALS.md → Decided); EXECUTION MANDATE rule 1 clarified so that supporting changes and ordering no longer produce proposals.

Verified live by Claude on the installed build `041c723`: `/healthz.version` = MCP `serverInfo.version` = `3.0.0-dev+041c723…` (R5 ✓); foreign `Host` → 400, foreign `Origin` → 403 with the Turkish message, loopback/no Origin → 200, Penpot static `manifest.json` with foreign Origin → 200 + `ACAO: *`, Penpot `healthz` with foreign Origin → 403 (S1 + P-2 ✓); `fetch C:\Windows\win.ini` refused (S6 ✓); E3 sizes on this repo: git_status 431 B, git_log 2.6 KB, find_files 5.3 KB, search_text 3.5 KB (all within targets ✓); `talvora_git_run commit` without a `Card:` trailer refused by WorkContract (A3 ✓); S5: the old admin DPAPI file is gone ✓.

| # | Severity | Type | Where | Finding | Fix card |
|---|---|---|---|---|---|
| 1 | **HIGH** | tooling / bug | `src/Talvora/Tools/KnowledgeTools.cs` `AddDefaultSearchRoots` (catch only `InvalidOperationException`); `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.KnowledgeS6.cs:45` | **The quality gate is red for a normal user.** Claude ran `Invoke-QualityGate.ps1 -Scope Full` as `MSI\tayla`: `knowledge-s6-scoped-fetch` → `Win32Exception (1314) WTSQueryUserToken failed` → `7 kontrol, 1 hata / Kontrol başarısız: SourceEdit`. Every "Hepsi geçti." in the Log since S6 was produced by `talvora_run_powershell` **as LocalSystem**, which holds `SeTcbPrivilege`. The product bug: outside LocalSystem (dev `dotnet run`, tests, any non-service host) `search`/`fetch` default roots throw instead of degrading. | D7 |
| 2 | MEDIUM | bug | `talvora_agent_brief` context (`scripts/Get-CodexContext.ps1` output via the user runner) | Live: every non-ASCII character in the context is lost (`beğendim` → `be�endim`, `—` → `-`, `§3` → `�3`; `ç`, `ı`, `§` count = 0) while `agentsSections` keeps them. ChatGPT reads Turkish copy decks and spec section numbers from this context — corrupted copy would end up in the product. | D5 |
| 3 | MEDIUM | gap | `src/Talvora/Tools/AgentBriefTools.cs` section list | The brief does not return the new `## PERMANENT USER-APPROVED AGENT PROMPT` section of AGENTS.md (added by `8714266`), so a ChatGPT session that relies on the brief never sees it. | D5 |
| 4 | LOW | tooling | `scripts/Get-CodexContext.ps1:41` | `-Card D2` fails ("could not be found"): D-cards use `## D<n> — ` headings, the helper only matches `### `. (Agent reported it correctly in "Findings for Claude".) | D6 |
| 5 | LOW | rule | queue | N5 (visual redesign that renders N1–N4 outcomes) was the next open card although N1–N4 are blocked — it would have been built on protocol v3. Queue reordered (P-11). | — (STATUS) |
| 6 | INFO | process | 14 proposals for file-list plumbing and ordering | Over-strict reading of EXECUTION MANDATE rule 1; clarified in AGENTS.md (supporting changes and ordering are not proposal material). | — |

---

### D5 — `talvora_agent_brief`: keep UTF-8 and include the permanent-prompt section

> **Amended by P-20 (Claude, 2026-10-21)** — Step 4 acceptance is replaced by the P-20 decision text in `docs/codex/PROPOSALS.md` → "Decided".

> Audit 2026-10-20 (3), findings 2 and 3.

**Files:** `scripts/Get-CodexContext.ps1`, `src/Talvora/Tools/AgentBriefTools.cs`, `tests/Talvora.Smoke/SmokeScenarios.AgentBrief.cs`, `tests/QualityGateRegression.ps1`.

**Do:**
- `Get-CodexContext.ps1`: first statements `[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)` and `$OutputEncoding = [Text.UTF8Encoding]::new($false)`; read every file with `-Encoding UTF8`.
- `AgentBriefTools`: run the script with `pwsh -NoProfile -NonInteractive -OutputFormat Text -File …` and decode stdout as UTF-8 (the interactive runner already captures UTF-8 — verify end-to-end); add `## PERMANENT USER-APPROVED AGENT PROMPT` as the **first** extracted section.

**Acceptance:** smoke on this repo: `context` contains `ç`, `ğ`, `—` and `§`; `agentsSections` starts with the permanent-prompt section; `-Card N5` output contains `### 3.1 "Kod terminali"` intact.

#### Exact implementation — copy this, do not reinterpret

Cause (verified): the child `pwsh` writes redirected output in the console code page (cp857 on this machine); the runner decodes it as UTF-8, so every non-ASCII character breaks.

Step 1 — `scripts/Get-CodexContext.ps1`: directly after the line `Set-StrictMode -Version Latest` insert exactly:

```powershell
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
```

Step 2 — `src/Talvora/Tools/AgentBriefTools.cs`:

a) Replace the `RequiredHeadings` initializer with exactly:

```csharp
private static readonly string[] RequiredHeadings =
[
    "## PERMANENT USER-APPROVED AGENT PROMPT",
    "## WHO MUST FOLLOW THESE RULES",
    "## Language policy",
    "## EXECUTION MANDATE",
    "## TOKEN ECONOMY",
    "## QUEUE ADDITIONS",
    "## CONTINUE PROTOCOL",
];
```

b) Replace the `arguments` list initializer with exactly:

```csharp
var arguments = new List<string>
{
    "-NoProfile",
    "-NonInteractive",
    "-File",
    scriptPath,
};
```

Nothing else in the tool changes.

Step 3 — `tests/Talvora.Smoke/SmokeScenarios.AgentBrief.cs`: add these assertions to the existing live scenario for this repository (use the scenario's existing failure style):

```csharp
foreach (var mustContain in new[] { "ç", "ğ", "—", "§" })
{
    if (!response.Context.Contains(mustContain, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"agent_brief context lost the character '{mustContain}'.");
    }
}
if (!response.AgentsSections.StartsWith(
        "## PERMANENT USER-APPROVED AGENT PROMPT", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "agent_brief must start with the permanent-prompt section.");
}
```

(If the scenario calls the tool with a `card`, call it with `card: "N5"`; the N5 card cites §3.1, so `§` and `—` are guaranteed in the context.)

Step 4 — deploy (runtime change) and verify live through the installed service: `talvora_agent_brief {workspaceRoot, card: "N5"}` → context contains `### 3.1 "Kod terminali"` exactly as in `docs/codex/DESIGN-WORKLOG.md`.

### D6 — Context helper finds `##` and `###` card headings

> Audit 2026-10-20 (3), finding 4.

**Files:** `scripts/Get-CodexContext.ps1`, `tests/QualityGateRegression.ps1`.

**Do:** match card headings with `^#{2,3} <ID> — ` (exact ID, no prefix match: `D1` must not match `D10`); the card ends at the next heading of the same or higher level.

**Acceptance:** fixture test for `## D2 — `, `### N5 — `, `### UI1 — `, and `D1` vs `D10`; `-Card D7` prints this roadmap's D7 card.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `scripts/Get-CodexContext.ps1`: replace the single line

```powershell
$cardPattern = '(?ms)^###\s+' + [regex]::Escape($Card) + '\s+[—–-].*?(?=^#{2,3}\s+|\z)'
```

with

```powershell
$cardPattern = '(?ms)^#{2,3}[ \t]+' + [regex]::Escape($Card) + '[ \t]+[—–-].*?(?=^#{2,3}[ \t]+|\z)'
```

Nothing else in the script changes. (`[ \t]+` after the id is what keeps `D1` from matching `D10`.)

Step 2 — `tests/QualityGateRegression.ps1`: add one fixture block (same style as the existing fixtures in that file: temporary repo copy with its own `docs/codex/STATUS.md` and `docs/codex/ROADMAP.md`). ROADMAP fixture content — the lines below are shown with a leading `|` so this roadmap's own heading parser does not see them; write each line **without** the `| ` prefix:

```text
| ## D2 — two-hash card
| D2 BODY
| ### D10 — ten
| D10 BODY
| ### Q2a — split card
| Q2A BODY
| ## Next section
```

Assertions: `-Card D2` output contains `D2 BODY` and not `D10 BODY`; `-Card D10` contains `D10 BODY` and not `Q2A BODY`; `-Card D1` throws "could not be found"; `-Card Q2a` contains `Q2A BODY` and not `Next section`.

Step 3 — verify: `pwsh -NoProfile -File tests/QualityGateRegression.ps1`, then `pwsh -NoProfile -File scripts/Get-CodexContext.ps1 -Card D1` on the real repo prints `=== CARD D1 (docs/codex/ROADMAP.md) ===`.

### D7 — Knowledge roots degrade instead of throwing; the gate passes as a normal user

> Audit 2026-10-20 (3), finding 1 (HIGH).

**Files:** `src/Talvora/Tools/KnowledgeTools.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.KnowledgeS6.cs`, `scripts/Invoke-QualityGate.ps1` (only the identity line below).

**Do:**
- In `AddDefaultSearchRoots`, resolve the user profile as: (1) if the process runs as LocalSystem → `WindowsSessionLauncher.GetActiveInteractiveUser()` (today's behaviour); (2) otherwise → the **current process user's** profile (`Environment.GetFolderPath(UserProfile)` + `OneDrive*` environment variables of the current process). Catch `InvalidOperationException` **and** `System.ComponentModel.Win32Exception` around (1); on failure fall back to (2). Never throw from root resolution.
- The S6 regression must pass in both identities: run its root-resolution assertions against an injectable resolver (LocalSystem branch with a fake interactive user; current-user branch for real).
- `Invoke-QualityGate.ps1` prints one line at the start: `Kimlik: <DOMAIN\user>` so every Log entry shows which identity produced the result.

**Acceptance:** `pwsh scripts/Invoke-QualityGate.ps1 -Scope Full` run **as the signed-in user** (not through `talvora_run_powershell`) ends with `Hepsi geçti.`; the same command through `talvora_run_powershell` (LocalSystem) also ends with `Hepsi geçti.`; Log records both identity lines.

**Rule from now on (also in AGENTS.md):** a card's final gate counts only if run as the signed-in user — ChatGPT uses `talvora_user_run` once A1 exists; until then ChatGPT must state "gate run as LocalSystem" in the Log and Claude re-runs it as the user at audit time.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `src/Talvora/Tools/KnowledgeTools.cs`: add `using System.Security.Principal;` to the usings. Add these members to the `KnowledgeTools` class (place them directly above `AddDefaultSearchRoots`):

```csharp
internal sealed record KnowledgeUserProfile(
    string ProfilePath,
    IReadOnlyList<string> OneDriveRoots);

// Test seam only. Production code never sets it.
internal static Func<KnowledgeUserProfile?>? UserProfileResolverOverride { get; set; }

internal static KnowledgeUserProfile? ResolveKnowledgeUserProfile()
{
    if (UserProfileResolverOverride is { } overrideResolver)
    {
        return overrideResolver();
    }

    if (IsRunningAsLocalSystem())
    {
        try
        {
            var user = WindowsSessionLauncher.GetActiveInteractiveUser();
            return CreateKnowledgeUserProfile(
                user.UserProfile,
                name => user.Environment.TryGetValue(name, out var value) ? value : null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // LocalSystem never falls back to its own or another user's profile.
            return null;
        }
    }

    return CreateKnowledgeUserProfile(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable);
}

private static KnowledgeUserProfile? CreateKnowledgeUserProfile(
    string? profile,
    Func<string, string?> readVariable)
{
    if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
    {
        return null;
    }

    var oneDrive = new List<string>();
    foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
    {
        var value = readVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            oneDrive.Add(value);
        }
    }

    return new KnowledgeUserProfile(profile, oneDrive);
}

private static bool IsRunningAsLocalSystem()
{
    using var identity = WindowsIdentity.GetCurrent();
    return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
}
```

Step 2 — in `AddDefaultSearchRoots`, replace the whole `try { var user = WindowsSessionLauncher.GetActiveInteractiveUser(); … } catch (InvalidOperationException) { … }` block with exactly:

```csharp
var userProfile = ResolveKnowledgeUserProfile();
if (userProfile is not null)
{
    var profile = userProfile.ProfilePath;
    roots.Add(Path.Combine(profile, "Documents"));
    roots.Add(Path.Combine(profile, "Desktop"));
    roots.Add(Path.Combine(profile, "Downloads"));
    roots.AddRange(userProfile.OneDriveRoots);

    try
    {
        roots.AddRange(Directory.GetDirectories(
            profile, "OneDrive*", SearchOption.TopDirectoryOnly));
    }
    catch (Exception ex) when (ex is
        UnauthorizedAccessException or IOException or SecurityException)
    {
        // Missing/inaccessible OneDrive directories are optional.
    }
}
```

Nothing else in `KnowledgeTools.cs` changes (Public Documents and `ProgramData\Talvora\Knowledge` lines stay).

Step 3 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.KnowledgeS6.cs`: replace

```csharp
var user = WindowsSessionLauncher.GetActiveInteractiveUser();
var profile = Path.GetFullPath(user.UserProfile!);
```

with

```csharp
var resolved = KnowledgeTools.ResolveKnowledgeUserProfile()
    ?? throw new InvalidOperationException("S6: no knowledge user profile could be resolved.");
var profile = Path.GetFullPath(resolved.ProfilePath);
```

and add, directly after the line `Console.WriteLine("PASS S6 active-session roots exclude other profiles");`, these two checks (they run in every identity):

```csharp
var fakeProfile = Path.Combine(workspace, "fake-profile");
Directory.CreateDirectory(Path.Combine(fakeProfile, "Documents"));
KnowledgeTools.UserProfileResolverOverride =
    () => new KnowledgeTools.KnowledgeUserProfile(fakeProfile, []);
try
{
    var fakeRoots = ((IEnumerable<string>)rootsMethod.Invoke(null, null)!).ToArray();
    Check(fakeRoots.Any(r => r.Equals(Path.Combine(fakeProfile, "Documents"),
            StringComparison.OrdinalIgnoreCase)),
        "the resolved user's Documents folder was not used as a root");

    KnowledgeTools.UserProfileResolverOverride = () => null;
    var noUserRoots = ((IEnumerable<string>)rootsMethod.Invoke(null, null)!).ToArray();
    Check(!noUserRoots.Any(r => r.StartsWith(fakeProfile, StringComparison.OrdinalIgnoreCase)),
        "roots must not contain a user profile when none can be resolved");
}
finally
{
    KnowledgeTools.UserProfileResolverOverride = null;
}
Console.WriteLine("PASS S6 profile resolver degrades without throwing");
```

(`rootsMethod` returns the final-path-resolved existing directories, so `fakeProfile\Documents` must exist — it is created above. If `ResolveFinalPath` returns a different casing or `\\?\` prefix, compare with `Path.GetFullPath` on both sides; do not change `GetSearchRoots`.)

Step 4 — `scripts/Invoke-QualityGate.ps1`: directly after the line `New-Item -ItemType Directory -Path $script:GateCaptureRoot -Force | Out-Null` add exactly:

```powershell
Write-Host ("Kimlik: " + [Security.Principal.WindowsIdentity]::GetCurrent().Name)
```

Step 5 — verify in this order: (a) `dotnet build tests/Talvora.SourceEdit.Regression -c Release --no-restore -v q -nologo -clp:ErrorsOnly`; (b) run the full gate. ChatGPT cannot run it as the signed-in user yet (A1 not done): run it through `talvora_run_powershell`, write "gate run as LocalSystem (Kimlik: NT AUTHORITY\SYSTEM); signed-in run will be checked by Claude" in the Log and mark the card `[x]` — Claude re-runs it as the user at the next audit. Codex runs it in its own shell.

---

### Audit 2026-10-18 (2) · range `audit/2026-10-18 (8b1a611)..dd4ee5b` · executor: ChatGPT via Talvora · result: FIXES REQUIRED

Cards delivered: K0, K1, K2, E5 `[x]`; E1, E2 `[~]` (live Codex session deferred by the user); E3, S1, Q6, N1 blocked with proposals P-1…P-4 (all four now decided in `PROPOSALS.md`).

Verified by Claude: local `Invoke-QualityGate.ps1 -Scope Full` → `49 kontrol, 0 hata` / `Hepsi geçti.`; GitHub CI green for every commit since `fca44fe` (the K1 CI miss was fixed by `fca44fe`); installed runtime `/healthz.sourceCommit = b79ac3e` (E2); `global.json` pinned `10.0.402` + `rollForward: disable`, ILLink 10.0.13 locks; Codex registration on the workstation is `/mcp/dev` + 8 `enabled_tools`, other TOML untouched (`config.toml.bak` kept); **K2 fixed live**: `talvora_apply_patch` add + delete of a probe file in this repository both `committed` through the installed service (probe removed, tree clean); E2 marker live: `"Structured result: see structuredContent."`; proposals were well-evidenced and partial work was reverted as required — good discipline.

| # | Severity | Type | Where | Finding | Fix card |
|---|---|---|---|---|---|
| 1 | MEDIUM | rule / safety | `src/Talvora/SourceEditing/SourceEditPathGuard.cs` `OpenDirectory` (sharing-violation branch, `return OpenDirectory(path, denyDeleteShare: false)`) | K2 asked for identity probing with bounded retries. The implementation additionally **falls back to a shared handle when the rename-protecting (deny-delete) open fails**, i.e. the commit guard silently stops protecting the parent directory against renames. A safety mechanism was weakened without a proposal and without any signal in the result. | D1 |
| 2 | LOW | bug | same file, `ReadAndValidateIdentity`; `SourceEditModels.cs` `SourceEditDirectoryIdentity` | Volume serial changed from the 32-bit `BY_HANDLE_FILE_INFORMATION` value to the 64-bit `FILE_ID_INFO` value. Identities persisted by the previous version (journals pending across an upgrade: `FileId128 == null`) are now compared against a different serial → spurious conflict/quarantine during recovery. | D2 |
| 3 | LOW | bug | `src/Talvora/ToolResultTextPolicy.cs` | Structured **failures** that are not `isError` (`success:false`, `timedOut:true`, non-zero `exitCode`) also lose their text (verified live: a rejected `apply_patch` returned only the marker). Text-reading clients and logs then see no failure at all. | D3 |
| 4 | LOW | tooling | `scripts/Invoke-QualityGate.ps1:127-132` | `HealthHandle` and `NativeInstallerRuntime` are **always** skipped locally, even when the service is installed and answering (verified: both `SKIP` while `/healthz` was up). Local gate never exercises them. | D4 |
| 5 | MEDIUM | robustness | repository files written through Talvora | Work done by ChatGPT through Talvora runs as **LocalSystem**: `scripts/Invoke-QualityGate.ps1`, `src/Talvora/ToolResultTextPolicy.cs`, `docs/codex/STATUS.md`, `.git/index`, `src/Talvora/bin`, `src/Talvora/obj` are now **owned by `NT AUTHORITY\SYSTEM`** (atomic replace creates a new file owned by the writer). Builds/tests also run as SYSTEM. Risk: user-context tools (Codex sandbox, Visual Studio, git) hitting ownership/ACL surprises; artefacts built with SYSTEM's environment/profile. | A1 |
| 6 | LOW | rule | commits `f0b5f9d`+`a8d0177` (K0), `456da3f`+`fca44fe` (K1); commit bodies | Two commits per card (the K1 one was a legitimate CI follow-up; K0's second commit was a fix-up); commit bodies write `Hepsi gecti` without diacritics. Rule clarified in AGENTS.md (EXECUTION MANDATE rule 4, Language policy). No code action. | — |
| 7 | INFO | process | E1/E2 `[~]` | Live Codex round-trip deferred by the user. When Codex is used again, the first session must confirm: `talvora_local` lists 8 tools; a read + patch of a scratch file works; a tool result arrives without duplicate JSON. | user queue U7/U8 |

---

## D1 — Make the rename-protection fallback visible (keep it, never silent)

> **Amended by P-5 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Why:** audit finding 1.

**Files:** `src/Talvora/SourceEditing/SourceEditPathGuard.cs`, `src/Talvora/SourceEditing/SourceEditCommitter.cs` (only to surface the warning), `src/Talvora/SourceEditing/SourceEditModels.cs` (warning code constant only), `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.PathGuard.cs`.

**Do:** keep the fallback (it is what made K2 work on this repository), but record it: the guard exposes `IReadOnlyList<string> UnprotectedDirectories`; the committer adds one `SourceEditWarning(Code: "DIRECTORY_RENAME_UNPROTECTED", Message: "Another process holds delete access to '<relative dir>'; the commit continued without rename protection for that directory.", Path: <dir>)` per directory to the transaction result, and logs it once at Information level. No retry change.

**Don't:** remove the fallback; change identity comparison (D2 does that).

**Acceptance:** the existing external-DELETE-holder regression now also asserts exactly one `DIRECTORY_RENAME_UNPROTECTED` warning; normal commits have no such warning.

#### Exact implementation — copy this, do not reinterpret

Step 1 — `src/Talvora/SourceEditing/SourceEditPathGuard.cs`:

a) Add a field and a property next to the existing `createdDirectories` field:

```csharp
private readonly SortedSet<string> unprotectedDirectories =
    new(StringComparer.OrdinalIgnoreCase);

public IReadOnlyCollection<string> UnprotectedDirectories => unprotectedDirectories;
```

b) Change the signature of the static `OpenDirectory` to return whether rename protection was obtained, and change its body exactly as follows (only the marked lines differ from today):

```csharp
private static SafeFileHandle OpenDirectory(
    string path,
    bool denyDeleteShare,
    out bool renameProtected)                                   // NEW parameter
{
    // (unchanged: desiredAccess / share computation)
    for (var attempt = 0; attempt <= 3; attempt++)
    {
        var handle = CreateFileW(/* unchanged arguments */);
        if (!handle.IsInvalid)
        {
            renameProtected = denyDeleteShare;                  // NEW
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        if (error == SharingViolation && attempt < 3)
        {
            Thread.Sleep(50 << attempt);
            continue;
        }

        if (error == SharingViolation && denyDeleteShare)
        {
            var shared = OpenDirectory(path, denyDeleteShare: false, out _);   // CHANGED
            renameProtected = false;                                         // NEW
            return shared;                                                   // CHANGED
        }

        throw new SourceEditDomainException(/* unchanged */);
    }

    throw new InvalidOperationException("Unreachable source-edit directory open.");
}
```

c) Update the four call sites:
- the two `denyDeleteShare: false` calls (in `CapturePreparedIdentities` and in the expected-identity re-check): add `, out _`.
- in `HoldExisting` (the `denyDeleteShare: true` call):

```csharp
var handle =
    OpenDirectory(
        path,
        denyDeleteShare: true,
        out var renameProtected);
if (!renameProtected)
{
    unprotectedDirectories.Add(path);
}
```

- in `CreateAndHoldDirectory` (the other `denyDeleteShare: true` call): same pattern —

```csharp
handle =
    OpenDirectory(
        path,
        denyDeleteShare: true,
        out var renameProtected);
if (!renameProtected)
{
    unprotectedDirectories.Add(path);
}
```

Step 2 — `src/Talvora/SourceEditing/SourceEditEngine.cs`, in the prepared-transaction commit path (the method that declares `using var pathGuard = committer.AcquirePreparedPathGuard(prepared);`), replace

```csharp
var committed =
    BuildPlanResult(
        plan,
        success: true,
        status: "committed",
        replayed: false,
        error: null);
```

with

```csharp
var committed =
    BuildPlanResult(
        plan,
        success: true,
        status: "committed",
        replayed: false,
        error: null);
if (pathGuard.UnprotectedDirectories.Count > 0)
{
    committed = committed with
    {
        Warnings =
        [
            .. committed.Warnings,
            .. pathGuard.UnprotectedDirectories.Select(directory =>
                new SourceEditWarning(
                    "DIRECTORY_RENAME_UNPROTECTED",
                    $"Another process holds delete access to '{Path.GetRelativePath(plan.WorkspaceRoot, directory)}'; the commit continued without rename protection for that directory.",
                    directory)),
        ],
    };
}
```

and in the `catch` right below (receipt persistence), change `Warnings = [ new SourceEditWarning("SOURCE_EDIT_RECEIPT_PERSISTENCE_PENDING", …) ]` to keep earlier warnings:

```csharp
Warnings =
[
    .. committed.Warnings,
    new SourceEditWarning(
        "SOURCE_EDIT_RECEIPT_PERSISTENCE_PENDING",
        $"Commit is durable in the WAL, but receipt persistence/cleanup needs recovery: {ex.Message}",
        null),
],
```

No logging is added (decision P-5). The journal-recovery path is not changed.

Step 3 — `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.PathGuard.cs`:
- in `DirectoryIdentitySharingWithExternalProcessAsync`, after the existing `Assert(result.Success, …)` add:

```csharp
AssertEqual(
    1,
    result.Warnings.Count(w => w.Code == "DIRECTORY_RENAME_UNPROTECTED"),
    "A commit that lost rename protection must report exactly one DIRECTORY_RENAME_UNPROTECTED warning.");
```

- in `PathGuardBlocksDeleteParentRenameAsync`, after its success assert add:

```csharp
AssertEqual(
    0,
    result.Warnings.Count(w => w.Code == "DIRECTORY_RENAME_UNPROTECTED"),
    "A normally protected commit must not report DIRECTORY_RENAME_UNPROTECTED.");
```

## D2 — Compare legacy directory identities with the legacy serial

**Why:** audit finding 2.

**Files:** `SourceEditPathGuard.cs`, `SourceEditModels.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.PathGuard.cs` (or `.Transaction.cs` for the journal fixture).

**Do:** store both serials: `SourceEditDirectoryIdentity(Path, ulong VolumeSerialNumber, ulong FileId, Guid? FileId128 = null, uint? LegacyVolumeSerialNumber = null)`. New identities fill `VolumeSerialNumber` (FILE_ID_INFO), `FileId128` and `LegacyVolumeSerialNumber` (BY_HANDLE value). Comparison: if `expected.FileId128` is set → compare 64-bit serial + 128-bit id; else (legacy record) → compare `(uint)` BY_HANDLE serial with the stored serial and the 64-bit `FileId`.

**Acceptance:** regression with a journal fixture whose identity has only the old fields (32-bit serial + 64-bit id of the real directory) recovers without conflict; a different directory with the same legacy fields still conflicts.

## D3 — Keep the text of structured failures

> **Amended by P-6 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Why:** audit finding 3.

**Files:** `src/Talvora/ToolResultTextPolicy.cs`, new `src/Talvora/StructuredResultFailure.cs` (shared detector), `src/Talvora/TalvoraDesktopProgressNotifier.cs` (use the shared detector instead of its private copy), `tests/Talvora.Smoke/SmokeScenarios.ToolResultText.cs`.

**Do:** move the existing detection rules from `TryDescribeStructuredFailure` (`timedOut == true`, `success == false`, numeric `exitCode != 0`, case-insensitive property names) into `StructuredResultFailure.IsFailure(JsonElement)`; `ToolResultTextPolicy.Apply` returns the result unchanged when it is a structured failure. The notifier calls the same helper (no behaviour change there).

**Acceptance:** smoke: `success:false`, `timedOut:true`, `exitCode:1` results keep their original text; successful structured results still get the marker; notification regression unchanged.

#### Exact implementation — copy this, do not reinterpret

Step 1 — new file `src/Talvora/StructuredResultFailure.cs`, exactly:

```csharp
using System.Text.Json;

namespace Talvora;

/// <summary>Shared detection of structured tool failures that are not flagged isError.</summary>
internal static class StructuredResultFailure
{
    internal const string TimedOut = "timedOut";
    internal const string Unsuccessful = "success";
    internal const string NonZeroExit = "exitCode";

    internal static bool IsFailure(JsonElement json) => Describe(json) is not null;

    internal static string? Describe(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetPropertyIgnoreCase(json, "timedOut", out var timedOut) &&
            timedOut.ValueKind == JsonValueKind.True)
        {
            return TimedOut;
        }

        if (TryGetPropertyIgnoreCase(json, "success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return Unsuccessful;
        }

        if (TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code) &&
            code != 0)
        {
            return NonZeroExit;
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement json, string name, out JsonElement value)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
```

Step 2 — `src/Talvora/ToolResultTextPolicy.cs`: change the first `if` to

```csharp
if (result.IsError is true ||
    result.StructuredContent is not { } structured ||
    structured.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
    StructuredResultFailure.IsFailure(structured))
{
    return result;
}
```

Step 3 — `src/Talvora/TalvoraDesktopProgressNotifier.cs`: replace the body of `TryDescribeStructuredFailure` with (messages unchanged):

```csharp
switch (StructuredResultFailure.Describe(json))
{
    case StructuredResultFailure.TimedOut:
        message =
            "Bu işlem zaman sınırını aştı ve tamamlanmış sayılmıyor. " +
            "Son doğrulanmış durum ve varsa gerçek hata ayrıntısı kanıt bölümünde korunuyor.";
        return true;
    case StructuredResultFailure.Unsuccessful:
    case StructuredResultFailure.NonZeroExit:
        message = BuildFriendlyResultFailureMessage(toolName);
        return true;
    default:
        message = string.Empty;
        return false;
}
```

Keep the notifier's own `TryGetPropertyIgnoreCase` (other code uses it).

Step 4 — `tests/Talvora.Smoke/Talvora.Smoke.csproj`: inside the existing `<ItemGroup>` that links `ToolResultTextPolicy.cs`, add

```xml
<Compile Include="..\..\src\Talvora\StructuredResultFailure.cs"
         Link="StructuredResultFailure.cs" />
```

Step 5 — `tests/Talvora.Smoke/SmokeScenarios.ToolResultText.cs`: add at the end of `RunToolResultTextPolicy()`:

```csharp
foreach (var failureJson in new object[]
{
    new { success = false, error = "rejected" },
    new { timedOut = true },
    new { exitCode = 1, standardError = "boom" },
})
{
    var failureStructured = JsonSerializer.SerializeToElement(failureJson);
    var failureText = failureStructured.GetRawText();
    var failureInput = new CallToolResult
    {
        StructuredContent = failureStructured,
        Content = [new TextContentBlock { Text = failureText }],
    };
    var failureOutput = ToolResultTextPolicy.Apply(failureInput);
    if (failureOutput.Content.Count != 1 ||
        failureOutput.Content[0] is not TextContentBlock kept ||
        kept.Text != failureText)
    {
        throw new InvalidOperationException(
            "Structured failures must keep their original text: " + failureText);
    }
}
```

Step 6 — deploy and re-run the live probe: `talvora_git_run commit -m "probe"` in this repo (WorkContract refusal, `exitCode: 1`) must now return the refusal text in the text block instead of `Structured result: see structuredContent.`.

## D4 — Run service-dependent checks locally when the service is up

**Why:** audit finding 4.

**Files:** `scripts/Invoke-QualityGate.ps1`, `tests/QualityGateRegression.ps1`.

**Do:** before the PowerShell regressions, probe `http://127.0.0.1:7676/healthz` (2 s timeout, no proxy). If it answers with `isWindowsService = true`: run `HealthHandleRegression.ps1`; additionally run `NativeInstallerRuntimeRegression.ps1` when an interactive user session exists (`(Get-CimInstance Win32_ComputerSystem).UserName` not empty). Otherwise keep today's `SKIP …` lines. CI behaviour unchanged (no service during the gate there).

**Acceptance:** fixture tests for both branches (stubbed probe); local run on this workstation shows `PASS HealthHandle` and `PASS NativeInstallerRuntime`.

---

## Phase A — Same rules for ChatGPT + Talvora as for Codex

The user often implements cards from a ChatGPT chat using the Talvora connector instead of Codex. These cards make Talvora itself carry and enforce the working contract, so the rules do not depend on the chat remembering them.

### A2 — `talvora_agent_brief`: the project's rules and current card in one cheap call

**Why:** ChatGPT does not read `AGENTS.md` by itself; reading it with generic file tools is expensive and easy to skip.

**Files:** new `src/Talvora/Tools/AgentBriefTools.cs`, `src/Talvora.Shared/TalvoraToolManifest.cs`, `src/Talvora.Shared/TalvoraMcpToolSurfacePolicy.cs` (Dev surface + Full), `src/Talvora/SourceEditing/SourceEditRoutingContract.cs` (server instructions), surface/metadata smoke tests, README tool list.

**Do:**
- Tool `talvora_agent_brief { workspaceRoot: string, card?: string }`, read-only, idempotent, title **"Proje talimatlarını getir"**. Description (English, exact): "Return the binding agent rules (AGENTS.md mandate, token economy, continue protocol) and the current card for a workspace. Call this first in every session before changing files in that workspace."
- Behaviour: resolve `workspaceRoot` (must exist). If `AGENTS.md` exists, extract the sections `## WHO MUST FOLLOW THESE RULES`, `## Language policy`, `## EXECUTION MANDATE…`, `## TOKEN ECONOMY…`, `## QUEUE ADDITIONS…`, `## CONTINUE PROTOCOL…` by heading (until the next `## `). If `scripts/Get-CodexContext.ps1` exists, run it **as the interactive user** (A1 runner; until A1 lands use `InteractiveUserProcessRunner` directly) with `-Card <card>` when given, 60 s timeout, and append its stdout. Structured result: `{ workspaceRoot, agentsSections: string, context: string, truncated: bool }`; total ≤ 60 000 characters (truncate `context` first, then sections, set `truncated`).
- Server instructions (`SourceEditRoutingContract.ServerInstructions`): append exactly: "If a workspace contains AGENTS.md, call talvora_agent_brief for it before any change and follow its rules strictly; the rules apply to every agent, including chat assistants."
- Surface: Dev and Full only (not Admin). Update counts/tests.

**Acceptance:** smoke on this repo: sections present, context contains the first open card, size ≤ 60 000; a workspace without AGENTS.md returns empty sections and no error; tools/list shows the Turkish title.

### A1 — Run builds/tests as the signed-in user and keep file ownership

> **Amended by P-7 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Why:** audit finding 5.

**Files:** new `src/Talvora/Tools/UserRunTools.cs`, `src/Talvora.Shared/InteractiveUserProcessRunner.cs` (reuse only), `src/Talvora/SourceEditing/SourceEditCommitter.cs` + `src/Talvora.Shared/AtomicFile.cs` (owner preservation), manifest/surface policy, smoke + source-edit regression.

**Do:**
1. Tool `talvora_user_run { executable, arguments[], workingDirectory, environment?, timeoutSeconds = 600, maxOutputCharacters = 16000 }` — Dev + Full surfaces, title **"Kullanıcı olarak çalıştır"**, description (English, exact): "Run an executable as the signed-in Windows user (not LocalSystem) and return exit code and bounded output. Use for builds, tests and scripts inside user workspaces." Implementation = `InteractiveUserProcessRunner.RunAsync` (no shell; arguments as a vector); output shaping exactly as E3's head 2 000 + tail rule; `success = exitCode == 0 && !timedOut`.
2. Ownership: before replacing an existing file, capture its owner SID; after the atomic replace, set the same owner on the new file (`FileSecurity.SetOwner`, SYSTEM holds `SeRestorePrivilege` — enable it via the existing privilege helper or `AdjustTokenPrivileges`; verify with Context7 `/dotnet/docs`). New files and directories created by a source-edit transaction get the owner of the workspace root. Failure to set the owner adds a `SourceEditWarning("OWNER_NOT_PRESERVED", …)` and does not fail the commit.
3. One-time repair on this workstation (log it, no script in the repo): as SYSTEM, `icacls "C:\Users\tayla\Talvora-MCP" /setowner "MSI\tayla" /T /C /Q`, then `git status` must still be clean.

**Don't:** change `talvora_run_powershell` / `talvora_run_process` (they stay LocalSystem by design); touch files outside the workspace.

**Acceptance:** smoke: `talvora_user_run` of `whoami` returns the interactive user; a source-edit replace of a user-owned file keeps the owner (regression with a temp file owned by the current user); a new file gets the workspace-root owner; after the repair, `Get-Acl` of the five files listed in finding 5 shows `MSI\tayla`.

#### Exact implementation — copy this, do not reinterpret

Part A — the tool.

Step A1 — new file `src/Talvora/Tools/UserRunTools.cs`, exactly:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraUserRunResponse(
    bool Success,
    int ExitCode,
    bool TimedOut,
    string User,
    string WorkingDirectory,
    long ElapsedMilliseconds,
    string StandardOutput,
    string StandardError,
    long StandardOutputTotalCharacters,
    long StandardErrorTotalCharacters);

[McpServerToolType]
public static class UserRunTools
{
    [McpServerTool(
        Name = "talvora_user_run",
        Title = "Kullanıcı olarak çalıştır",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraUserRunResponse)),
     Description("Run an executable as the signed-in Windows user (not LocalSystem) and return exit code and bounded output. Use for builds, tests and scripts inside user workspaces.")]
    public static async Task<TalvoraUserRunResponse> Run(
        string executable,
        string[] arguments,
        string workingDirectory,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 600,
        int maxOutputCharacters = ToolOutputCompactor.DefaultMaximumOutputCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeoutSeconds is < 1 or > 43200)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var cwd = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException(cwd);
        }

        var user = WindowsSessionLauncher.GetActiveInteractiveUser();
        var result = await InteractiveUserProcessRunner.RunAsync(
            executable,
            cwd,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken,
            ProcessRunner.DefaultMaximumCapturedCharacters,
            user,
            closeStandardInput: true).ConfigureAwait(false);

        return new TalvoraUserRunResponse(
            result.ExitCode == 0 && !result.TimedOut,
            result.ExitCode,
            result.TimedOut,
            user.User,
            cwd,
            result.ElapsedMilliseconds,
            ToolOutputCompactor.Compact(
                FileLog.RedactSensitiveData(result.StandardOutput), maxOutputCharacters),
            ToolOutputCompactor.Compact(
                FileLog.RedactSensitiveData(result.StandardError), maxOutputCharacters),
            result.StandardOutputTotalCharacters,
            result.StandardErrorTotalCharacters);
    }
}
```

(`ToolOutputCompactor` is the E3 helper; if it lives in another namespace, add that `using`. Do not create a second compactor.)

Step A2 — register `talvora_user_run` in `TalvoraToolManifest.cs` and on **Dev + Full** only in `TalvoraMcpToolSurfacePolicy.cs` (not Admin); update the surface/metadata smoke expectations and README tool list. These are supporting changes (no proposal).

Part B — ownership.

Step B1 — new file `src/Talvora/SourceEditing/FileOwnership.cs`, exactly:

```csharp
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Talvora.SourceEditing;

internal static partial class FileOwnership
{
    internal static SecurityIdentifier? TryGetOwner(string path)
    {
        try
        {
            FileSystemSecurity security = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Owner);
            return security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            PrivilegeNotHeldException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool TrySetOwner(string path, SecurityIdentifier owner)
    {
        try
        {
            if (TryGetOwner(path) is { } current && current == owner)
            {
                return true;
            }

            using var restore = RestorePrivilege.Enable();
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var security = directory.GetAccessControl(AccessControlSections.Owner);
                security.SetOwner(owner);
                directory.SetAccessControl(security);
            }
            else
            {
                var file = new FileInfo(path);
                var security = file.GetAccessControl(AccessControlSections.Owner);
                security.SetOwner(owner);
                file.SetAccessControl(security);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            PrivilegeNotHeldException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Enables SeRestorePrivilege on the process token for the scope, then restores it.</summary>
    private sealed partial class RestorePrivilege : IDisposable
    {
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x00000002;

        private readonly nint token;
        private readonly TokenPrivileges previous;
        private readonly bool changed;

        private RestorePrivilege(nint token, TokenPrivileges previous, bool changed)
        {
            this.token = token;
            this.previous = previous;
            this.changed = changed;
        }

        internal static RestorePrivilege Enable()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            {
                return new RestorePrivilege(0, default, false);
            }

            var privileges = new TokenPrivileges { PrivilegeCount = 1, Attributes = SePrivilegeEnabled };
            if (!LookupPrivilegeValueW(null, "SeRestorePrivilege", out privileges.Luid) ||
                !AdjustTokenPrivileges(token, false, ref privileges,
                    (uint)Marshal.SizeOf<TokenPrivileges>(), out var previous, out _) ||
                Marshal.GetLastPInvokeError() != 0)
            {
                // Not held (normal user): owner changes to another account will fail and
                // are reported as OWNER_NOT_PRESERVED by the caller.
                return new RestorePrivilege(token, default, false);
            }

            return new RestorePrivilege(token, previous, true);
        }

        public void Dispose()
        {
            if (changed)
            {
                var restore = previous;
                AdjustTokenPrivileges(token, false, ref restore, 0, out _, out _);
            }

            if (token != 0)
            {
                CloseHandle(token);
            }
        }

        // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES: LUID is 4-byte aligned → Pack = 4 (16 bytes total).
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenProcessToken(nint process, uint access, out nint token);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LookupPrivilegeValueW(string? system, string name, out long luid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AdjustTokenPrivileges(
            nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
            ref TokenPrivileges newState, uint bufferLength,
            out TokenPrivileges previousState, out uint returnLength);

        [LibraryImport("kernel32.dll")]
        private static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);
    }
}
```

(Layout note: `Marshal.SizeOf<TokenPrivileges>()` must be 16. Enforce it with this static constructor inside `RestorePrivilege`: `static RestorePrivilege() { if (Marshal.SizeOf<TokenPrivileges>() != 16) throw new InvalidOperationException("TOKEN_PRIVILEGES layout mismatch."); }` (the ownership regression then exercises it). If Context7/Win32 docs show a different size, fix only the layout — this is an API-detail fill, not a design change.)

Step B2 — `src/Talvora/SourceEditing/SourceEditPathGuard.cs`: add

```csharp
private readonly SortedSet<string> ownerNotPreserved = new(StringComparer.OrdinalIgnoreCase);

public IReadOnlyCollection<string> OwnerNotPreserved => ownerNotPreserved;

public System.Security.Principal.SecurityIdentifier? WorkspaceRootOwner { get; private set; }

public void RecordOwnerNotPreserved(string path) => ownerNotPreserved.Add(path);
```

In `AcquirePrepared` set `guard.WorkspaceRootOwner = FileOwnership.TryGetOwner(prepared.ChangeSet.WorkspaceRoot);` right after `var guard = new SourceEditPathGuard();`; in `AcquireJournal` use `plan.WorkspaceRoot`. In `CreateAndHoldDirectory`, directly after the successful `CreateDirectoryW(path, IntPtr.Zero)` call add:

```csharp
if (WorkspaceRootOwner is { } rootOwner &&
    !FileOwnership.TrySetOwner(path, rootOwner))
{
    RecordOwnerNotPreserved(path);
}
```

Step B3 — `src/Talvora/SourceEditing/SourceEditCommitter.cs`:
- `CommitUpdateAsync`: first line inside the method after the `expectedBefore` assignment: `var originalOwner = FileOwnership.TryGetOwner(change.FullPath);`. After the final `FlushPathToDisk(change.FullPath);` add:

```csharp
if (originalOwner is not null &&
    !FileOwnership.TrySetOwner(change.FullPath, originalOwner))
{
    pathGuard.RecordOwnerNotPreserved(change.FullPath);
}
```

- `CommitAddAsync`: after `FlushPathToDisk(target);` add:

```csharp
if (pathGuard.WorkspaceRootOwner is { } rootOwner &&
    !FileOwnership.TrySetOwner(target, rootOwner))
{
    pathGuard.RecordOwnerNotPreserved(target);
}
```

- `CommitMoveAsync`: if the move path uses `File.Replace` (edited move), apply the same `originalOwner` pattern as update to the destination; a plain rename keeps the owner — no change.

Step B4 — `src/Talvora/SourceEditing/SourceEditEngine.cs`: in the same place as D1's warning merge, add (after the D1 block):

```csharp
if (pathGuard.OwnerNotPreserved.Count > 0)
{
    committed = committed with
    {
        Warnings =
        [
            .. committed.Warnings,
            .. pathGuard.OwnerNotPreserved.Select(path =>
                new SourceEditWarning(
                    "OWNER_NOT_PRESERVED",
                    $"The file owner of '{Path.GetRelativePath(plan.WorkspaceRoot, path)}' could not be preserved.",
                    path)),
        ],
    };
}
```

Step B5 — tests in `tests/Talvora.SourceEdit.Regression` (new runner partial `SourceEditRegressionRunner.Ownership.cs`, registered like the other partials):
1. update keeps owner: create a file in a fixture workspace, read its owner, apply an update, assert owner unchanged and no `OWNER_NOT_PRESERVED` warning;
2. add takes the workspace-root owner: assert the new file's owner equals `FileOwnership.TryGetOwner(fixture.Root)`;
3. (only when the test process runs as LocalSystem) set the fixture file owner to `BUILTIN\Administrators` first, update, assert it is still `BUILTIN\Administrators`. Skip with a `SKIP` line otherwise.

Part C — one-time repair on this workstation (no script in the repo).

Run once through `talvora_run_powershell` (LocalSystem):

```powershell
icacls "C:\Users\tayla\Talvora-MCP" /setowner "MSI\tayla" /T /C /Q
git -C "C:\Users\tayla\Talvora-MCP" -c safe.directory=C:/Users/tayla/Talvora-MCP status --short
```

Then check owners of the six paths from P-7 with `(Get-Acl <path>).Owner` → all `MSI\tayla`. Paste the six lines (path → owner) into the Log.

Part D — from now on the agent runs builds/tests/gate with `talvora_user_run` (`executable: "pwsh"`, `arguments: ["-NoProfile","-File","scripts/Invoke-QualityGate.ps1","-Scope","Full"]`, `workingDirectory: repo root`). The Log's gate line must show `Kimlik: MSI\tayla`. Run protocol step 2 (`git fetch origin`, `git pull --ff-only origin main`) the same way (`executable: "git"`), so the signed-in user's SSH keys are used (audit 4, finding 2).

### A3 — Work-contract gate in `talvora_git_run`

**Why:** the one-card-one-commit / trailer / STATUS rules must hold no matter which agent commits.

**Files:** new `src/Talvora/SourceEditing/WorkContractGate.cs`, `src/Talvora/Tools/GitTools.cs` (call it for `commit`, using `GitArgumentParser` from S8 — if S8 is not done yet, implement the parser here exactly as specified in S8 and mark S8's parser part as done in the Log), new `.talvora/work-contract.json` in this repo, tests.

**Do:**
- Opt-in file `.talvora/work-contract.json`: `{ "schemaVersion": 1, "statusFile": "docs/codex/STATUS.md", "cardTrailer": "Card", "legacyCardTrailers": ["Codex-Card"], "auditTagPrefix": "audit/" }`.
- When the repository has this file and the git subcommand is `commit`: read the message from `-m`/`-F`/`--file` arguments; refuse (structured failure, English message, nothing executed) unless (1) the message has exactly one `Card: <ID>` trailer (or a legacy one), (2) `statusFile` is staged (`git diff --cached --name-only` contains it), (3) no commit after the newest `audit/*` tag already carries the same card id — **unless** the body contains a line `CI follow-up: <run id>` (EXECUTION MANDATE rule 4 exception). `--amend` is refused in such repositories.
- Repositories without the file are unaffected. Document honestly in `SECURITY.md`: commits made through general process/PowerShell tools are not gated.

**Acceptance:** table-driven tests (valid; missing trailer; two trailers; STATUS not staged; duplicate card after the audit tag; duplicate with CI follow-up line; `--amend`; repo without contract file).
