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

### Audit 2026-11-08 (48, deep) · range `audit/2026-11-08-47..2f86766` plus P-54 · result: P-54 decided, PK1 and PK2 amended

D64 follows its card. P-54 was Claude's miss (PK1 did not name the lock files); PK1 and PK2 now carry the lock step, and PK2 the MSBuildLocator check that 18.9.x's new `Microsoft.NET.StringTools` dependency may need.

### Audit 2026-11-08 (42–43) · cards still open

D46 is done (Claude checks its live wording at the next audit). D47, D48 and D49 are Claude's `[C]` items since audit 46 (user decision: Claude built and ran their literal blocks at audit 42, so the agent does not type them again); Claude ships them with WL3 after the agent's queue and re-checks each against the code of that day first. The agent skips them.

### D48 — A vault note belongs to the vault's owner (Claude, `[C]`)

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

### D49 — Plain Turkish service names, also for older registries (Claude, `[C]`)

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

### D47 — Plain Turkish for the remaining Control Center texts (Claude, `[C]`)

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
9. `WorklogHistoryView.xaml`: with search text and no matching session, the empty line reads "Aramayla eşleşen çalışma yok." instead of "Henüz kayıtlı çalışma yok." (README screen pass 2026-11-08). After WL3, Claude takes the README screenshots again before the first release.
10. `WorklogHistoryView.xaml`, list card (audit 46 screen pass): the files part (" dosya değişti" after the bound count) collapses when `Totals.FilesChanged` is 0, the way `WorklogCardMapper.cs` leaves zero parts out of the desktop card, so a session without file changes reads "4 adım" instead of "4 adım · 0 dosya değişti".
11. `WorklogHistoryView.xaml`, list card (audit 47, user decision 2026-11-08 "Görev notu"): when `Header.TaskNote` is set it is the card's headline (`Tv.Type.Body`, one line, ellipsis) and `Current.Title` moves under it as a muted caption "Son adım: …"; a session without a task note looks as today. Since D61 most agent sessions end with the deploy step, so three of the last five read "Kurulum devredildi · doğrulama bekleniyor" in the list.

## Releases and the public page (user request 2026-11-08)

User: "github'da bulunan talvora mcp readme'si yani github arayüzünü tamamen insan yazmış gibi en başından ele alarak tamamen en başından tekrar tasarla … ekran görüntülerini yenile, ayrıca artık stabil release'ler de oluşturmaya başlayalım. ne zaman release güncellemesi yapılacağına tamamen sen karar vereceksin. proje 0.0.x.x şeklinde olmalı ve windows kullanıcılarının rahatlıkla kurabileceği şekilde olmalı." User choices the same day: README in Turkish with a short English summary; the installer stays unsigned and the README explains the SmartScreen step; the installer installs PowerShell 7 itself; the Control Center shows a quiet new-version link once a day. Claude wrote the README (`docs/codex/reference/d57/README.md.txt`) and took the screenshots (`docs/media/kontrol-merkezi-gece.png`, `kontrol-merkezi-gunduz.png`, `calisma-gunlugu.png`, `kod-karti.png`). Releases: only Claude creates the `v0.0.x.x` tag and writes `docs/releases/<version>.md` (AGENTS.md rule 10b); the first one, 0.0.1.0, follows RL5 and the open D-cards.

### RL1 — Talvora's version is 0.0.x.x and comes from the release tag

**Why:** versions are `0.0.<feature>.<fix>` (user decision 2026-11-08). Today `Directory.Build.props` has `<Version>3.0.0-dev</Version>` and `InformationalVersion` = `$(Version)+$(SourceRevisionId)`, so `/healthz` reports `3.0.0-dev+<commit>`, the overview line ends with `3.0.0-dev` (`OverviewViewModel`, `InformationalVersion.Split('+')[0]`) and Ayarlar → Hakkında shows `3.0.0-dev` (`SettingsView.GetUi7AboutMetadata`).

