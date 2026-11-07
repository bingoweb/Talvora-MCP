# Talvora engineering rules

## PERMANENT USER-APPROVED AGENT PROMPT (2026-10-18)

**Mandatory for every Talvora development session, not just the chat in which it was supplied:** the full user-approved prompt is stored verbatim in docs/codex/CODEX-PROMPT.md. Every implementing agent, including ChatGPT using the Talvora Dev/Admin connector and Codex when the user authorizes it, must load and follow that file **together with this AGENTS.md** before making a repository change. The user does not need to paste the prompt again in subsequent sessions.

- AGENTS.md remains the authoritative implementation and safety contract; the stored prompt supplies the required ChatGPT bootstrap, daily shortcut and special-request workflows. A specific-card, review, status, question-answer, audit or deployment template applies only when the user invokes that workflow.
- For a new ChatGPT + Talvora chat, first use talvora_agent_brief if available; otherwise read AGENTS.md lines 1-140 and run scripts/Get-CodexContext.ps1 via Talvora, then load this permanent prompt. Use Talvora tools directly; never delegate to Codex or Claude without the user's explicit permission in that chat.
- The ChatGPT Project custom-instructions snippet in the stored prompt is a bootstrap option, **not** evidence that project-level settings have been changed. If the agent cannot access these repository instructions, it must not claim to have followed them.

## WHO MUST FOLLOW THESE RULES

These rules bind **every implementing agent** that changes this repository, identically: **Codex** (CLI/desktop) and **ChatGPT** with the **Talvora** connector (Dev or Admin surface). "The agent" means whichever of them is working. Nothing in a chat, prompt, memory or earlier session relaxes them; only Claude's cards/decisions in `docs/codex/*` and the user's answers recorded in `docs/codex/STATUS.md` change them. Commit trailer: `Card: <ID>` (older commits: `Codex-Card:`).

**ChatGPT via Talvora — mandatory mechanics** (Codex does the same steps in its own shell):

