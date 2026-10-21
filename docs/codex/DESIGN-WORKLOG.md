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

## 7. Visual spec — "Phosphor" (hacker terminal), superseding the earlier table (Claude, 2026-10-20)

**The authoritative design is code, not prose:** `docs/codex/reference/worklog-phosphor/src/*` — eight files written and **compiled by Claude** in a scratch WPF project with the same settings as `Talvora.Tray` (net10.0-windows, UseWPF + UseWindowsForms, TreatWarningsAsErrors, AssemblyName `Talvora.Tray`): 0 warnings, 0 errors. `docs/codex/reference/worklog-phosphor/previews/*.png` are the target renders (150 %), including a real window capture. Card N5 copies these files verbatim. Where any text in this document disagrees with those files, **the files win**.

Summary of what the files implement (for reading, not for re-implementing):

| Element | Phosphor design |
|---|---|
| Palette | near-black green surfaces `#05080A` / `#090E0C` / `#0E1611`, terminal `#030504`; phosphor green `#39FF88` (prompt, success, cursor), cyan `#5BD8FF` (running, links, file names), amber `#FFB547` (waiting/stopped), red `#FF4D6A` (failure); text `#D8FFE7` / muted `#8CB9A0` / faint `#557363` |
| Type | Cascadia Mono everywhere (fallback Cascadia Code, Consolas); title 14.5 semibold with a soft green phosphor glow; body 12; meta 11–11.5 |
| Header | shell-prompt line `talvora@<machine>:~/<project>` (green, real values) · actor (`codex`, `chatgpt·dev`, `claude`…) · braille spinner `⠋⠙⠹…` while running · state tag `[ÇALIŞIYOR]` / `[TAMAM]` / `[HATA]` / `[DURDU]` / `[BEKLİYOR]` in state colour · pin + close (Segoe Fluent Icons) |
| Title | `> Title▍` — prompt mark coloured by state, block cursor blinking (530 ms) only while running |
| Facts / outcome | fact line indented 2 ch; failure reason prefixed with a red `!`, next step below in muted text |
| Timeline | `── oturum ────` rule, rows `[ ✓ ] 14:02 label` with boot-log style tags: `[ ▸ ]` cyan running, `[ ✓ ]` green, `[ ✗ ]` red, `[ ■ ]` amber, `[ · ]` faint muted; max 5 rows + `+N adım daha` |
| Code terminal | real-terminal block: title bar with three window dots, `diff · HH:mm:ss`, `kopyala`; per-file tabs `name +a −r`; rows with line-number gutter, `+`/`−` markers, green/red text on 10 % tints, hunk headers cyan on a cyan tint, file headers with a cyan bar; wrapped code continues in the code column; scrolls inside after the max height (420 DIP default; user-resized height − 240, clamped 160–900) |
| Texture | 1 px scanline every 3 px at 3.5 % opacity (decorative, never hit-testable) |
| Live line | 2 px phosphor→cyan gradient sweeping across the top while running (1.6 s) |
| Footer | `14 adım · 06:12 · 5 dosya değişti` (duration as `mm:ss`/`h:mm:ss`) · `ayrıntılar ▸` (hidden until N6 wires it — see N5 step 4) |
| Motion | spinner, cursor blink, live line: only while running, window visible, `SystemParameters.ClientAreaAnimation` true and not high contrast; enter fade 180 ms + 12 px rise; close fade 160 ms |
| High contrast | system colours (`WindowBrush`, `WindowTextBrush`, `GrayTextBrush`), no tints, no glow, no scanlines, no animation |

The worklog never follows the light theme (terminal identity per AGENTS.md). The Control Center keeps its own Gece/Gün tokens; UI7 must not change these files.

## 8. Control Center integration

- New rail item **"Çalışma günlüğü"** (between "Hizmetler" and "Bağlantı zinciri" in `DESIGN-CONTROL-CENTER.md` §4) listing sessions (project, actor, start/end, steps, files changed, outcome) for the last 7 days, newest first; selecting a session shows its full timeline and evidence (redacted). Search and filters: project, actor, outcome.
- Storage: the tray appends each **terminal** session snapshot (not every heartbeat) to `%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl`, bounded to 7 days / 2 000 sessions / 20 MB with rotation; evidence code previews are truncated to 8 KB in history.
- Settings → "Bildirimler": show code preview in the card by default (on/off), failure-only mode (cards only for failures and agent jobs), quiet hours (no card, history only). Defaults: on, off, off.

