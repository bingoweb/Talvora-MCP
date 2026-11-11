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

### Audit 2026-11-11 (51) · range `audit/2026-11-09-50..29b7d5e` (D65, D66) · card DEP19

### DEP19 — Tomlyn 2.11.1 from Dependabot PR #19

**Why:** Dependabot PR #19 raises `Tomlyn` 2.11.0 → 2.11.1 (patch: "Fix CRLF escaping in multi-line basic strings"; Talvora's `talvora_toml_*` tools write TOML through it). The PR's own CI is red with `NU1004` because Dependabot changed only `Directory.Packages.props` and `src/Talvora/packages.lock.json`, while every test project locks Tomlyn as `CentralTransitive`. This is the DEP1 case.

**Files:** `Directory.Packages.props`, every `packages.lock.json` the script regenerates, `THIRD-PARTY-NOTICES.md` (the Tomlyn row: 2.11.0 → 2.11.1), `docs/codex/CONTEXT7.md` (the Tomlyn row only: `Tomlyn` 2.11.1, id unchanged `/xoofx/tomlyn/2.10.1`, gap text "2.10.2–2.11.1: release notes"), `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `ThirdPartyNoticesSourceRegression.ps1` (notices against the package pins); the Full gate restores in locked mode.

**Do:**
1. Start `scripts/Apply-DependabotPr.ps1 -Pr 19` detached (`talvora_user_process_start`, about 6 minutes) and poll `artifacts/dependabot/pr-19.result` until `READY` (or `FAILED`: read `pr-19.log`, then proposal).
2. Update the two document rows above by hand; nothing else changes.
3. Commit, push both remotes, then close PR #19 with `talvora_gh_run` (`pr close 19 --comment "main üzerinde uygulandı."`); never merge it on GitHub.

**Context7:** gerekmiyor (patch release; no Tomlyn API that Talvora calls changes — `noLookup` with this reason).

**Acceptance:** `READY`; signed-in Full `Hepsi geçti.`; deploy and the installed smokes (dependency graph changed); CI green on the commit; PR #19 closed. Commit text: "TOML kütüphanesi 2.11.1 sürümüne güncellendi", body one sentence, `Card: DEP19`.

### D67 — PowerShell 7 downloads to a private, locked file and shows its progress

**Why:** audit 50 finding 3 (hardening and one usability gap). RL2 runs elevated but writes `Talvora-PowerShell-<version>.msi` under `Path.GetTempPath()`, which for the interactive setup is the user's own `%TEMP%`: the name is predictable (a link planted at that name would make the elevated download write elsewhere) and the file is closed between the SHA-256 check and `msiexec`, so a process of the same user could swap it after the check. During the ~30 s download the window shows only "PowerShell 7 indiriliyor…" without movement. The hidden `--verify-powershell-download` prints "do­ruland²" when its output is redirected.

**Files:** `src/Talvora.Installer/InstallerEngine.PowerShell.cs`, `src/Talvora.Installer/Program.cs` (the `--verify-powershell-download` branch only), `tests/NativeInstallerSourceRegression.ps1`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `NativeInstallerSourceRegression.ps1` (RL2 part with its Add-Type harness), `QualityGateChangedRegression.ps1` (`Talvora.Installer/Program.cs`).

**Do:**
1. Red first, in the RL2 harness: the MSI path's file name matches `^Talvora-PowerShell-7\.\d+\.\d+-[0-9a-f]{32}\.msi$`; creating the download file where a file already exists fails instead of overwriting; while the verified stream is open, opening the same file for writing throws `IOException`; `ValidatePowerShell7MsiHash` takes that open `FileStream` (the wrong-hash case keeps its message).
2. `GetPowerShellMsiPath`: `Path.Combine(Path.GetTempPath(), $"Talvora-PowerShell-{version}-{Guid.NewGuid():N}.msi")`. The download writes with `FileMode.CreateNew`, `FileShare.None`.
3. After the download: open `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`, compute SHA-256 from that stream (position 0), run `msiexec` while it stays open, dispose it after `msiexec` exits, then delete the file as today. `VerifyPowerShell7DownloadAsync` uses the same open-then-hash step.
4. Progress: when the response has `Content-Length`, report `new InstallProgress(3, $"PowerShell 7 indiriliyor… %{percent}")` each time the whole percent changes (percent = downloaded × 100 / length, `CultureInfo.InvariantCulture`); without a length report the single message `"PowerShell 7 indiriliyor…"`. Both use the one-character ellipsis `…` (U+2026), replacing today's three dots in this message only (audit 51: the other progress texts are Claude's in [C] D47); the percent sign stands before the number, as in Turkish. Copy with an 81,920-byte buffer loop instead of `CopyToAsync` to count bytes; keep the 10-minute deadline.
5. `Program.cs`, `--verify-powershell-download` branch: `Console.OutputEncoding = new UTF8Encoding(false);` before writing.

**Don't:** change the pin, the URL check, the timeout or the `msiexec` arguments.

**Context7:** `/dotnet/docs`, query "FileStream FileShare.Read denies other processes write access while the handle stays open .NET 10". Vendor source: none needed.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; as SYSTEM, `Talvora-Kurulum.exe --verify-powershell-download` prints "PowerShell 7 7.6.9 paketi doğrulandı." readably and leaves no `Talvora-PowerShell-*.msi` in the temp folder; while the verified stream is open, `(New-Object -ComObject WindowsInstaller.Installer).OpenDatabase(<path>, 0)` succeeds (proof that Windows Installer can still read the locked file) — run this in the harness, never a real `msiexec /i`; deploy and the installed smokes; CI.

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

**Audit 51 addition (setup window texts):** the installer's progress texts mix three dots with `…` and "hizmet" with "servis" ("Talvora hizmeti durduruluyor..." next to "Talvora servisi başlatılıyor..."). When Claude ships D47, every progress text in `src/Talvora.Installer/*.cs` ends with `…` (U+2026) and says "servis", as the Control Center does; D67 already changes the PowerShell download text. Re-check `NativeInstallerSourceRegression.ps1` for pinned texts first.

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

### RL3 — Talvora is in Apps & features and can be uninstalled

**Why:** a downloaded installer must also be removable from Windows Settings → Apps. Today nothing registers Talvora there and there is no uninstall (only the developer reset script). The install flow creates: the `Talvora` service, `C:\Program Files\Talvora\Versions\<commit>-<stamp>`, the install user's `HKU\<sid>\Software\Microsoft\Windows\CurrentVersion\Run` value `RunValueName`, the Start menu shortcut `Talvora Kontrol Merkezi.lnk`, the Codex entry in that user's `.codex\config.toml`, and `C:\ProgramData\Talvora` with its protected ACL.

**Files:** `src/Talvora.Installer/InstallerEngine.Uninstall.cs` (new), `src/Talvora.Installer/InstallerEngine.Flow.cs`, `src/Talvora.Installer/Program.cs`, `tests/NativeInstallerSourceRegression.ps1`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `NativeInstallerSourceRegression.ps1` and `InstallerRollbackStateRegression.ps1` (the `InstallerEngine` flow and service files), `CodexCliIntegrationSourceRegression.ps1` (`InstallerEngine.CleanupState.cs`); `QualityGateChangedRegression.ps1` (`Talvora.Installer/Program.cs`). `NativeInstallerSourceRegression.ps1` also pins Claude's KUR1 window (`SetupWindowKeepsSantralLookAndDeveloperPlate`): do not edit `InstallerForm.cs`, `SetupTheme.cs`, `SetupArt.cs` or `SetupControls.cs`; the uninstall window and its words already exist there.

**Do:**
1. After a successful install: copy the running setup file to `C:\Program Files\Talvora\Talvora-Kurulum.exe` (overwrite) and write `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Talvora`: `DisplayName` "Talvora"; `DisplayVersion` (the informational version before `+`); `Publisher` "Taylan Soylu"; `DisplayIcon` "<installed tray exe>,0"; `InstallLocation` "C:\Program Files\Talvora"; `UninstallString` `"C:\Program Files\Talvora\Talvora-Kurulum.exe" --uninstall`; `QuietUninstallString` `"C:\Program Files\Talvora\Talvora-Kurulum.exe" --uninstall --silent`; `URLInfoAbout` "https://github.com/bingoweb/Talvora-MCP"; `NoModify` 1 and `NoRepair` 1 (DWORD); `EstimatedSize` (KB of `C:\Program Files\Talvora`, DWORD).
2. `--uninstall` without `--silent` opens the window Claude shipped in KUR1: in `Program.cs`, after the administrator check and the `Application.SetHighDpiMode` / `EnableVisualStyles` lines, `Application.Run(new InstallerForm(SetupMode.Uninstall, async (progress, removeData, cancellationToken) => { await InstallerEngine.UninstallAsync(progress, removeData, cancellationToken); return null; }))` instead of the install window. That window already shows the title "Talvora'yı kaldır", the text, the unticked box "Kayıtları ve ayarları da sil (C:\ProgramData\Talvora)" (its value arrives as `removeData`), the buttons "Kaldır" and "Vazgeç" and the end "Talvora kaldırıldı.", and it cannot be closed while the work runs. `UninstallAsync(IProgress<InstallProgress> progress, bool removeData, CancellationToken cancellationToken)` reports "Talvora hizmeti durduruluyor..." (10), "Dosyalar siliniyor..." (60) and "Talvora kaldırıldı." (100) and throws with a Turkish message on failure (the window shows it with the log path). `--uninstall --silent` (plus `--remove-data` for the box) runs `UninstallAsync` without a window and logs like `--silent`.
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
   - checkout with `fetch-depth: 0`, using the same pinned commits as `windows-ci.yml` (`actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1`, and `actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0` where .NET is set up); version = the tag without `v` (or the `version` input); fail unless it matches `^0\.0\.\d{1,5}\.\d{1,5}$`;
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

**Files:** `src/Talvora.Tray/ReleaseCheckService.cs` (new), `src/Talvora.Tray/Views/SettingsView.xaml` and `.xaml.cs`, `src/Talvora.Tray/ViewModels/SettingsViewModel.cs`, `src/Talvora.Tray/Controls/NavRailItem.cs` and `.xaml`, `src/Talvora.Tray/Views/ShellWindow.xaml` (`NavSettings`), `src/Talvora.Tray/ViewModels/ShellViewModel.cs`, `src/Talvora.Tray/ControlCenterSmoke.cs` (test hook), `tests/Talvora.Notification.Regression/Program.RL5.cs` (new), `docs/codex/STATUS.md`, `.context7/verification.json`. After RL1.

**Do:**
1. Red first: `Program.RL5.cs` with a fake HTTP handler: a newer `tag_name` sets the "new version" state; the same, an older or an invalid tag does not; an `html_url` outside `https://github.com/bingoweb/Talvora-MCP/releases/` is ignored; a `geliştirme sürümü` build sends no request at all.
2. `ReleaseCheckService`: when the tray's own version (RL1) is a release, 2 minutes after the tray starts and then every 24 hours: `GET https://api.github.com/repos/bingoweb/Talvora-MCP/releases/latest` with `User-Agent: Talvora/<version>`, `Accept: application/vnd.github+json`, 15 s timeout, no token. `tag_name` must match `^v0\.0\.\d+\.\d+$`; compare as `System.Version`; when newer, keep `{version, htmlUrl, checkedAtUtc}` in `%LOCALAPPDATA%\Talvora\ControlCenter\release-check.json` (written with `JsonFileStore.WriteAsync`, read back when the tray starts). Any failure is silent until the next 24 h (no event, no toast). A development build writes "Sürüm denetimi geliştirme sürümünde kapalı." once to the tray log and never calls GitHub.
3. Ayarlar ve kurulum → Hakkında: under the version line, when a newer version is known: "Yeni sürüm hazır: <version>" (`Tv.Type.Body`) and a link button "Sürüm notlarını aç" that opens `htmlUrl` in the default browser. The sidebar item shows a dot while a newer version is known: `NavRailItem` gets a `HasDot` dependency property (bool, default false); its template adds, in the icon's grid column, `<Ellipse x:Name="Dot" Width="6" Height="6" Fill="{DynamicResource Tv.Accent}" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,14,-3,0" Visibility="Collapsed" IsHitTestVisible="False" />` and a template trigger `HasDot` = `True` → `Dot` visible, so the dot sits on the icon whether the rail is expanded or not. `ShellViewModel` exposes `IsNewVersionKnown` and `NewVersionHelpText` ("Yeni sürüm hazır" or empty) from `ReleaseCheckService`; `ShellWindow.xaml` binds `NavSettings`'s `HasDot` and `AutomationProperties.HelpText` to them. No toast, no tray balloon; nothing downloads or installs.

**Don't:** download or install anything, contact any other address, add a setting.

**Context7:** `/dotnet/docs`, query "HttpClient Timeout with DefaultRequestHeaders UserAgent and Accept for one GET request in .NET 10". Vendor source: `https://docs.github.com/rest/releases/releases#get-the-latest-release`.

**Acceptance:** red first; Changed and Full `Hepsi geçti.`; deploy: the tray log holds the development-build line and no request leaves; both-theme PNGs of Hakkında and the rail with a forced "new version" state: in `--control-center-smoke` only, `TALVORA_CONTROL_CENTER_SMOKE_NEW_VERSION` (e.g. `0.0.9.9`) forces that state and the smoke saves the variants `ui7-about-new-version-initial` and `-alternate` attached for Claude's visual check; the installed smokes; CI.
