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

### Audit 2026-11-14 (58, deep scan of the whole repository) · range `audit/2026-11-14-57..dffe9ce` (Claude's D47, WL3, D81 and release 0.0.1.0; D79 by ChatGPT) · D82, D83, D84 for the agent · `[C]` D85, D86 for Claude

### D84 — Secret locations are closed to the file tools; wider log masking (user decision 2026-11-14)

**Why:** Talvora's generic file tools run as LocalSystem and can read every file on the machine. `%ProgramData%\Talvora\Secrets\*` (secrets of the user's other projects) and `%ProgramData%\Talvora\Context7\api-key` (the user's Context7 key, CTX3) are protected only by agent rules: one broad `talvora_search_text` or `talvora_read_text` over ProgramData would put a key into the chat. The tools that need those files read them themselves (`HostingerMcpTools`, `Context7Tools`), so nothing needs them through the generic tools. Separately, `FileLog.RedactSensitiveData` (`src/Talvora.Shared/FileLog.cs`; used by the call trace, the service and tray logs and the worklog evidence) masks neither the Context7 key format (`ctx7sk…`) nor a value given after a space (`--token X`, `--password X`, `--api-key X`), nor `token=` / `_authToken=`. In audit 58 the user chose "Kapat ve maskele" knowing that SYSTEM PowerShell stays on the Dev surface: this protects against accidents, not against a determined caller.

**Files:** new `src/Talvora/Tools/ProtectedSecretPaths.cs` (the one helper); the tool files named in step 3 (`FileTools.cs`, `ConfigAssetTools.Text.cs`, `ConfigAssetTools.Json.cs`, `ConfigAssetTools.Archive.cs`, `DeveloperTools.FileBinary.cs`, `DeveloperTools.FileInfo.cs`, `DeveloperTools.FileSearch.cs`, `SourceEditTools.cs`, `ConfigFormatTools.Dotenv.cs`, `ConfigFormatTools.Ini.cs`, `ConfigFormatTools.Xml.cs`, `StructuredConfigTools.Yaml.cs`, `StructuredConfigTools.Toml.cs`, `SqliteTools.cs`); `src/Talvora.Shared/FileLog.cs`; tests in `tests/Talvora.SourceEdit.Regression/` (`SourceEditRegressionRunner.Redaction.cs` for masking, a new `SourceEditRegressionRunner.D84.cs` for the guard, registered the way the runner registers its other partial files); `docs/codex/STATUS.md`; `.context7/verification.json`. **Tests that read these files:** the whole SourceEdit regression, `tests/PrivacySecuritySourceRegression.ps1`, `tests/Talvora.Smoke/SmokeScenarios.PrivacySecurity.cs` (installed smoke), `tests/ToolManifestDocumentationRegression.ps1` (no tool is added or removed, so it must stay green unchanged).

**Protected set (exact):** (1) the directory `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora", "Secrets")` and everything under it; (2) the file `Path.Combine(<same ProgramData>, "Talvora", "Context7", "api-key")`. Nothing else (the Context7 ledger files beside the key stay readable).

**Do:**
1. Red first: guard tests against a temporary fixture root (the helper takes the protected entries as an optional parameter; the default is the real set above). `IsProtected` is true for the fixture `Secrets` directory itself, a file two levels under it, an upper-case and a `/`-separated spelling, a spelling with a trailing `.` on the last segment, the 8.3 short name of the fixture directory (when the volume has short names; skip that case with a log line otherwise) and a path through a junction that points into `Secrets`; false for `Secrets2\x`, `Context7\lookups-2026-11-14.jsonl` and `Context7\api-key.bak`. Masking tests (step 4) fail today.
2. `ProtectedSecretPaths.ThrowIfProtected(string path)`: compare `Path.GetFullPath(path)` and, when the path or its nearest existing parent exists, its final path (open with `FILE_FLAG_BACKUP_SEMANTICS` and read `GetFinalPathNameByHandleW`, the way `KnowledgeTools.cs` already does; strip the `\\?\` prefix) with each protected entry: equal, or a whole-segment prefix (entry + `\`), `StringComparison.OrdinalIgnoreCase`. On a match throw `UnauthorizedAccessException` with exactly: `Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez.` The check runs before any existence check, so a missing file inside `Secrets` gets this message too.
3. Call it first thing for every path argument of: `talvora_read_text`, `talvora_read_text_range`, `talvora_tail_text`, `talvora_read_bytes`, `talvora_read_source`, `talvora_file_hash`, `talvora_dotenv_get`, `talvora_dotenv_list`, `talvora_json_get`, `talvora_yaml_get`, `talvora_toml_get`, `talvora_ini_get`, `talvora_ini_list`, `talvora_xml_query`, every `talvora_sqlite_*` tool that takes a database path, `talvora_write_text`, `talvora_append_text`, `talvora_replace_text`, `talvora_write_bytes`, `talvora_copy` (source and destination), `talvora_move` (source and destination), `talvora_delete`, `talvora_archive_create` (every source), `talvora_archive_extract` (destination). `talvora_search_text` refuses a `root` inside the set and silently skips protected files and directories while walking from a root above them. `talvora_list`, `talvora_find_files` and `talvora_path_info` keep showing names and attributes (listing names is allowed).
4. `FileLog.RedactSensitiveData`, added to the existing rules (every current output stays the same): (a) `\bctx7sk[-_A-Za-z0-9]{8,}` → `[REDACTED]`; (b) a command-line flag `--token`, `--api-key`, `--api_key`, `--apikey`, `--password`, `--passwd`, `--secret`, `--client-secret`, `--access-token`, `--auth-token` or `--runtime-api-key` (any case) followed by whitespace or `=` and a value: keep the flag and the separator, replace the value with `[REDACTED]`; a value starting with `env:` is kept (it names a variable); (c) `token`, `authToken` and `_authToken` join the names of the existing `name=value` rule. Exact cases for the tests: `ctx7sk-fixture_0123456789abcdef` → `[REDACTED]`; `--token abc123` → `--token [REDACTED]`; `--password=hunter2` → `--password=[REDACTED]`; `--runtime-api-key env:CONTROL_PLANE_API_KEY` unchanged; `gh auth login --with-token` unchanged; `//registry.npmjs.org/:_authToken=npm_abc123` → `//registry.npmjs.org/:_authToken=[REDACTED]`.

**Don't:** change `HostingerMcpTools`, `Context7Tools`, `talvora_note_write` or any tool that reads these files for its own work; add any check to `talvora_run_powershell`, `talvora_run_process`, `talvora_user_run`, `talvora_job_start` or other process tools (user decision: PowerShell stays the conscious path); touch the real `C:\ProgramData\Talvora\Secrets` or the real key in any test; redact tool results (only what goes through `FileLog`).

**Context7:** `Context7: gerekmiyor (Talvora's own path checks; the final-path call already exists in KnowledgeTools.cs)` → `noLookup`.

**Acceptance:** red first; the SourceEdit regression and the pinned tests green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD build, deploy and both installed smokes; two live calls on the installed service, never on the real key: `talvora_read_text {path: "C:\\ProgramData\\Talvora\\Secrets\\talvora-d84-probe.txt"}` (a file that does not exist) answers exactly the step 2 message, and `talvora_list {path: "C:\\ProgramData\\Talvora\\Context7"}` still lists names; CI.

### D82 — The deploy follow-up does not depend on how much output the caller asked to see

**Why:** `TryPersistFollowedDeployAsync` (`src/Talvora/WorklogFollower.cs`) finds the deploy hand-off only as one whole JSON line `{…"launched":true…}` in the `standardOutput` of the `talvora_run_powershell` result. That text is already shortened by `ToolOutputCompactor.Compact` (`src/Talvora/Tools/ProcessTools.cs`: the first `min(2000, max/2)` characters, `… N characters omitted …`, the last characters). The hand-off line of `scripts/Deploy-Windows-Installer.ps1` is about 800 characters (installer and manifest paths, sha256, three commits, runtime hash, task GUID, note), so any call with `maxOutputCharacters` below that splits the line: no `pending-deploy.json` is written, the new service has nothing to confirm, and the step stays "Kurulum devredildi · doğrulama bekleniyor" for good. Audit 58 measured it on 2026-11-14: Claude's deploys with `maxOutputCharacters` 4000 and 1500 (12:16, 12:27, 13:19 UTC) ended "Yeni sürüm çalışıyor · <commit>"; the three with 600 (13:32, 13:53, 14:08 UTC) stayed "doğrulama bekleniyor" although the service restarted each time (Application event log). The method returns false without a log line, and `Program.WL6.cs` only tests a 50-character hand-off line.

**Files:** `scripts/Deploy-Windows-Installer.ps1` (the two hand-off objects), `src/Talvora/WorklogFollower.cs` (`TryPersistFollowedDeployAsync`), `tests/Talvora.Notification.Regression/` (new `Program.D82.cs` and its registration in `Program.cs`, the way D79 registers), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL6, N9Subjects), `tests/CanonicalDeployArtifactIdentityRegression.ps1`, `tests/NativeInstallerSourceRegression.ps1`.

**Do:**
1. Red first: a D82 test that gives `TryPersistFollowedDeployAsync` (through the same path `Program.WL6.cs` uses) a `talvora_run_powershell` result whose `standardOutput` is `ToolOutputCompactor.Compact(<line>, 600)`, where `<line>` is a hand-off line with the script's new property order and real lengths (two paths of about 70 characters, a 64-hex sha256, three 40-hex commits, a 64-hex runtime hash, a GUID and the script's note), and asserts that the pending deploy is written with that `sourceCommit`. It fails today.
2. Script: in both hand-off objects put `launched`, `sourceCommit` and `taskInstance` first, in this order; the other properties keep their order after them. Nothing else in the script changes.
3. Notifier: read the hand-off by its fields, not as one whole line: `standardOutput` must contain `"launched":true` (whitespace around `:` allowed) and `"sourceCommit":"<7–64 hex characters>"`; take the commit from that field. Every other condition stays (deployment category, the active worklog item, the 7-character minimum); `"launched":false` or no commit still writes nothing.
4. When the category is Deployment and no hand-off is found, log one Information line: `Deploy hand-off not found in the tool result; the deploy step stays unconfirmed.`
5. The WL6 tests stay green (whole line, size bound, replay).