## 9. Implementation cards

Each card: test first; `Talvora.Notification.Regression` extended with behaviour tests (not source regexes); Context7 for WPF/WPF-UI/MCP SDK APIs; Awwwards evidence for N5–N7; before/after PNG of real cards (use a deterministic preview harness: `Talvora.Tray.exe --worklog-preview <scenario> --out <png>` added in N5) attached to the Gitea issue; installed-runtime check that a real `talvora_apply_patch` and a real failing `dotnet build` produce the expected cards.

### N1 — Correct states, titles and reasons; remove duplication

> **Amended by P-4, P-10 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `TalvoraDesktopWorklogState.cs`, `TalvoraDesktopProgressNarrative.cs`, new `src/Talvora/WorklogFailureClassifier.cs`, new `src/Talvora/WorklogCopy.cs` (copy deck §6 as data), `tests/Talvora.Notification.Regression/*`.
**Do:** fix W1, W2 (classifier §5.3 with redacted raw message to evidence), W3, W6, W9, W11, W12, W18 (reuse `GitArgumentParser` from S8; if S8 is not done yet, add the parser here and let S8 reuse it), W19. Narratives become (category, running title, succeeded title, failed title) lookups from `WorklogCopy`.
**Don't:** change the protocol yet (N2); change tray layout.
**Acceptance:** regression scenarios: success → past-tense title; failure → failed title + reason + no duplicated history line; inspection failure → no red card; parallel steps → no "paralel adım" text; no tool name in Title/Message for any category (property test over all manifest tools).

#### Exact implementation (scope = decisions P-4 + P-10) — copy this, do not reinterpret

Protocol stays v3. No tray/layout change. All strings below are the approved copy — use them character for character.

Step 1 — new file `src/Talvora/WorklogCopy.cs`, exactly:

```csharp
namespace Talvora;

internal sealed record WorklogTitles(string Running, string Succeeded, string Failed);

/// <summary>Approved Turkish copy (DESIGN-WORKLOG §6, decisions P-4/P-10). Do not edit without Claude.</summary>
internal static class WorklogCopy
{
    internal const string RunningFallback = "Devam ediyor.";
    internal const string SucceededFallback = "Adım başarıyla tamamlandı.";
    internal const string CancelledFallback = "Adım durduruldu.";
    internal const string CancelledTitle = "İşlem durduruldu";

    internal static readonly WorklogTitles SourceEdit = new("Kod değişikliği uygulanıyor", "Kod değişikliği uygulandı", "Kod değişikliği uygulanamadı");
    internal static readonly WorklogTitles Build = new("Derleniyor", "Derleme başarılı", "Derleme başarısız");
    internal static readonly WorklogTitles Tests = new("Testler çalışıyor", "Testler geçti", "Testler başarısız");
    internal static readonly WorklogTitles Restore = new("Paketler geri yükleniyor", "Paketler hazır", "Paketler geri yüklenemedi");
    internal static readonly WorklogTitles PackageBuild = new("Kurulum paketi hazırlanıyor", "Kurulum paketi hazırlandı", "Kurulum paketi hazırlanamadı");
    internal static readonly WorklogTitles Deploy = new("Yeni sürüm kuruluyor", "Kurulum devredildi · doğrulama bekleniyor", "Yeni sürüm kurulamadı");
    internal static readonly WorklogTitles Commit = new("Değişiklikler kaydediliyor", "Değişiklikler kaydedildi", "Kayıt oluşturulamadı");
    internal static readonly WorklogTitles Push = new("Uzak depoya gönderiliyor", "Uzak depoya gönderildi", "Gönderim yapılamadı");
    internal static readonly WorklogTitles OtherGit = new("Git işlemi yapılıyor", "Git işlemi tamamlandı", "Git işlemi tamamlanamadı");
    internal static readonly WorklogTitles Service = new("Servis işlemi yapılıyor", "Servis işlemi tamamlandı", "Servis istenen duruma geçmedi");
    internal static readonly WorklogTitles Package = new("Paket işlemi yapılıyor", "Paket işlemi tamamlandı", "Paket işlemi tamamlanamadı");
    internal static readonly WorklogTitles FileMutation = new("Dosyalar düzenleniyor", "Dosyalar düzenlendi", "Dosya işlemi tamamlanamadı");
    internal static readonly WorklogTitles Network = new("Bağlantı denetleniyor", "Bağlantı denetlendi", "Bağlantı kurulamadı");
    internal static readonly WorklogTitles Process = new("Program çalışıyor", "Program bitti", "Program hata ile bitti");
    internal static readonly WorklogTitles Codex = new("Codex çalışıyor", "Codex görevi tamamlandı", "Codex görevi tamamlanamadı");
    internal static readonly WorklogTitles Claude = new("Claude çalışıyor", "Claude görevi tamamlandı", "Claude görevi tamamlanamadı");
    internal static readonly WorklogTitles Generic = new("İşlem yürütülüyor", "İşlem tamamlandı", "İşlem tamamlanamadı");
}
```

