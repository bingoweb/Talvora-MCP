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

## Phase OB — Obsidian vault (user request 2026-11-01)

The user installed Obsidian. Claude built one vault for every project at `C:\Users\tayla\Obsidian\Taylan` (local git history, no remote, nothing paid). Each project's `docs/` folder is linked into the vault as a directory junction named `Repo belgeleri`, so the repository stays the only source (AGENTS.md → `### OBSIDIAN VAULT`). Agents read notes with the existing `talvora_read_source` / `talvora_search_text` (the vault is a git folder, so it is a recognised workspace). OB1 adds the one missing piece: a write tool that refuses secrets and Templater code (`<%`, which the vault's Templater plugin could execute) by code.

### OB1 — `talvora_note_write`: write vault notes, refuse secrets and the linked repo docs

**Why:** user decision 2026-11-01 (ChatGPT and Claude read and write vault notes; no secret may enter the vault). Secret protection must be code, not convention.

**Files:**
- new `src/Talvora/Tools/NoteTools.cs` (reference copy)
- new `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs` (reference copy)
- `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs`
- `src/Talvora.Shared/FileLog.cs`
- `src/Talvora.Shared/TalvoraToolManifest.cs`
- `src/Talvora.Shared/TalvoraToolTitles.cs`
- `src/Talvora.Shared/TalvoraToolGuides.cs`
- `src/Talvora.Shared/TalvoraMcpToolSurfacePolicy.cs`
- `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs`
- `tests/Talvora.Smoke/SmokeScenarios.SurfacePolicy.cs`
- `tests/CodexCliIntegrationSourceRegression.ps1`
- `tests/FocusedMcpSurfaceSourceRegression.ps1`
- `README.md`, `docs/ARCHITECTURE.md`
- `.context7/verification.json`

**Context7:** `/modelcontextprotocol/csharp-sdk`: "McpServerTool async Task method with CancellationToken parameter binding, ModelContextProtocol 2.2.0" (the SDK binds the token; it is not part of the input schema). `/dotnet/docs`: "FileAttributes.ReparsePoint DirectoryInfo.Attributes junction detection .NET 10". Vendor: the two Official links in `docs/codex/CONTEXT7.md`.

#### Exact implementation

Step 1 (red first). Copy `docs/codex/reference/ob1/SourceEditRegressionRunner.Notes.cs` → `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `617e0451159036130bd64d11b0f130016380883654e8e5db1a8fe4cb34b3c0ab`). In `SourceEditRegressionRunner.cs`, directly after the line `("ap2-agent-patch-dialect", Ap2AgentPatchToleranceAsync),` add:

```csharp
            ("ob1-vault-note-writer", RunVaultNoteWriterAsync),
```

Build the test project: the red state is the compile error `CS0103` for `NoteTools`; quote it in the Log.

Step 2. Copy `docs/codex/reference/ob1/NoteTools.cs` → `src/Talvora/Tools/NoteTools.cs` (normalised SHA-256 `bac639e6a6d458d3ec48786829a9aee8e3a984915c047646e511c2e207c82ae6`).

Step 3. `src/Talvora.Shared/FileLog.cs`: insert directly before `    private static string FormatExceptionDetails(string value)`:

```csharp
    public static bool ContainsSensitiveData(string? value) =>
        !string.IsNullOrEmpty(value) &&
        !string.Equals(RedactSensitiveData(value), value, StringComparison.Ordinal);

```

Claude verified steps 1–3 in a scratch clone on 2026-11-01 (re-verified after adding the `<%` refusal): the test project builds with 0 warnings and `ob1-vault-note-writer` is green, including the real-junction refusal.

Step 4. `src/Talvora.Shared/TalvoraToolManifest.cs`: directly after `"talvora_worklog_note",` add `"talvora_note_write",`.

Step 5. `src/Talvora.Shared/TalvoraToolTitles.cs`: directly after `["talvora_worklog_note"] = "Görev satırını ayarla",` add:

```csharp
        ["talvora_note_write"] = "Kasaya not yaz",
