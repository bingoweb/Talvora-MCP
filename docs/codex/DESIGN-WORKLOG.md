# Talvora desktop worklog — Review and redesign spec "Seyir Defteri"

> Author: Claude, 2026-10-18 (review of HEAD `8b1a611` + live render on the installed build). Implementer: Codex, cards `N1`–`N8` (bottom).
> Language: English spec; every visible string is ordinary Turkish (copy deck §6 is authoritative).
> AGENTS.md "Mandatory Windows desktop progress reporting" stays fully binding (one live card, nonmodal, full text, draggable/pin, dark terminal identity allowed, no fabricated telemetry, plain Turkish, evidence separated and redacted, best-effort delivery). This spec makes the card **informative**, removes duplication and inconsistency, and gives it a polished look. Where this spec and AGENTS.md disagree, AGENTS.md wins — report the conflict as an open question.

---

## 1. What the user actually sees today (live evidence)

Captured on 2026-10-18 13:51 from the installed build (`docs/codex/evidence/worklog-card-2026-10-18.png`) after one `talvora_search_text` call with wrong argument names:

```
(!) TALVORA // TRACE  (HATA)                                   [pin] [x]
İlgili kaynakları inceliyorum
İstenen işi doğru yapmak için ilgili dosya, metin ve eşleşmeleri kontrol ediyorum.

Bu teknik adım tamamlanmadı. Yalnız doğrulanmış sonuçlar tamamlandı olarak gösterilir.

Son yaptıklarım:
• Hata: İlgili kaynakları inceliyorum          ← clipped by the 125 px message box
search_text  •  1 sn  •  13:51:29
```

Every problem in this one card is a defect class:

| # | Defect | Where (code) |
|---|---|---|
| W1 | **Title tense contradicts state.** A failed/completed card keeps the present-progressive start title ("…inceliyorum" + HATA). The failure title "Bir hata buldum" passed by `RunToolCallAsync` is discarded because `EndWorklog` re-uses `pendingFailure.Subject`; successful cards also use `narrative.Subject` as title. | `TalvoraDesktopProgressNotifier.cs:164-196`, `TalvoraDesktopWorklogState.cs:201-312` |
| W2 | **No reason.** Failure text is a per-tool canned sentence; the real error never reaches the card. (The MCP client also only gets `An error occurred invoking 'talvora_search_text'.` — see Q6 in ROADMAP.) | `BuildFriendlyResultFailureMessage`, `BuildFriendlyExceptionMessage` |
| W3 | **History repeats the title.** On failure/cancel the ending step is added to "Son yaptıklarım" even when it is the only step, so the card says the same thing twice. | `EndWorklog` → `AddWorklogHistory` |
| W4 | **No real history.** A "worklog generation" ends whenever no tool call is in flight. Agents call tools sequentially, so every call starts a new generation, clears history and **creates a new window** (presenter keys windows by operation id) — the card flickers step by step and never tells the story of the session. "Son yaptıklarım" only appears for parallel calls. | `BeginWorklog` (`_activeWorklog.Count == 0` → reset), `DesktopProgressNotificationPresenter.PublishCore` |
| W5 | **Raw tool name in the plain area** (`search_text` in the meta line) — forbidden by AGENTS.md. | `DesktopProgressNotificationWindow.Visual.cs:564` |
| W6 | **Facts duplicated.** Files and +/− counts appear in the prose (`DescribeEvidenceForUser`) *and* in the diff header; elapsed time appears in heartbeat prose ("Geçen süre …") *and* in the meta line; `Sonuç: başarılı` repeats the badge. | `ComposeWorklogMessage`, `UpdateEvidence`, `ApplyStatusVisual` |
| W7 | **Fabricated "real" numbers on failure.** +/− counts are computed from the *requested* patch arguments; when the patch is rejected (stale revision, invalid), the card still states "Gerçek kod değişikliği: +12 / -3 satır". | `BuildPatchEvidence` + `DescribeEvidenceForUser` |
| W8 | **Wrong counts.** File list is capped at 16 but prose uses `Files.Count` ("16 dosyada çalışıyorum") while the summary says the true count; unified-diff input (`inputFormat: unified`) yields "0 dosya" because only `*** Update File:` markers are parsed; preview-limit text hard-codes "524.288" while the constant is 524 160. | `TalvoraDesktopProgressEvidence.cs` |
| W9 | **Exit codes and jargon in plain text:** "Sonuç: tamamlanamadı // kod 1", "Repository", "exit/sonuç", "diff", "MCP çağrısı", "paralel adım". | `DescribeStructuredResult`, failure copy, Codex worklog |
| W10 | **Turkish without diacritics** in Codex job cards ("Codex projeyi gelistiriyor", "MCP cagrisi", "Gecen sure"). | `TalvoraDesktopCodexWorklog.cs` |
| W11 | **Voice inconsistency:** "istediğin" (sen), "İsteğinizi" (siz), first person ("inceliyorum"), third person ("Claude … ilerliyor"). | narratives |
| W12 | **Tautological filler:** every start card adds "Amaç: …" sentences such as "İstenen davranışın gerçekten programda yer alması için." | `BeginWorklog` |
| W13 | **Stale flapping for Claude jobs:** tray marks active cards "BEKLİYOR — Yeni durum bekliyorum" after 15 s without data, but Claude job heartbeats are every 25 s → the card flips to "waiting" in every cycle. | `ActiveStaleThreshold = 15 s` vs `ClaudeBackgroundJobTools.Heartbeat` 25 s |
| W14 | **Missing context:** no project name, no actor (Codex / ChatGPT Dev / ChatGPT Admin / Claude), no step counter, no session duration, no "what changed in this session", no link to anything after the card disappears (no history anywhere). | — |
| W15 | **Weak results for the most valuable steps:** build shows no error/warning counts, tests show no pass/fail counts or failing test names, commit shows no hash, push shows no destination; Codex/Claude job completion shows only a canned sentence. | narratives/evidence |
| W16 | **Lifetime mismatch:** completed cards close after 2 min (AGENTS.md: about 3 min). | `CompletedLifetime` |
| W17 | **Readability:** meta/trace/badge text at 9–9.5 px in muted colours; message area capped at 125 px so even short texts scroll; English "TALVORA // TRACE". | `Visual.cs` |
| W18 | **Push detection** uses `arguments[0] == "push"` (misses `-C <dir> push`) — same bug class as S8. | `TalvoraDesktopProgressNarrative.cs:170` |
| W19 | **Noise:** a failed read/search (agent passed wrong arguments, will self-correct) produces a red error card. | `ShouldNotifyToolCall` / failure path |