Step 2 — `src/Talvora/TalvoraDesktopProgressNarrative.cs`:

a) Replace the record with

```csharp
private sealed record OperationNarrative(
    OperationCategory Category,
    WorklogTitles Titles);
```

b) Rewrite `BuildNarrative` so every branch returns `new(<category>, WorklogCopy.<X>)` with this exact mapping (keep the existing detection logic of each branch; only the returned values change):

| Existing branch | Category | Copy |
|---|---|---|
| `claude_exec` / `claude_run` / `claude_start` | GenericExecution | `WorklogCopy.Claude` |
| `run_powershell` + canonical package-build script | PackageBuild | `WorklogCopy.PackageBuild` |
| `run_powershell` + `Install.ps1` | Deployment | `WorklogCopy.Deploy` |
| any other `run_powershell` (including the old "log" branch — delete that branch) | GenericExecution | `WorklogCopy.Generic` |
| `dotnet_build` | Verification | `WorklogCopy.Build` |
| `dotnet_test` | Verification | `WorklogCopy.Tests` |
| `dotnet_restore` | Verification | `WorklogCopy.Restore` |
| `apply_patch` / `apply_edits` / `structural_edit` / `semantic_edit` | SourceEdit | `WorklogCopy.SourceEdit` |
| `git_run` with subcommand `push` | VersionControl | `WorklogCopy.Push` |
| `git_run` with subcommand `commit` | VersionControl | `WorklogCopy.Commit` |
| other `git_run` | VersionControl | `WorklogCopy.OtherGit` |
| contains `service_` | ServiceOperation | `WorklogCopy.Service` |
| contains `choco_` or `package_` | PackageOperation | `WorklogCopy.Package` |
| file mutation branch | FileMutation | `WorklogCopy.FileMutation` |
| tunnel / network / `http_` branch (except `http_request`, see Step 3) | NetworkOperation | `WorklogCopy.Network` |
| process / job branch | ProcessOperation | `WorklogCopy.Process` |
| fallback | GenericExecution | `WorklogCopy.Generic` |

For the git subcommand use the shared `GitArgumentParser.GetSubcommand(arguments)` (from A3/S8) on the `arguments` array — never `gitArgs[0]` (fixes W18).

c) Delete the inspection branches (`read_source` … `find_files`, `git_diff`/`git_log`, `file_hash`/`path_info`, `http_request`) — see Step 3. Delete `BuildPlainCompletion`. In `TryBuildDeferredCompletion` replace the subject comparison with `narrative.Category == OperationCategory.Deployment`, the title with `WorklogCopy.Deploy.Succeeded` and keep the existing message text unchanged.

Step 3 — inspection steps are silent (P-10, AGENTS.md "low-value reads remain silent"): in `ShouldNotifyToolCall` add these names to the `=> false` list: `read_source`, `read_text`, `read_text_range`, `read_bytes`, `tail_text`, `search_text`, `knowledge_search`, `search`, `fetch`, `find_files`, `git_diff`, `git_log`, `file_hash`, `path_info`, `http_request`.

Step 4 — `src/Talvora/WorklogFailureClassifier.cs`, new, exactly:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Talvora;