**Don't:** change `ToolOutputCompactor`, the default `maxOutputCharacters`, the pending file's format or path, `CompleteDeploy`, any title, or the installer.

**Context7:** `Context7: gerekmiyor (Talvora's own output parsing and deploy script)` → `noLookup`.

**Acceptance:** red first; the complete Notification regression and the two pinned PowerShell tests green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD build and deploy **with `maxOutputCharacters: 600` on the deploy call**, `/healthz` = HEAD, both installed smokes; CI. Claude checks at the next audit that this deploy step ended "Yeni sürüm çalışıyor · <HEAD>" (`[~] visual check pending (Claude)`).

### D83 — Test and build titles for cargo, uv and Python test runs

**Why:** the user's permanent rule (Rust, cargo and uv first) makes `talvora_cargo_run`, `talvora_uv_run` and `cargo` / `uv` / `python -m …` runs through `talvora_user_run` the normal way new projects build and test, but none of them has a narrative. `talvora_cargo_run {arguments: ["test"]}` falls back to "Cargo çalıştır · tamamlandı" (an imperative title plus a verdict, the pattern D79 removed), `talvora_uv_run {arguments: ["run", "pytest"]}` to "uv komutu çalıştır · tamamlandı"; through `talvora_user_run`, the Python branch of `WorklogSubject.ForExecutable` takes the module as the program (`python -m unittest` → "unittest bitti", `python -m pytest` → "pytest bitti"), and `cargo test`, `uv run pytest` and `pytest` end as "<program> bitti". A test run never reads as a test and never updates the session's test state. Seen on 2026-11-14 in Claude's demo sessions ("unittest bitti").

