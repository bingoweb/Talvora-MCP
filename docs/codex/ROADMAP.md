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

### Audit 2026-11-14 (59) · range `audit/2026-11-14-58..6b6faec` (Claude's D85, D86 and release 0.0.1.1; D84, D82, D83 by ChatGPT) · D87 for the agent

### D87 — The secret guard never breaks a search or a delete; a folder that contains the secret set refuses as a whole (user decision 2026-11-14)

**Why:** two problems in D84, measured on the installed 6b6faec on 2026-11-14.
(1) Regression: `ProtectedSecretPaths.IsProtected` opens the nearest existing entry to read its final path and throws `Win32Exception` when Windows cannot open it. A folder with `a.txt` and `z.txt` (both containing `needle`) and a junction `broken` to the missing `C:\does-not-exist-d84` made `talvora_search_text {root: <folder>, query: "needle"}` fail as a whole with "Sistem belirtilen dosyayı bulamıyor." (before D84: both matches), and `talvora_delete` of `broken` failed the same way. Any entry Windows cannot open by name does this (broken links, common under `node_modules`; app execution aliases; AF_UNIX socket files).
(2) Gaps in D84's tool list (Claude's card text, not the agent's work): the guard checks only the path it is given, so deleting, moving, copying or archiving a folder that contains the set (e.g. `C:\ProgramData\Talvora`) still reaches the secrets, a copy or an extract into such a folder can write into them, and the step-5 tools have no check. In audit 59 the user chose "Tüm işlemi reddetsin": an operation that would delete, move, read or write any part of the set refuses as a whole, before it touches anything, with the D84 message. Folders that do not contain the set behave exactly as before (`talvora_delete` gets no other guard: audit 40, "Dokunma").

**Files** (under `src/Talvora/Tools/`): `ProtectedSecretPaths.cs`; `FileTools.cs` (delete, copy, move); `ConfigAssetTools.Archive.cs` (archive create and extract); the set and delete tools in `ConfigAssetTools.Json.cs`, `StructuredConfigTools.Yaml.cs`, `StructuredConfigTools.Toml.cs`, `ConfigFormatTools.Ini.cs`, `ConfigFormatTools.Dotenv.cs`, `ConfigFormatTools.Xml.cs`; `ConfigAssetTools.Download.cs`; `SqliteTools.cs`. Also a new `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.D87.cs`, registered the way D84 is; `docs/codex/STATUS.md`; `.context7/verification.json`. `DeveloperTools.FileSearch.cs` stays as it is (step 2 fixes the walk). **Tests that read these files:** the whole SourceEdit regression (D84 included), `tests/PrivacySecuritySourceRegression.ps1`, `tests/Talvora.Smoke/SmokeScenarios.PrivacySecurity.cs` (installed smoke), `tests/ToolManifestDocumentationRegression.ps1` (no tool is added or removed, so it must stay green unchanged).

**Do:**
1. Red first, on a temporary fixture root, never the real set. Test seam: `internal static IReadOnlyList<string>? EntriesOverrideForTests` in `ProtectedSecretPaths`, used instead of the real set while it is not null (tests set it and reset it in `try`/`finally`). Cases:
   - (a) the regression folder above (`a.txt`, `z.txt`, junction `broken` to a missing folder): `IsProtected` on `broken` and on `broken\x` returns false without throwing; `talvora_search_text` over the folder returns both matches; `talvora_delete` of `broken` removes the junction; a broken junction inside the fixture `Secrets`, reached through the fixture folder's 8.3 short name, stays protected (skip that case with a log line when the volume has no short names, as D84 does);
   - (b) `talvora_delete`, `talvora_move` (source), `talvora_copy` (source) and `talvora_archive_create` of the fixture's parent folder refuse with the D84 message and change nothing;
   - (c) `talvora_copy` of a folder holding `Secrets\x.txt` into the fixture's parent, and `talvora_archive_extract` into the fixture's parent of a zip with the entry `Secrets/x.txt` (and, with `allowOutsideDestination: true`, of a zip whose entry climbs into the fixture `Secrets`), refuse before any file is written;
   - (d) each of the twelve config writers with a path inside the fixture `Secrets`, `talvora_http_download` with a destination inside it (URL `http://127.0.0.1:9/never`; the refusal comes before any request) and a SQLite source `file:<fixture Secrets path with / separators>/x.db?mode=ro` refuse with the D84 message;
   - (e) still allowed: deleting, copying and archiving a sibling folder of the fixture `Secrets`; extracting a zip without such entries into the fixture's parent.
   The search and delete cases of (a) and the cases (b)–(d) fail today.