### 1.1 The code terminal (the user's favourite feature) — why it sometimes shows nothing or the wrong thing

The vertical "terminal" expansion that shows the code of a change **must be kept and made better** (§3.1). Today it fails in these ways:

| # | Defect | Where (code) |
|---|---|---|
| C1 | **Disappears almost immediately.** Every sequential tool call starts a new worklog generation and a **new window**; the next step after a patch (usually a build/test seconds later) has no code evidence, so the code panel vanishes and the card shrinks. The user rarely gets to read it. Same when a parallel step without evidence becomes "latest": `Enqueue(..., latest.Evidence)` sends `null` and the panel collapses (`UpdateEvidence` → `SizeToContent.Height`). | `TalvoraDesktopWorklogState.cs` (generation reset, continuation frames), `DesktopProgressNotificationWindow.cs:380-397` |
| C2 | **Shows code that was never applied.** The preview is built from the *request arguments* before the edit runs. If the edit is rejected (stale revision, invalid patch, validation failure) the card still shows the code — under a red badge with "+12 −3". | `BuildInitialEvidence`, `BuildTerminalEvidence` (keeps `initial.CodePreview`) |
| C3 | **Nothing for many real code changes.** Only `apply_patch` and `apply_edits` produce a preview. `structural_edit` (pattern/rewrite) and `semantic_edit` (rename) arguments have no `changes[]`, so `BuildStructuredEditEvidence` returns `null`; `write_text`, `replace_text`, `append_text`, `write_bytes` (text), `json/yaml/toml/xml/ini/dotenv *_set/_delete`, `copy`/`move` into a workspace show nothing; Codex/Claude background jobs (which edit files themselves) show nothing. | `TalvoraDesktopProgressEvidence.cs:13-49` |
| C4 | **Wrong file headers / counts for unified diffs.** `inputFormat: "unified"` patches have no `*** Update File:` lines → no `@@ FILE` headers, "0 dosya"; a removed line whose content starts with `--` (SQL comment, YAML `---`) is treated as a `---` header and not counted; `*** Move to:` is ignored. | `BuildPatchEvidence` |
| C5 | **Size jumps.** First code card is forced to ≥ 460 DIP even for a 2-line change (`Math.Clamp(220 + lines*17, 460, 720)`), and only once per window (`_autoDiffHeightExpanded`); a saved user size replaces the auto expansion; code-free updates snap back to content height. | `ExpandForCodeHeight`, presenter `ApplyPreferredSize` branch |
| C6 | **Not really a terminal.** Plain `TextBox`, one colour, no line numbers, no per-file separation, wrapped lines indistinguishable from new lines; header text "KOD DEĞİŞİKLİKLERİ · HATA +12 −3" mixes state into the code title. | `BuildDiffPanel`, `UpdateEvidence` |
| C7 | **Heavy repeats.** Every 5 s heartbeat re-sends the whole preview (up to 512 KB) over the pipe. | `UpdateWorklogProgress` → `Enqueue(..., current.Evidence)` |

## 2. Concept — "Seyir Defteri" (the ship's logbook)

The card is a **logbook of the current session**, not a toast per tool call. One card per *session* (project + actor + continuous activity). At a glance it answers, in this order:

1. **Where and who?** — project chip + actor chip.
2. **What is happening right now?** — one honest, tense-correct sentence.
3. **On what exactly?** — a fact line with real objects (files, counts, target, destination).
4. **How did it end?** — outcome with the real reason and what happens next.
5. **What has happened in this session?** — compact timeline.
6. **Totals** — steps, duration, files changed, last test/build status.
7. **Proof** — "Teknik ayrıntılar" expander with diff preview, raw result line, tool name, exit code (redacted).

Visual identity: the existing dark terminal/neon direction, refined and aligned with the Control Center "Gece" tokens (`docs/codex/DESIGN-CONTROL-CENTER.md` §3.1): same turquoise accent, same state colours, same Aurora gradient used **only** for the 2 px live line at the top of an active card.