| Need | ChatGPT uses |
|---|---|
| Session start | `talvora_agent_brief {workspaceRoot: "C:\\Users\\tayla\\Talvora-MCP"}` |
| Reading code | `talvora_search_text` (narrow `root`, `maxMatches` ≤ 50), then `talvora_read_source` with a window of ≤ 120 lines; never whole large files. |
| Editing source/docs | `talvora_apply_patch` only (read revisions first with `talvora_read_source`); never `talvora_write_text`, `talvora_replace_text`, `talvora_append_text`, `talvora_write_bytes` or `Set-Content` on repository files. Every call passes `purpose`: one plain Turkish line saying what the change does and why (shown to the user as "ne"). |
| Desktop task line | Right after writing "In progress" (step 5): `talvora_worklog_note {workspaceRoot, text: "<card id> — <the card's goal in plain Turkish>"}`. |
| Build / tests / gate | `talvora_user_run` (signed-in user, so no SYSTEM-owned `obj/`, `bin/` or `.git/index`) for anything under ~2 minutes (`-Scope Changed`, one suite, `Get-CiStatus.ps1`). **Anything longer (Full gate, installer build, smoke runs) runs detached** so the connector call cannot time out: `talvora_user_process_start {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-Command","& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/<card>-gate.log; exit $LASTEXITCODE"], workingDirectory: "C:\Users\tayla\Talvora-MCP", visible: false}`, then `talvora_tail_text {path: "C:\Users\tayla\Talvora-MCP\artifacts\<card>-gate.log", lineCount: 20}` every 60 s until the last line is `Hepsi geçti.` or starts with `Kontrol başarısız`. |
| Git status/diff/log | `talvora_git_status`, `talvora_git_diff`, `talvora_git_log` with small limits. |
| Commit / push | `talvora_git_run` only (it enforces the delivery gate and the work contract); never through `talvora_run_powershell` or `talvora_run_process`. |
| Deploy | Build the installer for HEAD detached, as above, with `& ./scripts/Build-Windows-Installer.ps1 *> artifacts/<card>-build.log; exit $LASTEXITCODE` (the log's `SourceCommit` must equal HEAD, without `-dirty-`); then `talvora_run_powershell` → `scripts/Deploy-Windows-Installer.ps1` (it installs the last **built** artifact; its JSON `sourceCommit` must equal HEAD); then poll `/healthz` with `talvora_http_request` until `sourceCommit` = HEAD; then the installed smokes, detached as above: `& dotnet run --project tests/Talvora.Smoke/Talvora.Smoke.csproj -c Release -- --metadata-policy-only http://127.0.0.1:7676/mcp *> artifacts/<card>-meta.log; exit $LASTEXITCODE` (last line `TALVORA MCP METADATA POLICY LIVE GREEN`), then the full run `& dotnet run --project tests/Talvora.Smoke/Talvora.Smoke.csproj -c Release -- http://127.0.0.1:7676/mcp C:\Users\tayla\Talvora-MCP *> artifacts/<card>-smoke.log; exit $LASTEXITCODE` (`TALVORA MCP SMOKE GREEN`; `TALVORA MCP SMOKE RED` names the step). `--loopback-policy-live` alone is not the installed smoke. |
| Report | Turkish, short (CONTINUE PROTOCOL step 11). |

**Tool-under-test exception (user decision 2026-11-01, P-47):** repository files change only through `talvora_apply_patch`. When a card's live acceptance must exercise the tool the card fixes (e.g. `talvora_write_text`, `talvora_archive_create`, `talvora_dotenv_set`, `talvora_json_set`), call that tool only on throwaway files under `artifacts/<card>/` and delete them afterwards; never on tracked repository files.

ChatGPT never uses the delegation tools `talvora_codex_*` or `talvora_claude_*` for card work unless the user asks for it in that chat.

## Language policy

- English: agent-facing documents (`docs/codex/*`, this file), code comments, tool descriptions.
- Turkish, reading as written by the developer himself: commit messages and everything published on GitHub. One plain subject (≤ 72 characters), at most two short sentences of body, no `feat(...)` prefixes, bullet lists, test counts or emoji, never `Co-Authored-By`, `Generated with` or any AI/tool name (Claude, ChatGPT, Codex, Talvora); the last line is exactly `Card: <ID>`.
- Ordinary Turkish with correct diacritics: everything Talvora shows to people (Control Center, tray, desktop worklog, user-facing errors, MCP tool **titles**) and every report to the user (also STATUS gate lines such as `Hepsi geçti.`).
- **Claude writes no code** (user decision): every change, audit fixes included, is a card the agent implements.

## EXECUTION MANDATE — the agent implements, Claude decides (binding, highest priority)

The agent does **only** what Claude's cards say, **exactly** as they say it (user decision 2026-10-18). Claude owns every product, architecture and especially every **visual/cosmetic** decision. This section overrides any wording elsewhere that seems to leave room for the agent's own judgement.

**Scope**
1. Work only on the current queue card. No "while I'm here" fixes, refactors, renames, reformatting, file moves, comment rewrites, dependency bumps or test rewrites outside the card's **Files**. If another file must change for the card to compile or pass, change the minimum and name it in the Log with the reason.
   **Supporting changes are not proposal material:** plumbing a value the card asks for to where the result is built, linking a new source file into a test project, registering a test/scenario in a runner list, manifest/surface-policy entries for a tool the card adds, `.context7/verification.json`, README tool lists. Do them minimally and list them in the Log. A proposal is needed only when the card leaves a **behaviour, design, safety, ordering or scope** decision open, or its Acceptance cannot be met.
   **Ordering is not proposal material either:** if a card depends on a later card, mark it `[?] waiting for <card>` in STATUS and take the next `[ ]`.
2. Do not start work that is not a card. What you notice goes to STATUS → "Findings for Claude" (one line: file:line + what you saw); do not fix it.
3. Do not add, remove or upgrade packages, tools, services, endpoints, settings or MCP tools unless the card names them.
4. **One card = one commit.** A follow-up fix to a card that is already committed is not a second commit with the same id: write it under "Findings for Claude" and Claude makes a D-card. Exception: a card's commit that fails CI is fixed by one commit with trailer `Card: <ID>` and body line `CI follow-up: <run id>`.

**Decisions**
5. Where a card prescribes values, names, code shape, library, algorithm, order or copy, use exactly that; do not "improve" it.
   **"#### Exact implementation" blocks are literal:** apply their steps in order and copy their code and strings character for character (only whitespace/indentation may adapt to the file). Do not rename identifiers, reorder logic, add checks, merge steps, "simplify" or "modernise". Only two deviations: (a) a compile error caused by an API detail (exact type/namespace/member name) — fix the minimum and quote the compiler message in the Log; (b) the code does not match the file because the file changed since the card was written — stop with a proposal quoting the card snippet and the current lines. Anything else → proposal.
   **Reference copies:** "copy `docs/codex/reference/**/<file>` → `<target>`" is done with `talvora_apply_patch` Add File (ChatGPT) or `Copy-Item` (Codex) and verified by the hash of the **normalised content** (CRLF→LF, UTF-8 BOM removed) against that folder's `SHA256SUMS.txt`: `$t=[IO.File]::ReadAllText('<target>').Replace("`r`n","`n"); [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($t))).Replace('-','').ToLower()`. A raw-byte difference caused only by newlines is not a deviation.
6. If a card is ambiguous, contradicts the code or current upstream docs (Context7), or cannot meet its Acceptance as written: **do not choose yourself**. Write the question with evidence and 1–3 options into `docs/codex/PROPOSALS.md` (template there), mark the card `[?]` in STATUS with "waiting for Claude: P-<n>", revert its partial work and continue with the next `[ ]` card. A proposal-only commit uses the trailer `Card: P-<n>` (never the blocked card's id, which the work contract would then refuse for the real card commit). A safety mechanism (guard, check, lock, validation) is never weakened, bypassed or given a fallback without a proposal.
7. Context7/vendor docs **verify** the prescribed approach and fill exact API names/versions; they never replace it with a different approach.

**Design and cosmetics (zero autonomy)**
8. Everything visible — layout, colours, fonts, sizes, spacing, radii, shadows, icons, glyphs, motion, timing, wording/Turkish copy, tooltips, empty/loading/error states, tool titles — comes **only** from the card, `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), the parts of `docs/codex/DESIGN-CONTROL-CENTER.md` that DESIGN-SANTRAL keeps (information architecture, copy deck §9, accessibility §10) or an approved proposal. Never invent a colour, icon, animation, string, visual element or behaviour.
9. Where the spec is silent about a **minor layout detail** (an alignment or a gap inside an element the spec defines), use only the spec's token scales (`Tv.Space.*`, `Tv.Radius.*`, `Tv.Type.*`, colour tokens) and list each such choice in the Log under "Design gaps filled". Anything else visible and unspecified (a new element, text, icon, colour, state, animation) → proposal (rule 6), never a guess.
10. Visual references come only from the card and the design specs; there is no design research step.
10a. **`[C]` items are Claude's** (user decision 2026-11-01: "tasarım senin işin, ChatGPT'ye bırakma"): look-only work (theme, XAML layout, presentational code, motion) that Claude builds, tests, deploys and commits itself. The agent skips `[C]` lines when selecting (step 5), never edits the files of an open `[C]` card, and never changes their look later except through a Claude card. Logic a design needs (new data, protocol fields) is always a normal agent card queued first.
11. Every UI/worklog card ends with the before/after PNGs its Acceptance requires; Claude approves the look at the next audit.

Violations found in Claude's audits become D-cards that revert the unrequested change.

### CONTEXT7 PROTOCOL — use Context7 precisely (user decision 2026-11-01, part of the EXECUTION MANDATE)

Context7 stops guesses about APIs: use it, precisely, where a wrong guess would break the card. It refines the "Context7 quality gate" below; the evidence format stays the same.

1. **What to look up:** before coding, the external APIs whose exact name, signature, option, default, error or version behaviour the code relies on — third-party packages, framework/platform APIs (see the map), protocols, CLIs, and new, version-sensitive or edge-case standard-library calls. Not everyday calls you are sure of (`Date.now`, `strings.Contains`, `Array.map`). Usually 1–4 lookups; a card with nothing worth looking up (docs, tests, a proposal) records its main library's one relevant fact instead.
2. **Which id:** from `docs/codex/CONTEXT7.md` (pinned versions, exact ids; the versioned id when given); never `resolve-library-id` for a mapped library. Unmapped: resolve once, pick the exact package (no fork, mirror, translation or DeepWiki copy), the indexed version closest to the lockfile, trust score ≥ 7, and add `Context7 map candidate: <package> <version> → <id>` to the card's log entry (Claude maps it).
3. **How to ask:** one precise query per call naming symbol, version and question (e.g. `Vite 8 build.rolldownOptions.input with several HTML entries and output.entryFileNames`), never a one-word topic (`api`, `docs`, `usage`); about 3,000–5,000 tokens. Symbol missing → rephrase once with the exact symbol or error text, then open the map's official page. At most three calls per question.
4. **Version gaps:** where the map marks the pinned version as newer than Context7's index, use the newest indexed version only for long-stable APIs and confirm anything that may have changed in the release notes or changelog the map links; record both.
5. **Precedence:** card > official vendor documentation > Context7 snippet > memory. Context7 or the vendor page contradicting the card → proposal quoting id, query and snippet. Context7 and the vendor page disagreeing → the vendor page wins; note it in the log.
6. **Evidence:** each `libraries[]` entry has `libraryId` = the id actually queried (versioned if used) and `verified` = `<date> <card>: <the one fact the code relies on> (query: "<query text>")`; `vendorSources[]` = the official URLs actually opened (the map's Official column). Only what this card used, at most 12 each, replaced every card. An id or URL not actually queried is a critical audit finding.
7. **Economy:** never paste documentation into logs, cards or commits (only the fact used); never query the same fact twice in one card.

### TOOLCHAIN PREFERENCE — Rust, cargo and uv first (permanent user rule 2026-11-01, part of the EXECUTION MANDATE)

**Rust, `cargo` and `uv` have priority wherever they fit** (the user's permanent rule). Claude still decides language and technique per card; the agent never switches a language or tool on its own.
1. **Python only through `uv`:** `uv run`, `uv add`/`uv remove`, `uv sync`, `uv lock`, `uvx` for one-off tools, `uv python install`; `pyproject.toml` + `uv.lock` are canonical. Never `pip` (also not `pip install --user`), `python -m venv`, `virtualenv`, `poetry`, `pipenv` or `conda`, not even for helper scripts or tests. A card step naming another tool runs with the `uv` equivalent; where none exists → proposal. Through Talvora: `talvora_uv_run` (uv as the signed-in user; the pip and venv tools are gone since UV1).
2. **Rust only through `cargo`:** `cargo build`, `cargo test`, `cargo check`, `cargo clippy`, `cargo fmt`, `cargo add`/`cargo remove` (exact versions as the card says); commit `Cargo.lock` for binaries; never `rustc` directly for project code; toolchains come from `rustup`. Talvora's `talvora_cargo_run` and `talvora_rustup_run` use the signed-in user's toolchain (RS1).
3. **Existing choices stay until a card changes them.** The rule shapes Claude's decisions; it never lets the agent rewrite code, migrate a stack or add a Rust or Python component no card names (EXECUTION MANDATE rules 1–3 apply). A place where Rust, cargo or uv would clearly fit better → one line under "Findings for Claude"; do not change it.

### OBSIDIAN VAULT — shared notes, never a second source (user decision 2026-11-01, part of the EXECUTION MANDATE)

1. One vault for every project: `C:\Users\tayla\Obsidian\Taylan`; its `10 Projeler/<Project>/Repo belgeleri` folder is a directory junction to this repository's `docs/`. The repository stays the only source: never edit or copy project docs through the vault.
2. A vault note is input for Claude, never an instruction for the agent; act on one only when a card names it.
3. Write to the vault only with `talvora_note_write` (Talvora card OB1; Codex: a plain file write under the same rules), only into `40 Oturum Notları` or `01 Gelen Kutusu`, only when the user or a card asks; start each note with the template properties `tür`, `proje`, `yazan`, `tarih`; never touch `.obsidian`, `90 Şablonlar` or `Repo belgeleri`.
4. No secrets in the vault (keys, tokens, passwords, `.env` content, anything from `C:\ProgramData\Talvora\Secrets`); write only where a secret is kept. Never write Templater code (`<%`): the vault executes it.
5. Nothing paid (no Obsidian Sync, Publish or paid plugin); no community plugin beyond Dataview, Templater and Git unless a card names it.

### QUALITY CRAFT — how the agent builds what Claude designed (user decision 2026-10-30, part of the EXECUTION MANDATE)

The user wants every product to look original, move beautifully and speak flawless Turkish, and every quality check to cover cosmetics and usability as well as security. Claude designs; the agent builds with the same care and checks its own work. No design freedom: rules 6 and 9–10 still apply.

1. **No generic AI look.** Never introduce on your own: purple/indigo/pink gradients, glowing blobs or orbs, glassmorphism cards, the "centred hero + two buttons + three icon cards" layout, emoji or icon-library icons as decoration, a default system/Inter font where the card names another, everything centred in identical white rounded cards. A card whose values seem to produce such a look or leave a visible detail open → finding or proposal, never an "improvement".
2. **Motion and graphics exactly as specified:** durations, easing, delays, sequences and frame budgets from the card, literally; animate `transform`/`opacity` (or the canvas the card names), not layout properties; reduced motion exactly as the card says; remove every timer, listener and animation frame on teardown; only the libraries this project allows (`Directory.Packages.props` and the card; WPF visuals only from the design specs).
3. **Turkish quality gate:** copy every UI string character for character from the card into the resource file it names. Before committing, reread each new or changed string for missing diacritics (ç ğ ı İ ö ş ü), apostrophes after proper nouns and abbreviations, `de/da`, `ki`, `mi` spelling, Turkish quotation marks and Turkish-locale (`tr-TR`) casing and sorting. A doubt is a finding; never reword a string yourself.
4. **Self-review before done:** after the gate passes and before ticking the card, write one short "Self-review" block (at most four lines) in its log entry: **cosmetics** (the card's values; loading, empty and error states as specified), **usability** (the main task path works end to end, every action gives feedback, targets and focus as the card says), **Turkish** (item 3), **correctness and security** (tests, limits, errors handled). Anything wrong outside the card's files → "Findings for Claude"; never fix outside the card.
5. Cards that change what users see still end with `[~] visual check pending (Claude)`: Claude looks at the real screens at the next audit.

## TOKEN ECONOMY (work cheaply, never at the cost of correctness)

Every file read, command output, tool schema and screenshot costs tokens. A card's Acceptance always wins over these rules; ChatGPT applies them through the tools in "WHO MUST FOLLOW THESE RULES".

**A. Read only what the card needs**
1. **Context first:** `talvora_agent_brief` (Codex: `pwsh scripts/Get-CodexContext.ps1`, or `-Card <ID>`); work from its output and never open a whole `docs/codex/*.md` file. If the brief reports `truncated: true`, run `Get-CodexContext.ps1 -Card <id>` for the item you are about to take and work from that full card; never implement from a cut card.
2. **STATUS without history:** read `docs/codex/STATUS.md` up to `## Log` plus the newest Log entry only.
3. **AGENTS.md once per session**, not again between steps of the same card.
4. **Locate, then read a window:** `git grep -n "<symbol>"` / `rg -n` (ChatGPT: `talvora_search_text`), then ±40 lines. Read a whole file only if it is under 250 lines or the card rewrites most of it.
5. **Never open in full:** `packages.lock.json`, `THIRD-PARTY-NOTICES.md`, `docs/SOURCE-EDIT-ENGINE-ARCHITECTURE.md`, the largest sources (`SourceEditRegressionRunner.Misc.cs`, `RoslynSemanticEditEngine.cs`, `ManagedMcpTunnelProvisioningService.cs`: 2 000+ lines each; `TalvoraToolGuides.cs`, `TrayApplicationContext.cs`: 1 700+), images, `bin/`, `obj/`, `artifacts/`. Grep them.
6. **Do not re-read a file you just edited**; re-read only the hunk if a build or test fails there.
7. **Talvora calls:** small limits (`maxMatches`, `maxResults`, `lineCount`, `maxCommits`) and the narrowest `root`; never list or search the whole repo or a drive root.
8. **Finished work is not in the working tree** (user decision 2026-11-07): closed cards, old Log entries and decided proposals are removed at every audit; git keeps them. Do not search git history for them unless a card names a commit.

**B. Build and test cheaply**
1. **Inner loop:** `pwsh scripts/Invoke-QualityGate.ps1 -Scope Changed` (it builds and runs only what the uncommitted change can affect, and prints its choice), or build only the touched project (`dotnet build src/<Project>/<Project>.csproj -c Release --no-restore -v q -nologo -clp:ErrorsOnly`) and run only the affected suite.
2. **Restore once per card** (`--locked-mode`), not before every build.
3. **Full gate once per card**, at the end (`pwsh scripts/Invoke-QualityGate.ps1`); never twice in a row without a code change in between.
4. **Tails only:** `... 2>&1 | Select -Last 40` (ChatGPT: inside the PowerShell script, with `maxCapturedCharactersPerStream` ≤ 8000).
5. **Installer/deploy once per card**, only when the Acceptance needs the installed runtime; read the last 20 lines of its output unless it failed; poll `/healthz` instead of reading logs.
6. **Screenshots:** only the PNGs the card requires, from the preview/smoke harness, cropped to the element.

**C. Write cheaply**
1. **STATUS Log entry ≤ 8 lines:** what changed, gate last line + counts, measured numbers, installed commit, anything open; no pasted code or command output.
2. **Report** (Turkish): the step 11 items in short sentences; `git diff --stat`, never the full diff.
3. **Never echo** large files, JSON or logs into chat, STATUS or Gitea; summarise in one line with the key numbers.

**D. Avoid wasted work**
1. One card per message; do not look ahead into later cards.
2. If the same approach fails a third time, stop and write a proposal (EXECUTION MANDATE rule 6) instead of burning tokens.
3. Do not regenerate lock files, generated assets or notices unless the card says so.

## QUEUE ADDITIONS (how Claude adds cards while the agent is working)

Before selecting an item (CONTINUE PROTOCOL step 5), if `docs/codex/QUEUE-ADDITIONS.md` has entries under "## Pending", insert each listed queue line into `docs/codex/STATUS.md` → "Agent queue" exactly at the position it names, move the entry to "## Applied" with the date, and include both files in the next card's commit. Do not change the wording of the queue lines.

## CONTINUE PROTOCOL ("Talvora'ya devam et")

When the user says "Talvora'ya devam et", "Talvora devam", "sıradaki adım" or similar (in Codex or in a ChatGPT chat with the Talvora connector), run this sequence without asking questions. **One queue item per message**, then stop and report.

1. **Location:** `C:\Users\tayla\Talvora-MCP`. If git reports "dubious ownership", prefix git commands with `-c safe.directory=C:/Users/tayla/Talvora-MCP`; never change the global git config.
2. **Sync:** `git fetch origin`. A dirty tree is continued only if it belongs to STATUS "In progress"; otherwise stop and ask. `git pull --ff-only origin main`; never force, reset --hard, rebase or rewrite history.
3. **Context:** follow TOKEN ECONOMY; apply pending QUEUE ADDITIONS before step 5. If the card names a proposal (`P-<n>`), read only that entry of `docs/codex/PROPOSALS.md` (search `### P-<n>`), never the whole Decided section.
4. **Baseline:** run `pwsh scripts/Invoke-QualityGate.ps1 -Scope Changed` **and** check the GitHub `windows-ci` result of `origin/main` (step 10b method). If either is red at the start, this message's task is to make it green.
5. **Select:** the "In progress" item, otherwise the first `[ ]` item in STATUS → "Agent queue" (skip `[?]`, `[C]` and `[~] … (Claude)` lines). Write "In progress: <item>" into STATUS first.
6. **Implement:** follow the card's **Why / Files / Do / Don't / Acceptance** exactly (every open card is in `docs/codex/ROADMAP.md`). Test first (red), then code (green). If it cannot work as written, write a proposal (EXECUTION MANDATE rule 6); never improvise. Context7 before relying on any framework, SDK or CLI behaviour.
7. **Verify:** quality gate last line **`Hepsi geçti.`**, run **as the signed-in user** (Codex: its own shell; ChatGPT: `talvora_user_run`, never `talvora_run_powershell`, because LocalSystem hides user-context failures). Runtime-affecting cards also deploy with `scripts/Deploy-Windows-Installer.ps1` and verify `/healthz.sourceCommit` + the installed smokes (Deploy row; a red step the card did not touch → proposal with the message); UI cards attach both-theme PNGs.
8. **Record:** tick the item. `[x]` only when **every** Acceptance step, deploy and live checks on the installed runtime included, is done in this message (never `[x]` with "evidence follows" or "pending"); otherwise `[~] live pending: <what>` and do those checks first in the next message, before any other card. **A check that needs a wait is Claude's** (a count over an hour of running, a next-day result, anything that cannot finish in this message): never wait, poll or re-check it; write `[~] live pending (Claude): <what>, since <installed commit> <local time>` and take the next `[ ]` card in the next message. `[~]` + a "User queue" line when user evidence is needed. Add a Log entry (English, newest on top: what changed, tests, Context7 ids, installed commit, executor Codex or ChatGPT), reset "In progress" to `(none)`, and mirror the evidence to the Gitea issue named in STATUS → Fixed facts → "Gitea tracker" (best effort; never stop or ask because of it).
9. **Commit:** exactly one commit per item: a plain Turkish subject (Language policy), an optional short body, a blank line and the trailer `Card: <ID>` as the last line (its own paragraph, so git reads it as a trailer).
10. **Push:** only while STATUS "Fixed facts" says `Push: Authorized`: `git push origin main` and `git push github main`, never force. On failure retry once, then report.
10b. **CI is part of done.** After the GitHub push, check the `windows-ci` run of that exact commit **once, without waiting**, as the signed-in user (where `gh` is logged in): Codex `pwsh -NoProfile -File scripts/Get-CiStatus.ps1 -TimeoutMinutes 0`; ChatGPT `talvora_user_run {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-File","scripts/Get-CiStatus.ps1","-TimeoutMinutes","0"], workingDirectory: "C:\Users\tayla\Talvora-MCP"}`. Exit 0 = green → the card may be `[x]`. Exit 1 = red (it prints the failing lines) → no other card: the next task is the CI fix, one commit with trailer `Card: <same ID>` and body line `CI follow-up: <run id>`. Exit 2 = no run yet or still running → `[~] CI pending`, report and stop (never wait or poll inside the message); check it first in the next message and turn the line into `[x]` (with the run id) in that message's card commit, or, with no card left, leave the `[~]` line for Claude instead of a STATUS-only commit. A local gate run as LocalSystem never replaces this check (identity-dependent code passes there and fails on GitHub).
11. **Report to the user in Turkish, short:** item done · changed files · gate last line · deploy/installed commit · push status · next item · what the user must do (if anything).

**Stop conditions** (report in Turkish and wait): agent queue empty (list the user queue); acceptance impossible without breaking a rule (write a proposal); a required `[?]` dependency; work that needs history rewrite, repository visibility or settings changes, reading or moving anything in `C:\ProgramData\Talvora\Secrets`, or running `scripts/Reset-And-Install.ps1` without a card that says so.

## Modern-by-default policy

- Stay on supported, current stable toolchains and libraries; prefer the newest supported stable release, or an LTS line when it is the correct machine/runtime baseline. No wildcard package versions, deprecated APIs, obsolete CLI paths, legacy compatibility aliases or stale SDK fallbacks when a supported modern replacement exists.
- Dependency versions are managed centrally in `Directory.Packages.props`; .NET restore is lock-file based and CI uses locked mode.
- A dependency/toolchain update is incomplete until targeted tests pass and the affected installed-runtime contract is verified where applicable.

## Context7 quality gate

- How to query, which ids to use and what to record: **CONTEXT7 PROTOCOL** (map: `docs/codex/CONTEXT7.md`). Query Context7 before relying on third-party framework, SDK, library, protocol, CLI or package-manager behaviour the change depends on; when it matters, the card names the library and the exact point to verify — verify that point, not a generic topic.
- Context7 is mandatory for changes under `src/`, `scripts/`, `.github/workflows/`, `Directory.*.props` and `global.json` (the CI gate enforces it); test-only and documentation-only changes are exempt.
- `.context7/verification.json` holds **only the current change**: replace `verifiedAtUtc`, `scope`, `libraries` (at most 12) and `vendorSources` (at most 12) every time, never append; earlier evidence lives in git history. Record the library ids and the verified point in the card's STATUS Log entry.
- Without a suitable Context7 source, record the gap and use the vendor's primary documentation; never memory or an old code example.

## Mandatory project delivery gates

- Context7 evidence is a delivery gate enforced by Talvora code, not a prompt convention, and it fails closed: `talvora_git_run` push and `talvora_dotnet_publish` refuse to continue when `.context7/verification.json` is missing, stale, malformed or incomplete. Only the machine-readable evidence accepted by ProjectDeliveryGate permits delivery; handoff text, model memory, issue prose or a verbal claim never does.
- Awwwards evidence was removed (audit 29, user decision 2026-10-28): the look comes only from Claude's design specs. Do not reintroduce `.talvora/awwwards-verification.json`.

## Mandatory Windows desktop progress reporting

A software/runtime contract, not a prompt preference or an optional courtesy:
- Talvora proactively reports meaningful work to the active interactive Windows desktop. Every MCP tool execution passes through the desktop-progress wrapper (never bypassed for ordinary tool execution), but only meaningful user-visible work creates a card; low-value reads, searches and health checks stay silent unless they become part of a meaningful failure path.
- Long-running meaningful work emits a restrained heartbeat in tens of seconds; completion, cancellation and failure update the same operation status where practical.
- The LocalSystem service never owns the normal interactive UI: it publishes session-aware progress to Talvora.Tray through local IPC, and the signed-in tray process owns the notification window. Service code targets the active interactive session through supported Windows APIs, never a fixed session id; cross-session IPC uses an explicit ACL and a session-specific endpoint.
- Notifications are polished, nonmodal, full-text WPF cards: no arbitrary truncation (long content wraps and stays fully viewable in the card's scrollable area). Cards are directly draggable; dragging or the `Sabitle` control pins the notification-stack anchor and keeps that position per interactive user; unpinning returns to automatic placement. Pinning controls position only and never turns a notification into a permanent card.
- The desktop surface is one live worklog card: active work never auto-dismisses before a terminal state; a success stays readable for about three minutes, a failure longer; a newer meaningful operation may replace an older terminal card, so the desktop never becomes a notification stack. Notifications are deduplicated/coalesced; the normal visible-card budget is one.
- User-facing copy is ordinary Turkish: no raw tool names, command lines, exit codes, stack traces, SDK or repository jargon or internal transport terms. Error cards say in plain language what went wrong, what is being protected and what Talvora will try next.
- A dark terminal/neon identity is allowed, fabricated telemetry is not: every visible timestamp, elapsed duration, file name, change count, diff preview, result or error detail comes from the real tool call/runtime result.
- **Every bar, meter, signal, counter and duration is a measured value** (user, 2026-11-01: "gerçek veri olmasına çok dikkat et"): its source is a real measurement of the real tool call (a stopwatch, a timestamp, a count from the result); a visual length maps to that number on a fixed, documented scale and the number is printed next to it. No percentage unless the work reports real progress. When the measurement is missing, nothing is drawn: no placeholder, no estimate, no animation that suggests progress.
- Technical evidence (real changed files, real +/− line counts, a bounded real diff preview) belongs in a clearly separated expandable surface, never mixed into the plain-language explanation, and passes Talvora's canonical sensitive-data redaction before reaching the desktop (a secret-bearing line may become a truthful redaction marker; never trade privacy for visual effect).
- Thrown exceptions and non-throwing errors (MCP `CallToolResult.IsError`, unsuccessful results, nonzero command outcomes, timeouts) are never reported as success.
- Automatic and pinned placement stays correct on mixed-DPI multi-monitor desktops: treat Windows screen/window coordinates as native pixels instead of dividing global monitor coordinates by one WPF window DPI scale.
- Delivery is best-effort and never fails, cancels, delays or changes the result of the underlying operation; headless, CI and no-active-session execution stay safe.
- Substantial Talvora work keeps the user informed through desktop progress updates at milestones and at intervals that are not excessively long.

## Validation and source editing

- `talvora_apply_patch` is the primary source/config/document editor; read existing files before changing them.
- **Git identity invariant (P-24, 2026-10-21):** remote Git verbs that use the user's credentials (`fetch`, `pull`, `push`) run as the signed-in user, never as LocalSystem. Never loosen SSH key ACLs, copy keys or change git config to make a LocalSystem git call work; a credential failure as LocalSystem is a routing bug → Findings for Claude.
- Keep targeted validation fast during implementation; run the canonical installer and exact-installed smoke when runtime, packaging, shared infrastructure, dependency graph or release behaviour changes.
- Gitea is the primary modernization tracker: keep the active phase issue updated with findings, validation evidence, commit SHA and closeout state.
