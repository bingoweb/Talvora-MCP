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
- **Security stays proportionate** (user, 2026-11-14): Talvora is a local MCP its owner runs on their own computer by their own choice. A card adds a security or privacy guard only against a concrete, likely accident and never where it makes an ordinary tool task harder; no extra refusals, confirmations or restrictions "just in case". A guard that breaks a normal task is a bug. The agent never adds a guard a card does not name.
- Push as STATUS → Fixed facts → "Push" says.

---

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-14 (60) · range `audit/2026-11-14-59..49e101d` (D87 by ChatGPT) · D88 for the agent

### D88 — File steps say what happened: deleted, copied, moved, written, appended

**Why:** every `talvora_delete`, `talvora_copy`, `talvora_move`, `talvora_write_text` and `talvora_append_text` step reads "Dosyalar düzenlendi · <name>" (`WorklogCopy.FileMutation`, chosen in the file branch of `TalvoraDesktopProgressNarrative.cs`), whatever the tool did. On 2026-11-14 the D87 live check deleted a broken junction and Çalışma günlüğü said "Dosyalar düzenlendi · broken". The worklog must say precisely what was done (user rule 2026-11-13).

**Files:** `src/Talvora/WorklogCopy.cs` (five new triples), `src/Talvora/TalvoraDesktopProgressNarrative.cs` (the file branch), `tests/Talvora.Notification.Regression/` (new `Program.D88.cs` and its registration in `Program.cs`, the way D83 registers), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL5, WL6, N9Subjects, D79, D82, D83), `ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first: a D88 test with every row below (running, succeeded and failed titles, each with its subject), plus one other tool of the same branch that keeps "Dosyalar düzenlendi · <name>".

   | Tool | Running | Succeeded | Failed |
   |---|---|---|---|
   | `talvora_delete` | Siliniyor | Silindi | Silinemedi |
   | `talvora_copy` | Kopyalanıyor | Kopyalandı | Kopyalanamadı |
   | `talvora_move` | Taşınıyor | Taşındı | Taşınamadı |
   | `talvora_write_text` | Dosya yazılıyor | Dosya yazıldı | Dosya yazılamadı |
   | `talvora_append_text` | Dosyaya ekleniyor | Dosyaya eklendi | Dosyaya eklenemedi |

   The subject stays as today (the name of `path`, else `source`, else `destination`), added with `WorklogSubject.WithSubject` (" · <name>"; no name → no " · "). The category stays `FileMutation`.
2. Add the five triples to `WorklogCopy` and pick them by tool name in the file branch; every other tool in that branch keeps `WorklogCopy.FileMutation`.

**Don't:** change any other title, `TalvoraToolTitles` (the MCP approval titles), a category, a subject, the tray, or any tool's behaviour.

**Context7:** `Context7: gerekmiyor (Talvora's own title table)` → `noLookup`.

**Acceptance:** red first; the complete Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Claude checks a real delete step's title at the next audit (`[~] visual check pending (Claude)`).