**Files:** `src/Talvora/WorklogSubject.cs` (`ForExecutable` and the new classifier), `src/Talvora/TalvoraDesktopProgressNarrative.cs` (the tool switch), `src/Talvora/WorklogCopy.cs` (two new triples), `tests/Talvora.Notification.Regression/` (new `Program.D83.cs` and its registration), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL5, WL6, N9Subjects, D79), `ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first: a D83 test with every row below and every example in it (running, succeeded and failed titles, and the operation kind), plus `talvora_list` still `{title} · tamamlandı`, `python tool.py` still "tool.py bitti" and `python -m pip list` still "pip bitti".
2. One classifier for a program and its argument vector, used by `ForExecutable` (`talvora_user_run`, `talvora_run_process`, `talvora_job_start`, `talvora_user_process_start`) and by the tool switch for `talvora_cargo_run` (program `cargo`), `talvora_uv_run` (program `uv`) and `talvora_python_run` (program `python`), each with the tool's `arguments`. The program is the executable's file name without folder and extension, compared ignoring case. "First word" = the first argument that starts with neither `-` nor `+` (so `cargo +nightly test` has the first word `test`). "Test pair" = two neighbouring arguments `-m pytest` or `-m unittest`.

   | Command (program, arguments) | Titles | Kind |
   |---|---|---|
   | `cargo` with first word `test` or `nextest` | `WorklogCopy.Tests` | Tests |
   | `cargo` with first word `build` | `WorklogCopy.Build` | Build |
   | any other `cargo` command | `CargoRun` · {first word} | Program |
   | `uv` with first word `run` and, after it, an argument that is exactly `pytest` or a test pair (`uv run pytest -q`, `uv run --with pytest-cov pytest`, `uv run python -m unittest`) | `WorklogCopy.Tests` | Tests |
   | any other `uv` command | `UvRun` · {first word} | Program |
   | `python`, `python3` or `py` with a test pair | `WorklogCopy.Tests` | Tests |
   | `pytest` | `WorklogCopy.Tests` | Tests |

   New triples in `WorklogCopy`: `CargoRun = ("Cargo komutu çalışıyor", "Cargo komutu tamamlandı", "Cargo komutu hata ile bitti")`, `UvRun = ("uv komutu çalışıyor", "uv komutu tamamlandı", "uv komutu hata ile bitti")`; the subject is added with `WorklogSubject.WithSubject` (no first word → no subject and no " · "). Test and build rows carry no subject.
3. The tool category of `talvora_cargo_run`, `talvora_uv_run` and `talvora_python_run` is `ProcessOperation`; everything else keeps its category.
4. A Tests or Build row updates the session's test or build state exactly as `dotnet test` and `dotnet build` do today (the existing `OperationKind` path); the result fact stays as today (`{süre}` when nothing else is measured).

**Don't:** change the npm/pnpm/yarn/bun, `dotnet`, `git`, script or inline-command rules, `TalvoraToolTitles` (the MCP titles stay imperative), the generic fallback or the tray.

**Context7:** `Context7: gerekmiyor (Talvora's own title table)` → `noLookup`.

**Acceptance:** red first; the complete Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and installed smokes; CI. Claude checks a real `cargo test` and `uv run pytest` step at the next audit (`[~] visual check pending (Claude)`).

### D85 — The installer starts msiexec from System32; Turkish service description (Claude, `[C]`)

**Why:** when PowerShell 7 is missing, the elevated installer runs `new ProcessStartInfo("msiexec.exe")` (`src/Talvora.Installer/InstallerEngine.PowerShell.cs`). With a bare name, Windows looks for the program in the installer's own folder first, usually Downloads, so an `msiexec.exe` left there would run with administrator rights (CWE-427). Every other program the installer starts already uses a System32 path. The Windows service description in services.msc is English ("Talvora Local MCP - LocalSystem automatic resilient service").

**Done by Claude:** `Path.Combine(Environment.SystemDirectory, "msiexec.exe")`; the service description becomes "Talvora'nın yerel servisi: yapay zekâ ajanlarına bu bilgisayarda gerçek bir çalışma masası açar."; `tests/NativeInstallerSourceRegression.ps1` pins both. A security fix: release 0.0.1.1 follows on the next day (at most one release a day).

### D86 — Çalışma günlüğü shows each fact once; tray menu Turkish (Claude, `[C]`)

**Why:** the detail column's "Kanıtlar" binds `Evidence.Summary` and `Evidence.Result` raw (`WorklogHistoryView.xaml`), so a commit step reads "a05b675 · 1 dosya · +6 −1  //  Komut: git commit" and then "Sonuç: a05b675 · 1 dosya · +6 −1" (seen 2026-11-14); the desktop card already splits the command off. The tray menu says "Kontrol Merkezini aç" (the rest of the product writes "Kontrol Merkezi'nde", "Kontrol Merkezi'nden"), "Gitea'yı Aç" and "Gitea'yı Yeniden Başlat" in title case, and names the core "Talvora MCP" (Hizmetler says "Talvora Çekirdeği" since D49).

**Done by Claude:** the fact once, the command as a muted line ("Komut: git commit"), the result only when it says more than the fact; menu texts "Kontrol Merkezi'ni aç", "Gitea'yı aç", "Gitea'yı yeniden başlat", "Talvora Çekirdeği"; the WL3 smoke and `MainSurfaceTurkishRegression.ps1` pin them.
