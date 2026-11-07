# Talvora desktop worklog — Review and redesign spec "Seyir Defteri"

> Author: Claude, 2026-10-18. Cards N1–N10 built this spec and are finished; audit 40 removed them and the pre-redesign evidence (old §1 and §9; git keeps both). New worklog cards are written in `docs/codex/ROADMAP.md`.
> Language: English spec; every visible string is ordinary Turkish (copy deck §6 is authoritative).
> AGENTS.md "Mandatory Windows desktop progress reporting" stays fully binding (one live card, nonmodal, full text, draggable/pin, dark terminal identity allowed, no fabricated telemetry, plain Turkish, evidence separated and redacted, best-effort delivery). This spec makes the card **informative**, removes duplication and inconsistency, and gives it a polished look. Where this spec and AGENTS.md disagree, AGENTS.md wins — report the conflict as an open question.

---

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

### 6.1 Every step names what it works on (Claude, 2026-10-23 — user request "hangi işlem? ne tamamlandı?")

The user's complaint, verbatim in spirit: *"İşlem yürütülüyor… hangi işlem? Düzeltme yapıldı… ne düzeltildi? İşlem tamamlandı… ne tamamlandı?"* A title that only names a category is not information. From N9 on, **every title carries its subject** — taken only from the call's real arguments, never a command line:

| Step | Title (running → succeeded) | Subject source |
|---|---|---|
| quality gate (`Invoke-QualityGate.ps1`, any runner) | Kalite kontrolü çalışıyor · tam → Kalite kontrolü geçti · tam | script name; `-Scope Full` → "tam", Fast/none → "hızlı" |
| installer build / deploy / Dependabot / CI / context scripts | Kurulum paketi hazırlanıyor, Yeni sürüm kuruluyor, Paket güncellemesi hazırlanıyor, GitHub denetimi okunuyor, Görev bağlamı hazırlanıyor (and their done/failed forms) | script name (`WorklogSubject.KnownScripts`) |
| any other script | `Build-Foo.ps1 çalışıyor → bitti → hata ile bitti` | script file name |
| `dotnet build/test/restore/publish` (tool or runner) | Derleniyor · Talvora.slnx → Derleme başarılı · Talvora.slnx | target/project file name |
| npm/pnpm/yarn/bun/npx | `npm build çalışıyor → bitti` | program + script name |
| node/python + file | `server.js çalışıyor` | file name |
| other programs | `ffmpeg.exe çalışıyor` | executable file name |
| source edit | Kod değişikliği uygulandı · GitTools.cs ve 1 dosya | first two file names + count |
| git push / pull / fetch / other | Uzak depoya gönderildi · origin/main, Git işlemi tamamlandı · pull origin/main | remote/branch or subcommand |
| service / package / file / network | … · `Talvora Dev`, … · `ripgrep`, … · `app.json`, … · `api.github.com` | service, package, file name, host |
| anything else | **the tool's approved Turkish title** (`TalvoraToolTitles`), e.g. "Ortam değişkenini ayarla · tamamlandı" | `src/Talvora.Shared/TalvoraToolTitles.cs` |

"İşlem yürütülüyor / tamamlandı" may only appear for a tool that has no approved title — a test fails if any manifest tool lacks one. The quality-gate summary line `N kontrol, M hata` becomes the fact `N kontrol · M hata`.

`TalvoraToolTitles.cs` is the approved title table for **all** tools (Claude decided it; card Q1 later applies the same table to `tools/list` instead of generating a proposal table).

### 6.2 The answer rows: ne · nerede · fark · sonuç

Under the title the card answers the user's four questions in fixed rows (labels faint, 58 DIP column; values real):

```
# görev  D12 — git pull oturum kullanıcısıyla çalışsın        ← talvora_worklog_note (agent, once per task)
> Kod değişikliği uygulandı · GitTools.cs ve 1 dosya
  ne      git pull artık oturum kullanıcısının SSH anahtarını kullanıyor   ← apply_patch "purpose" (agent)
  nerede  GitTools.cs › ShouldUseInteractiveUserForRemoteGitAsync        ← receipts + hunk headers (real)
          GitSafeDirectoryRegression.ps1
  fark    +5 −1 · 2 dosya                                                 ← receipts (real)
```