2. `IsProtected` never throws for a path that `Path.GetFullPath` accepts: an entry that exists but cannot be opened for its final path is treated like a missing segment — keep going up to the nearest parent whose final path resolves and append the remaining segments; when nothing resolves, the lexical full path decides.
3. New `ProtectedSecretPaths.ThrowIfContainsProtected(string directory)`: refuses with the D84 message when a protected entry lies inside `directory` (compare each entry as given and, when it exists, its final path with `directory` as given and its final path; whole-segment prefix, `OrdinalIgnoreCase`). `talvora_delete`, `talvora_move` (source), `talvora_copy` (source) and `talvora_archive_create` (`sourceDirectory`) call it after `ThrowIfProtected` when the path is a directory.
4. Writes into a folder: `talvora_copy` checks the target path of every entry in its existing manifest preflight, and `talvora_archive_extract` checks the target path of every entry in its existing validation pass (also with `allowOutsideDestination`), both before the first file is written; one protected target refuses the whole operation. `talvora_move` needs nothing more on the destination side: a folder move cannot land inside the set unless its destination is inside it, which `ThrowIfProtected` already refuses.
5. The twelve config writers (`talvora_json_set`, `talvora_json_delete`, `talvora_yaml_set`, `talvora_yaml_delete`, `talvora_toml_set`, `talvora_toml_delete`, `talvora_ini_set`, `talvora_ini_delete`, `talvora_dotenv_set`, `talvora_dotenv_delete`, `talvora_xml_set`, `talvora_xml_delete`), `talvora_http_download` (`destinationPath`) and every SQLite tool that takes a database source call `ThrowIfProtected` first thing, like D84's readers. A SQLite `file:` source is checked on its path: the text between `file:` and the first `?`, percent-decoded, `/` read as `\`, leading `\` characters dropped when a drive letter follows (`file:///C:/x.db` → `C:\x.db`); `:memory:` and `|DataDirectory|` sources are not paths and stay unchecked.

**Don't:** change `talvora_list`, `talvora_find_files`, `talvora_path_info` or the `talvora_watch_*` tools (names stay visible), the D84 message, the masking rules, `HostingerMcpTools`, `Context7Tools`, `talvora_note_write` or any process tool; add a guard for folders that do not contain the set; call any delete, move, copy or archive tool on `C:\ProgramData\Talvora`, `C:\ProgramData` or any other real folder above the set to test this (the fixture tests prove it); touch the real `C:\ProgramData\Talvora\Secrets` or the real key in any test or live check.

**Context7:** `Context7: gerekmiyor (Talvora's own path checks; the final-path call already exists)` → `noLookup`.

**Acceptance:** red first; the SourceEdit regression and the pinned tests green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD build, deploy and both installed smokes; live on the installed service with a throwaway folder `artifacts/D87/probe` (tool-under-test exception; never the real set): the step-1 (a) folder, created with `mklink /J` through `talvora_user_run` — `talvora_search_text` returns both matches and `talvora_delete` removes the broken junction; `talvora_read_text {path: "C:\\ProgramData\\Talvora\\Secrets\\talvora-d84-probe.txt"}` (a file that does not exist) still answers the D84 message; CI.
