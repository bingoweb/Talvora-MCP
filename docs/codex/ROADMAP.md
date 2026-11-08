# Talvora — Codex Roadmap (task cards)

> **Only open cards live here** (user decision 2026-11-07). Claude removes finished cards and older audit sections at every audit; git keeps them (`git show audit/<tag>^:docs/codex/ROADMAP.md` is this file before that audit's cleanup; the first cleanup is `audit/2026-11-07-40`).
> Every card is **one agent task and one commit**. Order = `docs/codex/STATUS.md` → "Agent queue".
> Card template: **Why** (finding + evidence) · **Files** · **Do** · **Don't** · **Acceptance**.
> Language: this file is English. Everything Talvora shows to people (Control Center, tray, desktop worklog, error messages returned to users) is **ordinary Turkish** (AGENTS.md). The agent reports to the user in **Turkish**.
> Design specs: `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), `docs/codex/DESIGN-CONTROL-CENTER.md` (Gözlemevi: information architecture, copy deck §9 and accessibility §10 stay valid). New UI and worklog cards are written in this file.

---

## General rules for every card

- AGENTS.md applies in full (Context7 gate, modern-by-default, delivery gates, desktop progress contract, `talvora_apply_patch` as primary editor).
- **Test first** (red), then code. Prefer **behavioural** tests. Do **not** add new regex-over-source assertions unless the card prescribes one; when you touch a file covered by a `*SourceRegression.ps1`, replace the affected assertions with behaviour where feasible, otherwise update them in the same commit and say so in the Log.
- **Definition of done** (every card):
  1. `dotnet restore Talvora.slnx --locked-mode` and the installer project restore pass **without** env overrides.
  2. `dotnet build Talvora.slnx -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors`).
  3. Targeted tests for the card + the always-on set (`scripts/Invoke-QualityGate.ps1`, last line `Hepsi geçti.`).
  4. Runtime/packaging/shared-infrastructure cards: canonical installer (`scripts/Deploy-Windows-Installer.ps1`) + exact-installed smoke; record the installed `sourceCommit` from `/healthz`.
  5. `.context7/verification.json` holds this change only (AGENTS.md → CONTEXT7 PROTOCOL). There is no Awwwards evidence.
  6. `docs/codex/STATUS.md` updated (queue tick + one Log entry, English), one commit with trailer `Card: <ID>`; mirror the evidence to the active Gitea issue.
- User-facing strings: ordinary Turkish, no tool names / exit codes / stack traces / transport terms. Errors say what went wrong, what is protected, what happens next. Example: `"Bu istek Talvora'ya yerel bir uygulamadan gelmediği için reddedildi. Sistem güvende; bağlantınızı Talvora ayarlarından yeniden yapın."`
- Tool **descriptions** stay English (model-facing). Tool **titles** (human-facing in client approval dialogs) are short Turkish.
- Never print, copy or commit secrets. Never read files in `C:\ProgramData\Talvora\Secrets` content; listing names and ACLs is enough.
- Push as STATUS → Fixed facts → "Push" says.

---

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-08 (43, deep, the uv/Rust/user-toolchain modernization) · range `audit/2026-11-08-42..154414b` plus every subsystem the move touches · result: D52–D56, [C] UR6 done, WL3 items 7–8

The user asked for a deep bug check of the modernization ("bu geçiş ve modernizasyon sırasında oluşan bir bug, hata ya da eksik kalan bir şey var mı"): UV1, RS1, RS2, D51, UR1–UR5. Claude read every changed line, compared `InteractiveUserProcessRunner` with `ProcessRunner`, traced every program launch that still runs as LocalSystem and tried each suspicion on the installed 00a9616.

Correct and confirmed: `SignedInUserToolchain` keeps no token handle (the user context is a copied environment dictionary); D51 covers every tool whose top-level `exitCode` belongs to another process (the Codex and Claude job records keep theirs nested); the env tools already reach the signed-in user (`target=user`); nothing has written into the service profile since the 00a9616 install (newest writes 2026-11-07 22:13–22:47 UTC, install 23:57 UTC) and none of UR5's folders came back; the user is in `docker-users` (Docker Desktop was not running, so the engine itself could not be checked); 7676 missing from the TCP listener table is this Windows build (netstat lists 4 listeners in all), not Talvora; no other project's AGENTS.md names a removed pip tool; the Control Center's Çalışma günlüğü shows the moved tools with correct Turkish titles. Measured cost: each program that runs as the user now starts PowerShell first (≈0.4 s against ≈0.02 s direct), so an `*_info` call is 1–2 s slower; accepted.

Findings:
1. **D52** — a `.cmd`/`.bat` program that runs as the user gets its arguments re-parsed by cmd.exe (live: `talvora_npm_run ["--version","a&echo","TALVORA_BATCH_PROBE"]` printed `12.2.1` and then `TALVORA_BATCH_PROBE`).
2. **D53** — the first `tools/list` after every service start carries "an caller-supplied" wherever a description said "an arbitrary"; the second list is repaired because the wire policy normalizes the shared tool objects again. This is the `--metadata-policy-only` stop seen right after the 00a9616 install. Two more slips of the same function are live in every list: "any accessible" becomes "accessible" without an article (eleven tools), and `talvora_java_run`/`talvora_javac_run` end with "resolution;".
3. **D54** — `talvora_resolve_command cargo` answers `found: false` (it looks at the service PATH; since U14 only the user's PATH has cargo); the machine `JAVA_HOME`/`ANDROID_HOME` and the newest Adoptium JDK win over the user's own value, although Windows gives the user's value precedence (latent: no user value is set here); the smoke never checks `talvora_python_info`'s uv fields; the UR policy test names its files one by one; the Python "not found" text points to Chocolatey; RS1's private runner duplicates `RunCliAsync`.
4. **D55 + D56** — `talvora_job_start` and `talvora_dev_server_start` still start their program as LocalSystem. Live: `talvora_job_start {"executable":"cargo","arguments":["--version"]}` → "An error occurred trying to start process 'cargo' … Sistem belirtilen dosyayı bulamıyor."; a SYSTEM `npm run dev`, `dotnet watch` or `go run` would also refill the caches UR5 and UR6 removed and leave SYSTEM-owned files in a repository. User decision 2026-11-08 ("Evet, varsayılan sen ol"): jobs and dev servers run as the signed-in user by default, and `runAs: "service"` keeps LocalSystem for a job that needs it; while nobody is signed in, such a job cannot start. Windows does not hand handles to a process in another session (CreateProcessAsUser: handles are not inherited across sessions), so D55 adds a small relay that runs inside the user's session and D56 wires the job system to it. After D56 Claude adds the routing line to AGENTS.md.
5. **[C] UR6 (done by Claude in this audit)** — UR5's list missed four service-profile caches, untouched since the install: NuGet v3-cache 791.7 MB, the pnpm store 775.4 MB, the pnpm cache 303.8 MB and `.android` 318.7 MB. The user chose "Hepsini sil"; a LocalSystem job deleted exactly these four (sizes re-measured first, unchanged); the full installed smoke afterwards printed `TALVORA MCP SMOKE GREEN` and none of the eleven deleted folders (UR5 and UR6) came back.
6. **WL3 items 7–8 (Claude's look work)** — a failed session shows only "Başarısız" although its `PlainReason` and `NextStep` are in the data (user: "Evet, nedeni göster"); "Kanıtlar" stays as a lone heading when a session has no evidence.

Seen in passing, outside Talvora: `C:\Users\tayla\Repos\Classroom` and its `.git` are owned by SYSTEM since 2026-10-17, so the user's own git refuses the repository ("dubious ownership"); for Classroom's next audit.

Queue: D52, D53, D54, D55, D56 first (D55 needs D52's `LiteralProcessStart`, D56 needs D55), then the audit-42 cards, PK1 and PK2.

### D53 — Tool descriptions are normalized once, in the right order, as whole sentences

**Why:** `TalvoraMcpToolMetadataPolicy.NormalizeDescription` replaces "an caller-supplied" → "a caller-supplied" before it replaces "arbitrary" → "caller-supplied", so one pass turns every "an arbitrary" into "an caller-supplied". `McpToolMetadataWirePolicy.Apply` writes the result back into the tool objects that every request shares (its own comment: "the Tool objects are shared by every surface's request"), so the second `tools/list` normalizes the text again and repairs it. The first client after each service start therefore receives about twenty broken descriptions (`talvora_dotnet_run`, `talvora_npm_run`, `talvora_go_run`, `talvora_cargo_run`, `talvora_java_run`, `talvora_http_request`, `talvora_flutter_run` …) and keeps them in its cache; on 2026-11-08 the installed `--metadata-policy-only` was that first client and stopped with "Live description contains grammar regression: talvora_dotnet_run", then passed on the rerun. Two more slips of the same function are live in every list: "any accessible" → "accessible" leaves the noun without an article ("Run Cargo with a caller-supplied argument vector in accessible working directory.", also `talvora_rustc_run`, `talvora_java_run`, `talvora_javac_run`, `talvora_npm_install`, `talvora_npm_ci`, `talvora_archive_list`, `_create`, `_extract`, `talvora_json_get`, `talvora_read_text_range`, `talvora_tail_text`, `talvora_xml_query`), and `NoAllowDenySentence` removes "no … allowlist is applied." after a semicolon, so `talvora_java_run` and `talvora_javac_run` end with "resolution;".

**Files:** `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs`, `src/Talvora/McpToolMetadataWirePolicy.cs`, `tests/Talvora.Notification.Regression/Program.D53.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`.

**Do:**
1. Red first: `Program.D53.cs` with `AssertD53DescriptionsAsWholeSentences()`, called right after `AssertD51ReportedExitCodesAsync()`. For every public static method in the `Talvora` assembly with `[McpServerTool]` (a non-empty `Name`) and `[Description]`: `once = TalvoraMcpToolMetadataPolicy.NormalizeDescription(name, text)`, `twice = NormalizeDescription(name, once)`. Fail with `D53: <name> changes when normalized twice: <once> → <twice>` when they differ; with `D53: <name> reads: <once>` when `once` matches `\ban caller-supplied\b` (case-insensitive) or ends with `;`; and, when the source text contains "any accessible", when `once` does not contain "an accessible" as many times. Fail when fewer than 200 tools were checked. Print `D53_DESCRIPTIONS_GREEN`. Today it fails on the first "an arbitrary" tool.
2. `NormalizeDescription`: move the `"an caller-supplied"` → `"a caller-supplied"` replacement to directly after the `"arbitrary"` → `"caller-supplied"` replacement; replace `"any accessible"` with `"an accessible"` (instead of `"accessible"`); add `SemicolonNoAllowDenyClause` = `;\s*no [^.]*\b(?:allow-?list|allowlist|deny-?list|denylist)\b[^.]*\.` (IgnoreCase, CultureInvariant, Compiled), replaced by `"."` before `NoAllowDenySentence` runs.
3. `McpToolMetadataWirePolicy.Apply`: normalize each tool's description once per tool name and reuse it (a `ConcurrentDictionary<string, string?>` keyed by `tool.Name`, filled with `NormalizeDescription(tool.Name, tool.Description)` the first time, the way `DescribedInputSchemas` caches the schema), so a request never normalizes text a request produced.

**Don't:** change any other description text, title, annotation or schema; change the smoke's grammar check.

**Context7:** `/dotnet/docs`, queries "ConcurrentDictionary GetOrAdd" and "RegexOptions Compiled". Vendor sources: `https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd`, `https://learn.microsoft.com/dotnet/standard/base-types/regular-expression-options`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy; the first `--metadata-policy-only` right after the install is green (before the fix that first run stops on "an caller-supplied", P-50), then the full installed smoke green; CI.

### D54 — Command lookup, JAVA_HOME and ANDROID_HOME follow the signed-in user; the small UR follow-ups

**Why:** (1) `talvora_resolve_command` resolves on the Talvora service PATH. Since U14 removed `C:\Users\tayla\.cargo\bin` from the machine PATH it answers `{"command":"cargo","found":false,"paths":[]}` (live, 2026-11-08) while `talvora_cargo_run` runs the user's cargo 1.99.1, so an agent that checks first concludes Rust is missing. (2) `EnumerateModernJavaHomes` (`BuildRunnerTools.Jvm.cs`) tries the machine `JAVA_HOME`, then the newest Eclipse Adoptium JDK, and the signed-in user's `JAVA_HOME` last; `ResolveAndroidSdkRoot` (`MobileTools.Android.cs`) tries the machine `ANDROID_HOME`/`ANDROID_SDK_ROOT` before the user's. Windows gives a user variable precedence over the machine one, and these tools now run in the user's environment, so a user-level `JAVA_HOME` or `ANDROID_HOME` is ignored (latent today: none is set). (3) The installed smoke never checks `talvora_python_info`'s `uvAvailable`/`uvVersion`. (4) `Assert-SignedInUserToolchain` names its files one by one, so a new `BuildRunnerTools.*.cs` would escape it. (5) `RequirePython`'s message sends the user to "Talvora Chocolatey tools", against the uv rule. (6) RS1's private `RunAsSignedInUserAsync` (`BuildRunnerTools.GoRust.cs`) duplicates `RunCliAsync` since UR1.

**Files:** `src/Talvora/Tools/SignedInUserToolchain.cs`, `src/Talvora/Tools/DeveloperTools.Project.cs`, `src/Talvora/Tools/BuildRunnerTools.Jvm.cs`, `src/Talvora/Tools/MobileTools.Android.cs`, `src/Talvora/Tools/RuntimeTools.Python.cs`, `src/Talvora/Tools/BuildRunnerTools.GoRust.cs`, `src/Talvora.Shared/TalvoraToolGuides.cs`, `tests/ModernizationPolicyRegression.ps1`, `tests/Talvora.Smoke/SmokeScenarios.ApplicationDevelopment.cs`, `tests/Talvora.Smoke/SmokeScenarios.Workspace.Runtime.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`.

**Do:**
1. Red first:
   a. `SmokeScenarios.ApplicationDevelopment.cs`, after the RS1 check: when `talvora_rust_info` has a `cargoExecutable`, `talvora_resolve_command {"command":"cargo"}` must answer `found: true` with that path first (case-insensitive); message `D54: resolve_command does not see the signed-in user's cargo.` Red against 00a9616.
   b. `tests/ModernizationPolicyRegression.ps1`: the four `Assert-SignedInUserToolchain` lines take every `BuildRunnerTools*.cs`, `MobileTools*.cs`, `RuntimeTools*.cs` and `WindowsToolchainTools*.cs` file under `src/Talvora/Tools` (`Get-ChildItem -Filter`, repository-relative paths, `WindowsToolchainTools.VisualStudio.cs` left out with the existing comment) instead of fixed lists; and two new lines: `Assert-NotContains 'src/Talvora/Tools/BuildRunnerTools.Jvm.cs' 'EnvironmentVariableTarget\.Machine' 'D54: the signed-in user''s JAVA_HOME wins; a user value overrides the machine value on Windows.'` and the same for `src/Talvora/Tools/MobileTools.Android.cs` with `ANDROID_HOME`. Both new lines are red today.
   c. `SmokeScenarios.Workspace.Runtime.cs`, after `talvora_python_info`: when `CommandResolver.Resolve(["uv.exe", "uv"], [])` is not null, `uvAvailable` must be true and `uvVersion` must start with `uv `; message `D54: python_info does not report the signed-in user's uv.` (a new check, green on 00a9616).
2. `SignedInUserToolchain.ResolveAll(IEnumerable<string> commandNames)`: as the service `CommandResolver.ResolveAll(commandNames, environment: WindowsSessionLauncher.GetActiveInteractiveUser().Environment)`, otherwise `CommandResolver.ResolveAll(commandNames)`.
3. `talvora_resolve_command` gets `string scope = "user"`: `"user"` → `SignedInUserToolchain.ResolveAll([command])`; `"service"` → today's `CommandResolver.ResolveAll([command])`; anything else → `ArgumentOutOfRangeException(nameof(scope), scope, "scope is user or service.")`. The output record stays. Description (attribute and `TalvoraToolGuides`): "Resolve a command on the signed-in user's PATH (scope=user, default) or the Talvora service PATH (scope=service) with Windows PATHEXT. Returns every match when includeAll=true."; guide parameter `("scope", "user (default): the signed-in user's PATH, where the developer tools run; service: the Talvora service PATH.")`.
4. `EnumerateModernJavaHomes`: first `SignedInUserToolchain.Variable("JAVA_HOME")` (when the folder exists), then the Adoptium JDKs, newest first; the `EnvironmentVariableTarget.Machine` read goes (the user's environment block already holds the machine value when the user has none). `ResolveAndroidSdkRoot`: for `ANDROID_HOME`, then `ANDROID_SDK_ROOT`, `SignedInUserToolchain.Variable(variable)` first; the machine-target read goes; the candidate folders stay.
5. `RequirePython`: "Python was not found for the signed-in user. Install it with talvora_uv_run (arguments: python, install) or pass pythonExecutable."
6. `BuildRunnerTools.GoRust.cs`: every `RunAsSignedInUserAsync(` call becomes `RunCliAsync(` with the same arguments; delete `RunAsSignedInUserAsync`.

**Don't:** change any output record, add a tool, or change another resolver.

**Context7:** `/dotnet/docs`, query "Environment.GetEnvironmentVariable EnvironmentVariableTarget User Machine". Vendor sources: `https://learn.microsoft.com/dotnet/api/system.environment.getenvironmentvariable`, `https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-createenvironmentblock`.

**Acceptance:** 1a and 1b red first; Changed and Full `Hepsi geçti.`; deploy; `--metadata-policy-only` and the full installed smoke green; CI.

### D55 — A relay runs one job inside the signed-in user's session

**Why:** D56 runs background jobs and dev servers as the signed-in user (user decision 2026-11-08). The job system keeps a live process with stdin, stdout and stderr and rotating logs (`JobTools`), but LocalSystem cannot give pipe handles to a process in the user's session: `CreateProcessAsUser` does not inherit handles across sessions. The interactive runner solves this for bounded runs with files; a job needs live streams. A small relay inside the user's session starts the program with its own pipes and copies them to three named pipes the service listens on, so `JobTools` keeps its pumps, logs, stdin and stop unchanged.

**Files:** `src/Talvora.Shared/UserJobRelay.cs` (new), `tests/Talvora.Smoke/SmokeScenarios.SharedInfrastructure.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. After D52.

**Do:**
1. Red first: a relay test in `SmokeScenarios.SharedInfrastructure.cs` against a `UserJobRelay.Run` that only throws `NotImplementedException`. Everything runs as the current user: create three `NamedPipeServerStream`s named `talvora-relay-smoke-<guid>-in` (`PipeDirection.Out`), `-out` and `-err` (`PipeDirection.In`), `PipeOptions.Asynchronous`; write a request (step 2) whose program is pwsh with `-NoLogo -NoProfile -NonInteractive -Command "$line = [Console]::In.ReadLine(); [Console]::Out.WriteLine('got:' + $line); [Console]::Error.WriteLine('relay-err'); exit 3"`; start `Task.Run(() => UserJobRelay.Run(requestPath))`; wait up to 10 s for the three connections; write `ping` and a newline to the stdin pipe and dispose it; read the out and err pipes to their end; assert out = `got:ping` and err = `relay-err` (trimmed), `Run` returned 3, the exit-code file holds `3`, the child-PID file holds a positive integer. Then the same `args.cmd` and the first `ProcessRunner` argument list through the relay (stdin pipe disposed at once) → the same `ARG…` lines on the out pipe. Messages start with `D55:`.
2. `UserJobRelay` (public static class) with:
   - `public sealed record UserJobRequest(string Executable, string WorkingDirectory, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string?> Environment, bool ClearEnvironment, string StandardInputPipe, string StandardOutputPipe, string StandardErrorPipe, string ChildProcessIdPath, string ExitCodePath);` (`System.Text.Json` defaults).
   - `public const string RunnerScript` with exactly:
```powershell
param(
    [Parameter(Mandatory = $true)][string]$RequestPath,
    [Parameter(Mandatory = $true)][string]$HelperAssemblyPath
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom($HelperAssemblyPath)
exit ([Talvora.Shared.UserJobRelay]::Run($RequestPath))
```
   - `public static int Run(string requestPath)`: read, then delete the request file; connect `new NamedPipeClientStream(".", name, direction, PipeOptions.Asynchronous)` for stdin (`PipeDirection.In`), stdout and stderr (`PipeDirection.Out`), 15 000 ms each; `var startInfo = LiteralProcessStart.Create(request.Executable, request.WorkingDirectory, request.Arguments, request.Environment, request.ClearEnvironment, out var temporaryCommandScript); startInfo.RedirectStandardInput = true;` start it; write `process.Id` (invariant culture) to `ChildProcessIdPath`; three copy loops on `Task.Run`: the child's `StandardOutput.BaseStream` → stdout pipe and `StandardError.BaseStream` → stderr pipe (when a pipe write throws `IOException`, keep reading the child's stream into nothing until it ends, so the program never blocks on a full pipe); the stdin pipe → `process.StandardInput.BaseStream` with `Flush()` after every chunk, closing `process.StandardInput` when the pipe ends or throws. Then `process.WaitForExit()`, wait at most 5 s for the two output loops, write `process.ExitCode` to `ExitCodePath`, `LiteralProcessStart.DeleteTemporaryCommandScript(temporaryCommandScript)`, return the exit code. When the program cannot start: write the exception message to the stderr pipe, write exit code 1, return 1.

**Don't:** change any tool (D56 wires the relay); inherit handles across sessions; poll.

**Context7:** `/dotnet/docs`, queries "NamedPipeClientStream Connect timeout PipeDirection" and "Process StandardInput BaseStream Flush". Vendor sources: `https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeclientstream`, `https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw`.

**Acceptance:** the relay test red first, then Changed and Full `Hepsi geçti.`; deploy; `--metadata-policy-only` and the full installed smoke green; CI.

### D56 — Background jobs and dev servers run as the signed-in user

**Why:** user decision 2026-11-08 ("Evet, varsayılan sen ol"). `talvora_job_start` ("Start any executable as a long-running background development job under the Talvora LocalSystem service") and `talvora_dev_server_start` (through `JobTools.Start`) are the last developer launchers that run as LocalSystem. Live on 00a9616: `talvora_job_start {"executable":"cargo","arguments":["--version"]}` → "An error occurred trying to start process 'cargo' … Sistem belirtilen dosyayı bulamıyor." (since U14 cargo is only on the user's PATH); a SYSTEM `npm run dev`, `dotnet watch` or `go run` would refill the service-profile caches UR5 and UR6 removed and leave SYSTEM-owned files in a repository (the reason AGENTS.md routes builds to `talvora_user_run`).

**Files:** `src/Talvora/Tools/JobTools.cs`, `src/Talvora/Tools/JobTools.Lifecycle.cs`, `src/Talvora/Tools/JobTools.Persistence.cs`, `src/Talvora/Tools/JobTools.StopDelete.cs`, `src/Talvora/Tools/DevServerTools.Lifecycle.cs`, `src/Talvora.Shared/InteractiveUserProcessRunner.cs`, `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs`, `src/Talvora.Shared/TalvoraToolGuides.cs`, `tests/Talvora.Smoke/SmokeScenarios.Workspace.WatchJobGit.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. After D55.

**Do:**
1. Red first, in the installed smoke (`SmokeScenarios.Workspace.WatchJobGit.cs`, after the existing job steps): (a) `talvora_job_start {"executable": "C:\\Windows\\System32\\whoami.exe", "workingDirectory": root}`; call `talvora_job_get` every 500 ms for up to 30 s until `state` is no longer `Running`; the trimmed stdout from `talvora_job_read_output` must equal (case-insensitive) the trimmed stdout of `talvora_user_run {"executable": "C:\\Windows\\System32\\whoami.exe", "arguments": [], "workingDirectory": root}`; (b) the same job with `"runAs": "service"` must print `nt authority\system`; (c) when `talvora_rust_info` has a `cargoExecutable`: `talvora_job_start {"executable": "cargo", "arguments": ["--version"], "workingDirectory": root}` → stdout starts with `cargo `. Delete each job afterwards with `talvora_job_delete`. Messages start with `D56:`. Red against 00a9616: (a) prints `nt authority\system`, (b) rejects the unknown `runAs`, (c) cannot start.
2. `JobTools.Start(..., string runAs = "user")` accepts `"user"` or `"service"`, otherwise `ArgumentOutOfRangeException(nameof(runAs), runAs, "runAs is user or service.")`. `"service"` is today's path, except that it builds its start info with `LiteralProcessStart.Create(executable, cwd, arguments, environment, clearEnvironment, out var temporaryCommandScript)` (D52) plus `RedirectStandardInput = true`, and deletes the temporary command script when the job's process has exited and in `talvora_job_delete`.
3. `"user"`:
   a. `var user = WindowsSessionLauncher.GetActiveInteractiveUser();` when nobody is signed in, throw `InvalidOperationException("runAs=user needs a signed-in Windows user; sign in or use runAs=service. " + inner.Message)`.
   b. A bare executable resolves with `SignedInUserToolchain.Resolve([executable])` (the user's PATH and PATHEXT, so `npm` finds `npm.cmd`); a rooted path stays; nothing found → `FileNotFoundException($"{executable} was not found on the signed-in user's PATH.")`. A missing working directory defaults to `user.UserProfile`.
   c. `<job folder>\user`: add `public static void ProtectDirectory(string path, string userSid)` to `InteractiveUserProcessRunner` (the ACL `CreateProtectedRunDirectory` sets today: LocalSystem full control, the user modify, inheritance protected) and let `CreateProtectedRunDirectory` call it; create the folder and protect it. Write `UserJobRelay.RunnerScript` there as `jobrunner.ps1` (UTF-8 without BOM) and the `UserJobRequest` as `request.json`, with `ChildProcessIdPath` = `user\child.pid` and `ExitCodePath` = `user\exit.code`.
   d. Three pipes: `talvora-job-<jobId>-in` (`PipeDirection.Out` on the service side), `-out` and `-err` (`PipeDirection.In`), each `NamedPipeServerStreamAcl.Create(name, direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security)`, where `security` allows LocalSystem `FullControl` and the user's SID `ReadWrite | Synchronize`, nothing else.
   e. Start the relay with `WindowsSessionLauncher.StartProcess(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-RequestPath", request, "-HelperAssemblyPath", typeof(UserJobRelay).Assembly.Location], user.SessionId, userFolder, environment: null, visible: false, newConsole: false, expectedUserSid: user.Sid)` (pwsh resolved the way `InteractiveUserProcessRunner` resolves it); wait at most 30 s for the three connections; otherwise kill the relay's process tree, dispose the pipes and throw `InvalidOperationException("The signed-in user's job relay did not connect.")`.
   f. The runtime: `Process` = `Process.GetProcessById(launch.ProcessId)` (the relay, whose exit code is the program's, so state and `exitCode` work as today); `StandardInput` = `new StreamWriter(stdinPipe, new UTF8Encoding(false)) { AutoFlush = true }`; the two pumps read `new StreamReader(stdoutPipe, ConsoleOutputEncoding.Instance)` and `new StreamReader(stderrPipe, ConsoleOutputEncoding.Instance)` into the same log files with the same rotation; the runtime owns the three pipes and disposes them when the job exits, is stopped or deleted. `talvora_job_stop` kills the relay's process tree (the program is its child).
   g. `job.json` stores `RunAs` ("user" or "service"); a job written before D56 reads as "service". No output record changes (`TalvoraJobStartResponse`, `TalvoraJobInfoResponse`, the dev-server records), so clients keep their cached schemas.
4. `talvora_dev_server_start` gets `string runAs = "user"` and passes it to `JobTools.Start`.
5. Texts: `talvora_job_start`'s description override in `TalvoraMcpToolMetadataPolicy` becomes "Start a long-running development job as the signed-in user (runAs=user, default) or under the Talvora Windows service (runAs=service). Use talvora_user_run for bounded commands."; its `[Description]` starts with the same first sentence and keeps the log sentence; `talvora_dev_server_start`'s first sentence becomes "Start any executable through Talvora's persistent job system, as the signed-in user unless runAs=service, and wait for optional TCP/HTTP readiness probes."; `TalvoraToolGuides` adds `("runAs", "user (default): the signed-in Windows user; service: the Talvora LocalSystem service, only for jobs that need it.")` to both tools.
6. The smoke's existing job and dev-server steps stay unchanged and pass as the user; a step that needs LocalSystem because it writes under `C:\ProgramData\Talvora` gets `"runAs": "service"` alone, and the Log names it.

**Don't:** use handle inheritance across sessions; change an output schema, the log rotation or reading, or add a tool.

**Context7:** `/dotnet/docs`, queries "NamedPipeServerStreamAcl.Create PipeSecurity PipeAccessRule" and "Process.Kill entireProcessTree". Vendor sources: `https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create`, `https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights`, `https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw`.

**Acceptance:** the three smoke checks red first against 00a9616; Changed and Full `Hepsi geçti.`; deploy; `--metadata-policy-only` and the full installed smoke green; CI. Then `[~] live pending (Claude)`: at the next audit Claude checks that the service profile gained no developer cache after real use.

### D45 — Çalışma günlüğü measures a session up to its last step

**Why:** audit 42 finding 1 (data truth; Claude's D43). A worklog session closes 90 s after its last step and `EndedAtUtc` is that close, so every real session carries a 90–127 s quiet tail and D43 shows "1 dk 30 sn" for a ping that took a tenth of a second. The duration ends with the newest step (timeline times are step ends). The stored format does not change. The literal blocks below were built and run by Claude in a scratch clone (Release 0 warnings, Notification regression green, Full gate green).

**Files:** `src/Talvora.Tray/WorklogHistoryStore.cs`, `tests/Talvora.Notification.Regression/Program.D45.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`Enumerable.Max` over `DateTimeOffset`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D45.cs`:

```csharp
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D45: a session's duration ends with its last step, not with the card's quiet close 90 s later.</summary>
    private static void AssertD45WorkEndsWithLastStep()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var start = new DateTimeOffset(2026, 11, 7, 15, 44, 23, offset);
        var closed = start.AddSeconds(93);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d45-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new WorklogHistoryStore(Path.Combine(directory, "worklog-sessions.jsonl"), () => now);
            var frame = new DesktopWorklogFrame(
                "d45", 2, closed,
                new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                    "Düzenleme tamamlandı.", "1 dosya değişti", null, 3),
                [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, "Düzenleme tamamlandı.", 3)],
                new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                null, 15);
            store.AppendTerminal(new DesktopProgressMessage(
                "d45", "talvora_apply_patch", "Düzenleme tamamlandı.", "",
                DesktopProgressKind.Completed, closed, 3, null,
                WorklogFrame: frame, SessionClosed: true));
            var session = store.LoadRecent().Single(item => item.SessionId == "d45");
            if (session.WorkEndedAtUtc != start.AddSeconds(3) ||
                LocalTimeText.SessionShort(session.StartedAtUtc, session.WorkEndedAtUtc, now) != "Bugün 15:44 · 3 sn" ||
                LocalTimeText.SessionLong(session.StartedAtUtc, session.WorkEndedAtUtc, now) !=
                    "Bugün 15:44:23 → 15:44:26 · 3 sn")
                throw new InvalidOperationException(
                    $"D45: the work must end with the last step (15:44:26), got {session.WorkEndedAtUtc.ToOffset(offset):HH:mm:ss}.");
            if ((session with { Timeline = [] }).WorkEndedAtUtc != session.EndedAtUtc)
                throw new InvalidOperationException("D45: a session without steps keeps its stored end.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D45_WORK_ENDS_WITH_LAST_STEP_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD45WorkEndsWithLastStep();` on its own line directly below `AssertD43SessionTimeText();`. Red: CS1061 (`WorkEndedAtUtc` does not exist yet).

Step 2. `WorklogHistoryStore.cs`: replace

```csharp
    // D43: the day once, then the time and the measured duration (the Olaylar page's day words).
    [JsonIgnore]
    public string ListTimeText => LocalTimeText.SessionShort(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);

    [JsonIgnore]
    public string DetailTimeText => LocalTimeText.SessionLong(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);
```

with

```csharp
    // D43: the day once, then the time and the measured duration (the Olaylar page's day words).
    // D45: the duration ends with the last step; the card closes 90 s later and that quiet tail is not work.
    [JsonIgnore]
    public string ListTimeText => LocalTimeText.SessionShort(StartedAtUtc, WorkEndedAtUtc, DateTimeOffset.Now);

    [JsonIgnore]
    public string DetailTimeText => LocalTimeText.SessionLong(StartedAtUtc, WorkEndedAtUtc, DateTimeOffset.Now);

    /// <summary>D45: the end of the newest step (timeline times are step ends), kept inside the session.</summary>
    [JsonIgnore]
    public DateTimeOffset WorkEndedAtUtc
    {
        get
        {
            if (Timeline is not { Count: > 0 })
            {
                return EndedAtUtc;
            }

            var last = Timeline.Max(entry => entry.TimestampUtc);
            return last < StartedAtUtc ? StartedAtUtc : last > EndedAtUtc ? EndedAtUtc : last;
        }
    }
```

**Don't:** change `EndedAtUtc`, the stored JSON, the 90-second close, the timeline rows or `LocalTimeText`.

**Acceptance:** `D45_WORK_ENDS_WITH_LAST_STEP_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)` (Claude reads the real Çalışma günlüğü).

### D50 — Çalışma günlüğü keeps every session

**Why:** audit 42 finding 8 (data truth). The service gives one project and one actor the same `SessionId` for all their sessions (`SHA256(projectRoot + actor)` in `TalvoraDesktopWorklogState.GetOrCreateSession`, so the desktop card follows the project), and `WorklogHistoryStore` deduplicates by `SessionId` when it appends, loads and prunes: each new session of that project and actor replaces the previous one. DESIGN-WORKLOG §8 asks for every session of the last 7 days (up to 2,000). Live: a week of ChatGPT work on Talvora-MCP left exactly one "ChatGPT · Dev / Talvora-MCP" entry, and Claude's 00:16 probe replaced Claude's 23:36 one. A history entry becomes the `SessionId` plus its start; a repeated close of the same session stays one entry; a card click still opens the newest session of its id (the list is newest first). The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora.Tray/WorklogHistoryStore.cs`, `src/Talvora.Tray/ViewModels/WorklogHistoryViewModel.cs`, `tests/Talvora.Notification.Regression/Program.D50.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`Enumerable.DistinctBy` keeps the first element of each key), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D50.cs`:

```csharp
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D50: two sessions of the same project and actor are two history entries; a repeated close is one.</summary>
    private static void AssertD50HistoryKeepsEverySession()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d50-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "worklog-sessions.jsonl");
            var store = new WorklogHistoryStore(path, () => now);
            void Close(DateTimeOffset start, string title)
            {
                var closed = start.AddSeconds(93);
                var frame = new DesktopWorklogFrame(
                    "d50-same-project-and-actor", 2, closed,
                    new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                    new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded, title, null, null, 3),
                    [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, title, 3)],
                    new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                    null, 15);
                store.AppendTerminal(new DesktopProgressMessage(
                    "d50", "talvora_apply_patch", title, "", DesktopProgressKind.Completed, closed, 3, null,
                    WorklogFrame: frame, SessionClosed: true));
            }

            Close(new DateTimeOffset(2026, 11, 7, 15, 0, 0, offset), "İlk oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            foreach (var reader in new[] { store, new WorklogHistoryStore(path, () => now) })
            {
                var titles = reader.LoadRecent()
                    .Where(session => session.SessionId == "d50-same-project-and-actor")
                    .Select(session => session.Current.Title)
                    .ToArray();
                if (titles is not ["İkinci oturum", "İlk oturum"])
                    throw new InvalidOperationException(
                        $"D50: expected both sessions newest first, got [{string.Join(", ", titles)}].");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D50_HISTORY_KEEPS_EVERY_SESSION_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD50HistoryKeepsEverySession();` on its own line directly below `AssertD45WorkEndsWithLastStep();` (D45 adds that line). Red: `D50: expected both sessions newest first, got [İkinci oturum].`

Step 2. `WorklogHistoryStore.cs`: replace

```csharp
    [JsonIgnore]
    public bool HasCodePreview => !string.IsNullOrWhiteSpace(Evidence?.CodePreview);
```

with

```csharp
    [JsonIgnore]
    public bool HasCodePreview => !string.IsNullOrWhiteSpace(Evidence?.CodePreview);

    // D50: a project and an actor keep one SessionId across all their sessions (the desktop card follows it);
    // one history entry is that id plus its start, so every session of the last 7 days stays in the history.
    [JsonIgnore]
    public string HistoryKey =>
        SessionId + "@" + StartedAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
```

then replace the line `_sessions!.RemoveAll(item => item.SessionId == session.SessionId);` with `_sessions!.RemoveAll(item => item.HistoryKey == session.HistoryKey);`, in `EnsureLoaded` the line `.DistinctBy(row => row.Session.SessionId)` with `.DistinctBy(row => row.Session.HistoryKey)`, and in `Prune` the line `.DistinctBy(item => item.SessionId)` with `.DistinctBy(item => item.HistoryKey)`.

Step 3. `WorklogHistoryViewModel.cs` (a reload or a filter keeps the session the user chose): replace `var previousSessionId = SelectedSession?.SessionId;` with `var previousKey = SelectedSession?.HistoryKey;`; replace `if (previousSessionId is not null) SelectSession(previousSessionId);` with `if (previousKey is not null) SelectedSession = Sessions.FirstOrDefault(s => s.HistoryKey == previousKey);`; replace `var selectedId = SelectedSession?.SessionId;` with `var selectedKey = SelectedSession?.HistoryKey;`; replace

```csharp
        SelectedSession = selectedId is null ? null :
            Sessions.FirstOrDefault(s => s.SessionId == selectedId);
```

with

```csharp
        SelectedSession = selectedKey is null ? null :
            Sessions.FirstOrDefault(s => s.HistoryKey == selectedKey);
```

**Don't:** change the service's session id, the desktop card, `SelectSession(string)`, the retention, the bounds or the stored format. Sessions that were already replaced cannot come back; say so in the Log.

**Acceptance:** `D50_HISTORY_KEEPS_EVERY_SESSION_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] live check pending (Claude): two sessions of the same project and actor both stay in Çalışma günlüğü`.

### D46 — A CI run that is still going is not a failed step

**Why:** audit 42 finding 2. `scripts/Get-CiStatus.ps1` exits 2 while the windows-ci run is still going (AGENTS.md step 10b: `[~] CI pending`), and the agent checks CI right after every push, so almost every card ends its worklog session with a red "GitHub denetimi okunamadı" step and a failed session; exit 1 (CI red) reads "okunamadı" too. Exit 2 becomes a finished step "GitHub denetimi sürüyor", exit 0 "GitHub denetimi geçti", exit 1 "GitHub denetimi başarısız". The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora/WorklogSubject.cs`, `src/Talvora/TalvoraDesktopProgressNarrative.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `tests/Talvora.Notification.Regression/Program.D46.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/modelcontextprotocol/csharp-sdk` — "CallToolResult StructuredContent JsonElement"; `/dotnet/docs` — "JsonElement.TryGetInt32"; record both queries.

#### Exact implementation

Step 1 (red first). Create `Program.D46.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D46: Get-CiStatus.ps1 exit 2 (CI still running) is a finished check; exit 1 is a failed one.</summary>
    private static async Task AssertD46CiStillRunningAsync()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["executable"] = JsonSerializer.SerializeToElement(@"C:\Program Files\PowerShell\7\pwsh.exe"),
            ["arguments"] = JsonSerializer.SerializeToElement(
                new[] { "-NoProfile", "-File", "scripts/Get-CiStatus.ps1", "-TimeoutMinutes", "0" }),
        };
        foreach (var (exitCode, kind, title) in new[]
        {
            (2, DesktopProgressKind.Completed, "GitHub denetimi sürüyor"),
            (1, DesktopProgressKind.Failed, "GitHub denetimi başarısız"),
            (0, DesktopProgressKind.Completed, "GitHub denetimi geçti"),
        })
        {
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(observed);
            try
            {
                var result = new CallToolResult
                {
                    Content = [],
                    StructuredContent = JsonSerializer.SerializeToElement(
                        new { exitCode, timedOut = false, standardOutput = "CI" }),
                };
                _ = await notifier.RunToolCallAsync("talvora_user_run", arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var terminal = observed.LastOrDefault(message =>
                    message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
                if (terminal is null || terminal.Kind != kind || !terminal.Title.StartsWith(title, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"D46: exit {exitCode} must end as {kind} \"{title}\", got {terminal?.Kind} \"{terminal?.Title}\".");
            }
            finally
            {
                await notifier.DisposeAsync();
            }
        }
        Console.WriteLine("D46_CI_STILL_RUNNING_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD46CiStillRunningAsync();` on its own line directly below `await AssertD41CallerIdentityAsync();`. Red: `D46: exit 2 must end as Completed "GitHub denetimi sürüyor", got Failed …`.

Step 2. `WorklogSubject.cs`: replace the line

```csharp
        ["Get-CiStatus.ps1"] = new("GitHub denetimi okunuyor", "GitHub denetimi okundu", "GitHub denetimi okunamadı"),
```

with

```csharp
        ["Get-CiStatus.ps1"] = new("GitHub denetimi okunuyor", "GitHub denetimi geçti", "GitHub denetimi başarısız"),
```

Step 3. `TalvoraDesktopProgressNarrative.cs`: directly above `    private static bool ResultContainsText<T>(` add

```csharp
    // D46: Get-CiStatus.ps1 exits 2 while the windows-ci run is still going (AGENTS.md step 10b).
    private static bool IsCiStillRunning<T>(OperationNarrative narrative, T result) =>
        string.Equals(narrative.Subject, "Get-CiStatus.ps1", StringComparison.OrdinalIgnoreCase) &&
        ResultExitCode(result) == 2;

    private static int? ResultExitCode<T>(T result)
    {
        try
        {
            var json = result is CallToolResult { StructuredContent: JsonElement structured }
                ? structured
                : JsonSerializer.SerializeToElement(result);
            return json.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
                exitCode.ValueKind == JsonValueKind.Number &&
                exitCode.TryGetInt32(out var code)
                    ? code
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
```

Step 4. `TalvoraDesktopProgressNotifier.cs`, method `RunToolCallAsync`: replace

```csharp
            var failed = TryDescribeFailure(result);
            try
            {
```

with

```csharp
            var failed = TryDescribeFailure(result);
            // D46: a CI run that is still going is a finished check, never a failed step.
            var ciStillRunning = failed && IsCiStillRunning(narrative, result);
            if (ciStillRunning)
            {
                failed = false;
            }

            try
            {
```

then replace

```csharp
                else
                {
                    if (TryBuildDeferredCompletion(
                            narrative,
                            result,
                            out var deferredTitle,
                            out var deferredMessage))
```

with

```csharp
                else
                {
                    if (ciStillRunning)
                    {
                        TryEndWorklog(
                            operationId,
                            DesktopProgressKind.Completed,
                            "GitHub denetimi sürüyor",
                            "GitHub denetimi henüz bitmedi; sonucu bir sonraki adımda okunacak.",
                            terminalEvidence is null
                                ? null
                                : terminalEvidence with { Result = "Sonuç: GitHub denetimi sürüyor" },
                            stopwatch.Elapsed);
                    }
                    else if (TryBuildDeferredCompletion(
                            narrative,
                            result,
                            out var deferredTitle,
                            out var deferredMessage))
```

and in the `catch (Exception ex)` block right below replace the line

```csharp
                    failed ? narrative.Titles.Failed : narrative.Titles.Succeeded,
```

with

```csharp
                    failed ? narrative.Titles.Failed : ciStillRunning ? "GitHub denetimi sürüyor" : narrative.Titles.Succeeded,
```

**Don't:** change `Get-CiStatus.ps1` or its exit codes, AGENTS.md step 10b, the other scripts' titles or the failure classifier.

**Acceptance:** `D46_CI_STILL_RUNNING_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; push and run the step-10b CI check once as usual; record `[~] live check pending (Claude): the CI check right after the push reads "GitHub denetimi sürüyor"` (Claude reads the worklog history).

### D48 — A vault note belongs to the vault's owner

**Why:** audit 42 finding 4 (consistency; Claude's OB1 reference file). The service runs as LocalSystem, so `talvora_note_write` leaves notes and the folders it creates owned by `NT AUTHORITY\SYSTEM` (live: `40 Oturum Notları/2026-11-07 Talvora OB1.md`), while source edits keep their workspace owner through `FileOwnership`. The literal blocks below were built and run by Claude in a scratch clone (SourceEdit regression green, Full gate green).

**Files:** `src/Talvora/Tools/NoteTools.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "FileSystemSecurity.SetOwner SecurityIdentifier"; record the query.

#### Exact implementation

Step 1. `SourceEditRegressionRunner.Notes.cs`: replace

```csharp
            await AssertNoteRefusedAsync(root, "a.md", "api_key=abcdef123456", "create", "OB1: secrets are refused.");
```

with

```csharp
            // D48: a note in a new folder; the folder and the note carry the vault's owner.
            await NoteTools.WriteAsync(root, "Yeni klasör/Alt/not.md", "# Not\n", "create");
            var vaultOwner = Talvora.SourceEditing.FileOwnership.TryGetOwner(root);
            Assert(vaultOwner is not null &&
                vaultOwner == Talvora.SourceEditing.FileOwnership.TryGetOwner(Path.Combine(root, "Yeni klasör")) &&
                vaultOwner == Talvora.SourceEditing.FileOwnership.TryGetOwner(Path.Combine(root, "Yeni klasör", "Alt", "not.md")),
                "D48: a new note and its new folders belong to the vault owner.");

            await AssertNoteRefusedAsync(root, "a.md", "api_key=abcdef123456", "create", "OB1: secrets are refused.");
```

Run as the signed-in user, a new note already belongs to that user, so this check cannot be red; the red evidence is the live OB1 note owned by `NT AUTHORITY\SYSTEM` (Claude, audit 42). Quote it in the Log.

Step 2. `NoteTools.cs`: replace

```csharp
        await AtomicFile.WriteAllTextAsync(
            fullPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken);

        return new TalvoraNoteWriteResponse(Path.GetRelativePath(root, fullPath), mode, content.Length);
```

with

```csharp
        // D48: folders this note creates get the vault owner too (the service writes as LocalSystem).
        var createdDirectories = new List<string>();
        for (var directory = Path.GetDirectoryName(fullPath);
             directory is not null && directory.Length > root.Length && !Directory.Exists(directory);
             directory = Path.GetDirectoryName(directory))
        {
            createdDirectories.Add(directory);
        }

        await AtomicFile.WriteAllTextAsync(
            fullPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken);

        // D48: the note belongs to the vault's owner, as source edits keep their workspace owner.
        if (Talvora.SourceEditing.FileOwnership.TryGetOwner(root) is { } owner)
        {
            foreach (var directory in createdDirectories)
            {
                Talvora.SourceEditing.FileOwnership.TrySetOwner(directory, owner);
            }

            Talvora.SourceEditing.FileOwnership.TrySetOwner(fullPath, owner);
        }

        return new TalvoraNoteWriteResponse(Path.GetRelativePath(root, fullPath), mode, content.Length);
```

**Don't:** change the refusals, the 64,000-character limit, `AtomicFile` or `FileOwnership`.

**Acceptance:** SourceEdit regression green (`ob1-vault-note-writer`); signed-in Full `Hepsi geçti.`; build and deploy; live on the installed runtime: `talvora_note_write {vaultRoot: "C:\\Users\\tayla\\Obsidian\\Taylan", path: "40 Oturum Notları/2026-11-07 Talvora OB1.md", content: "D48: not artık kasanın sahibine ait.\n", mode: "append"}` succeeds, and `(Get-Acl -LiteralPath 'C:\Users\tayla\Obsidian\Taylan\40 Oturum Notları\2026-11-07 Talvora OB1.md').Owner` run with `talvora_user_run` names the signed-in user, not `NT AUTHORITY\SYSTEM`; record both lines.

### D49 — Plain Turkish service names, also for older registries

**Why:** audit 42 finding 5. The real registry (`managed-mcps.json`, written 2026-09-30) names the core "Talvora Core Service" and Gitea "Gitea MCP", components "Secure MCP Tunnel", "Gitea MCP sunucusu", "Penpot local MCP", "Penpot MCP": the Hizmetler titles, the detail pages and every event built from `registration.DisplayName` ("Gitea MCP zaten hazır") show them, while the smoke fixtures already say "Talvora Çekirdeği" and "Gitea". New registries get the plain names from discovery; a registry written earlier is read with them (the file changes only when the tray saves it itself). The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora.Shared/ManagedMcpDisplayNames.cs` (new), `src/Talvora.Shared/ManagedMcpRegistryStore.cs`, `src/Talvora.Tray/ManagedMcpRecoveryDiscovery.cs`, `src/Talvora.Tray/ControlCenterSmoke.UI4.cs`, `tests/Talvora.Notification.Regression/Program.D49.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "C# record with expression nondestructive mutation"; record the query.

#### Exact implementation

Step 1 (red first). Create `Program.D49.cs`:

```csharp
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D49: registry names read in plain Turkish, also from a registry written with the old names.</summary>
    private static async Task AssertD49PlainServiceNamesAsync()
    {
        var registration = new ManagedMcpRegistration
        {
            Id = "gitea",
            DisplayName = "Gitea MCP",
            Description = "Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri.",
            Endpoint = "http://127.0.0.1:8081/mcp",
            Components =
            [
                new ManagedMcpComponentRegistration { Id = "tunnel", DisplayName = "Secure MCP Tunnel", Kind = "scheduled-task" },
                new ManagedMcpComponentRegistration { Id = "caddy", DisplayName = "Caddy", Kind = "windows-service" },
            ],
        };
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d49-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "managed-mcps.json");
            await ManagedMcpRegistryStore.WriteAsync(path, new ManagedMcpRegistryDocument { Mcps = [registration] });
            var read = (await ManagedMcpRegistryStore.TryReadAsync(path))?.Mcps.Single();
            if (read is null ||
                read.DisplayName != "Gitea" ||
                read.Description != "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri." ||
                read.Components[0].DisplayName != "Güvenli tünel" ||
                read.Components[1].DisplayName != "Caddy" ||
                ManagedMcpDisplayNames.For("Talvora Core Service") != "Talvora Çekirdeği" ||
                ManagedMcpDisplayNames.For("Talvora Dev") != "Talvora Dev")
                throw new InvalidOperationException("D49: registry names must read in plain Turkish.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D49_PLAIN_SERVICE_NAMES_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD49PlainServiceNamesAsync();` on its own line directly below `await AssertD46CiStillRunningAsync();` (D46 adds that line). Red: CS0103 (`ManagedMcpDisplayNames` does not exist yet).

Step 2. Create `src/Talvora.Shared/ManagedMcpDisplayNames.cs`:

```csharp
namespace Talvora.Shared;

/// <summary>
/// D49: older registry names in plain Turkish (copy deck §9: no "MCP" or English on the main surfaces).
/// Applied whenever the registry is read, so a registry written before D49 shows the same names everywhere.
/// </summary>
public static class ManagedMcpDisplayNames
{
    private static readonly Dictionary<string, string> Plain = new(StringComparer.Ordinal)
    {
        ["Talvora Core Service"] = "Talvora Çekirdeği",
        ["Gitea MCP"] = "Gitea",
        ["Secure MCP Tunnel"] = "Güvenli tünel",
        ["Gitea MCP sunucusu"] = "Gitea araç sunucusu",
        ["Penpot local MCP"] = "Penpot yerel araç sunucusu",
        ["Penpot MCP"] = "Penpot araç sunucusu",
        ["Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri."] =
            "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri.",
        ["İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot MCP sunucusu."] =
            "İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot araç sunucusu.",
    };

    public static string For(string text) =>
        Plain.TryGetValue(text, out var plain) ? plain : text;

    public static ManagedMcpRegistration Normalize(ManagedMcpRegistration registration) =>
        registration with
        {
            DisplayName = For(registration.DisplayName),
            Description = For(registration.Description),
            Components = registration.Components
                .Select(component => component with { DisplayName = For(component.DisplayName) })
                .ToList(),
        };
}
```

Step 3. `ManagedMcpRegistryStore.cs`, method `TryReadAsync`: replace

```csharp
        ValidateDocument(document);
        return document;
```

with

```csharp
        ValidateDocument(document);
        // D49: a registry written before D49 keeps its old names on disk; every reader gets the plain ones.
        return document with { Mcps = document.Mcps.Select(ManagedMcpDisplayNames.Normalize).ToList() };
```

and in `TryReadRecoveryManifestsAsync` replace

```csharp
                ValidateRegistration(registration);
                registrations.Add(registration);
```

with

```csharp
                ValidateRegistration(registration);
                registrations.Add(ManagedMcpDisplayNames.Normalize(registration));
```

Step 4. `ManagedMcpRecoveryDiscovery.cs`, exact replacements:

- `DisplayName = "Talvora Core Service",` → `DisplayName = "Talvora Çekirdeği",` (1×)
- `DisplayName = "Secure MCP Tunnel",` → `DisplayName = "Güvenli tünel",` (2×)
- `DisplayName = "Gitea MCP",` → `DisplayName = "Gitea",` (1×)
- `Description = "Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri.",` → `Description = "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri.",` (1×)
- `DisplayName = "Gitea MCP sunucusu",` → `DisplayName = "Gitea araç sunucusu",` (1×)
- `"İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot MCP sunucusu."` → `"İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot araç sunucusu."` (1×)
- `DisplayName = "Penpot local MCP",` → `DisplayName = "Penpot yerel araç sunucusu",` (1×)
- `DisplayName = "Penpot MCP",` → `DisplayName = "Penpot araç sunucusu",` (1×)

The folder in `Path.Combine(programFiles, "Gitea MCP", "gitea-mcp.exe")` and the task names (`"Gitea MCP Server"`, `"Gitea MCP Tunnel"`, `"Talvora Penpot MCP"`) are real Windows names: never change them.

Step 5. `ControlCenterSmoke.UI4.cs`: replace `State(Registration("gitea", "Gitea MCP"), ControlCenterHealthState.Ready, "Hazır")` with `State(Registration("gitea", "Gitea"), ControlCenterHealthState.Ready, "Hazır")`.

**Don't:** rename ids, folders, scheduled tasks or services; change `ValidateRegistration` or the stored file.

**Acceptance:** `D49_PLAIN_SERVICE_NAMES_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)`.

### D47 — Plain Turkish for the remaining Control Center texts

**Why:** audit 42 finding 3 (copy deck §9: no "MCP", "tunnel-client", "runtime", "control plane" on main surfaces). D37 changed 23 event texts; 75 more still reach Olaylar, the event slips, the tray balloons and menu, the Hizmetler states and the chain rows with MCP, readiness, health, runtime, browser smoke, Secure MCP Tunnel, Control plane or OAuth discovery (seen live: "Gitea MCP zaten hazır / Sağlık ve readiness kontrolleri onarım gerektiren bir arıza göstermedi."). The setup wizard and its errors add 38 rows under the user's decision of 2026-11-08 ("Adlar kalsın, cümleler Türkçe"): OpenAI's own names stay in parentheses inside Turkish sentences, "OpenAI Admin API anahtarı (Admin API key)", so the user finds the same name on OpenAI's pages. Claude applied the whole table in a scratch clone: the new test is red on the current source and green after it, D37's test stays green, Full gate green.

**Files:** the sixteen files in the table, all under `src/Talvora.Tray/` (`ControlCenterRepairService.cs`, `GiteaTrayClient.cs`, `ControlCenterDashboardService.cs`, `ControlCenterLifecycleService.cs`, `ControlCenterLifecycleService.Penpot.cs`, `ControlCenterWindow.Actions.cs`, `ControlCenterComponentHealthService.cs`, `ManagedMcpProtocolProbeService.cs`, `TrayApplicationContext.cs`, `ControlCenterSetupService.cs`, `ControlCenterWindow.Setup.cs`, `ControlCenterAdminCredentialStore.cs`, `ManagedMcpTunnelProvisioningService.cs`, `ManagedMcpTunnelProvisioningService.Pending.cs`, `BusinessTunnelClient.cs`, `Program.cs`), `tests/MainSurfaceTurkishRegression.ps1` (new, reference copy), `tests/GiteaProtocolReadinessRegression.ps1`, `tests/TunnelClientV016IntegrationSourceRegression.ps1`, `.context7/verification.json`.

**Context7:** none needed (strings only); record the map's .NET entry with one relevant fact (interpolation holes stay as they are), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

The table is `docs/codex/reference/d47/texts.tsv` (read it with `talvora_read_source`): one row per change with the columns `file`, `count`, `match`, `old`, `new`. Replace `old` with `new`, character for character, exactly `count` times in that file. `match` = `literal` means the whole C# string literal, quotes included (`"Gitea MCP"` → `"Gitea"`), because the same words also stand inside longer strings and English `TrayLog.Write` lines that stay; `match` = `text` means the text inside a literal (holes such as `{registration.DisplayName}` stay as they are). A few texts end with a space because the literal goes on in the next line with `+`: keep that space. `GetFriendlySetupError` in `ControlCenterWindow.Setup.cs` picks its message by the substrings `tunnel ID`, `Admin API key`, `Runtime API key` and `temel kurulum`; every new text keeps the substring its old text had, and those four match strings do not change.

Step 1 (red first). Copy `docs/codex/reference/d47/MainSurfaceTurkishRegression.ps1` → `tests/MainSurfaceTurkishRegression.ps1` (reference copy, AGENTS.md rule 5; normalised SHA-256 in that folder's `SHA256SUMS.txt`). Run it: red `D47: old text still present: Sağlık ve readiness kontrolleri onarım gerektiren bir arıza göstermedi.`

Step 2. Apply the 113 rows of `texts.tsv`, one `talvora_apply_patch` per file.

Step 3. Two existing tests pin texts this card changes; update them in the same commit:
- `tests/GiteaProtocolReadinessRegression.ps1`: `$statusBody.Contains('Gitea MCP health açık, protokol hazır değil')` → `$statusBody.Contains('Gitea araç sunucusu açık, protokolü hazır değil')`;
- `tests/TunnelClientV016IntegrationSourceRegression.ps1`, in the three `Assert-Contains $componentHealth` lines: `'"Tunnel runtime"'` → `'"Tünel çalışma katmanı"'`, `'"Control plane"'` → `'"Yönetim katmanı"'`, `'"Tünel MCP gözlemi"'` → `'"Tünel gözlemi"'` (the assertion messages stay).

**Don't:** change `TrayLog.Write` or `ILogger` lines, exception types, event categories or dedup keys; `BuildTunnelRuntimeDetail` (technical evidence under "Teknik kanıt"); the four match strings in `GetFriendlySetupError`; the setup labels in `StepperCard.xaml` (Claude's [C] WL3); the other probe details in `ManagedMcpProtocolProbeService.cs`; `ManagedMcpRecoveryDiscovery.cs` (D49 owns its names).

**Acceptance:** `MAIN_SURFACE_TURKISH_GREEN` and `EVENT_TEXT_TURKISH_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)`.

### WL3 — Çalışma günlüğü and Olaylar polish (Claude)

**Why:** audit 42 screen pass; user decisions 2026-11-08 ("Sıraya koy, sonra yap" for the look fixes and the list mark; setup names in parentheses). Build after D45 and D50, which change the same page's data.

**Do (Claude, under the agent rules: smoke first, Full gate, deploy, captures in both themes, CI):**
1. `WorklogHistoryView.xaml`, detail column: the task note, the fact line, the evidence summary and the evidence result collapse when their text is empty (one `HistoryOptionalText` style on `Tv.Type.Body` with triggers `Text` = "" and `{x:Null}` → `Visibility` `Collapsed`), so no empty line leaves a 40 px hole.
2. `WorklogHistoryView.xaml`, list card: the bold outcome line and the time line become one Caption line: an 8 px dot (`Tv.Ready` completed, `Tv.Offline` failed, `Tv.TextMuted` cancelled, `Tv.Attention` running), the outcome word, " · ", `ListTimeText`. The card loses one line.
3. `EventsView.xaml`: when the filtered list holds no event, a centred `Tv.Type.Body` line in `Tv.TextMuted`: "Bu süzgeçte olay yok." (with search text: "Aramayla eşleşen olay yok.").
4. `EventsView.xaml`: while the raw log is open the toggle reads "Ham günlüğü gizle" (content and `AutomationProperties.Name`).
5. `StepperCard.xaml` setup labels: "Talvora Dev tünel kimliği (tunnel ID)", "Talvora Admin tünel kimliği (tunnel ID)", "OpenAI Admin API anahtarı (Admin API key)" (label and automation name; the placeholder "Admin API key" stays).
6. `ControlCenterSmoke.D44.cs`: after the click, assert that the Olaylar list holds the recovery event the smoke applied (feed it through the snapshot the Olaylar page reads); the light-theme capture of audit 42 showed an empty list.
7. `WorklogHistoryView.xaml`, detail column, right under the `OutcomeLabel` line (audit 43, user decision 2026-11-08 "Evet, nedeni göster"; the data is already in `WorklogOutcome`): `{Binding Current.Outcome.PlainReason, TargetNullValue='', FallbackValue=''}` in `Tv.Type.Body` with `Tv.TextMuted`, then `{Binding Current.Outcome.NextStep, StringFormat='Sonraki adım: {0}', TargetNullValue='', FallbackValue=''}` in `Tv.Type.Caption`; both use the `HistoryOptionalText` collapse of item 1, so a completed session shows neither line.
8. `WorklogHistoryView.xaml`: the "Kanıtlar" heading collapses when the session has no evidence (summary, result, files and code preview all empty), with the same trigger style; check the bound values on a real session without evidence first (audit 43 screen pass: the "scratchpad" session showed the heading alone).

## Toolchain modernization (user request 2026-11-08; Claude, one-off)

User: "Talvora'da rust, uv modernizasyonu yapmamız anlamlı olur mu araştır", then "Evet, yaz" and "bu arada tüm bu modernizasyonu bu defalık sen yap". Research result: Talvora stays .NET (Roslyn and WPF cannot move, the C# and Rust MCP SDKs are both Tier 1, the work is process-bound); Rust is for new standalone components; the toolchain tools follow the uv/cargo rule. Claude's one-off items (UV1, RS1, RS2, D51, UR1–UR6) are done and live in git history. **PK1 and PK2 below are ordinary agent cards**: the agent implements them in queue order.

### PK1 — MCP C# SDK 2.4.x and Tomlyn 2.10.2 (after UR4)

**Why:** `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` 2.4.0 (2026-11-07) and `Tomlyn` 2.10.2 are out; Talvora pins 2.3.1 and 2.10.1. The 2.4.0 notes list no breaking change; the items that touch Talvora: protocol property names now match case-sensitively (#1934), legacy output schemas of delegating server tools are fixed (#1928), and the legacy OAuth metadata fallback needs an opt-in (#1952; Talvora's server uses no SDK OAuth, so nothing changes there).

**Do:** `Directory.Packages.props`: both `ModelContextProtocol` lines to the newest 2.4.x stable on nuget.org when the card runs (2.4.0 on 2026-11-08; never a preview, never 2.5 or later), `Tomlyn` to 2.10.2. `docs/codex/CONTEXT7.md`: the MCP C# SDK row's version; the TOML row's version and id (`/xoofx/tomlyn/2.10.2` when Context7 lists it, otherwise `/xoofx/tomlyn` with a gap note). Nothing else changes. A compile error or a test that fails because of the update → proposal with the exact message.

**Context7:** `/modelcontextprotocol/csharp-sdk` (query "2.4.0 release notes breaking changes"), `/xoofx/tomlyn`. Vendor sources: `https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0`, `https://www.nuget.org/packages/ModelContextProtocol`, `https://www.nuget.org/packages/Tomlyn`.

**Acceptance:** restore and signed-in Full `Hepsi geçti.`; install; `--metadata-policy-only` and the full installed smoke green (Deploy row); CI.

### PK2 — `Microsoft.Build.Framework` follows the SDK's MSBuild (after PK1)

**Why:** Talvora compiles against `Microsoft.Build.Framework` 17.14.28 (`ExcludeAssets="runtime"`); at run time MSBuildLocator loads the MSBuild of the installed .NET SDK 10.0.403, which is MSBuild 18.9.15 (`dotnet msbuild -version`). The compile reference follows the runtime MSBuild's line and is never newer than it; 18.10 and 18.11 are on nuget.org but newer than the SDK's MSBuild.

**Do:** first run `dotnet msbuild -version` as the signed-in user; when it does not start with `18.9.` → proposal with the output (the SDK changed). Then `Directory.Packages.props`: `Microsoft.Build.Framework` 17.14.28 → 18.9.6 (the newest 18.9.x on 2026-11-08). `Microsoft.Build.Locator` 1.11.2 stays (current). Add a `docs/codex/CONTEXT7.md` row for MSBuild with the id Context7 returns for "msbuild" (or a gap note and the Microsoft Learn link).

**Context7:** search "msbuild"; vendor sources: `https://learn.microsoft.com/visualstudio/msbuild/find-and-use-msbuild-versions`, `https://www.nuget.org/packages/Microsoft.Build.Framework`.

**Acceptance:** signed-in Full `Hepsi geçti.` (it runs the semantic source edit regression); install; `dotnet run --project tests/Talvora.Smoke/Talvora.Smoke.csproj -c Release -- --semantic-source-edit-only http://127.0.0.1:7676/mcp C:\Users\tayla\Talvora-MCP` → `TALVORA SEMANTIC SOURCE EDIT SMOKE GREEN`, and the full installed smoke green; CI.