| Row | Source | Shown when |
|---|---|---|
| `# görev` | `talvora_worklog_note {workspaceRoot, text}` — Turkish, one line, ≤ 140 chars, redacted; per session until replaced or the session restarts | a note exists |
| `ne` | optional `purpose` argument of `talvora_apply_patch` — the agent's own words, one line, ≤ 140 chars, redacted | the step has a purpose |
| `nerede` | per committed file: file name › declarations its hunks touched (`DeclarationLine.PlaceName` of the hunk-header context: type, function/method, PowerShell function, Markdown heading). Max 3 rows + "+N dosya daha" | succeeded source edit with real receipt counts |
| `fark` | `+added −removed · N dosya` summed from the engine receipts | same as `nerede` |
| `sonuç` | the existing real fact line (build/test/git/gate/service/package/Remotion) | no `nerede` rows |

Agent-written text (`görev`, `ne`) is labelled as such by its position, is never invented by Talvora, and is shown verbatim after redaction. If an agent does not send it, the rows are simply absent. AGENTS.md now requires both (CONTINUE PROTOCOL step 5 and the mechanics table).

### 6.3 "Eskisi ile farkı ne?" — word-level highlight in the code terminal

The code terminal (§3.1) already shows old lines red and new lines green. From N5 on (reference files updated by Claude) it also marks **exactly what changed inside a line**:

- A block of removed lines followed by a block of added lines is paired line by line in order (max 8 pairs). For each pair the span between the common prefix and suffix, widened to whole words, gets a stronger tint (`Wl.AddedEmphasisBg` / `Wl.RemovedEmphasisBg`, 30 %) and bold text. `const limit = 5;` → `const limit = 50;` marks `5` and `50`. Pairs sharing less than half of the longer line are rewrites and stay unmarked.
- Display only: the shared indentation of each hunk is removed so nested code does not wrap on its leading spaces; hunk headers are compacted to `@@ 790 · <context>` (one line, ≤ 52 chars). "kopyala" still copies the real preview.
- High contrast: tints off, the changed span stays **bold**.
- The title's subject (after the first " · ") is drawn in regular weight, muted, so the operation reads first.

Target renders: `docs/codex/reference/worklog-phosphor/previews/01…08` (regenerated 2026-10-23); `06-small-change.png` is the showcase of §6.1–6.3.

**Polish (audit 2026-10-23 (12), card D18):** finished cards keep a quiet 2 px line in their state colour where the live line runs while working (55 %, off in high contrast); a one-step session does not repeat its title as the only timeline row; file tabs appear only for two or more files; a `@@ FILE x` + `+++ b/x` pair is one file; hunk headers stay on one line (≤ 48 chars); the diff header shows the time of the code change, not of the latest frame; "nerede" names the declaration above the first changed line. Showcase of the live case: `previews/09-one-step-receipt.png`. Until D18 runs, the updated files and previews live in `docs/codex/reference/worklog-phosphor-next/`; D18 promotes them.

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

### 7b. "tmux" redesign — UI12 (Claude builds it as `[C]`; user decision 2026-11-01)

The card becomes a **tmux pane attached to the agent's session**: a shell prompt, the command that is running, the shell history
of the session with real step times, a split pane for the diff and a tmux status bar. It replaces the Phosphor layout of §7; colours,
fonts and the high-contrast rules of §7 stay. **Every number and bar is measured** (AGENTS.md desktop rules): sources are listed
in the table, and anything without a source is not drawn.