## 3. Card anatomy

```
╭──────────────────────────────────────────────────────────────╮
│▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁  ← 2 px live line (Aurora shimmer, active only) │
│  ◉  Classroom  ·  Codex                    ● Çalışıyor   📌  ✕ │  header 28 px
│                                                              │
│  Kod değişikliği uygulanıyor                                 │  title 15/20 semibold
│  3 dosya · +42 −7 · kiosk-routes.js, app.js ve 1 dosya       │  fact line 13/18, TextMuted
│                                                              │
│  BU OTURUMDA                                                  │  overline 11
│  ✓ 14:02  Testler geçti · 1 837 test                          │  timeline rows 12.5/18
│  ✓ 14:01  Kaynaklar incelendi · 12 dosya                      │
│  ✕ 13:59  Kod değişikliği uygulanamadı · dosya değişmişti     │
│     +9 adım daha                                             │
│ ──────────────────────────────────────────────────────────── │
│  14 adım · 6 dk 12 sn · 5 dosya değişti         Ayrıntılar ▸  │  footer 12/16 mono digits
╰──────────────────────────────────────────────────────────────╯
```

Terminal variants (same card, same position, title/badge change; timeline keeps the new top row):

```
✓ Tamamlandı   Codex görevi tamamlandı
               23 dk · 41 adım · 9 dosya değişti · testler geçti
✕ Hata         Derleme başarısız · 3 hata
               ControlCenterWindow.cs (2), Theme.xaml (1)
               Kodunuz değişmedi; ajan hatayı düzeltip yeniden deneyecek.
⏸ Durduruldu   Claude çalışması durduruldu · isteğinizle
⧗ Yanıt bekleniyor   Codex 1 dk 10 sn'dir yeni bilgi göndermedi (son: 14:21:03)
```

Expanded "Ayrıntılar" (technical evidence only, sunken mono panel, scrollable, copy button). Code is **not** repeated here; it lives in the code terminal (§3.1):

```
ADIM      Kod değişikliği (talvora_apply_patch)
SONUÇ     Uygulandı · işlem 7f3a… · 3 dosya
HATA      —            (redacted raw error when failed)
ÇIKIŞ     0            (exit code, process steps only)
```

### 3.1 "Kod terminali" — keep the vertical expansion, make it exact and beautiful

When a step **really** changes code, the card grows downward and a terminal pane slides open under the timeline. This stays the signature moment of the card.

```
│  ✓ 14:02  Kod değişikliği uygulandı · 3 dosya · +42 −7               │
│ ┌─ KOD TERMİNALİ ─────────────────────────────── 14:02:11 ─ Kopyala ┐ │
│ │ ● ● ●   kiosk-routes.js  +38 −5   app.js  +3 −1   ui.css  +1 −1   │ │  file tabs (click = jump)
│ │ ───────────────────────────────────────────────────────────────── │ │
│ │ kiosk-routes.js                                                   │ │
│ │  41 │   const limit = 5;                                  (red)   │ │
│ │  41 │ + const limit = 50;                                 (green) │ │
│ │  42 │   router.post('/pairing-codes', async (req, res) => {       │ │
│ │     │ ↳ wrapped continuation shown with a hanging indent          │ │
│ │ …                                                                 │ │
│ └───────────────────────────────────────────────────────────────────┘ │
```

Rules:
- **Only real, applied changes.** The pane is filled from the *result* of the step: before/after text that the engine actually committed (source-edit receipts), or the real file contents before/after for the other mutators, or real agent edit events. A rejected/failed edit **never** shows a diff in the terminal; it shows one muted line: "Değişiklik uygulanmadı; dosyalar olduğu gibi duruyor." (only when verified).
- **Stays visible for the whole session.** The pane keeps the **latest code change of the session** while later steps (build, test, commit) run; the pane header shows which step and time it belongs to. A newer code change replaces it with a short slide (160 ms). The pane closes only with the card or when the user collapses it (collapse state persists per user).
- **Every code-changing path feeds it** (C3): source-edit family (incl. structural/semantic via result diffs), text/config/file mutators inside a recognized workspace, `copy`/`move` into a workspace (shown as added/renamed file), Codex and Claude jobs (§5.4.1).
- **Terminal look:** `Tv.SurfaceSunken` background, traffic-light dots (decorative, not buttons), Cascadia Mono 12 / 17, line-number gutter (`TextFaint`), `+` lines green text on 8 % green tint, `−` lines red text on 8 % red tint, context lines `TextMuted`, file headers in `Tv.Accent` semibold, hunk separators as a thin dashed rule, wrapped continuations with `↳` hanging indent (text still wraps — no truncation, AGENTS.md). File tabs show per-file +/−; clicking scrolls to that file.
- **Reveal:** on a new change the added lines appear with a quick top-to-bottom reveal (≤ 600 ms total, real content only); instant when reduced motion is on.
- **Height:** the card grows by `min(rendered code height, 55 % of work area)`; small changes get a small pane (no 460 DIP minimum). The pane scrolls inside when longer. A user-resized height is respected as the **maximum** pane height, not a fixed size; width never changes automatically.
- **Copy** copies the redacted unified diff (same text that is shown). Very large changes: first 400 changed lines per file and a final line "… {n} satır daha · tamamı Çalışma günlüğünde" (N7 keeps up to 64 KB per session; never claim the full diff is shown when it is not).
- **Secrets:** existing line-level redaction stays (`[gizli değer gösterilmedi]`), applied before rendering and before copy.