**Files:** `Directory.Build.props`, `scripts/Build-Windows-Installer.ps1`, `src/Talvora.Tray/Views/SettingsView.xaml.cs`, `src/Talvora.Tray/ViewModels/OverviewViewModel.cs`, `tests/ReleaseVersionRegression.ps1` (new; registered with the gate's PowerShell regressions), `tests/Talvora.Notification.Regression/Program.cs`, `tests/Talvora.Notification.Regression/Program.UI7.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `GitSafeDirectoryRegression.ps1`, `InstallerAstGrepMetadataRegression.ps1` and `NativeInstallerSourceRegression.ps1` (`Build-Windows-Installer.ps1`); `ModernizationPolicyRegression.ps1` (`Directory.Build.props`: the lock-file and NuGet-audit lines stay).

**Do:**
1. Red first:
   - `tests/ReleaseVersionRegression.ps1`: `dotnet msbuild src/Talvora.Shared/Talvora.Shared.csproj -getProperty:Version -getProperty:InformationalVersion -p:TalvoraReleaseVersion=0.0.7.3 -p:SourceRevisionId=abc1234` → `0.0.7.3` and `0.0.7.3+abc1234`; without `TalvoraReleaseVersion` → `0.0.0.0` and `0.0.0.0-dev+abc1234`; `dotnet msbuild src/Talvora.Shared/Talvora.Shared.csproj -t:TalvoraCheckReleaseVersion -p:TalvoraReleaseVersion=1.2.3` exits non-zero and prints `0.0.x.x`; prints `RELEASE_VERSION_GREEN`.
   - `Program.UI7.cs`: `GetUi7AboutMetadata("0.0.0.0-dev+1234567890abcdef")` must give `("geliştirme sürümü", "1234567")` and `GetUi7AboutMetadata("0.0.1.0+1234567890abcdef")` must give `("0.0.1.0", "1234567")` (replacing the `3.0.0-dev` expectation); `Program.cs` line 140 expects `0.0.0.0-dev+`.
2. `Directory.Build.props`, replacing the two version lines:
```xml
    <Version Condition="'$(TalvoraReleaseVersion)' != ''">$(TalvoraReleaseVersion)</Version>
    <Version Condition="'$(TalvoraReleaseVersion)' == ''">0.0.0.0</Version>
    <InformationalVersion Condition="'$(TalvoraReleaseVersion)' != ''">$(TalvoraReleaseVersion)+$(SourceRevisionId)</InformationalVersion>
    <InformationalVersion Condition="'$(TalvoraReleaseVersion)' == ''">0.0.0.0-dev+$(SourceRevisionId)</InformationalVersion>
```
   and, after the `PropertyGroup`:
```xml
  <Target Name="TalvoraCheckReleaseVersion" BeforeTargets="BeforeBuild" Condition="'$(TalvoraReleaseVersion)' != ''">
    <Error Condition="!$([System.Text.RegularExpressions.Regex]::IsMatch('$(TalvoraReleaseVersion)', '^0\.0\.\d{1,5}\.\d{1,5}$'))" Text="TalvoraReleaseVersion must be 0.0.x.x; got '$(TalvoraReleaseVersion)'." />
  </Target>
```
3. `Build-Windows-Installer.ps1` gets `[string] $ReleaseVersion = ''`. When it is set it must match `^0\.0\.\d{1,5}\.\d{1,5}$` (otherwise `throw "ReleaseVersion must be 0.0.x.x."`), every `dotnet` publish/build call the script makes gets `-p:TalvoraReleaseVersion=$ReleaseVersion`, the setup file is written as `artifacts/installer/Talvora-Kurulum-$ReleaseVersion.exe` and the manifest JSON gains `"version"`. Without it nothing changes (`Talvora-Setup.exe`, which `Deploy-Windows-Installer.ps1` uses).
4. `GetUi7AboutMetadata`: a version part that starts with `0.0.0.0-dev` becomes `geliştirme sürümü`; everything else stays. `OverviewViewModel`'s status line uses `SettingsView.GetUi7AboutMetadata(informationalVersion).Version` instead of `Split('+')[0]`.

**Don't:** change `/healthz` fields (its `version` keeps the full informational version), any tool, the deploy script, or the smoke's version fixtures (`ControlCenterSmoke.UI4.cs` keeps "3.0.0" as a managed service's sample version).

**Context7:** `/dotnet/docs`, queries "MSBuild getProperty command line" and "assembly InformationalVersion Version property". Vendor sources: `https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties`, `https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy: `/healthz` `version` starts with `0.0.0.0-dev+`, the overview line ends with `· geliştirme sürümü`, Hakkında shows `geliştirme sürümü` and the short commit; `scripts/Build-Windows-Installer.ps1 -ReleaseVersion 0.0.0.1` writes `Talvora-Kurulum-0.0.0.1.exe` whose file version is `0.0.0.1` (never run it; delete it afterwards); the installed smokes; CI.

### RL2 — A fresh Windows gets PowerShell 7 from the installer

**Why:** user decision 2026-11-08 ("Kendisi kursun"). Talvora's user-side tools need PowerShell 7 (`InteractiveUserProcessRunner`: "PowerShell 7 is required for interactive user execution."), Windows 10 and 11 ship only Windows PowerShell 5.1, and the installer never checks.

**Files:** `src/Talvora.Installer/InstallerEngine.PowerShell.cs` (new), `src/Talvora.Installer/InstallerEngine.Flow.cs`, `src/Talvora.Installer/Program.cs`, `src/Talvora.Installer/powershell-pin.json` (new, embedded resource), `src/Talvora.Installer/Talvora.Installer.csproj`, `tests/NativeInstallerSourceRegression.ps1`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `NativeInstallerSourceRegression.ps1` and `InstallerRollbackStateRegression.ps1` (the `InstallerEngine` flow and service files), `CodexCliIntegrationSourceRegression.ps1` (`InstallerEngine.CleanupState.cs`); `QualityGateChangedRegression.ps1` (`Talvora.Installer/Program.cs`).

**Do:**
1. `powershell-pin.json` = `{"version":"<v>","url":"https://github.com/PowerShell/PowerShell/releases/download/v<v>/PowerShell-<v>-win-x64.msi","sha256":"<64 lowercase hex>"}` for the newest stable 7.x on the day of the card (`https://api.github.com/repos/PowerShell/PowerShell/releases/latest`; the SHA-256 from that release's `hashes.sha256` asset). Record version and hash in the Log.
2. In the install flow, before "Yeni Talvora sürümü hazırlanıyor...": when `C:\Program Files\PowerShell\7\pwsh.exe` exists or `pwsh.exe` resolves on the machine PATH, write "PowerShell 7 hazır." to the installer log and go on. Otherwise:
   - progress "PowerShell 7 indiriliyor..." → download to `%TEMP%\Talvora-PowerShell-<v>.msi` (10-minute limit); failure → stop with "PowerShell 7 indirilemedi. İnternet bağlantını kontrol edip kurulumu yeniden başlat.";
   - SHA-256 check; mismatch → delete the file and stop with "İndirilen PowerShell 7 paketi doğrulanamadı; güvenlik için kurulmadı.";
   - progress "PowerShell 7 kuruluyor..." → `msiexec.exe /i "<msi>" /quiet /norestart ADD_PATH=1 REGISTER_MANIFEST=1 USE_MU=1 ENABLE_MU=1`; exit 0 or 3010 → delete the file and go on; anything else → stop with "PowerShell 7 kurulamadı (msiexec çıkış kodu {code}). PowerShell 7'yi https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows adresindeki adımlarla kurup Talvora kurulumunu yeniden başlat."
   Every stop happens before anything of Talvora changes. `--silent` runs the same steps without a window and exits non-zero on a stop.
3. A hidden switch `--verify-powershell-download` downloads and checks the pinned package, deletes it, prints "PowerShell 7 <v> paketi doğrulandı." and exits 0 (it installs nothing).
4. `NativeInstallerSourceRegression.ps1`: the pin parses, its `url` contains its `version`, `sha256` is 64 hex characters; the "already present" decision is taken through an injectable probe (no download when it answers true); a wrong hash is rejected (temporary file).

**Don't:** install anything else (Chocolatey, Git and the .NET SDK stay the user's choice) or touch an existing PowerShell 7.

**Context7:** `/dotnet/docs`, queries "HttpClient download stream to file" and "SHA256 HashData stream". Vendor sources: `https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows`, `https://github.com/PowerShell/PowerShell/releases`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy: the installer log says "PowerShell 7 hazır." on this machine; `Talvora-Setup.exe --verify-powershell-download` prints the verified line; the installed smokes; CI.

### RL3 — Talvora is in Apps & features and can be uninstalled

**Why:** a downloaded installer must also be removable from Windows Settings → Apps. Today nothing registers Talvora there and there is no uninstall (only the developer reset script). The install flow creates: the `Talvora` service, `C:\Program Files\Talvora\Versions\<commit>-<stamp>`, the install user's `HKU\<sid>\Software\Microsoft\Windows\CurrentVersion\Run` value `RunValueName`, the Start menu shortcut `Talvora Kontrol Merkezi.lnk`, the Codex entry in that user's `.codex\config.toml`, and `C:\ProgramData\Talvora` with its protected ACL.

**Files:** `src/Talvora.Installer/InstallerEngine.Uninstall.cs` (new), `src/Talvora.Installer/InstallerEngine.Flow.cs`, `src/Talvora.Installer/Program.cs`, `src/Talvora.Installer/InstallerForm.cs`, `tests/NativeInstallerSourceRegression.ps1`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `NativeInstallerSourceRegression.ps1` and `InstallerRollbackStateRegression.ps1` (the `InstallerEngine` flow and service files), `CodexCliIntegrationSourceRegression.ps1` (`InstallerEngine.CleanupState.cs`); `QualityGateChangedRegression.ps1` (`Talvora.Installer/Program.cs`).

**Do:**
1. After a successful install: copy the running setup file to `C:\Program Files\Talvora\Talvora-Kurulum.exe` (overwrite) and write `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Talvora`: `DisplayName` "Talvora"; `DisplayVersion` (the informational version before `+`); `Publisher` "Taylan Soylu"; `DisplayIcon` "<installed tray exe>,0"; `InstallLocation` "C:\Program Files\Talvora"; `UninstallString` `"C:\Program Files\Talvora\Talvora-Kurulum.exe" --uninstall`; `QuietUninstallString` `"C:\Program Files\Talvora\Talvora-Kurulum.exe" --uninstall --silent`; `URLInfoAbout` "https://github.com/bingoweb/Talvora-MCP"; `NoModify` 1 and `NoRepair` 1 (DWORD); `EstimatedSize` (KB of `C:\Program Files\Talvora`, DWORD).
2. `--uninstall` opens a window titled "Talvora'yı kaldır" with the text "Talvora bu bilgisayardan kaldırılacak: Windows hizmeti, Kontrol Merkezi, Başlat menüsü kısayolu ve Codex kaydı silinir. Kontrol Merkezi'nden kurduğun ek bileşenler (Gitea, Penpot, ChatGPT tüneli) bu kaldırmaya dahil değildir.", an unticked check box "Kayıtları ve ayarları da sil (C:\ProgramData\Talvora)" and the buttons "Kaldır" and "Vazgeç". Progress: "Talvora hizmeti durduruluyor...", "Dosyalar siliniyor..."; end: "Talvora kaldırıldı." `--uninstall --silent` (plus `--remove-data` for the check box) runs without a window.
3. Order: stop the tray processes first (D59: the tray's automatic recovery would start the service again); then stop the `Talvora` service (wait at most 60 s) and delete it; delete the Run value, the Start menu shortcut and the Codex table the installer wrote (the rest of `config.toml` untouched); delete `C:\Program Files\Talvora` except the running `Talvora-Kurulum.exe`, which is removed with `MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT)`; delete the Uninstall key. With the check box: delete everything under `C:\ProgramData\Talvora` **except `Secrets`**, which is never opened, moved or deleted.
4. `--uninstall --what-if` prints the plan (one item per line, the same order) and changes nothing.
5. `NativeInstallerSourceRegression.ps1`: the plan without data lists the service, the Run value, the shortcut, the Codex table, `C:\Program Files\Talvora` and the Uninstall key; with `--remove-data` it adds the `C:\ProgramData\Talvora` children and never `Secrets`; the Uninstall values are written as listed (through an injectable registry root).

**Don't:** remove Gitea, Penpot or tunnel components, repositories, Chocolatey or PowerShell 7; touch `Secrets`.

**Context7:** `/dotnet/docs`, query "Registry SetValue RegistryValueKind DWord". Vendor sources: `https://learn.microsoft.com/windows/win32/msi/uninstall-registry-key`, `https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy: the Uninstall key holds the listed values and Settings → Apps lists "Talvora"; `"C:\Program Files\Talvora\Talvora-Kurulum.exe" --uninstall --what-if` prints the plan; the installed smokes; CI. The real uninstall and reinstall on this machine is Claude's live check at the next audit, with the user's go-ahead (`[~] live pending (Claude)`).

### RL4 — A `v0.0.x.x` tag publishes the release

**Why:** user decision 2026-11-08: stable releases on GitHub, one installer and its SHA-256; Claude alone decides when, by pushing the tag after writing `docs/releases/<version>.md`.

**Files:** `.github/workflows/release.yml` (new), `tests/ReleaseWorkflowRegression.ps1` (new; registered with the gate's PowerShell regressions), `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `ModernizationPolicyRegression.ps1` and `CiDocsOnlyRegression.ps1` read `windows-ci.yml` (leave it unchanged). Pin `actions/checkout` and `actions/setup-dotnet` in `release.yml` to the same `@<sha> # vX.Y.Z` lines `windows-ci.yml` uses (the project's pinning rule).

**Do:**
1. Red first: `tests/ReleaseWorkflowRegression.ps1` reads `release.yml` and requires: the tag filter `v0.0.*.*`, `contents: write`, the tag check `^v0\.0\.\d{1,5}\.\d{1,5}$`, the `merge-base --is-ancestor` check against `origin/main`, the `docs/releases/` notes check, the windows-ci success check, the `-ReleaseVersion` build, the `SHA256SUMS.txt` step and `gh release create`; prints `RELEASE_WORKFLOW_GREEN`.
2. `release.yml`: `on: push: tags: ['v0.0.*.*']` and `workflow_dispatch` with inputs `version` (string) and `dry_run` (boolean, default true); `permissions: contents: write, actions: read`; `concurrency: release-${{ github.ref_name }}`; `runs-on: windows-latest`. Steps:
   - checkout with `fetch-depth: 0`; version = the tag without `v` (or the `version` input); fail unless it matches `^0\.0\.\d{1,5}\.\d{1,5}$`;
   - fail unless the commit is an ancestor of `origin/main`;
   - fail unless the `windows-ci` run of this commit concluded `success` (`gh run list --workflow windows-ci.yml --commit <sha> --json conclusion`, `GH_TOKEN: ${{ github.token }}`);
   - unless `dry_run`: fail with "Sürüm notu yok: docs/releases/<version>.md" when that file is missing;
   - the same toolchain setup steps `windows-ci.yml` uses before its installer build, then `pwsh -NoProfile -File scripts/Build-Windows-Installer.ps1 -ReleaseVersion <version>`;
   - `SHA256SUMS.txt` with one line `<lowercase sha256>  Talvora-Kurulum-<version>.exe`;
   - unless `dry_run`: `gh release create v<version> artifacts/installer/Talvora-Kurulum-<version>.exe SHA256SUMS.txt --title "Talvora <version>" --notes-file docs/releases/<version>.md --latest`.
3. Live check: `gh workflow run release.yml -f version=0.0.0.1 -f dry_run=true`, then that run once (no waiting loop): it must build the installer and publish nothing; record the run id.

**Don't:** create a tag or a release (AGENTS.md rule 10b), sign anything, upload other files, run the workflow without `dry_run`.

**Context7:** `/websites/github_en_actions` (map row added with this card), queries "workflow push tags filter" and "workflow_dispatch inputs boolean". Vendor sources: `https://docs.github.com/actions/writing-workflows/workflow-syntax-for-github-actions`, `https://cli.github.com/manual/gh_release_create`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; CI; the dry run green. The first real release is Claude's.

### RL5 — The Control Center says quietly when a new version is out

**Why:** user decision 2026-11-08 ("Evet, sessiz bir bildirim"): once a day, a link, nothing installed by itself.

**Files:** `src/Talvora.Tray/ReleaseCheckService.cs` (new), `src/Talvora.Tray/Views/SettingsView.xaml` and `.xaml.cs`, `src/Talvora.Tray/ViewModels/SettingsViewModel.cs`, the main window's sidebar item for "Ayarlar ve kurulum", `tests/Talvora.Notification.Regression/Program.RL5.cs` (new), `docs/codex/STATUS.md`, `.context7/verification.json`. After RL1.

**Do:**
1. Red first: `Program.RL5.cs` with a fake HTTP handler: a newer `tag_name` sets the "new version" state; the same, an older or an invalid tag does not; an `html_url` outside `https://github.com/bingoweb/Talvora-MCP/releases/` is ignored; a `geliştirme sürümü` build sends no request at all.
2. `ReleaseCheckService`: when the tray's own version (RL1) is a release, 2 minutes after the tray starts and then every 24 hours: `GET https://api.github.com/repos/bingoweb/Talvora-MCP/releases/latest` with `User-Agent: Talvora/<version>`, `Accept: application/vnd.github+json`, 15 s timeout, no token. `tag_name` must match `^v0\.0\.\d+\.\d+$`; compare as `System.Version`; when newer, keep `{version, htmlUrl, checkedAtUtc}` in the tray's settings folder. Any failure is silent until the next 24 h (no event, no toast). A development build writes "Sürüm denetimi geliştirme sürümünde kapalı." once to the tray log and never calls GitHub.
3. Ayarlar ve kurulum → Hakkında: under the version line, when a newer version is known: "Yeni sürüm hazır: <version>" (`Tv.Type.Body`) and a link button "Sürüm notlarını aç" that opens `htmlUrl` in the default browser. The sidebar item "Ayarlar ve kurulum" shows a 6 px dot in `Tv.Accent` beside its label while a newer version is known (`AutomationProperties.HelpText` "Yeni sürüm hazır"). No toast, no tray balloon; nothing downloads or installs.

**Don't:** download or install anything, contact any other address, add a setting.

**Context7:** `/dotnet/docs`, query "HttpClient timeout user agent". Vendor source: `https://docs.github.com/rest/releases/releases#get-the-latest-release`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy: the tray log holds the development-build line and no request leaves; both-theme PNGs of Hakkında with a forced "new version" state (test hook) attached for Claude's visual check; the installed smokes; CI.

## Context7 proof (user request 2026-11-08)

User: "context7 kapısının projelerde gerçekten işe yarayıp yaramadığını ve yarıyor ise en faydalı ve optimum şekilde kullanılıp kullanılmadığını denetle". Claude's check: the push gate (`ProjectDeliveryGate`) only checks the form of the working-tree file, so invented evidence that was never committed (`/made/up`, vendor source "see above") passed a live `talvora_git_run` push; the CI check (`Validate-Context7Gate.ps1`) demands that the file changes with the code but passes the same invented content; Codex's session shows real Context7 queries; ChatGPT makes as many outside requests per card as its evidence names queries, but nothing can prove it, and the vendor pages its evidence lists were never opened through Talvora (the gate demands a vendor URL even when Context7 answered). Most cards looked up one stable library call. User decisions: Talvora records every Context7 question and the push gate accepts only recorded ones; the card says when no lookup is needed (`Context7: gerekmiyor`); documentation-only pushes need no evidence; TahtaPilot and Classroom get the same CI check after CTX2.

### CTX2 — The push gate accepts only recorded Context7 questions

**Why:** see the section above. The gate becomes proof instead of a form check, documentation-only pushes stop needing evidence, and a card can say no lookup is needed.

**Files:** `src/Talvora/SourceEditing/ProjectDeliveryGate.cs`, `src/Talvora/Tools/GitTools.cs` (passes the push arguments), `src/Talvora/Tools/BuildRunnerTools.Dotnet.cs` (publish keeps the working-tree check), `scripts/Validate-Context7Gate.ps1` (the same format rules for CI), `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.QualityAudit.cs` (the gate's tests), `tests/StrictEvidenceJsonRegression.ps1` (the CI script's tests), `.context7/verification.json`, `docs/codex/STATUS.md`. After CTX1. **Tests that read these files:** `ModernizationPolicyRegression.ps1`, `StrictEvidenceJsonRegression.ps1`, `GitSafeDirectoryRegression.ps1`, `NativeInstallerSourceRegression.ps1`; run them right after the change.

**Do (method):**
1. Red first in `SourceEditRegressionRunner.QualityAudit.cs`, with temporary repositories (a bare remote and a clone) and a temporary ledger: (a) a push whose commits change only `README.md` and `docs/x.md` passes with no evidence file; (b) a push whose commits change `src/a.cs` but not `.context7/verification.json` is blocked; (c) evidence whose entry names a question the ledger lacks is blocked, and the message names the id and the question; (d) the same evidence passes once the ledger holds that id and question with status 200 written within the last 24 hours; (e) `"libraries": []` with `"noLookup": "CTX2: only renames a local variable"` passes, a `noLookup` under 20 characters is blocked; (f) a `vendorSources` entry that does not start with `https://` is blocked; (g) evidence changed only in the working tree is not seen. Messages start with `CTX2:`.
2. `ProjectDeliveryGate.EnsureSatisfied(string workingDirectory, IReadOnlyList<string>? pushArguments = null)`. For a push: the remote is the first non-option argument after `push`, the branch the next one (otherwise the current branch); the range is `refs/remotes/<remote>/<branch>..HEAD`, or only HEAD's own commit when that ref does not exist; the changed paths come from `git diff --name-only <base> HEAD`, run the way `GitTools` runs git (with its `safe.directory`). When every path matches `*.md`, `*.txt`, `docs/**`, `.context7/**` or `.talvora/**`, the push passes. Otherwise `.context7/verification.json` must be one of the paths and is read with `git show HEAD:.context7/verification.json`. `talvora_dotnet_publish` keeps reading the working-tree file with the format rules of step 3 and without steps 2 and 4.
3. Format, the same in the gate and in `Validate-Context7Gate.ps1` (with the same messages): `schemaVersion` 1, `scope` and a fresh `verifiedAtUtc` as today; either 1–12 `libraries` entries, each with a canonical `libraryId` and a `verified` text that contains `(query: "<question>")`, or no entries and a `noLookup` text of at least 20 characters; `vendorSources` may be missing or empty, otherwise 1–12 strings that start with `https://`.
4. Proof (the gate only; CI has no ledger): every entry's `libraryId` and the text inside `(query: "…")` must equal a ledger record with status 200 written in the 24 hours before the push (`Context7Ledger.ReadSince`). A miss blocks with: "Context7 evidence names a question Talvora did not record: <id> \"<question>\". Ask it with talvora_context7_docs and copy the question exactly."
5. Blocks stay `TALVORA_DELIVERY_GATE_BLOCKED` (an `McpException`, as today).

**Don't:** change the 14-day age rule, `schemaVersion`, the CI's development-scope patterns or the evidence file's place; touch `talvora_http_request`.

**Context7:** with `talvora_context7_docs` (CTX1): `/websites/git-scm`, query "git diff --name-only between a remote-tracking branch and HEAD"; record the fact used.

**Acceptance:** red first; the pinned tests; Changed and Full `Hepsi geçti.`; deploy; installed smokes; CI. Then `[~] live check pending (Claude): the next card's push passes the new gate with recorded questions`. After that Claude brings the same CI check to TahtaPilot and Classroom (user decision 2026-11-08).

## Toolchain modernization (user request 2026-11-08; Claude, one-off)

User: "Talvora'da rust, uv modernizasyonu yapmamız anlamlı olur mu araştır", then "Evet, yaz" and "bu arada tüm bu modernizasyonu bu defalık sen yap". Research result: Talvora stays .NET (Roslyn and WPF cannot move, the C# and Rust MCP SDKs are both Tier 1, the work is process-bound); Rust is for new standalone components; the toolchain tools follow the uv/cargo rule. Claude's one-off items (UV1, RS1, RS2, D51, UR1–UR6) are done and live in git history. **PK1 and PK2 below are ordinary agent cards**: the agent implements them in queue order.

### PK1 — MCP C# SDK 2.4.x and Tomlyn 2.10.2 (after UR4)

**Why:** `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` 2.4.0 (2026-11-07) and `Tomlyn` 2.10.2 are out; Talvora pins 2.3.1 and 2.10.1. The 2.4.0 notes list no breaking change; the items that touch Talvora: protocol property names now match case-sensitively (#1934), legacy output schemas of delegating server tools are fixed (#1928), and the legacy OAuth metadata fallback needs an opt-in (#1952; Talvora's server uses no SDK OAuth, so nothing changes there).

**Do:** `Directory.Packages.props`: both `ModelContextProtocol` lines to the newest 2.4.x stable on nuget.org when the card runs (2.4.0 on 2026-11-08; never a preview, never 2.5 or later), `Tomlyn` to 2.10.2. `docs/codex/CONTEXT7.md`: the MCP C# SDK row's version; the TOML row's version and id (`/xoofx/tomlyn/2.10.2` when Context7 lists it, otherwise `/xoofx/tomlyn` with a gap note). Then, once, as the signed-in user: `dotnet restore Talvora.slnx --force-evaluate` (P-54: this card authorises it, TOKEN ECONOMY D3), and `dotnet restore Talvora.slnx --locked-mode` must pass. In `git diff -- '*packages.lock.json'` only `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `ModelContextProtocol.Core` and `Tomlyn` may change (Claude checked 2.4.0's dependencies: all inside the current central pins); any other package moving → proposal with the diff. Nothing else changes. A compile error or a test that fails because of the update → proposal with the exact message.

**Files:** `Directory.Packages.props`, the seven `packages.lock.json` (`src/Talvora`, `src/Talvora.Shared`, `src/Talvora.Tray`, `src/Talvora.Installer`, `tests/Talvora.Notification.Regression`, `tests/Talvora.Smoke`, `tests/Talvora.SourceEdit.Regression`), `docs/codex/CONTEXT7.md`, `.context7/verification.json`, `docs/codex/STATUS.md`.

**Context7:** with `talvora_context7_docs` (CTX1). Context7 indexes documentation, not release notes, and on 2026-11-08 its newest versions were `/modelcontextprotocol/csharp-sdk/v2.3.0` and `/xoofx/tomlyn/2.10.1` (gaps for 2.4.x and 2.10.2). Ask the versioned ids about the SDK calls Talvora relies on — "McpServerTool attribute UseStructuredContent and OutputSchemaType", "AddCallToolFilter and AddListToolsFilter filter delegates", "McpException message returned to the client" — then open the v2.4.0 release notes and check each against them (CONTEXT7 PROTOCOL rule 4). Vendor sources: `https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0`, `https://github.com/xoofx/Tomlyn/releases`.

**Acceptance:** the `--locked-mode` restore and signed-in Full `Hepsi geçti.`; install; `--metadata-policy-only` and the full installed smoke green (Deploy row; they check #1934 and #1928 — a difference → proposal quoting it, no tool code change); CI.

### PK2 — `Microsoft.Build.Framework` follows the SDK's MSBuild (after PK1)

**Why:** Talvora compiles against `Microsoft.Build.Framework` 17.14.28 (`ExcludeAssets="runtime"`); at run time MSBuildLocator loads the MSBuild of the installed .NET SDK 10.0.403, which is MSBuild 18.9.15 (`dotnet msbuild -version`). The compile reference follows the runtime MSBuild's line and is never newer than it; 18.10 and 18.11 are on nuget.org but newer than the SDK's MSBuild.

**Do:** first run `dotnet msbuild -version` as the signed-in user; when it does not start with `18.9.` → proposal with the output (the SDK changed). Then `Directory.Packages.props`: `Microsoft.Build.Framework` 17.14.28 → 18.9.6 (the newest 18.9.x on 2026-11-08). `Microsoft.Build.Locator` 1.11.2 stays (current). Once, as the signed-in user, `dotnet restore Talvora.slnx --force-evaluate` (this card authorises it), then the `--locked-mode` restore must pass; the lock diff may move only `Microsoft.Build.Framework` and its new dependency `Microsoft.NET.StringTools` 18.9.6, in the lock files of the three projects that reference it (`src/Talvora`, `tests/Talvora.Notification.Regression`, `tests/Talvora.SourceEdit.Regression`). After the build, `src/Talvora/bin/Release/net10.0-windows/` must hold no `Microsoft.Build*.dll` besides `Microsoft.Build.Locator.dll` and no `Microsoft.NET.StringTools.dll`: MSBuildLocator loads the SDK's MSBuild, and its documentation (Context7 `/microsoft/msbuildlocator`) says to put `ExcludeAssets="runtime"` and `PrivateAssets="all"` on every MSBuild package reference. If `Microsoft.NET.StringTools.dll` appears or the build reports `MSBL001`: pin `Microsoft.NET.StringTools` 18.9.6 in `Directory.Packages.props`, add `<PackageReference Include="Microsoft.NET.StringTools" ExcludeAssets="runtime" PrivateAssets="all" />` next to `Microsoft.Build.Framework` in those three project files, restore with `--force-evaluate` again and check the folder again. `docs/codex/CONTEXT7.md`: the MSBuild row's pinned version becomes 18.9.6.

**Files:** `Directory.Packages.props`, the three project files and their `packages.lock.json`, `docs/codex/CONTEXT7.md`, `.context7/verification.json`, `docs/codex/STATUS.md`.

**Context7:** with `talvora_context7_docs`: `/microsoft/msbuildlocator`, "ExcludeAssets runtime for every MSBuild package reference with Microsoft.Build.Locator"; vendor sources only when opened: `https://learn.microsoft.com/visualstudio/msbuild/find-and-use-msbuild-versions`, `https://www.nuget.org/packages/Microsoft.Build.Framework`.

**Acceptance:** signed-in Full `Hepsi geçti.` (it runs the semantic source edit regression); install; `dotnet run --project tests/Talvora.Smoke/Talvora.Smoke.csproj -c Release -- --semantic-source-edit-only http://127.0.0.1:7676/mcp C:\Users\tayla\Talvora-MCP` → `TALVORA SEMANTIC SOURCE EDIT SMOKE GREEN`, and the full installed smoke green; CI.