| Element | Look | Source of every value (real only) |
|---|---|---|
| Prompt row | `talvora@MSI:~/Talvora-MCP · chatgpt·dev` left; state word right (no brackets: ÇALIŞIYOR, TAMAM, HATA, DURDU, BEKLİYOR) | `Header.ProjectName`, machine name, `Header.ActorName` |
| Task line | `# görev …` (unchanged) | `Header.TaskNote` |
| Command line | `$ ` + title, typed in at 18 ms per character (max 360 ms) when the title changes; block cursor blinks only while running; finished: `$ title   41 sn sürdü` (no-break spaces keep it on one line) | `Current.Title`; duration = the folded history row's `DurationSeconds` (WL1, stopwatch of that call); the row itself is not repeated in the history |
| Answers | `ne / nerede / fark / sonuç` (content unchanged) | §6.2 |
| History | shell history rows `✓ 14:02  label ········ 0,9 sn ▮▮▮▮`; mark coloured by state; dotted leader; real duration right-aligned; bar after it | `Timeline[i].TimestampUtc`, `.State`, `.Label`, `.DurationSeconds` (WL1). No `DurationSeconds` → no number and no bar |
| Signal bars | width = `round(56 × log10(1 + 10·s) / log10(1 + 6000))` DIP, min 2 DIP, max 56 DIP (fixed log scale: 0.1 s ≈ 4 DIP, 1 s ≈ 15 DIP, 10 s ≈ 30 DIP, 10 min = 56 DIP), height 6 DIP, colour = row state at 70 % | `DurationSeconds` only |
| Code pane | a tmux split: one box-drawing border line `─ diff ── kiosk-routes.js +3 −1 ── 14:02:11 ─` then the existing diff rows; hunk headers show the **whole** declaration and wrap (no `…` cut in the view) | `Evidence.CodePreview`, parser |
| Status bar | 22 DIP tmux bar at the bottom, edge to edge: left `[talvora] 0:<project>*` (the actor is already in the prompt row), right `mm:ss · n adım · k dosya` (zero counts omitted, no glyph) and `ayrıntılar ▸` when evidence exists; background by state (running `#12301F`, success `#0F3A22`, failure `#3A1018`, waiting/cancelled `#3A2A0E`), text `#D8FFE7` | `Totals.SessionSeconds`, `Totals.Steps`, `Totals.FilesChanged`. While **running** the clock advances from `SessionSeconds + (now − frame.TimestampUtc)` (wall clock since the measured value); in any other state it is frozen at the frame value |

Motion vocabulary (all off when reduced motion or high contrast is active, or when the card is hidden):
- **enter**: the existing 180 ms fade with a 12 px rise;
- **type**: a new command line types in;
- **row-in**: a new history row slides down 8 px and fades in over 220 ms (cubic ease-out);
- **ok**: on the transition to success, a lighter band crosses the status bar once, left to right, in 420 ms;
- **fail**: on the transition to failure, the card jitters 2 px, twice, in 120 ms, and the status bar flashes once;
- **wait**: three status-bar dots pulse in opacity every 1.2 s, only while waiting.

The live gradient line and the braille spinner are retired; the status bar and the cursor carry "running".

**UI12b additions (user, 2026-11-01: the measurable parts of concepts B and C):**
- **Oturum sinyali** (from B; **replaced by UI12c below**): a 22 DIP strip under `── oturum sinyali`, above the status bar. The x axis is the session's real time (start = frame time − `Totals.SessionSeconds`, end = frame time). Each step with a measured `DurationSeconds` is a plateau from `timestamp − duration` to `timestamp`, at least 2 DIP wide. Its height is `3 + (history bar width / 56) × 17` DIP (the same fixed log scale), drawn as a 28 % body with a 2 px crest in the step's state colour. Steps without a measurement are not drawn; with no measured step the strip is hidden. The running step is not drawn until it has a measured end. Motion: only a newly arrived peak grows from the baseline (260 ms, cubic ease-out).
- **ANSI frame** (from C): the code pane gets a double frame (outer 1 px `BorderStrong` with 2 px gap, inner 1 px `BorderStrong`). Its header reads `═╡ diff ╞` + a double rule + `╡ HH:mm:ss ╞═` + `kopyala`. Decoration only; it carries no data. C's percentage bar is **not** built: tool calls report no real progress.