Rules (card):
- Width default 440 DIP (min 360, max 720); height **grows with content** up to 60 % of the work area, then the timeline scrolls; user resize persists (existing behaviour) and is applied to every card, not only code cards.
- The same window instance is reused for the whole session (no window recreation between steps → no flicker). A new session replaces the old terminal card in place with a 180 ms cross-fade.
- Hover pauses auto-dismiss (existing). Click on the card body (not buttons) opens the Control Center "Çalışma günlüğü" page at this session (N7). `Esc` closes. Drag pins (existing contract).
- Minimum font size 11 DIP; body text ≥ 4.5:1 contrast on `#101722`.

## 4. Information model (protocol v4)

Stop composing prose in the service. Send structured data; the tray renders it. Bump `DesktopProgressProtocol.Version` to 4 (no v3 compatibility path — both ends ship together in one installer).

```csharp
public sealed record DesktopWorklogFrame(
    string SessionId,                 // stable for project+actor+continuous activity
    long Sequence,
    DateTimeOffset TimestampUtc,
    WorklogSessionHeader Header,
    WorklogStep Current,              // the step the title describes
    IReadOnlyList<WorklogTimelineEntry> Timeline, // newest first, max 30 (tray shows 3–5)
    WorklogTotals Totals,
    DesktopProgressEvidence? Evidence, // technical surface only
    int ExpectedNextUpdateSeconds,     // for stale detection (W13)
    DesktopProgressLane Lane = DesktopProgressLane.Worklog);

public sealed record WorklogSessionHeader(
    string? ProjectName,              // folder name of the workspace root / git root; null if unknown
    string ActorName);                // "Codex", "ChatGPT · Dev", "ChatGPT · Yönetim", "Claude", "Talvora"

public sealed record WorklogStep(
    WorklogStepCategory Category,
    WorklogStepState State,           // Running, Succeeded, Failed, Cancelled, Waiting
    string Title,                     // already tense-correct Turkish from the copy deck
    string? FactLine,                 // real objects only
    WorklogOutcome? Outcome,          // terminal only
    double ElapsedSeconds);

public sealed record WorklogOutcome(
    WorklogFailureKind? FailureKind,  // see §5.3
    string PlainReason,               // Turkish, no jargon
    string? NextStep);                // Turkish, honest ("ajan yeniden deneyebilir"), null if unknown

public sealed record WorklogTimelineEntry(
    DateTimeOffset TimestampUtc, WorklogStepState State, string Label); // past tense, short

public sealed record WorklogTotals(
    int Steps, double SessionSeconds, int FilesChanged, WorklogCheckState LastBuild, WorklogCheckState LastTests);
```

### 4.1 Sessions (fixes W4)