internal static partial class WorklogFailureClassifier
{
    /// <summary>Returns the plain Turkish reason (DESIGN-WORKLOG §5.3) for a failed step.</summary>
    internal static string Describe(CallToolResult result, string normalizedToolName)
    {
        if (result.StructuredContent is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            if (TryGetErrorCode(json) is { } code)
            {
                return code switch
                {
                    "EXPECTED_REVISION_MISMATCH" or "SOURCE_EDIT_CONFLICT" =>
                        "Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. Kodunuz değişmedi. Ajan güncel hâli okuyup yeniden deneyecek.",
                    "PATCH_PARSE_ERROR" or "PATCH_CONTEXT_NOT_FOUND" or "PATCH_CONTEXT_AMBIGUOUS" or "PATCH_OVERLAPPING_EDITS" =>
                        "Değişiklik isteği dosyanın güncel hâliyle eşleşmedi. Kodunuz değişmedi. Ajan isteği düzeltip yeniden deneyebilir.",
                    "SOURCE_VALIDATION_FAILED" =>
                        "Değişiklik sözdizimi kontrolünden geçmedi. Kodunuz değişmedi. Ajan hatayı düzeltip yeniden deneyebilir.",
                    _ => "Bu adım tamamlanamadı.",
                };
            }

            if (StructuredResultFailure.Describe(json) == StructuredResultFailure.TimedOut)
            {
                return "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.";
            }

            if (normalizedToolName == "dotnet_build")
            {
                return "Derleme hata verdi. Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.";
            }

            if (normalizedToolName == "dotnet_test")
            {
                return "Testlerin bir kısmı başarısız oldu.";
            }

            if (ContainsText(json, "TALVORA_DELIVERY_GATE_BLOCKED") || ContainsText(json, "WorkContract:"))
            {
                return "Teslim kontrolü eksik kanıt nedeniyle gönderimi durdurdu. Kanıt dosyaları güncellenince tekrar denenebilir.";
            }
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
        return CategoryPrefix().Match(text) is { Success: true } match
            ? match.Groups["c"].Value switch
            {
                "InvalidArguments" => "İstek eksik ya da hatalı bilgi içeriyordu. Ajan isteği düzeltip yeniden deneyebilir.",
                "NotFound" => "Aranan dosya ya da kayıt bulunamadı.",
                "AccessDenied" => "Windows bu işleme izin vermedi.",
                "Timeout" => "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.",
                _ => "Bu adım tamamlanamadı.",
            }
            : "Bu adım tamamlanamadı.";
    }

    private static string? TryGetErrorCode(JsonElement json)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(property.Name, "error", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var inner in property.Value.EnumerateObject())
                {
                    if (string.Equals(inner.Name, "code", StringComparison.OrdinalIgnoreCase) &&
                        inner.Value.ValueKind == JsonValueKind.String)
                    {
                        return inner.Value.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static bool ContainsText(JsonElement json, string needle) =>
        json.GetRawText().Contains(needle, StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<c>InvalidArguments|NotFound|AccessDenied|Timeout|IO|Failed):")]
    private static partial Regex CategoryPrefix();
}
```

Step 5 — `src/Talvora/TalvoraDesktopProgressNotifier.cs` `RunToolCallAsync`:
- `var displayName = narrative.Titles.Running;`
- success terminal: `TryEndWorklog(operationId, DesktopProgressKind.Completed, narrative.Titles.Succeeded, WorklogCopy.SucceededFallback, terminalEvidence, stopwatch.Elapsed);` (both the normal and the classification-exception branch).
- failure terminal (result is a failure): `TryEndWorklog(operationId, DesktopProgressKind.Failed, narrative.Titles.Failed, result is CallToolResult call ? WorklogFailureClassifier.Describe(call, canonicalToolName) : "Bu adım tamamlanamadı.", terminalEvidence, stopwatch.Elapsed);`
- cancelled: title `WorklogCopy.CancelledTitle`, message `WorklogCopy.CancelledFallback`.
- thrown exception: title `narrative.Titles.Failed`, message `"Bu adım tamamlanamadı."`.
- Delete `BuildFriendlyResultFailureMessage` and `BuildFriendlyExceptionMessage` if no longer referenced (keep `TryDescribeStructuredFailure` only as the boolean failure detector — make its `message` output unused or remove the out parameter; behaviour of *detecting* failure must not change).

Step 6 — `src/Talvora/TalvoraDesktopWorklogState.cs`:
- `ActiveWorklogItem` keeps the narrative; `PendingFailure`/`PendingCancellation` store the **terminal title** passed to `EndWorklog`, not `ended.Narrative…`:
  `_pendingFailure = new PendingFailure(ended.ToolName, title, message, evidence);` and same for cancellation; the final `Enqueue` uses `pendingFailure.Subject` (which now holds the failed title). Rename the record field `Subject` → `Title` for clarity.
- `BeginWorklog`: message = `WorklogCopy.RunningFallback` (no "Amaç:" sentence).
- `UpdateWorklogProgress`: message = `WorklogCopy.RunningFallback` (no elapsed time in prose; the meta line already shows it).
- parallel continuation (inside `if (_activeWorklog.Count > 0)`): message = `WorklogCopy.RunningFallback`; title = `latest.Narrative.Titles.Running`.
- Remove the "Son yaptıklarım" history completely: delete `_recentWorklogUpdates`, `_lastWorklogHistory`, `AddWorklogHistory` and every call; `ComposeWorklogMessage(current, evidence)` returns `current.Trim()` only (delete `DescribeEvidenceForUser` — facts are shown once, in the diff header; N3 brings real facts).
- every `narrative.Subject` / `item.Narrative.Subject` → `….Titles.Running`.

Step 7 — `TalvoraDesktopCodexWorklog.cs` and `TalvoraDesktopClaudeWorklog.cs`: narratives become `new OperationNarrative(OperationCategory.GenericExecution, WorklogCopy.Codex)` / `WorklogCopy.Claude`. Progress messages exactly:
- Codex: `$"{commands} komut, {mcpCalls} araç çağrısı."` (plus the existing redacted activity line on a new line when present).
- Claude: `$"{toolCalls} geliştirme adımı."`
End titles: completed → `Titles.Succeeded`; failed/timed_out → `Titles.Failed`; cancelled → `WorklogCopy.CancelledTitle`. End messages exactly:
- completed: `WorklogCopy.SucceededFallback`
- cancelled: `"Görev isteğiniz üzerine durduruldu."`
- timed_out: `"Görev ayrılan sürede bitmedi; yapılan değişiklikler tamamlanmış sayılmıyor."`
- failed: `"Görev tamamlanamadı; yapılan değişiklikler tamamlanmış sayılmıyor."`

Step 8 — tests in `tests/Talvora.Notification.Regression/Program.cs` (behaviour, using the existing delivery-override harness):
1. success of `talvora_apply_patch` → terminal frame Title `Kod değişikliği uygulandı`, Message `Adım başarıyla tamamlandı.`;
2. structured failure `{ success:false, error:{ code:"EXPECTED_REVISION_MISMATCH" } }` → Title `Kod değişikliği uygulanamadı`, Message starts with `Dosya bu arada değişmişti`;
3. `talvora_search_text` (success or failure) → **no frame at all**;
4. for every frame produced in tests 1–2: Message does not contain `Son yaptıklarım`, `Amaç:`, `Geçen süre`, `Sonuç:` or any tool name (`apply_patch`, `talvora_`);
5. Codex job end `timed_out` → Title `Codex görevi tamamlanamadı` and the exact timed-out message;
6. ASCII-Turkish guard: no frame text matches `gelistir|dogrul|cagri|gecen sure|tamamladi` (case-insensitive).

No PNG for N1 (P-11).

### N2 — Protocol v4, sessions and actor/project context

> **Amended by P-11 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** `src/Talvora.Shared/DesktopProgressProtocol.cs` (v4 records §4), `src/Talvora/TalvoraDesktopWorklogState.cs` (session model §4.1), `Program.cs` (pass surface path / client info into the filter), `TalvoraDesktopCodexWorklog.cs`, `TalvoraDesktopClaudeWorklog.cs`, `src/Talvora.Tray/DesktopProgressPipeListener.cs`, `DesktopProgressPresentationState.cs`, `DesktopProgressNotificationPresenter.cs` (key by `SessionId`, reuse window), regression tests.
**Do:** §4, §4.1, §5.2 (`ExpectedNextUpdateSeconds`), §5.1 lifetimes; bounds/validation for every new field (same style as v3 limits); frame size budget unchanged.
**Don't:** keep a v3 reader; let delivery block tool calls.
**Acceptance:** sequential calls in one project produce one window instance with a growing timeline (assert same window object); a different project creates a new session; Claude job with 25 s heartbeat never shows "Yanıt bekleniyor"; 3-minute success lifetime.

### N3 — Real facts from results

> **Amended by P-11 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** `src/Talvora/SourceEditing/SourceEditModels.cs` (+ `AddedLines`/`RemovedLines` per receipt), `SourceEditCommitter.cs` (compute from committed before/after text), `TalvoraDesktopProgressEvidence.cs`, build/test result summarizers (reuse `QualityTools.Diagnostics`, `ConfigFormatTools.TestReports`), git commit/push result parsing, `tests/Talvora.SourceEdit.Regression` (receipt counts), notification regression.
**Do:** §4.2 table; fix W7 and W8 (true totals, unified-diff file detection from `+++ b/…`, preview-limit text from the constant).
**Acceptance:** rejected patch → no counts; unified diff of 2 files → "2 dosya"; build with 3 errors → "Derleme başarısız · 3 hata" + file names; test failure lists up to 3 failing names; commit card shows the real short hash.

### N3b — Code terminal: real, complete, persistent (protect the favourite feature)

> **Amended by P-11 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** new `src/Talvora/WorklogDiff/TextDiffBuilder.cs`, `TalvoraDesktopProgressEvidence.cs`, source-edit committer (expose before/after text or the diff to the notifier without re-reading), mutator tools in `DeveloperTools.FileMutation.cs`, `ConfigFormatTools.*`, `StructuredConfigTools.*`, `ConfigAssetTools.Json.cs`, `FileTools.cs` (workspace writes only), `TalvoraDesktopWorklogState.cs` (session keeps latest code change), `DesktopProgressProtocol.cs` (send the preview once per change: `CodeChangeId` + preview; heartbeats carry only the id), notification regression + source-edit regression.
**Do:** fix C1–C4 and C7 per §3.1, §5.4.1 (Claude part here; Codex part in N4), §5.5. Rejected/failed edits show no diff. The latest code change survives later non-code steps for the whole session.
**Don't:** remove or shrink the vertical expansion; truncate silently; show diffs for files outside recognized workspaces (privacy) — those show "1 dosya değişti" only.
**Acceptance (behaviour tests):** (1) patch → build → test sequence: the build and test frames still carry the same `CodeChangeId` and the tray keeps the pane; (2) stale-revision patch → no preview, muted "uygulanmadı" line; (3) `structural_edit` and `semantic_edit` rename produce real diffs from results; (4) `replace_text`/`json_set` inside a workspace produce diffs; (5) unified-diff patch with 2 files → 2 file headers, counts equal to the applied change; removed `-- comment` line counted; (6) heartbeat frames ≤ 4 KB while a 300 KB preview is active.

### N4 — Agent jobs tell their story

> **Amended by P-11 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** `TalvoraDesktopCodexWorklog.cs`, `TalvoraDesktopClaudeWorklog.cs`, `src/Talvora.Shared/CodexCliEventSummary.cs` (activity classification), `ClaudeCliStreamingProgress.cs`, regression tests.
**Do:** §5.4 and §5.4.1 (Codex code terminal); fix W10 (diacritics) and W13; completion totals and changed files; summary excerpt in evidence only.
**Acceptance:** recorded JSONL fixtures (sanitised) → expected activity lines, never a raw command; completion card shows totals; texts pass a diacritics check (test: no "gelistir|dogrul|cagri|gecen sure" ASCII forms).

### N5 — Visual redesign of the card + preview harness

> **Amended by P-11 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files / Do / Acceptance:** superseded by "#### Exact implementation — Phosphor card" at the end of this card (Claude, 2026-10-20). The earlier text below is kept only as history; follow the Exact implementation block.

~~`src/Talvora.Tray/DesktopProgressNotificationWindow*.cs` (move to XAML `Views/WorklogCard.xaml` + small view model), `DesktopProgressVisualTheme.cs` (map to Gece tokens), new `--worklog-preview` command in `Program.cs`, `ControlCenterSmoke.cs` (scenario renders).~~
**Do:** §3, §3.1 (terminal look, reveal, height rules — fixes C5, C6), §7; fix W5, W17; content-driven height up to 60 % work area; same window reused per session; keep drag/pin/resize/mixed-DPI behaviour and the manual-dismiss semantics. The vertical code expansion must keep working at every step of this card — it is the user's favourite feature.
**Acceptance:** preview PNGs for: running edit, success with timeline, **2-line change (small pane)**, **3-file 300-line change (tabs, scroll, colours, gutter, wrapped continuation)**, build failure after a code change (pane still open), inspection-failure row, Codex job running/completed, stale; contrast test on token pairs; mixed-DPI placement regression unchanged; reduced-motion render identical except animation.

#### Exact implementation — "Phosphor" card (copy verified files, do not reinterpret)

All design and code for this card already exists and is **compiled and rendered by Claude**: `docs/codex/reference/worklog-phosphor/` (`src/` = 8 files, `previews/` = target PNGs, `SHA256SUMS.txt`). Your job is to place these files, remove the old card code, wire two small call sites and prove the renders are identical. You write almost no new code.

Prerequisite: N2 is `[x]` (protocol v4 types `DesktopWorklogFrame`, `WorklogSessionHeader`, `WorklogStep`, `WorklogOutcome`, `WorklogTimelineEntry`, `WorklogTotals`, `WorklogStepState`, `WorklogCheckState` exist in `Talvora.Shared` with the names of DESIGN-WORKLOG §4). If N2 used different names, adapt **only the names** inside the copied files (rule 5a) and list each renamed identifier in the Log.

Step 1 — copy verbatim (AGENTS.md rule 5 "Reference copies": `Copy-Item` in Codex's shell, `talvora_apply_patch` Add File in ChatGPT; newline differences do not matter):

| From `docs/codex/reference/worklog-phosphor/src/` | To |
|---|---|
| `WorklogTheme.xaml` | `src/Talvora.Tray/Worklog/WorklogTheme.xaml` |
| `WorklogCardView.xaml` | `src/Talvora.Tray/Worklog/WorklogCardView.xaml` |
| `WorklogCardView.xaml.cs` | `src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs` |
| `WorklogCardViewModel.cs` | `src/Talvora.Tray/Worklog/WorklogCardViewModel.cs` |
| `UnifiedDiffViewParser.cs` | `src/Talvora.Tray/Worklog/UnifiedDiffViewParser.cs` |
| `WorklogCardMapper.cs` | `src/Talvora.Tray/Worklog/WorklogCardMapper.cs` |
| `WorklogPreviewScenarios.cs` | `src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs` |
| `DesktopProgressNotificationWindow.cs` | `src/Talvora.Tray/DesktopProgressNotificationWindow.cs` (replaces the existing file completely) |

Then verify (must print `IDENTICAL` eight times; line endings are normalised so CRLF/LF does not matter):

```powershell
$ref = 'docs/codex/reference/worklog-phosphor/src'
$map = @{
  'WorklogTheme.xaml' = 'src/Talvora.Tray/Worklog/WorklogTheme.xaml'
  'WorklogCardView.xaml' = 'src/Talvora.Tray/Worklog/WorklogCardView.xaml'
  'WorklogCardView.xaml.cs' = 'src/Talvora.Tray/Worklog/WorklogCardView.xaml.cs'
  'WorklogCardViewModel.cs' = 'src/Talvora.Tray/Worklog/WorklogCardViewModel.cs'
  'UnifiedDiffViewParser.cs' = 'src/Talvora.Tray/Worklog/UnifiedDiffViewParser.cs'
  'WorklogCardMapper.cs' = 'src/Talvora.Tray/Worklog/WorklogCardMapper.cs'
  'WorklogPreviewScenarios.cs' = 'src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs'
  'DesktopProgressNotificationWindow.cs' = 'src/Talvora.Tray/DesktopProgressNotificationWindow.cs'
}
foreach ($k in $map.Keys) {
  $a = [IO.File]::ReadAllText((Join-Path $ref $k)).Replace("`r`n", "`n")
  $b = [IO.File]::ReadAllText($map[$k]).Replace("`r`n", "`n")
  if ($a -ceq $b) { "IDENTICAL $k" } else { "DIFFERENT $k" }
}
```

A `DIFFERENT` line is allowed only for an N2 name adaptation listed in the Log.

Step 2 — delete (they are fully replaced): `src/Talvora.Tray/DesktopProgressNotificationWindow.Visual.cs`, `src/Talvora.Tray/DesktopProgressVisualTheme.cs`. In `src/Talvora.Tray/ControlCenterTheme.xaml` delete every `Talvora.Notification.*` `Color` and `SolidColorBrush` entry and the comment above them (nothing else uses them after this card; `git grep "Talvora.Notification\."` must return nothing in `src/`).

Step 3 — `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs` (only these changes):
- every `window.OperationId` / `message.OperationId` used as the window key → the session id the N2 presenter already uses (`frame.SessionId`);
- replace the whole `if (!string.IsNullOrWhiteSpace(message.Evidence?.CodePreview)) { … }` block in `PublishCore` with exactly:

```csharp
if (_hasPreferredSize)
{
    window.ApplyPreferredSize(_preferredWidthDip, _preferredHeightDip);
}
```

- delete the now unused constant `DefaultCodeNotificationHeightDip`.

Step 4 — the "ayrıntılar ▸" button stays hidden (`HasDetails` defaults to `false`). N6 sets it. Do not set it in N5.

Step 5 — preview command in `src/Talvora.Tray/Program.cs`: insert as the **first** statement of `Main` (before the `--reconnect` check):

```csharp
if (args.Length >= 2 &&
    string.Equals(args[0], "--worklog-preview", StringComparison.OrdinalIgnoreCase))
{
    var exitCode = 1;
    var renderThread = new Thread(() =>
    {
        try
        {
            _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            foreach (var path in Talvora.Tray.Worklog.WorklogPreviewScenarios.RenderAll(args[1]))
            {
                Console.WriteLine(path);
            }

            exitCode = 0;
        }
        catch (Exception ex)
        {
            TrayLog.Write("Worklog preview render failed", ex);
        }
    });
    renderThread.SetApartmentState(ApartmentState.STA);
    renderThread.Start();
    renderThread.Join();
    return exitCode;
}
```

Step 6 — `tests/Talvora.Notification.Regression/Program.cs`:
- add a helper class at the end of the file, exactly:

```csharp
internal static class WorklogTestFrames
{
    internal static DesktopWorklogFrame Running(string sessionId, string? codePreview, long sequence = 1) =>
        new(
            SessionId: sessionId,
            Sequence: sequence,
            TimestampUtc: DateTimeOffset.UtcNow,
            Header: new WorklogSessionHeader("Talvora-MCP", "Codex"),
            Current: new WorklogStep(
                WorklogStepCategory.SourceEdit,
                WorklogStepState.Running,
                "Kod değişikliği uygulanıyor",
                "1 dosya · +1000 −0 · big.cs",
                null,
                1),
            Timeline: [],
            Totals: new WorklogTotals(1, 1, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
            Evidence: codePreview is null ? null : new DesktopProgressEvidence(CodePreview: codePreview),
            ExpectedNextUpdateSeconds: 15);
}
```

(use the real enum member names from N2 for `WorklogStepCategory.SourceEdit` / `WorklogCheckState.Unknown`.)
- in the window-sizing regression thread, replace the block from `var window = new DesktopProgressNotificationWindow();` down to (and including) the last `throw` of the `default-size-code` checks with exactly:

```csharp
var window = new DesktopProgressNotificationWindow();
try
{
    window.ApplyPreferredSize(560, 700);
    var code = "+++ b/big.cs\n@@ -1,1 +1,1000 @@\n" +
        string.Join('\n', Enumerable.Range(1, 1000).Select(index => $"+changed{index}();"));
    window.Update(WorklogTestFrames.Running("resize-regression", code));
    window.Show();
    window.UpdateLayout();
    if (Math.Abs(window.ActualWidth - 560) > 0.75)
    {
        throw new InvalidOperationException("The card must keep the user's width.");
    }

    var heightWithCode = window.ActualHeight;
    window.Update(WorklogTestFrames.Running("resize-regression", codePreview: null, sequence: 2));
    window.UpdateLayout();
    if (window.ActualHeight >= heightWithCode || Math.Abs(window.ActualWidth - 560) > 0.75)
    {
        throw new InvalidOperationException("A frame without code must shrink the card and keep its width.");
    }

    window.Update(WorklogTestFrames.Running("resize-regression", code, sequence: 3));
    window.UpdateLayout();
    if (Math.Abs(window.ActualHeight - heightWithCode) > 1)
    {
        throw new InvalidOperationException("Returning code must re-open the terminal to the same height.");
    }
}
finally
{
    window.Close();
}
```

(The removed presenter assertions encoded the old fixed 720 DIP manual sizing, which this design replaces; presenter placement/pin tests elsewhere in the file stay unchanged.)

Step 7 — prove it:
1. build Tray + notification regression; run the regression;
2. `Talvora.Tray.exe --worklog-preview <tmp>` (from the Release build output, as the signed-in user) and compare each PNG with `docs/codex/reference/worklog-phosphor/previews/` byte-for-byte (`Get-FileHash`). Same machine and fonts → identical. If any differs, attach both PNGs to the Gitea issue and stop with a proposal — do not "fix" the design;
3. deploy; trigger one real `talvora_apply_patch` on a scratch file and capture the live card (crop to the card) into the Gitea issue next to `previews/09-live-window-150pct.png`.

**Acceptance:** eight `IDENTICAL` lines (or Log-listed N2 name adaptations only); old files deleted; preview PNGs byte-identical to the references; regression green; live card captured. User step U6 follows.

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