**D24 (Claude's own audit of UI12/UI12b, 2026-11-01):**
- Timeline and Signal changes of one frame are handled as one refresh after layout, so row-in and peak growth really run. They are verified in a live window: both transforms animate.
- When older steps fell out of the 30-step frame window, the strip's axis starts where the oldest kept step began, and the caption reads `── son N adım`, so empty space never claims idle time that really had work.
- Faint 1×3 DIP ticks on the baseline mark real time: every minute up to 10 min, every 5 min up to 1 h, then every 15 min.
- A hidden or unloaded card finishes a half-typed title.

**UI12c "zaman cetveli" (user, 2026-11-01: "realtime oturum sinyali görseli çok kötü, anlaşılmaz"; concept A chosen over a
time-share bar and step columns).** Diagnosis of the UI12b strip: it drew the same duration twice on two scales (linear width,
log height), printed no number, no clock and no legend, so no bar could be tied to a history row and the empty space did not
read as time. The ruler replaces it, still between the code pane and the status bar:

| Element | Look | Source (real only) |
|---|---|---|
| Caption | `── oturum ` + rule (`── son N adım ` when older steps fell out of the 30-step window, as in D24) | `Totals.Steps`, `Timeline.Count` |
| Axis | one **linear** real-time scale; start = frame time − `Totals.SessionSeconds` (or the oldest kept step's start, D24), end = frame time | frame timestamp, `SessionSeconds`, WL1 durations |
| Idle line | dotted 1 DIP line (dash 1/3, `Wl.Faint`) through the middle of the 14 DIP track: time in which no tool of this session ran | the axis itself |
| Step block | solid 14 DIP block from `timestamp − DurationSeconds` to `timestamp`, state colour at 85 %, radius 1, min 2 DIP; prints `label · duration`, else `duration`, in `Wl.Terminal` 11 px **only when the whole text fits** (never cut) | `Timeline[i]` with `DurationSeconds` (WL1) |
| Running block | cyan block from `frame time − Current.ElapsedSeconds` to frame time | `Current.ElapsedSeconds` (stopwatch of the running call) |
| Failure caret | 8×5 DIP red triangle above every failed block, so a 2 DIP failure stays findable | step state |
| Clock | 1 DIP axis line; 1×4 ticks on whole local minutes (axis ≤ 10 min), 5 min (≤ 1 h) or 15 min; labels `:mm` for minute ticks, `HH:mm` otherwise; `HH:mm` start at the left edge and end flush right; a tick label that would come within 8 DIP of another, or repeats an edge minute, is skipped, never shortened | frame timestamp, local time zone |
| Legend | `■ iş 5:39 · 4 adım` (green square) · `▼ 1 hata` (only with failures) · `┈ ara 3:25` (only when **every** finished step on the axis was measured) — 11 px, `Wl.Muted` text | work = **union** of the measured intervals (parallel steps count once), adım = drawn blocks, ara = axis − work |

Steps without a measurement are not drawn; with no measured step the ruler is hidden. Motion: only a newly arrived block draws
in from its left edge (260 ms, cubic ease-out); off under reduced motion, high contrast or when hidden. The preview harness
builds each fixture as a protocol frame and draws it with the mapper's own functions.

**UI12d (audit 36 polish):** the ruler legend counts finished steps like the status bar and names a running step separately ("2 adım · 1 sürüyor"); all measured seconds round to the nearest value ("14 sn" for 13.7 s, "1:00" for 59.6 s); when both ends of the ruler fall in the same minute the end labels show seconds ("22:12:01 … 22:12:16").

Built by Claude on 2026-11-01 (WL1 + UI12 + UI12b + D24 + UI12c + UI12d). The preview harness (`Talvora.Tray.exe --worklog-preview <dir>`) renders every state; fixture durations there are labelled design fixtures, while the live card reads only WL1 measurements.

## 8. Control Center integration

- New rail item **"Çalışma günlüğü"** (between "Hizmetler" and "Bağlantı zinciri" in `DESIGN-CONTROL-CENTER.md` §4) listing sessions (project, actor, start/end, steps, files changed, outcome) for the last 7 days, newest first; selecting a session shows its full timeline and evidence (redacted). Search and filters: project, actor, outcome.
- Storage: the tray appends each **terminal** session snapshot (not every heartbeat) to `%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl`, bounded to 7 days / 2 000 sessions / 20 MB with rotation; evidence code previews are truncated to 8 KB in history.
- Settings → "Bildirimler": show code preview in the card by default (on/off), failure-only mode (cards only for failures and agent jobs), quiet hours (no card, history only). Defaults: on, off, off.