- `SessionId` = hash of (project root or `"global"`, actor). A session stays open while steps keep arriving with gaps ≤ **90 s**, or while a background agent job is running. After the last step reaches a terminal state and 90 s pass with no new step, the session's last frame is its terminal frame.
- Actor: from the MCP request: surface path (`/mcp/dev` → "ChatGPT · Dev", `/mcp/admin` → "ChatGPT · Yönetim" when the client is the tunnel) and `clientInfo.name` of the initialize/request context when available (Codex CLI identifies itself — verify with Context7 what the C# SDK exposes for stateless requests). Background jobs set the actor explicitly ("Codex", "Claude").
- Project: from tool arguments (`workspaceRoot`, `workingDirectory`, `repositoryPath`, `root`, `path`) → nearest folder with `.git` or a project manifest → folder name. Never show full paths in the plain area.
- Parallel calls inside one session update the same card; the title follows the most recent *running* step; finished parallel steps go to the timeline. No "paralel adım" wording.
- The presenter keys windows by `SessionId`; the "one visible worklog card" budget stays.

### 4.2 Facts only from real results (fixes W7, W8, W15)

| Step | Fact line source | Example |
|---|---|---|
| Source edit | `SourceEditTransactionResult.Files` (receipts) + **real** added/removed line counts computed by the engine from before/after text (add `AddedLines`/`RemovedLines` to `SourceEditFileReceipt`) | `3 dosya · +42 −7 · kiosk-routes.js, app.js ve 1 dosya` |
| Source edit rejected | no counts; reason from `SourceEditError.Code` | `Değişiklik uygulanmadı · dosya bu arada değişmişti` |
| `dotnet build` / msbuild | diagnostics parsed from the result (existing `talvora_diagnostics_parse` logic) | `0 hata · 2 uyarı · Talvora.slnx · Release` |
| `dotnet test` / test runners | TRX/console summary (existing `ConfigFormatTools.TestReports`) | `148 test geçti` / `2 test başarısız: ParserTests.Push, …` |
| git commit | new HEAD short hash + subject (first 60 chars) | `a1b2c3d · fix(ui): …` |
| git push | remote + branch from result | `github/main` |
| Service lifecycle | service display name + final state | `Talvora Dev → çalışıyor` |
| Package install | package + version from result | `ripgrep 15.2.0` |
| Inspection | counts only | `12 dosya · 37 eşleşme` |
| Codex job | from `CodexCliEventSummary`: commands, file changes (names), last activity class | `41 adım · 9 dosya değişti` |
| Claude job | from `ClaudeCliProgress`: tool calls, failed tools, permission denials | `27 adım · 1 izin reddi` |

File list in the fact line: at most 2 names + "ve N dosya"; total count is always the real total (not the capped list length).

### 4.3 What is not shown

- No tool names, command lines, exit codes, transaction ids or paths in the header/title/fact/outcome/timeline — only in "Ayrıntılar".
- No "Amaç:" sentences, no "Sonuç: başarılı", no elapsed time in prose (footer and timeline times only).
- No red card for failed **inspection** steps (W19): they appear as a muted timeline row "⚠ Okuma başarısız · yanlış parametre" and the card keeps the session state. Red is reserved for mutations, builds, tests, deploys, git, services, packages and agent jobs.

## 5. Behaviour

### 5.1 Lifetimes (fixes W16)

- Active session: never auto-dismisses.
- Succeeded: 3 min after the session closes (AGENTS.md "about three minutes").
- Failed: 8 min. Cancelled: 5 min.
- Hover pauses; manual close of an active session suppresses it until the session ends (existing behaviour, now keyed by session).

### 5.2 Stale detection (fixes W13)

`ExpectedNextUpdateSeconds` is set by the producer (tool heartbeat 5 s → 15; Codex job 4 s → 20; Claude job 25 s → 60). Tray shows "Yanıt bekleniyor" only after `2 × ExpectedNextUpdateSeconds` without a frame, with the real last-update time.

### 5.3 Failure reasons (fixes W2)

Classify the real error once in the service (`WorklogFailureClassifier`) from the structured result / exception type, then pick the Turkish text:

| `WorklogFailureKind` | Detected from | Plain reason | Next step |
|---|---|---|---|
| `InvalidArguments` | `ArgumentException`, schema/binding errors | İstek eksik ya da hatalı bilgi içeriyordu. | Ajan isteği düzeltip yeniden deneyebilir. |
| `StaleRevision` | source edit error code for revision mismatch | Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. | Ajan güncel hâli okuyup yeniden deneyecek. |
| `NotFound` | `FileNotFound`/`DirectoryNotFound`/`KeyNotFound` | Aranan dosya ya da kayıt bulunamadı. | — |
| `AccessDenied` | `UnauthorizedAccessException` | Windows bu işleme izin vermedi. | — |
| `BuildErrors` | build diagnostics > 0 errors | Derleme {n} hata verdi. | Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor. |
| `TestFailures` | test summary failed > 0 | {n} test başarısız oldu. | — |
| `Timeout` | `timedOut: true` | İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor. | — |
| `DeliveryGate` | `ProjectDeliveryGate` refusal | Teslim kontrolü eksik kanıt nedeniyle gönderimi durdurdu. | Kanıt dosyaları güncellenince tekrar denenebilir. |
| `PermissionDenied` (agents) | Claude permission denials | Claude izin gerektiren adımları yapamadı. | İzin modunu değiştirerek yeniden başlatılabilir. |
| `Unknown` | anything else | Bu adım tamamlanamadı. | — |

The raw (redacted) error message goes to "Ayrıntılar". "What is protected" (AGENTS.md) uses exactly these sentences and only when verified: rejected source edit → append "Kodunuz değişmedi." to `PlainReason`; failed build/test → the `NextStep` text in the table already states it; every other kind → no protection sentence. No other protection wording.

### 5.4 Agent jobs

- Codex/Claude jobs open (or join) the session of their project with actor "Codex"/"Claude". Live activity line under the title is classified from real events, never a command line: `Testleri çalıştırıyor`, `Derliyor`, `Dosya düzenliyor · app.js`, `Kaynakları inceliyor`, `Git işlemi yapıyor`, `Talvora aracı kullanıyor · {category}`.
- Completion: title from the copy deck + totals + changed file names (from events) + "Codex'in özeti" (first 240 chars of the final answer, redacted) **inside "Ayrıntılar"** only.
- Talvora tool calls that Codex makes *during* its job (Codex is connected to Talvora MCP) join the same session as timeline steps.

#### 5.4.1 Code terminal for agent jobs

- **Claude:** `stream-json` tool-use events for `Edit`/`MultiEdit`/`Write` carry the real old/new text → build the diff when the matching tool *result* reports success (never from the request alone).
- **Codex:** check with Context7 and a real `codex exec --json` run which events carry file changes and whether they include diff text. If they do, use them after the change is reported applied. If they only carry paths, take a lightweight snapshot at job start (SHA-256 + text of tracked/untracked files under the working directory that are ≤ 1 MB, skipping `.git`, `bin`, `obj`, `node_modules`) and, when a file-change event arrives, diff that file against its snapshot; pre-existing uncommitted changes are therefore not attributed to Codex.
- Snapshots live in memory (bounded: 2 000 files / 64 MB, otherwise per-file lazy snapshot on first event) and are dropped when the job ends.

### 5.5 Diff engine

One shared `TextDiffBuilder` (service side) produces the unified diff + per-file added/removed counts from before/after text for every path above. Use **DiffPlex** (`DiffPlex` NuGet package, line-level `InlineDiffBuilder`/`Differ`): take the current stable version, licence and API from Context7/NuGet; add it to `Directory.Packages.props`, lock files and THIRD-PARTY-NOTICES. Size guard: files > 1 MB or > 20 000 lines show "Dosya çok büyük; değişiklik özeti: +{a} −{r}" without a preview. If DiffPlex cannot be used (licence/compatibility), stop with a proposal — do not write a custom diff algorithm. Binary files: "ikili dosya değişti · {boyut}". CRLF/LF-only changes: "Yalnız satır sonları değişti". Encoding-only: "Yalnız kodlama değişti".

## 6. Copy deck (authoritative)

Voice: **impersonal, present-progressive for running, past for done**, no "sen/siz", no first person, correct diacritics. Badge words: `Çalışıyor`, `Tamamlandı`, `Hata`, `Durduruldu`, `Yanıt bekleniyor`.

| Category | Running | Succeeded | Failed |
|---|---|---|---|
| Source edit | Kod değişikliği uygulanıyor | Kod değişikliği uygulandı | Kod değişikliği uygulanamadı |
| Build | Derleniyor | Derleme başarılı | Derleme başarısız |
| Tests | Testler çalışıyor | Testler geçti | {n} test başarısız |
| Restore | Paketler geri yükleniyor | Paketler hazır | Paketler geri yüklenemedi |
| Package build (installer) | Kurulum paketi hazırlanıyor | Kurulum paketi hazırlandı | Kurulum paketi hazırlanamadı |
| Deploy | Yeni sürüm kuruluyor | Kurulum devredildi · doğrulama bekleniyor | Yeni sürüm kurulamadı |
| Deploy verified | — | Yeni sürüm çalışıyor · {kısa sürüm} | — |
| Commit | Değişiklikler kaydediliyor | Değişiklikler kaydedildi | Kayıt oluşturulamadı |
| Push | Uzak depoya gönderiliyor | Uzak depoya gönderildi | Gönderim yapılamadı |
| Other git | Git işlemi yapılıyor | Git işlemi tamamlandı | Git işlemi tamamlanamadı |
| Service | {servis} {başlatılıyor/durduruluyor/yeniden başlatılıyor} | {servis} {çalışıyor/durdu} | {servis} istenen duruma geçmedi |
| Package (choco) | {paket} kuruluyor / güncelleniyor / kaldırılıyor | {paket} kuruldu / güncellendi / kaldırıldı | {paket} işlemi tamamlanamadı |
| File mutation | Dosyalar düzenleniyor | Dosyalar düzenlendi | Dosya işlemi tamamlanamadı |
| Network check | Bağlantı denetleniyor | Bağlantı denetlendi | Bağlantı kurulamadı |
| Process/job | {program adı} çalışıyor | {program adı} bitti | {program adı} hata ile bitti |
| Inspection | Kaynaklar inceleniyor | Kaynaklar incelendi | (muted row) Okuma başarısız |
| Codex job | Codex {proje} üzerinde çalışıyor | Codex görevi tamamlandı | Codex görevi tamamlanamadı |
| Claude job | Claude {proje} üzerinde çalışıyor | Claude görevi tamamlandı | Claude görevi tamamlanamadı |
| Generic | İşlem yürütülüyor | İşlem tamamlandı | İşlem tamamlanamadı |

Message fallbacks while protocol v3 is still in use (decision P-4; only when the step has no fact line): Started/Running `Devam ediyor.` · Succeeded `Adım başarıyla tamamlandı.` · Cancelled `Adım durduruldu.` (Failed always has a reason from §5.3.)

Timeline labels = Succeeded/Failed column + optional "· {kısa olgu}". Footer: `{n} adım · {süre} · {m} dosya değişti` (omit zero parts). Overline: `BU OTURUMDA`. Expander: `Ayrıntılar`. Copy button: `Kopyala`. Pin tooltip: `Konumu sabitle` / `Otomatik konuma dön`. Close: `Kapat`.

## 7. Visual spec

| Element | Spec |
|---|---|
| Surface | `#101722` (Gece `Tv.Surface`), 1 px border `#1F2A3B`, radius 14, shadow blur 24 / opacity 0.35; no shadow in high contrast |
| Live line | 2 px at the top inside the border, Aurora gradient, 1.6 s shimmer translate; static when reduced motion |
| Header | Talvora "T" mark 16 px (same silhouette as the tray icon; Aurora gradient fill when active, `TextMuted` otherwise) · project chip (`SurfaceRaised`, radius 6, padding 8,2, 12 px semibold `Text`) · actor chip (same, text `TextMuted`) · state badge (glyph + word, radius 6, padding 8,2, state colour text on 14 % tint) · icon buttons 28×28 (`Pin20`/`PinOff20`, `Dismiss20`) |
| Glyphs | WPF-UI `SymbolRegular`, exactly: Çalışıyor `ArrowSync20` (static, no spin), Tamamlandı `Checkmark20`, Hata `ErrorCircle20`, Durduruldu `Stop20`, Yanıt bekleniyor `Clock20`, muted inspection failure `Warning20`. Timeline rows use the same symbols at 12 px. State colours: running `Tv.Accent`, succeeded `Tv.Ready`, failed `Tv.Offline`, cancelled/waiting `Tv.Attention`, muted `Tv.TextFaint`. |
| Title | Segoe UI Variable Display 15 / 20 semibold `Tv.Text` |
| Fact line | Segoe UI Variable Text 13 / 18 `Tv.TextMuted`; numbers tabular |
| Outcome | 13 / 18 `Tv.Text`; first sentence reason, second next step in `Tv.TextMuted` |
| Timeline | overline 11 / 14 `Tv.TextFaint` letter-spacing 80; rows 12.5 / 18: state glyph (12 px, state colour) · time (Cascadia Mono 11.5, `TextFaint`) · label (`TextMuted`); max 5 rows visible, "+N adım daha" link expands in place |
| Footer | top divider `Tv.Border`; Cascadia Mono 11.5 digits, `TextFaint`; "Ayrıntılar ▸" text button `Tv.Accent` |
| Code terminal | §3.1: `Tv.SurfaceSunken #070A10`, Cascadia Mono 12 / 17, gutter `TextFaint`, `+` `#3DDC97` on 8 % tint, `−` `#FF6B81` on 8 % tint, file headers/tabs `Tv.Accent`, `↳` wrapped continuations |
| Evidence | same sunken surface, Cascadia Mono 12 / 17, two-column key/value layout, copy button |
| Motion | enter: 180 ms fade + 12 px rise (existing); state change: 120 ms colour cross-fade; timeline row insert: 160 ms height expand; all disabled when `SystemParameters.ClientAreaAnimation` is false |
| High contrast | `SystemColors` brushes (existing fallback), no gradients, focus rectangles visible |

Tokens come from the shared Gece dictionary (UI1); the worklog does **not** follow the light theme (terminal identity per AGENTS.md).

## 8. Control Center integration

- New rail item **"Çalışma günlüğü"** (between "Hizmetler" and "Bağlantı zinciri" in `DESIGN-CONTROL-CENTER.md` §4) listing sessions (project, actor, start/end, steps, files changed, outcome) for the last 7 days, newest first; selecting a session shows its full timeline and evidence (redacted). Search and filters: project, actor, outcome.
- Storage: the tray appends each **terminal** session snapshot (not every heartbeat) to `%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl`, bounded to 7 days / 2 000 sessions / 20 MB with rotation; evidence code previews are truncated to 8 KB in history.
- Settings → "Bildirimler": show code preview in the card by default (on/off), failure-only mode (cards only for failures and agent jobs), quiet hours (no card, history only). Defaults: on, off, off.

## 9. Implementation cards

Each card: test first; `Talvora.Notification.Regression` extended with behaviour tests (not source regexes); Context7 for WPF/WPF-UI/MCP SDK APIs; Awwwards evidence for N5–N7; before/after PNG of real cards (use a deterministic preview harness: `Talvora.Tray.exe --worklog-preview <scenario> --out <png>` added in N5) attached to the Gitea issue; installed-runtime check that a real `talvora_apply_patch` and a real failing `dotnet build` produce the expected cards.

### N1 — Correct states, titles and reasons; remove duplication

**Files:** `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `TalvoraDesktopWorklogState.cs`, `TalvoraDesktopProgressNarrative.cs`, new `src/Talvora/WorklogFailureClassifier.cs`, new `src/Talvora/WorklogCopy.cs` (copy deck §6 as data), `tests/Talvora.Notification.Regression/*`.
**Do:** fix W1, W2 (classifier §5.3 with redacted raw message to evidence), W3, W6, W9, W11, W12, W18 (reuse `GitArgumentParser` from S8; if S8 is not done yet, add the parser here and let S8 reuse it), W19. Narratives become (category, running title, succeeded title, failed title) lookups from `WorklogCopy`.
**Don't:** change the protocol yet (N2); change tray layout.
**Acceptance:** regression scenarios: success → past-tense title; failure → failed title + reason + no duplicated history line; inspection failure → no red card; parallel steps → no "paralel adım" text; no tool name in Title/Message for any category (property test over all manifest tools).

### N2 — Protocol v4, sessions and actor/project context

**Files:** `src/Talvora.Shared/DesktopProgressProtocol.cs` (v4 records §4), `src/Talvora/TalvoraDesktopWorklogState.cs` (session model §4.1), `Program.cs` (pass surface path / client info into the filter), `TalvoraDesktopCodexWorklog.cs`, `TalvoraDesktopClaudeWorklog.cs`, `src/Talvora.Tray/DesktopProgressPipeListener.cs`, `DesktopProgressPresentationState.cs`, `DesktopProgressNotificationPresenter.cs` (key by `SessionId`, reuse window), regression tests.
**Do:** §4, §4.1, §5.2 (`ExpectedNextUpdateSeconds`), §5.1 lifetimes; bounds/validation for every new field (same style as v3 limits); frame size budget unchanged.
**Don't:** keep a v3 reader; let delivery block tool calls.
**Acceptance:** sequential calls in one project produce one window instance with a growing timeline (assert same window object); a different project creates a new session; Claude job with 25 s heartbeat never shows "Yanıt bekleniyor"; 3-minute success lifetime.

### N3 — Real facts from results

**Files:** `src/Talvora/SourceEditing/SourceEditModels.cs` (+ `AddedLines`/`RemovedLines` per receipt), `SourceEditCommitter.cs` (compute from committed before/after text), `TalvoraDesktopProgressEvidence.cs`, build/test result summarizers (reuse `QualityTools.Diagnostics`, `ConfigFormatTools.TestReports`), git commit/push result parsing, `tests/Talvora.SourceEdit.Regression` (receipt counts), notification regression.
**Do:** §4.2 table; fix W7 and W8 (true totals, unified-diff file detection from `+++ b/…`, preview-limit text from the constant).
**Acceptance:** rejected patch → no counts; unified diff of 2 files → "2 dosya"; build with 3 errors → "Derleme başarısız · 3 hata" + file names; test failure lists up to 3 failing names; commit card shows the real short hash.

### N3b — Code terminal: real, complete, persistent (protect the favourite feature)

**Files:** new `src/Talvora/WorklogDiff/TextDiffBuilder.cs`, `TalvoraDesktopProgressEvidence.cs`, source-edit committer (expose before/after text or the diff to the notifier without re-reading), mutator tools in `DeveloperTools.FileMutation.cs`, `ConfigFormatTools.*`, `StructuredConfigTools.*`, `ConfigAssetTools.Json.cs`, `FileTools.cs` (workspace writes only), `TalvoraDesktopWorklogState.cs` (session keeps latest code change), `DesktopProgressProtocol.cs` (send the preview once per change: `CodeChangeId` + preview; heartbeats carry only the id), notification regression + source-edit regression.
**Do:** fix C1–C4 and C7 per §3.1, §5.4.1 (Claude part here; Codex part in N4), §5.5. Rejected/failed edits show no diff. The latest code change survives later non-code steps for the whole session.
**Don't:** remove or shrink the vertical expansion; truncate silently; show diffs for files outside recognized workspaces (privacy) — those show "1 dosya değişti" only.
**Acceptance (behaviour tests):** (1) patch → build → test sequence: the build and test frames still carry the same `CodeChangeId` and the tray keeps the pane; (2) stale-revision patch → no preview, muted "uygulanmadı" line; (3) `structural_edit` and `semantic_edit` rename produce real diffs from results; (4) `replace_text`/`json_set` inside a workspace produce diffs; (5) unified-diff patch with 2 files → 2 file headers, counts equal to the applied change; removed `-- comment` line counted; (6) heartbeat frames ≤ 4 KB while a 300 KB preview is active.

### N4 — Agent jobs tell their story

**Files:** `TalvoraDesktopCodexWorklog.cs`, `TalvoraDesktopClaudeWorklog.cs`, `src/Talvora.Shared/CodexCliEventSummary.cs` (activity classification), `ClaudeCliStreamingProgress.cs`, regression tests.
**Do:** §5.4 and §5.4.1 (Codex code terminal); fix W10 (diacritics) and W13; completion totals and changed files; summary excerpt in evidence only.
**Acceptance:** recorded JSONL fixtures (sanitised) → expected activity lines, never a raw command; completion card shows totals; texts pass a diacritics check (test: no "gelistir|dogrul|cagri|gecen sure" ASCII forms).

### N5 — Visual redesign of the card + preview harness

**Files:** `src/Talvora.Tray/DesktopProgressNotificationWindow*.cs` (move to XAML `Views/WorklogCard.xaml` + small view model), `DesktopProgressVisualTheme.cs` (map to Gece tokens), new `--worklog-preview` command in `Program.cs`, `ControlCenterSmoke.cs` (scenario renders).
**Do:** §3, §3.1 (terminal look, reveal, height rules — fixes C5, C6), §7; fix W5, W17; content-driven height up to 60 % work area; same window reused per session; keep drag/pin/resize/mixed-DPI behaviour and the manual-dismiss semantics. The vertical code expansion must keep working at every step of this card — it is the user's favourite feature.
**Acceptance:** preview PNGs for: running edit, success with timeline, **2-line change (small pane)**, **3-file 300-line change (tabs, scroll, colours, gutter, wrapped continuation)**, build failure after a code change (pane still open), inspection-failure row, Codex job running/completed, stale; contrast test on token pairs; mixed-DPI placement regression unchanged; reduced-motion render identical except animation.

### N6 — Evidence surface ("Ayrıntılar")

**Files:** `Views/WorklogCard.xaml`, `Controls/EvidencePanel` (shared with Control Center UI4), redaction tests.
**Do:** §3 expanded view; technical fields (tool name, transaction id, exit code, redacted raw error) live only here; copy (redacted text only); expander state persists per user. The code terminal (N3b/N5) is a separate, always-visible pane — do not merge it into this expander.
**Acceptance:** secrets in a diff fixture never reach the rendered text or clipboard; tool name absent from all other text elements (UIA tree test).

### N7 — "Çalışma günlüğü" history in the Control Center

**Files:** new `src/Talvora.Tray/WorklogHistoryStore.cs`, Control Center view + view model (after UI2 shell exists), card click → open session.
**Do:** §8 storage and page; bounded rotation; filters; empty state "Henüz kayıtlı çalışma yok."
**Acceptance:** 2 000+ sessions fixture stays within limits and loads < 300 ms; clicking a card opens the matching session.

### N8 — Notification settings

**Files:** Settings view (UI6), `DesktopProgressNotificationPresenter.cs`, settings store.
**Do:** §8 settings (code preview default, failure-only mode, quiet hours) — they only change presentation, never delivery to history.
**Acceptance:** each setting covered by a presenter behaviour test; defaults as specified.