```

Step 6. `src/Talvora.Shared/TalvoraToolGuides.cs`: directly before `["talvora_gh_run"] = new(` add:

```csharp
            ["talvora_note_write"] = new(
                "Create or append a Markdown note in the user's Obsidian vault; secret-looking text is refused.",
                P(("vaultRoot", "Absolute vault root, C:\\Users\\tayla\\Obsidian\\Taylan."),
                  ("path", "Vault-relative .md path, e.g. 40 Oturum Notları/2026-11-01 Talvora OB1.md."),
                  ("content", "Markdown text, at most 64000 characters, no secrets."),
                  ("mode", "create (new note) or append (existing note).")),
                """{"vaultRoot":"C:\\Users\\tayla\\Obsidian\\Taylan","path":"40 Oturum Notları/2026-11-01 Talvora OB1.md","content":"# OB1\n","mode":"create"}"""),
```

Step 7. Counts (the tool is Dev-only: it is in neither `DevelopmentExcludedTools` nor `AdministrationTools`; it is not open-world and not destructive):
- `TalvoraMcpToolSurfacePolicy.cs`: `ExpectedFullToolCount = 250` → `251`; `ExpectedDevelopmentToolCount = 213` → `214`. Admin `88` and shared `51` stay.
- `TalvoraMcpToolMetadataPolicy.cs`: `ExpectedReviewedToolCount = 250` → `251`. Open-world `85` and destructive `100` stay.
- `tests/CodexCliIntegrationSourceRegression.ps1` and `tests/FocusedMcpSurfaceSourceRegression.ps1`: the strings `'ExpectedFullToolCount = 250'` → `'ExpectedFullToolCount = 251'` and `'ExpectedDevelopmentToolCount = 213'` → `'ExpectedDevelopmentToolCount = 214'`.
- `README.md`: every Full count `250` → `251` and every Dev count `213` → `214` in the places `tests/ToolManifestDocumentationRegression.ps1` checks: badge text, badge URL, the summary line (`**251 total**, **214 Dev**, …`), the focused diagram line, the full diagram line, the `/mcp` and `/mcp/dev` table rows.
- `docs/ARCHITECTURE.md`: `/mcp` (**251 tools**), `/mcp/dev` (**214 tools**), "complete 251-tool capability set".

Step 8. `tests/Talvora.Smoke/SmokeScenarios.SurfacePolicy.cs`: directly after the `AssertSurface("talvora_worklog_note", development: true, administration: false);` statement add:

```csharp
        AssertSurface(
            "talvora_note_write",
            development: true,
            administration: false);
```

Step 9. Refresh `.context7/verification.json` with the two lookups above (replace, never append).

**Don't:**
- add read, search or list note tools (the existing tools cover reading);
- redact and then write: secret-looking content is refused whole, never partly written;
- relax the `<%` refusal (Templater in the vault executes such code; refuse, never escape);
- add folder checks beyond the reference file (the allowed folders are an AGENTS.md rule);
- run git in the vault, add a remote, or touch Obsidian settings;
- add a vault path setting or a default `vaultRoot` (the service runs as LocalSystem; the caller always passes it).

**Acceptance:**
- Red (CS0103), then green: `ob1-vault-note-writer` passes.
- Full gate `Hepsi geçti.` as the signed-in user (the Q7 dev payload budget included; if it is exceeded, stop with a proposal and never raise the budget yourself).
- Deploy; `/healthz.sourceCommit` = HEAD. Live check on the installed runtime: `talvora_note_write {vaultRoot: "C:\\Users\\tayla\\Obsidian\\Taylan", path: "40 Oturum Notları/2026-11-01 Talvora OB1.md", content: <a session note with the template properties tür: oturum, proje: Talvora, yazan: ChatGPT, tarih: 2026-11-01>, mode: "create"}` succeeds; the same tool with path `01 Gelen Kutusu/OB1 deneme.md` and content `api_key=abcdef123456` is refused with the Turkish secret message and creates no file.
- Push; CI once.

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-07 (40, deep, whole project) · range `audit/2026-11-07-39..47b43a8` plus every subsystem · result: D30–D39, P-49 decided, [C] SN9, [C] WL2, agent documents cleaned

User request: "Talvora'yı derinlemesine incele … kodları didik didik et", then "tüm talvora projesini baştan aşağı denetle" and "bitmiş olan işleri temizlemenin bir yolunu da bul … tüm dosyaları kontext ve token ekonomisi önceliği ile optimize et". Method: real failure records first (tray.log, Windows Application log, installer log, live `/healthz/traffic`), the analyzers of the whole solution in a scratch clone (`AnalysisMode=Recommended`, 794 warnings, product code clean of bug-class rules), a clone detector over `src` (38 duplicated 8-line windows in 95 k lines: low), then line-by-line reading of the risky parts of every subsystem (agent brief, desktop progress pipe, tunnel maintenance, atomic publication, file tools, archive extraction, storage maintenance, Control Center, worklog history, quality gate, governance documents).

Range: D26, D27, D28, RV1 and LB1 match their cards line for line; signed-in Full 68/0; CI success on every commit; installed 47b43a8; D26/D27/D28/LB1 visual checks passed on smoke captures in both themes (closed). RV1 data: 1,689 tunnel-client launches in 6.11 h, slowest `runtimes status` 9,874 ms against its 12 s limit.

| # | Severity | Where | Finding (verified) | Card |
|---|---|---|---|---|
| 1 | CRITICAL | `AgentBriefTools.Get` + `Get-CodexContext.ps1` | The brief is capped at 60,000 characters and cuts the end of the context; the context printed STATUS (30.7 k) first, so the card, the newest Log entry and the open proposals were cut. Proven with the live tool: `truncated: true`, no `=== CARD`. The cleanup below fixes today's brief; D30 makes the order safe for good. | D30 |
| 2 | HIGH | `tests/QualityGateParallelRegression.ps1` in the parallel phase | The 7-second overlap check runs beside the heaviest suites and fails under load (7.4 s, 8.9 s) while it passes alone (P-49). | D31 |
| 3 | HIGH (data truth) | `TalvoraLineTrafficMeter` (SN1, Claude's) | Talvora's own clients (`Talvora.Smoke`, `Talvora.Tray`) are counted: after every install the smoke's deliberate unknown-tool probes show as "Admin 1 çağrı · 1 hata" on the Santral. User: never count them. | D32 |
| 4 | HIGH (desktop notifications) | `DesktopProgressPipeListener`, `TalvoraDesktopProgressNotifier` | Under load the tray drops frames: 27 "IPC client timed out while sending a frame" and 4 "frame payload was truncated" in four days, all in the minutes of builds, gates and installs. The tray's 3 s read window starts at connect while the service still verifies the server through `Process.MainModule` (slow, uncancellable); the 2 s write budget is short for large frames; a final drop is logged only at Debug. A lost last frame leaves the card on "running". | D33 |
| 5 | MEDIUM (efficiency) | `_genericMcpTimer` (15 s) → `GetRuntimeStatusCachedAsync` (2 s cache) | About 240–290 `tunnel-client runtimes status` launches an hour around the clock, partly only to learn the health address that tunnel-client already publishes in `<StateRoot>\health\<alias>.url` (verified: `http://127.0.0.1:60409`, rewritten on every restart). | D34 |
| 6 | MEDIUM | `GetLatestTunnelClientTagAsync` | A failed GitHub lookup (offline, 403 rate limit) records no time, so it repeats every 15 s per tunnel, each with a log line and a deferral event. | D35 |
| 7 | MEDIUM | `AtomicFile.PublishDurable` | Retries only `IOException`; a concurrent `File.Replace` or a delete-pending destination raises `UnauthorizedAccessException`, which fails the write at once (seen in the AtomicRace regression). | D36 |
| 8 | MEDIUM (Turkish) | Tray event texts | Event slips show English and jargon: "tunnel-client update recovered", "Interrupted update recovered with … runtime readiness …", "Incomplete update journal was rolled back …", "latest kontrolü", "health/ready doğrulaması", "MCP" in titles. | D37 |
| 9 | PRODUCT (user decided) | `DesktopProgressNotificationPresenter.PublishCore` | "Yalnızca hatalar ve ajan işleri için kart göster" treats only Codex/Claude as agents, so ChatGPT's work shows no card. User: ChatGPT counts as an agent. | D38 |
| 10 | LOW | `PenpotMcpTools.RedactSensitiveText` | Penpot masks only its URL token; the shared `FileLog.RedactSensitiveData` (keys, Authorization, JWT) is not applied to its error text. | D39 |
| 11 | MEDIUM (Claude's look code) | `Controls/Switchboard.cs` | The 1 s traffic poll rebuilds every hotspot (`Children.Clear()`), so keyboard focus and the hover ring are lost every second; with failures on a line the "son …" text meets a neighbour's "yeniden bağlanıyor"; every frame re-creates pens, brushes and text layouts (isolated smoke: 5.58 % idle CPU). | [C] SN9 |
| 12 | LOW (Claude's look code) | Çalışma günlüğü | Sessions without a project show an empty first line; the detail pane is blank until a session is chosen. | [C] WL2 |
| 13 | HIGH (token economy, fixed by Claude) | agent documents | Finished work filled the documents: STATUS 203 k characters (172 k of old Log), ROADMAP 445 k (31 k open cards), PROPOSALS 105 k (CONTINUE PROTOCOL step 3 told the agent to read every decided proposal each session), DESIGN-WORKLOG 118 k (68 k finished N cards), `docs/archive/` 511 k; `HANDOFF.md` still named UI6, N8, UI7, UI8 as the next cards. Cleaned in this audit (user: "Dosyalardan sil, git'te kalsın"). | — |
| 14 | GOVERNANCE (fixed by Claude) | AGENTS.md, CODEX-PROMPT | Visual authority named only DESIGN-CONTROL-CENTER.md (Gözlemevi, superseded); conditions for long-finished cards (A1, A2, A3, T3, N10, R3) were still in the rules. | — |

User decisions in this audit: `talvora_delete` stays as it is (no guard, "Dokunma"); ChatGPT counts as an agent; Talvora's own test calls are never counted; the 2.5 GB `C:\ProgramData\Talvora\Diagnostics` folder (10 Oct driver/update leftovers) was deleted; items that only waited for the user's own check (E1, E2, S5, TI1, UI2, U2–U11) are closed; finished work leaves the agent documents at every audit (git keeps it); local `artifacts/` entries older than 7 days are pruned at every audit.

Checked and sound (no card): archive extraction (every entry resolved with `GetFullPath`, prefix-checked, reparse points refused); storage maintenance (only `Talvora…` prefixes); old version folders (only the current one kept); installer log (no warnings); tool failure guides (examples validated against the live schema by smoke H1); user-run guide text; culture-sensitive formatting (only integers and display dates); `AgentJobRegistry` keeps one timer-less `CancellationTokenSource` per remembered job (at most 120): no leak worth a card.

Notes: the Windows event log gets about 50 "Unknown tool … threw an unhandled exception" warnings per install from the deliberate surface-policy smoke probes (expected); two `resources/list` requests got "no handler" (a client probing resources; harmless); `ControlCenterWindow*.cs` hold `partial class ShellWindow` (misleading names, no defect).

### D30 — The agent brief always carries the current card

**Why:** audit 40 finding 1 (CRITICAL). `talvora_agent_brief` caps the brief at 60,000 characters and trims the end of the context. The context printed STATUS first, so the card, the newest Log entry and the open proposals were cut while the brief still returned. Claude's cleanup fixes today's brief; this card makes the order and the trimming safe for good. It also lets a UI or N card live in ROADMAP: audit 40 removed the finished cards from the design specs, and new UI/N cards are written in ROADMAP (P-48 showed the old routing failing).

**Files:** `scripts/Get-CodexContext.ps1`, `src/Talvora/Tools/AgentBriefTools.cs`, `tests/AgentContextOrderRegression.ps1` (new), `tests/QualityGateRegression.ps1`, `.context7/verification.json`.

**Context7:** the script change needs no lookup; for `AgentBriefTools.cs` record the map's .NET entry with the one fact used (C# range on `string`, `[..^n]`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `tests/AgentContextOrderRegression.ps1` with exactly this content, run it, see it fail:

```powershell
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# D30: the card comes first, STATUS (open lines only) comes last, and a UI/N card that its
# design spec does not hold (or whose spec file is missing) is read from ROADMAP.
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('talvora-d30-' + [guid]::NewGuid().ToString('N'))
try {
    $scripts = Join-Path $fixture 'scripts'
    $docs = Join-Path $fixture 'docs/codex'
    New-Item -ItemType Directory -Path $scripts, $docs -Force | Out-Null
    $helper = Join-Path $scripts 'Get-CodexContext.ps1'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/Get-CodexContext.ps1') -Destination $helper
    Set-Content -LiteralPath (Join-Path $docs 'STATUS.md') -Encoding utf8 -Value @(
        '## In progress', '(none)',
        '## Agent queue',
        '- [x] D7 — FINISHED_LINE_MUST_NOT_APPEAR',
        '- [-] D8 — DROPPED_LINE_MUST_NOT_APPEAR',
        '- [ ] D9 — Open card',
        '## User queue', '(none)',
        '## Log',
        '- 2026-11-07 — NEWEST_LOG_SENTINEL')
    Set-Content -LiteralPath (Join-Path $docs 'ROADMAP.md') -Encoding utf8 -Value @(
        '### D9 — Open card', 'D9_CARD_SENTINEL',
        '### UI9 — UI card written in ROADMAP', 'UI9_CARD_SENTINEL',
        '### N9 — Worklog card written in ROADMAP', 'N9_CARD_SENTINEL')
    Set-Content -LiteralPath (Join-Path $docs 'DESIGN-WORKLOG.md') -Encoding utf8 -Value @(
        '## 3. Worklog', 'WORKLOG_SPEC_SENTINEL')

    $context = @(& $helper | Out-String) -join ''
    $first = @($context -split '\r?\n' | Where-Object { $_.Trim() -ne '' })[0]
    if ($first -ne '=== CARD D9 (docs/codex/ROADMAP.md) ===') { throw "D30: the card must come first, got: $first" }
    $statusAt = $context.IndexOf('=== STATUS', [StringComparison]::Ordinal)
    if ($statusAt -lt 0) { throw 'D30: STATUS must still be printed.' }
    foreach ($section in @('=== NEWEST LOG', '=== PENDING QUEUE ADDITIONS', '=== OPEN PROPOSALS', '=== FILE LINE COUNTS')) {
        $at = $context.IndexOf($section, [StringComparison]::Ordinal)
        if ($at -lt 0 -or $at -gt $statusAt) { throw "D30: $section must come before STATUS." }
    }
    foreach ($forbidden in @('FINISHED_LINE_MUST_NOT_APPEAR', 'DROPPED_LINE_MUST_NOT_APPEAR')) {
        if ($context.Contains($forbidden)) { throw "D30: finished queue lines must not reach the brief ($forbidden)." }
    }
    if (-not $context.Contains('- [ ] D9 — Open card') -or -not $context.Contains('NEWEST_LOG_SENTINEL')) {
        throw 'D30: open queue lines and the newest Log entry must stay.'
    }
    # The fixture has no DESIGN-CONTROL-CENTER.md, and its DESIGN-WORKLOG.md has no N9 card.
    foreach ($card in @('UI9', 'N9')) {
        $out = @(& $helper -Card $card | Out-String) -join ''
        if ($out -notmatch "(?m)^=== CARD $card \(docs/codex/ROADMAP\.md\) ===" -or -not $out.Contains("${card}_CARD_SENTINEL")) {
            throw "D30: $card must be read from ROADMAP when its design spec does not hold it."
        }
    }
    Write-Output 'AGENT_CONTEXT_ORDER_GREEN'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
```

Step 2. In `scripts/Get-CodexContext.ps1`:

(a) Replace these four lines

```powershell
$roadmap = [IO.File]::ReadAllText((Join-Path $repoRoot $source))
$cardPattern = '(?ms)^#{2,3}[ \t]+' + [regex]::Escape($Card) + '[ \t]+[—–-].*?(?=^#{2,3}[ \t]+|\z)'
$cardMatch = [regex]::Match($roadmap, $cardPattern)
if (-not $cardMatch.Success) { throw "Card $Card could not be found in $source." }
```

with

```powershell
$cardPattern = '(?ms)^#{2,3}[ \t]+' + [regex]::Escape($Card) + '[ \t]+[—–-].*?(?=^#{2,3}[ \t]+|\z)'
$sourcePath = Join-Path $repoRoot $source
$roadmap = if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { [IO.File]::ReadAllText($sourcePath) } else { '' }
# D30: cited spec sections still come from the design spec when the card itself lives in ROADMAP.
$specSource = $source
$specText = $roadmap
$cardMatch = [regex]::Match($roadmap, $cardPattern)
if (-not $cardMatch.Success -and $source -ne 'docs/codex/ROADMAP.md') {
    # D30: finished UI/N cards were removed from the design specs; new UI/N cards are written in ROADMAP.
    $source = 'docs/codex/ROADMAP.md'
    $roadmap = [IO.File]::ReadAllText((Join-Path $repoRoot $source))
    $cardMatch = [regex]::Match($roadmap, $cardPattern)
}
if (-not $cardMatch.Success) { throw "Card $Card could not be found in $source." }
```

(b) In the referenced-spec block: `$text = Get-SpecSection -Document $roadmap -Number $number` becomes `$text = Get-SpecSection -Document $specText -Number $number`; in the two lines that print `($source)` and `in $source §` inside that block, use `$specSource` instead of `$source`.

(c) Order. Replace these six lines

```powershell
Add-ContextText '=== STATUS (without historical Log) ==='
Add-ContextText $statusHeader
Add-ContextText '=== NEWEST LOG ==='
if ($recent.Success) { Add-ContextText $recent.Value.TrimEnd() } else { Add-ContextText '(none)' }
Add-ContextText "=== CARD $Card ($source) ==="
Add-ContextText $cardText
```

with

```powershell
Add-ContextText "=== CARD $Card ($source) ==="
Add-ContextText $cardText
Add-ContextText '=== NEWEST LOG ==='
if ($recent.Success) { Add-ContextText $recent.Value.TrimEnd() } else { Add-ContextText '(none)' }
```

and insert directly above the last line of the script (`$script:ContextLines`):

```powershell
# D30: STATUS goes last and without finished queue lines, so a size cap trims STATUS, never the card.
Add-ContextText '=== STATUS (open items; finished work is in git history) ==='
Add-ContextText (($statusHeader -split '\r?\n' | Where-Object { $_ -notmatch '^- \[(x|-)\] ' }) -join "`n")
```

Step 3. In `src/Talvora/Tools/AgentBriefTools.cs` add below `MaxTotalCharacters`:

```csharp
    private const string TrimMarker =
        "\n[brief trimmed here: the rest of STATUS is in docs/codex/STATUS.md]";
```

and replace the first trimming block (the one with `fromContext`)

```csharp
        if (excess > 0)
        {
            var fromContext = Math.Min(excess, context.Length);
            context = context[..^fromContext];
            excess -= fromContext;
        }
```

with

```csharp
        if (excess > 0 && context.Length > 0)
        {
            // D30: STATUS is the last context section, so trimming cuts STATUS, never the card; say so.
            var fromContext = Math.Min(excess + TrimMarker.Length, context.Length);
            context = context[..^fromContext] + TrimMarker;
            excess = Math.Max(0, excess + TrimMarker.Length - fromContext);
        }
```

The second block (trimming `sections`) stays as it is.

Step 4. In `tests/QualityGateRegression.ps1` delete the live check: the line `$contextOutput = @(& $contextPath -Card K1 | Out-String) -join ''` and the `if` block right after it (the one that throws 'The context helper must print the selected card without neighbouring cards.'). Finished cards such as K1 leave ROADMAP at every audit, and the fixture checks further down in the same test already cover card extraction. In the same file, the live 250-line budget loop iterates over a fixed list of finished cards (`foreach ($card in @('UI1','UI2', … 'N8')) {`); replace that `foreach` line (both of its lines) with these lines, so the budget is measured on the open cards of the live queue:

```powershell
    # D30: measure the open cards of the live queue; finished cards leave the documents at every audit.
    $statusText = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs/codex/STATUS.md'))
    $queueText = [regex]::Match($statusText, '(?ms)^## Agent queue[^\r\n]*\r?\n(.*?)(?=^## )').Groups[1].Value
    $liveCards = @([regex]::Matches($queueText, '(?m)^- \[[ ~?]\] (?<id>[0-9A-Za-z.\-]+) [—–-]') |
        ForEach-Object { $_.Groups['id'].Value })
    foreach ($card in $liveCards) {
```

Nothing else in that file changes.

Step 5. Refresh `.context7/verification.json` (only this change).

**Don't:** change `MaxTotalCharacters`, the AGENTS sections, the queue selection, STATUS.md, or anything in `tests/QualityGateRegression.ps1` beyond Step 4.

**Acceptance:** `AGENT_CONTEXT_ORDER_GREEN`; `tests/QualityGateRegression.ps1` green; signed-in Full `Hepsi geçti.`; build and deploy; live: `talvora_agent_brief` for this repository returns `truncated: false` and its context starts with `=== CARD`.

### D31 — The parallel-gate timing regression runs on its own (decision P-49)

**Why:** audit 40 finding 2; P-49 option 2. `tests/QualityGateParallelRegression.ps1` checks that 3+1+3+1 s of work overlaps within 7 s of wall time; in the Full gate it runs beside the heaviest suites and failed at 7.4 s and 8.9 s, while it passes alone.

**Files:** `scripts/Invoke-QualityGate.ps1`, `.context7/verification.json`.

**Context7:** record the map's PowerShell 7 entry with the one fact used (a literal array of file names), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

In `$serialRegressions` add `'QualityGateParallelRegression.ps1'` as the last entry; the three existing names and their order stay. Nothing else changes.

**Don't:** change the 7-second threshold, the test or the parallel phase.

**Acceptance:** two consecutive signed-in Full gates `Hepsi geçti.`. Then D29 is next: implement its card unchanged.

### D32 — The traffic meter never counts Talvora's own clients

**Why:** audit 40 finding 3; user decision 2026-11-07 ("Talvora's own test calls are never counted on the Santral counters or lights"). After every install the smoke's deliberate unknown-tool probes showed as "Admin 1 çağrı · 1 hata".

**Files:** `src/Talvora/TalvoraLineTrafficMeter.cs`, `tests/Talvora.Notification.Regression/Program.SN1.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`string.StartsWith(string, StringComparison)`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). In `Program.SN1.cs`, inside `AssertSn1LineTraffic`, insert directly above `Console.WriteLine("SN1_LINE_TRAFFIC_GREEN");`:

```csharp
        // D32: Talvora's own clients (Talvora.Smoke, Talvora.Tray) are not traffic.
        var own = new TalvoraLineTrafficMeter(clock);
        own.Record("/mcp/admin", "Talvora.Smoke", ok: false);
        own.Record("/mcp", "Talvora.Tray", ok: true);
        own.Record("/mcp/dev", "openai-mcp", ok: true);
        var ownSnapshot = own.Snapshot(after: 0);
        if (ownSnapshot.NextSeq != 1 || ownSnapshot.Lines.Single(line => line.Line == "admin").Calls != 0)
            throw new InvalidOperationException("D32: Talvora's own clients must never be counted.");
```

Step 2. In `TalvoraLineTrafficMeter.Record`, insert as the first statement:

```csharp
        // D32: Talvora's own clients are not traffic (user decision 2026-11-07).
        if (clientName?.StartsWith("Talvora.", StringComparison.OrdinalIgnoreCase) == true) return;
```

**Don't:** change `ResolveClient`, the endpoint, the JSON shape or the ring size.

**Acceptance:** Notification regression green; signed-in Full `Hepsi geçti.`; build and deploy; live: after the post-install smoke, `/healthz/traffic` shows no admin call from the smoke.

### D33 — Desktop progress frames survive a busy machine

**Why:** audit 40 finding 4 (27 timed-out and 4 truncated frames in four days, all during heavy work).

**Files:** `src/Talvora.Tray/DesktopProgressPipeListener.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `tests/Talvora.Notification.Regression/Program.D33.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "Process.StartTime" (the value is fixed for the life of a process) and "ILogger.Log with a LogLevel variable"; record both queries.

#### Exact implementation

Step 1 (red first). Create `Program.D33.cs`:

```csharp
using Talvora;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D33: frames get enough time on a busy machine; the tray process is verified once.</summary>
    private static void AssertD33DeliveryBudgets()
    {
        if (DesktopProgressPipeListener.PipeReadTimeout != TimeSpan.FromSeconds(10) ||
            TalvoraDesktopProgressNotifier.PipeWriteTimeout != TimeSpan.FromSeconds(8))
            throw new InvalidOperationException("D33: pipe budgets must be 10 s (tray read) and 8 s (service write).");
        var started = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
        TrayPipeVerificationCache.Store(4242, started, trusted: true);
        if (!TrayPipeVerificationCache.TryGet(4242, started, out var trusted) || !trusted)
            throw new InvalidOperationException("D33: the same tray process must reuse its verdict.");
        if (TrayPipeVerificationCache.TryGet(4242, started.AddSeconds(1), out _))
            throw new InvalidOperationException("D33: a reused process id with another start time must be verified again.");
        Console.WriteLine("D33_DELIVERY_BUDGETS_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD33DeliveryBudgets();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 2. Timeouts: in the `PipeReadTimeout` initializer of `DesktopProgressPipeListener` change `TimeSpan.FromSeconds(3)` to `TimeSpan.FromSeconds(10)`; in the `PipeWriteTimeout` initializer of `TalvoraDesktopProgressNotifier` change `TimeSpan.FromSeconds(2)` to `TimeSpan.FromSeconds(8)` (`PipeConnectTimeout` stays 2 s).

Step 3. Add at the end of `TalvoraDesktopProgressNotifier.cs` (namespace `Talvora`):

```csharp
// D33: the tray's image path is checked once per tray process, not for every frame.
internal static class TrayPipeVerificationCache
{
    private static readonly object Gate = new();
    private static (int ProcessId, DateTime StartTimeUtc, bool Trusted)? _last;

    internal static bool TryGet(int processId, DateTime startTimeUtc, out bool trusted)
    {
        lock (Gate)
        {
            if (_last is { } entry && entry.ProcessId == processId && entry.StartTimeUtc == startTimeUtc)
            {
                trusted = entry.Trusted;
                return true;
            }

            trusted = false;
            return false;
        }
    }

    internal static void Store(int processId, DateTime startTimeUtc, bool trusted)
    {
        lock (Gate)
        {
            _last = (processId, startTimeUtc, trusted);
        }
    }
}
```

In `IsTrustedTrayPipeServer`, directly after the block `if (process.SessionId != expectedSessionId) { return false; }`, insert:

```csharp
            // D33: MainModule is slow and cannot be cancelled; check each tray process once.
            var startedUtc = process.StartTime.ToUniversalTime();
            if (TrayPipeVerificationCache.TryGet(process.Id, startedUtc, out var known))
            {
                return known;
            }
```

and turn the method's final `return actualPath.StartsWith(…) && string.Equals(…);` statement into `var trusted = <the same expression>;` followed by `TrayPipeVerificationCache.Store(process.Id, startedUtc, trusted);` and `return trusted;`.

Step 4. At the end of the delivery method (the one with `for (var attempt = 1; attempt <= 3; attempt++)`), replace the final `if (lastError is null) { _logger.LogDebug(…stayed unavailable…) } else { _logger.LogDebug(lastError, …failed after retrying…) }` block with:

```csharp
        // D33: a lost frame is visible in the service log, at most once per 10 minutes.
        var now = DateTimeOffset.UtcNow;
        var level = now - _lastDropWarningUtc >= TimeSpan.FromMinutes(10) ? LogLevel.Warning : LogLevel.Debug;
        if (level == LogLevel.Warning)
        {
            _lastDropWarningUtc = now;
        }

        if (lastError is null)
        {
            _logger.Log(
                level,
                "Desktop progress tray IPC stayed unavailable after retrying (operation {OperationId}).",
                message.OperationId);
        }
        else
        {
            _logger.Log(
                level,
                lastError,
                "Desktop progress notification failed after retrying without affecting the tool operation (operation {OperationId}).",
                message.OperationId);
        }
```

and add the field `private static DateTimeOffset _lastDropWarningUtc = DateTimeOffset.MinValue;` next to the other fields of the class.

**Don't:** change the protocol, the frame size limit, the single-instance pipe, the connect timeout or the retry count.

**Acceptance:** Notification regression green (`D33_DELIVERY_BUDGETS_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy; live: one tool call shows its desktop card. Claude counts the "IPC client timed out" lines in tray.log at the next audit (do not wait for it).

### D34 — Tunnel health without launching tunnel-client every round

**Why:** audit 40 finding 5 (240–290 process launches an hour) and RV1 (status up to 9.9 s against a 12 s limit). Safety rule of this card: a long-lived "ready" status is allowed only while the tunnel's own health answer says live, ready and not critically degraded; every other answer or failure drops it at once, so repairs are never delayed.

**Files:** `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs`, `src/Talvora.Tray/ManagedMcpTunnelHealthService.cs`, `tests/Talvora.Notification.Regression/Program.D34.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "Uri.IsDefaultPort and Uri.GetLeftPart(UriPartial.Authority)"; record the query.

#### Exact implementation

Step 1 (red first). Create `Program.D34.cs`:

```csharp
using System.Text.Json;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D34: the published health address is read from its file; a fully ready status is kept 10 minutes.</summary>
    private static void AssertD34TunnelHealthFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-d34-" + Guid.NewGuid().ToString("N"));
        var health = Directory.CreateDirectory(Path.Combine(root, "health")).FullName;
        try
        {
            var file = Path.Combine(health, "alias-x.url");
            File.WriteAllText(file, "http://127.0.0.1:60409");
            if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "alias-x") != "http://127.0.0.1:60409/health?details=true")
                throw new InvalidOperationException("D34: the published health address must be read.");
            foreach (var bad in new[] { "http://example.com:1", "https://127.0.0.1:60409", "http://127.0.0.1", "http://127.0.0.1:60409/other", new string('1', 300) })
            {
                File.WriteAllText(file, bad);
                if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "alias-x") is not null)
                    throw new InvalidOperationException("D34: an unsafe health address was accepted.");
            }
            if (ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(root, "missing") is not null)
                throw new InvalidOperationException("D34: a missing file must give no address.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var ready = new ManagedMcpTunnelRuntimeStatus(Ready: true, ProcessRunning: true, Healthy: true, IdentityMatches: true, Detail: "ok");
        if (ManagedMcpTunnelProvisioningService.RuntimeStatusCacheDurationFor(ready) != TimeSpan.FromMinutes(10) ||
            ManagedMcpTunnelProvisioningService.RuntimeStatusCacheDurationFor(ready with { IdentityMatches = false }) != TimeSpan.FromSeconds(15))
            throw new InvalidOperationException("D34: only a fully ready status may be kept for 10 minutes.");

        using var notReady = JsonDocument.Parse("""{"schema_version":1,"live":true,"ready":false}""");
        using var fine = JsonDocument.Parse("""{"schema_version":1,"live":true,"ready":true}""");
        if (!ManagedMcpTunnelHealthService.ShouldRenewRuntimeStatus(ManagedMcpTunnelHealthService.ParseSnapshot(notReady.RootElement)) ||
            ManagedMcpTunnelHealthService.ShouldRenewRuntimeStatus(ManagedMcpTunnelHealthService.ParseSnapshot(fine.RootElement)))
            throw new InvalidOperationException("D34: a not-ready health answer must drop the cached status, a ready one must not.");
        Console.WriteLine("D34_TUNNEL_HEALTH_FILE_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD34TunnelHealthFile();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 2. `ManagedMcpTunnelProvisioningService.cs`:

(a) In the `RuntimeStatusCacheDuration` initializer change `TimeSpan.FromSeconds(2)` to `TimeSpan.FromSeconds(15)`.

(b) In `GetRuntimeStatusCachedAsync` replace `now.Add(RuntimeStatusCacheDuration),` with `now.Add(RuntimeStatusCacheDurationFor(status)),` and add below that method:

```csharp
    // D34: a fully ready runtime is asked again only every 10 minutes; the health probe
    // (ManagedMcpTunnelHealthService.GetSnapshotAsync) drops this cache the moment the tunnel
    // stops answering or reports anything but live, ready and undegraded.
    internal static TimeSpan RuntimeStatusCacheDurationFor(ManagedMcpTunnelRuntimeStatus status) =>
        status.Ready && status.ProcessRunning && status.Healthy && status.IdentityMatches
            ? TimeSpan.FromMinutes(10)
            : RuntimeStatusCacheDuration;

    // D34: tunnel-client publishes its local health address in <StateRoot>\health\<alias>.url
    // (its status JSON names the file "health_url_file"); reading it needs no tunnel-client process.
    internal static string? TryReadHealthDetailsUrl(ManagedMcpRegistration registration)
    {
        if (registration.Tunnel is not { Required: true } ||
            !IsTunnelId(registration.Tunnel.TunnelId))
        {
            return null;
        }

        try
        {
            var configPath = ResolveConfigPath(registration);
            if (!File.Exists(configPath))
            {
                return null;
            }

            var config = LoadBusinessConfig(configPath);
            return TryReadHealthDetailsUrl(config.StateRoot, config.Alias);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string? TryReadHealthDetailsUrl(string stateRoot, string alias)
    {
        var path = Path.Combine(stateRoot, "health", alias + ".url");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 256)
        {
            return null;
        }

        var text = File.ReadAllText(path).Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.Host is not ("127.0.0.1" or "localhost") ||
            uri.IsDefaultPort ||
            uri.AbsolutePath != "/" ||
            uri.Query.Length > 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority) + "/health?details=true";
    }
```

(c) In `GetRuntimeStatusAsync` change `timeout: TimeSpan.FromSeconds(12),` to `timeout: TimeSpan.FromSeconds(20),`.

Step 3. `ManagedMcpTunnelHealthService.cs`:

(a) Move the lines of `GetSnapshotAsync` from `using var response = await Http.GetAsync(` down to `return ParseSnapshot(document.RootElement);` (inclusive), unchanged, into a new method placed directly below `GetSnapshotAsync`:

```csharp
    // D34: the request and parsing lines moved here unchanged from GetSnapshotAsync.
    private static async Task<ManagedMcpTunnelHealthSnapshot?> ReadSnapshotAsync(
        Uri healthUri,
        CancellationToken cancellationToken)
    {
        // (the moved lines)
    }

    internal static bool ShouldRenewRuntimeStatus(ManagedMcpTunnelHealthSnapshot snapshot) =>
        !snapshot.Live || !snapshot.Ready || snapshot.HasCriticalDegradation;
```

(b) The rest of `GetSnapshotAsync`, from `var runtimeStatus =` to its end, becomes:

```csharp
        // D34: the health address tunnel-client publishes comes first; tunnel-client runs only when the file gives none.
        var healthDetailsUrl = ManagedMcpTunnelProvisioningService.TryReadHealthDetailsUrl(registration);
        if (healthDetailsUrl is null)
        {
            var runtimeStatus =
                await ManagedMcpTunnelProvisioningService.GetRuntimeStatusCachedAsync(
                    registration,
                    cancellationToken);
            healthDetailsUrl = runtimeStatus.HealthDetailsUrl;
        }

        if (string.IsNullOrWhiteSpace(healthDetailsUrl) ||
            !TryValidateLoopbackHealthUri(
                healthDetailsUrl,
                out var healthUri))
        {
            return null;
        }

        try
        {
            var snapshot = await ReadSnapshotAsync(healthUri, cancellationToken);
            // D34: anything but a live, ready, undegraded answer drops the long-lived ready status.
            if (snapshot is null || ShouldRenewRuntimeStatus(snapshot))
            {
                ManagedMcpTunnelProvisioningService.InvalidateRuntimeStatusCache(registration.Id);
            }

            return snapshot;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ManagedMcpTunnelProvisioningService.InvalidateRuntimeStatusCache(registration.Id);
            throw;
        }
```

**Don't:** change the 15 s maintenance interval, the repair logic, `WaitForReadyAsync`, tunnel provisioning or the RV1 timing log (it stays on every call; it is how the result is measured).

**Acceptance:** Notification regression green (`D34_TUNNEL_HEALTH_FILE_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy; after one hour of normal running, tray.log has at most 30 `tunnel-client runtimes status` lines for that hour (before: about 240), and the Control Center shows the tunnels ready. Paste both numbers in the Log entry.

### D35 — The GitHub release lookup waits after a failure

**Why:** audit 40 finding 6.

**Files:** `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs`, `tests/Talvora.Notification.Regression/Program.D35.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "HttpMessageHandler SendAsync test double for HttpClient"; record the query.

#### Exact implementation

Step 1. Test seams: change `private static readonly HttpClient TunnelClientReleaseHttp = TalvoraHttp.CreateClient(` to `internal static HttpClient TunnelClientReleaseHttp { get; set; } = TalvoraHttp.CreateClient(` (its arguments and closing `);` stay), and `private static async Task<string> GetLatestTunnelClientTagAsync(` to `internal static async Task<string?> GetLatestTunnelClientTagAsync(`.

Step 2 (red first). Create `Program.D35.cs`:

```csharp
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D35: a failed GitHub release lookup waits 30 minutes instead of retrying every round.</summary>
    private static async Task AssertD35LatestBackoffAsync()
    {
        var handler = new D35FailingHandler();
        var original = ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp;
        ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = new HttpClient(handler);
        try
        {
            var first = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            var second = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            if (first is not null || second is not null || handler.Requests != 1)
                throw new InvalidOperationException($"D35: expected one request and no tag, got {handler.Requests} requests.");
        }
        finally
        {
            ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = original;
        }
        Console.WriteLine("D35_LATEST_BACKOFF_GREEN");
    }

    private sealed class D35FailingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            throw new HttpRequestException("D35 offline");
        }
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD35LatestBackoffAsync();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 3. Add `private static DateTimeOffset _tunnelClientLatestFailedAtUtc;` directly below `_cachedTunnelClientLatestTag`.

Step 4. In `GetLatestTunnelClientTagAsync`, directly after each of the two 6-hour cache checks (the one before `await TunnelClientReleaseGate` and the one inside the `try` after `now = DateTimeOffset.UtcNow;`), insert:

```csharp
        // D35: after a failed lookup, ask GitHub again only after 30 minutes.
        if (now - _tunnelClientLatestFailedAtUtc < TimeSpan.FromMinutes(30))
        {
            return _cachedTunnelClientLatestTag;
        }
```

Step 5. Wrap the lines from `using var request = new HttpRequestMessage(` to `return tag;` (inclusive) in:

```csharp
            try
            {
                // (the existing lines, unchanged)
            }
            catch (Exception ex) when (
                ex is HttpRequestException or InvalidDataException or JsonException ||
                (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // D35: one log line per failure window; the running client stays in use.
                _tunnelClientLatestFailedAtUtc = now;
                TrayLog.Write("tunnel-client latest release check failed; next try in 30 minutes", ex);
                return _cachedTunnelClientLatestTag;
            }
```

Step 6. In `StageLatestTunnelClientAsync`, directly after the `var latestTag = await GetLatestTunnelClientTagAsync(…).ConfigureAwait(false);` statement, insert:

```csharp
            // D35: no release known (GitHub unreachable): keep the running client.
            if (latestTag is null)
            {
                return config;
            }
```

**Don't:** change the 6-hour cache, the download path or the update journal.

**Acceptance:** Notification regression green (`D35_LATEST_BACKOFF_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy.

### D36 — Atomic publication also retries a busy destination

**Why:** audit 40 finding 7.

**Files:** `src/Talvora.Shared/AtomicFile.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "File.Replace UnauthorizedAccessException when the destination is in use or pending delete"; record the query.

#### Exact implementation

In `PublishDurable`, replace

```csharp
            catch (IOException) when (attempt < PublishAttempts && File.Exists(sourcePath))
```

with

```csharp
            // D36: a concurrent File.Replace or a delete-pending destination reports access denied, not IOException.
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                attempt < PublishAttempts && File.Exists(sourcePath))
```

**Don't:** change `PublishAttempts`, the delay or the replace/move order.

**Acceptance:** signed-in Full `Hepsi geçti.` (it runs the AtomicRace scenario); build and deploy.

### D37 — Event slips in plain Turkish

**Why:** audit 40 finding 8 (copy rule: no English, no "MCP", "tunnel-client", "runtime", "latest", "health" on main surfaces).

**Files:** `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.ClientUpdate.cs`, `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs`, `src/Talvora.Tray/TrayApplicationContext.cs`, `src/Talvora.Tray/ControlCenterSetupService.cs`, `tests/EventTextTurkishRegression.ps1` (new).

**Context7:** none needed (strings only); record the map's .NET entry with one relevant fact, per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Replace each old text with the new one, character for character (interpolation holes stay as they are). Each old text occurs exactly once in the four files, except `Gitea MCP zinciri sağlıklı ve kullanıma hazır.`, which occurs twice in `TrayApplicationContext.cs`: replace both. TrayLog lines, event categories and dedup keys stay unchanged.

| Old | New |
|---|---|
| `{registration.DisplayName} tunnel-client güncellendi` | `{registration.DisplayName} bağlantı istemcisi güncellendi` |
| `OpenAI tunnel-client {candidateConfig.TunnelClientVersion} sürümüne readiness doğrulamasından sonra atomik olarak geçirildi.` | `Güvenli bağlantı istemcisi {candidateConfig.TunnelClientVersion} sürümüne geçti; hazır olduğu doğrulandıktan sonra devreye alındı.` |
| `{registration.DisplayName} tunnel-client update recovered` | `{registration.DisplayName} yarım kalan güncelleme tamamlandı` |
| `Interrupted update recovered with {journal.CandidateConfig.TunnelClientVersion}; runtime readiness was verified before commit.` | `Yarıda kesilen güncelleme {journal.CandidateConfig.TunnelClientVersion} sürümüyle tamamlandı; sürüm hazır olduğu doğrulandıktan sonra devreye alındı.` |
| `{registration.DisplayName} tunnel-client update geri alındı` | `{registration.DisplayName} güncellemesi geri alındı` |
| `Interrupted candidate {journal.CandidateConfig.TunnelClientVersion} readiness doğrulamasını geçemedi; previous client {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.` | `Yeni sürüm {journal.CandidateConfig.TunnelClientVersion} hazırlık denetimini geçemedi; önceki sürüm {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.` |
| `{registration.DisplayName} tunnel-client update toparlandı` | `{registration.DisplayName} yarım güncelleme toparlandı` |
| `Incomplete update journal was rolled back to {journal.PreviousConfig.TunnelClientVersion}.` | `Yarım kalan güncelleme geri alındı; {journal.PreviousConfig.TunnelClientVersion} sürümü kullanılıyor.` |
| `Runtime profili oluşturuldu ve health/ready doğrulaması geçti.` | `Bağlantı profili oluşturuldu ve sağlık denetimi geçti.` |
| `Talvora MCP otomatik olarak düzeltildi` | `Talvora otomatik olarak düzeltildi` |
| `Yerel servis ile Dev ve Admin güvenli MCP tünelleri yeniden hazır.` | `Yerel servis ile Dev ve Admin güvenli bağlantıları yeniden hazır.` |
| `Talvora Dev ve Admin güvenli MCP tünelleri yeniden bağlandı.` | `Talvora Dev ve Admin güvenli bağlantıları yeniden kuruldu.` |
| `Gitea MCP otomatik kurtarma denemesi başarısız` | `Gitea otomatik kurtarma denemesi başarısız` |
| `Gitea MCP sağlık bakımı başarısız` | `Gitea sağlık bakımı yarıda kaldı` |
| `Gitea MCP yeniden başlatıldı` | `Gitea yeniden başlatıldı` |
| `Gitea MCP zinciri sağlıklı ve kullanıma hazır.` | `Gitea ve bağlantı zinciri sağlıklı, kullanıma hazır.` |
| `Gitea MCP yeniden başlatılamadı` | `Gitea yeniden başlatılamadı` |
| `{registration.DisplayName} tunnel-client güncellemesi ertelendi` | `{registration.DisplayName} bağlantı istemcisi güncellemesi ertelendi` |
| `Mevcut çalışan sürüm korunuyor; latest kontrolü daha sonra yeniden denenecek.` | `Çalışan sürüm korunuyor; yeni sürüm denetimi daha sonra yeniden yapılacak.` |
| `{registry.Mcps.Count} yönetilen MCP kaydı yüklendi.` | `{registry.Mcps.Count} yönetilen hizmet kaydı yüklendi.` |
| `Yönetilen MCP kayıtları veya otomatik kurtarma hazırlanamadı.` | `Yönetilen hizmet kayıtları ya da otomatik kurtarma hazırlanamadı.` |
| `Mevcut güvenli MCP tünelleri bağlandı.` | `Mevcut güvenli bağlantılar kuruldu.` |
| `Eksik güvenli MCP tüneli yapılandırması hazırlandı.` | `Eksik güvenli bağlantı ayarları hazırlandı.` |

Red first: create `tests/EventTextTurkishRegression.ps1` (UTF-8). This source-text check is prescribed on purpose, an exception to the general rule against new regex-over-source assertions: the texts are built at many call sites and only their wording changes. It reads the four source files above into one string; holds two arrays, `$old` with every text of the left column and `$new` with every text of the right column, as single-quoted PowerShell strings; throws `"D37: old event text still present: <text>"` for any old text it finds and `"D37: new event text missing: <text>"` for any new text it does not find; and ends with `Write-Output 'EVENT_TEXT_TURKISH_GREEN'`.

**Acceptance:** `EVENT_TEXT_TURKISH_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy.

### D38 — "Yalnızca hatalar ve ajan işleri" counts ChatGPT as an agent

**Why:** audit 40 finding 9; user decision 2026-11-07 (ChatGPT is the main agent).

**Files:** `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs`, `tests/Talvora.Notification.Regression/Program.N8.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`string.StartsWith(string, StringComparison.Ordinal)`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). In `Program.N8.cs`:
- replace `presenter.Publish(Message("filtered", "ChatGPT", DesktopProgressKind.Completed, terminal: true));` with `presenter.Publish(Message("filtered", "Talvora", DesktopProgressKind.Completed, terminal: true));` (a plain Talvora call is the non-agent case now);
- directly below `claudeCard.Close();` insert:

```csharp
                // D38: ChatGPT is an agent too (user decision 2026-11-07).
                presenter.Publish(Message("chatgpt-dev", "ChatGPT · Dev", DesktopProgressKind.Started));
                if (Window("chatgpt-dev") is not { } chatgptCard)
                    throw new InvalidOperationException("D38: ChatGPT agent card filtered.");
                chatgptCard.Close();
```

Step 2. In `PublishCore`, replace `message.WorklogFrame?.Header.ActorName is not ("Codex" or "Claude")` with `!IsAgentActor(message.WorklogFrame?.Header.ActorName)` and add to the class:

```csharp
    // D38: agents are Codex, Claude and ChatGPT (ChatGPT · Dev, ChatGPT · Yönetim) — user decision 2026-11-07.
    private static bool IsAgentActor(string? actor) =>
        actor is "Codex" or "Claude" ||
        actor?.StartsWith("ChatGPT", StringComparison.Ordinal) == true;
```

**Don't:** change the setting's text, its default or the history behaviour.

**Acceptance:** Notification regression green; signed-in Full `Hepsi geçti.`; build and deploy.

### D39 — Penpot error text uses the shared secret masking

**Why:** audit 40 finding 10.

**Files:** `src/Talvora/Tools/PenpotMcpTools.cs`, `tests/Talvora.Notification.Regression/Program.D39.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** none needed beyond the map's .NET entry (one fact), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D39.cs`:

```csharp
using Talvora.Tools;

internal static partial class Program
{
    /// <summary>D39: Penpot error text goes through the shared secret masking too.</summary>
    private static void AssertD39PenpotRedaction()
    {
        var text = PenpotMcpTools.RedactSensitiveText(
            "Penpot failed: userToken=abc123 Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");
        if (text.Contains("abc123", StringComparison.Ordinal) ||
            text.Contains("c2lnbmF0dXJl", StringComparison.Ordinal))
            throw new InvalidOperationException("D39: Penpot error text still shows a secret.");
        Console.WriteLine("D39_PENPOT_REDACTION_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD39PenpotRedaction();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 2. In `PenpotMcpTools.cs` replace

```csharp
    private static string RedactSensitiveText(string value) =>
        UserTokenPattern.Replace(value, "$1<redacted>");
```

with

```csharp
    // D39: Penpot's own token mask plus the shared masking (keys, Authorization, JWT).
    internal static string RedactSensitiveText(string value) =>
        Talvora.Shared.FileLog.RedactSensitiveData(UserTokenPattern.Replace(value, "$1<redacted>"));
```

**Acceptance:** Notification regression green (`D39_PENPOT_REDACTION_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy.

### SN9 — Switchboard keeps focus and hover, draws calmly (Claude)

`[C]` card, Claude builds it (audit 40 finding 11): update the hotspots in place when the socket ids do not change (no `Children.Clear()` on every traffic poll), so keyboard focus and the hover ring survive; move the reconnect timer to `outside.Y + 70` / `+ 88` so it never meets a neighbour's third text line; cache pens, brushes and the static text layer per theme and redraw only lamps, moving lights and counters each frame. Acceptance: the smoke keeps focus on one socket across five polls; isolated overview idle CPU measured before and after (before 5.58 %), after lower; screenshots in both themes.

### WL2 — Çalışma günlüğü: no blank project line, a real empty state (Claude)

`[C]` card, Claude builds it (audit 40 finding 12): a session without a project shows "Proje belirsiz" in `Sn.InkFaint`; with nothing selected the detail pane shows "Soldan bir oturum seç; adımları, kanıtları ve sonucu burada görürsün." centred in `Sn.InkMuted`. Acceptance: smoke captures in both themes.

### D29 — Smoke clears its fixture traffic after the SN3 check

**Why:** audit 39 finding 4: the later live-data capture showed the fixture's 13/1/2 calls.

**Files:** `src/Talvora.Tray/ControlCenterSmoke.SN3.cs` only (this file is test code inside a `[C]` area; this card is allowed to touch it).

#### Exact implementation

At the end of `AssertSn3Switchboard`, before the final `board.Settle();`, add `overview.ApplyTraffic(null);` with the comment `// D29: fixture traffic must not leak into the live-data capture.`

**Acceptance:** Full `Hepsi geçti.`; the smoke's `*-loaded.png` overview shows "veri yok" or real counts, never 13/1/2.

## Kept for a regression test (finished card, never implement)

`tests/QualityGateRegression.ps1` still reads card K1 from this file; D30 removes that check (its fixture covers the same thing). K1 is finished and is not in the queue: never select or implement it. Claude deletes this section once D30 is done.

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
