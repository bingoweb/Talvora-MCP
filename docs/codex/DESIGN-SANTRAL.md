# Talvora Kontrol Merkezi — redesign spec "Santral"

> Author: Claude (design direction), 2026-11-03. User chose concept **A · Santral** from three moving previews (Kalem video `C:\Users\tayla\Kalem\projeler\talvora-panel\cikti.mp4`; scenes 1–2 are this concept). This spec **replaces** "Gözlemevi" (`DESIGN-CONTROL-CENTER.md`, which stays as history; its information architecture, copy deck §9 and accessibility bars §10 remain valid unless this file says otherwise).
> Implementer: **Claude builds every card, SN1 included** (one-off user exception 2026-11-03: "bu defalık tüm tasarımı sen yap. chatgpt'ye iş bırakma, arayüzdeki her şey gerçek ve realtime verilere dayanmalı"). All SN lines are `[C]` in STATUS, so the agent skips them (AGENTS.md rule 10a) and never edits their files while they are open. Product strings are Turkish and come from this file character for character.

## 1. Why

The Gözlemevi screen reads like any dark admin dashboard: a ring gauge, a thin dotted chain with tiny labels, then a long list of rows. It does not show what Talvora actually is. Talvora is an **exchange**: it connects outside callers (ChatGPT through secure tunnels, local agents like Codex) to services on this computer, keeps those lines up and reconnects them when they drop. The new screen draws exactly that.

## 2. Concept — "Santral"

A telephone switchboard panel. Upper row: **outside lines** (ChatGPT · Dev, ChatGPT · Admin, ChatGPT · Gitea, local clients). Lower row: **sockets of this computer** (one per managed service). A live connection is a **patch cable** plugged from a socket to its outside line. Each socket and line has a **lamp with a word** (hazır · dikkat · kapalı · onarılıyor). A real tool call is a small **light that travels down the cable** from the outside line into the socket. When a line drops, its plug **falls out and swings**; when Talvora reconnects it, the plug **seats again** and a paper **event slip** is printed.

Everything visible is real: lamp = health state, cable = live connection, light = one measured tool call (SN1), counters = measured counts, reconnect timer = measured time since the state changed (SN1 part B). Nothing is drawn without a measurement.

User decisions (2026-11-03): concept A; a dark variant **"Gece vardiyası"** follows Windows dark mode (manual override in Ayarlar); cable lights are **real tool calls**. Gitea's calls are not seen by Talvora, so the Gitea cable carries no lights (its lamp still shows health).

## 3. Tokens

All colours live in `Themes/Tokens.Gun.xaml` (Gündüz) and `Themes/Tokens.Gece.xaml` (Gece vardiyası) with identical keys (`Sn.*`; file names kept so the theme manager and its tests stay unchanged), referenced by `DynamicResource`. Existing `Tv.*` keys are re-pointed to the matching `Sn.*` values so untouched views follow the new theme; they are removed in SN7.

| Key | Gündüz | Gece vardiyası | Use |
|---|---|---|---|
| `Sn.Panel` | `#E7DFCB` | `#1B1916` | window and panel |
| `Sn.PanelRaised` | `#EFE9DA` | `#24211D` | inputs, hover, selected rows |
| `Sn.PanelSunken` | `#DCD3BD` | `#12110F` | evidence and log panels |
| `Sn.Ink` | `#1F1C18` | `#EDE5D3` | primary text |
| `Sn.InkMuted` | `#5E574A` | `#B3A88F` | secondary text |
| `Sn.InkFaint` | `#6A6252` | `#9C927C` | captions; must reach 4.5:1 on `Sn.Panel` (SN2 verifies, adjusts lightness only) |
| `Sn.Rule` | `#8C826F` | `#4A443A` | separators, dotted section line |
| `Sn.Brass` / `Light` / `Dark` | `#B08D57` / `#E2C68F` / `#7A5E33` | `#C9A56A` / `#EBD3A0` / `#8A6B3B` | socket rings, plugs |
| `Sn.Socket` | `#151210` | `#050505` | socket hole |
| `Sn.Cable` | `#2A2622` | `#0E0D0C` | cable body (highlight `#FFF0D2` at 16 % / 10 %) |
| `Sn.Lamp.Ready` | `#2F9E6B` | `#45C08A` | hazır |
| `Sn.Lamp.Attention` | `#E39A1B` | `#F2AE3A` | dikkat, onarılıyor |
| `Sn.Lamp.Offline` | `#C3412F` | `#E2604C` | ulaşılamıyor |
| `Sn.Lamp.Off` | `#9A9184` | `#5A544A` | kapalı (unlit glass) |
| `Sn.Word.Ready` / `Attention` / `Offline` | `#1F6E4A` / `#9A5B00` / `#A3301F` | `#6FD4A6` / `#F5C46E` / `#F08A78` | lamp words (text contrast ≥ 4.5:1) |
| `Sn.Pulse` | `#F6E7B8` | `#FFE9B0` | call light |
| `Sn.Slip` | `#F7F2E5` | `#2A2620` | event slip paper |
| `Sn.Focus` | `#1F1C18` | `#EDE5D3` | 2 px focus ring, 2 px offset |

Texture: fine horizontal brushed lines (2 px period, 2–4 % ink) and a soft vignette, drawn once into a cached `DrawingBrush`; off in high contrast.

Typography (Windows built-in, no font files shipped): headings and labels **Bahnschrift SemiBold Condensed**, wordmark **Bahnschrift Bold Condensed** letter-spacing 6; body **Bahnschrift** (regular width); times, counters, IDs **Cascadia Mono** (fallback Consolas). Ramp: Wordmark 34, Display 48, Title 28, Label 22 (sockets), Body 15, Caption 13, Mono 14. Uppercase only via `ToUpper(new CultureInfo("tr-TR"))` (ADMİN, GİTEA).

Theme names in Ayarlar → Görünüm: **Sistem · Gündüz · Gece vardiyası** (default Sistem). The desktop worklog card keeps its own terminal identity and is not changed by this spec.

## 4. Overview layout (1280 × 800 reference; scales to 1920 × 1080)

```
 TALVORA  KONTROL MERKEZİ                                           16:24   ◐
 ──────────────────────────────────────────────────────────────────────────────
 DIŞ HATLAR      CHATGPT        CHATGPT        CHATGPT        YEREL
                   Dev           Admin          Gitea      Codex · Claude
                  (◉) ● hazır    (◉) ● hazır    (◉) ● hazır    (◉) ● hazır
                 128 çağrı      41 çağrı        —             6 çağrı
                   ╲                ╲              ╲         ╱
 · · · · · · · · · ·╲· · · · · · · · ╲· · · · · · · ╲· · ·╱· · · · · · · · · ·
 BU BİLGİSAYAR       ╲                ╲              ╲  ╱
          (◉) ● hazır   (◉) ● hazır   (◉) ● hazır   (◉) ● hazır   (◌) ○ kapalı
          Çekirdek        Dev            Admin          Gitea         Penpot
                                                                    isteğe bağlı
 Her şey yolunda.                                      ┌──────────────────────┐
 4 hat açık · Penpot isteğe bağlı olarak kapalı        │ 16:24:07  OLAY 0038  │
 son kontrol 16:24                                     │ Talvora Admin hattı… │
                                                       └──────────────────────┘
```

- One socket per managed service (registry order, Talvora core first); one outside line per socket that has a remote side. Mapping: core (`/mcp`) ↔ **YEREL** (sub-label = client names seen in the last 60 min from SN1, joined with " · ", else "istemci yok"); Dev (`/mcp/dev`) ↔ **CHATGPT · Dev**; Admin (`/mcp/admin`) ↔ **CHATGPT · Admin**; Gitea MCP ↔ **CHATGPT · Gitea**; Penpot ↔ no outside line (socket only; dashed ring while off).
- The services list leaves the overview: the switchboard **is** the list. Clicking (or Enter on) a socket or its outside line opens that service's detail. `Hizmetler` in the rail keeps the full table.
- Under each outside line: measured call count since the service started (`{n} çağrı`, `tr-TR` grouping) and on hover the last call time. Gitea and Penpot show `—`.
- The status sentence is the existing `StatusNarrator` output (copy deck §9), set in Display; sub-line Body muted.
- The most recent event appears as one slip (§6) bottom right; click opens Olaylar.
- Width < 1000 px: rows wrap to two columns per row, cables re-route; below 760 px the switchboard collapses to a vertical list of socket rows with lamps (no cables).

## 5. Motion vocabulary

| Name | When | Spec |
|---|---|---|
| Plug-in | a line becomes Ready (and on first paint, staggered 90 ms per cable) | cable draws from socket to line along its curve 450 ms ease-out, plug seats 80 ms, lamp fades in 120 ms |
| Call | each SN1 call event on that line | light travels from the outside line down the cable into the socket in 900 ms ease-in-out; max 12 lights per line per second (extra calls only increase the counter) |
| Attention | state Attention / Repairing | lamp amber, slow breathing (opacity 0.78 ↔ 1.0, 1.6 s period); word "dikkat" or "onarılıyor" |
| Drop | state Offline | plug leaves the line (120 ms), falls to hang under the socket (300 ms), damped swing (2.5 s, amplitude 70 px → 0), then still; outside-line lamp unlit; reconnect timer `yeniden bağlanıyor · mm:ss` from measured state-since |
| Re-seat | Offline → Ready | plug rises along an arc to the line 600 ms ease-in-out, seats, lamp green; an event slip prints |
| Slip | new event | slides up 400 ms ease-out with a 1° tilt; old slip slides away |

Reduced motion (`SystemParameters.ClientAreaAnimation == false`) or high contrast: no swing, no lights, no breathing; states change instantly and counters still update. Animations run only while the window is visible and not minimised; idle CPU < 1 %.

## 6. Event slip (Olaylar and overview)

Paper strip on `Sn.Slip`, perforation dots along the top edge, 1° tilt, soft shadow. Line 1 Mono Caption: time `HH:mm:ss` left, `OLAY {id:0000} · {KIND}` right (kind: OTOMATİK ONARIM · YAŞAM DÖNGÜSÜ · SORUN · BİLGİ). Line 2 Title: event title. Line 3 Body muted: event detail. The Olaylar page becomes a day-grouped stack of slips (same filters as today).

## 7. Other screens (same language, no logic change)

- **Hizmetler**: rows become "hat kartları": socket glyph + lamp + word + name + role + measured call count; same filters/actions.
- **Hizmet ayrıntısı**: header shows the socket and its outside line with the cable state; actions unchanged; "Teknik kanıt" on `Sn.PanelSunken` in Mono.
- **Ayarlar ve kurulum**: stepper drawn as a cable plugging into three sockets (Talvora çekirdeği · Güvenli tüneller · ChatGPT Business bağlantısı); texts unchanged.
- **Shell**: wordmark `TALVORA` + `KONTROL MERKEZİ` light, clock in Mono, rail on `Sn.Panel` with brass selection bar (3 px `Sn.Brass`), window title stays "Talvora Kontrol Merkezi".
- **New-version dot** (RL5, fixed by D75): while a newer release is known, `Ayarlar ve kurulum` shows a 6 px `Tv.Accent` dot on its icon's top-right corner, item-relative (`HorizontalAlignment="Left"`, `Margin="31,14,0,0"` in the item template's own grid), so the collapsed rail never clips it; screen readers hear "Yeni sürüm hazır" (the item's help text). The Control Center smoke checks that no element between the dot and its rail item clips it.

## 8. Copy (additions to copy deck §9)

| Situation | Text |
|---|---|
| Section labels | DIŞ HATLAR · BU BİLGİSAYAR |
| Outside lines | CHATGPT (sub: Dev / Admin / Gitea) · YEREL (sub: client names or "istemci yok") |
| Lamp words | hazır · dikkat · onarılıyor · kapalı · ulaşılamıyor |
| Counter | {n} çağrı; when the line has measured failures, a second line "{n} hata" in `Sn.Word.Offline` (SN8, user choice 2026-11-07); then "son {HH:mm:ss}" |
| Reconnect timer | yeniden bağlanıyor · {mm:ss} |
| Optional socket | isteğe bağlı |
| Theme options | Sistem · Gündüz · Gece vardiyası |
| Slip kinds | OTOMATİK ONARIM · YAŞAM DÖNGÜSÜ · SORUN · BİLGİ |

## 9. Cards

| Card | Owner | Scope |
|---|---|---|
| **[C] SN1** | Claude (one-off) | Line traffic meter in the service (`/healthz/traffic`), Tray poller, per-service state-since; data only, no visible change. |
| **[C] SN2** | Claude | Tokens (both themes), typography, texture, theme names in Ayarlar; `Tv.*` re-pointed. |
| **[C] SN3** | Claude | Switchboard control on Genel bakış (sockets, outside lines, cables, lamps with words, counters, status block, keyboard and UIA); replaces Nabız ring and chain strip. Plug-in and Call motion. Needs SN1 for lights and counters. |
| **[C] SN4** | Claude | Fault choreography: Attention, Drop (with reconnect timer), Re-seat; reduced-motion fallbacks. Needs SN1 part B. |
| **[C] SN5** | Claude | Event slips on Genel bakış and Olaylar. |
| **[C] SN6** | Claude | Hizmetler, Hizmet ayrıntısı, Ayarlar stepper and shell chrome in the Santral language. |
| **[C] SN8** | Claude | Measured failures per line and failed-call lights with an end spark; brass hover ring on sockets (audit 39, user choice). |
| **[C] SN7** | Claude | Proof: both themes at 100/125/150/200 % DPI, high contrast, reduced motion, idle CPU, UIA names; remove `Tv.*` aliases and the retired Nabız/Chain controls; README screenshots. |

## 10. Setup window (KUR1)

`Talvora-Setup.exe` speaks the same language at 640 × 480, scaled per monitor DPI (`src/Talvora.Installer/InstallerForm.cs`, `SetupTheme.cs`, `SetupArt.cs`, `SetupControls.cs`; built and rendered by Claude, user request 2026-11-08).

- Wordmark `TALVORA` + `KURULUM` (`KALDIRMA` when uninstalling), then a strip with two sockets: **KURULUM PAKETİ** (its version) and **BU BİLGİSAYAR** (the machine name), and a lamp with its word beside the second.
- Progress is the **Plug-in** motion driven by the installer's real percent: the cord leaves the package's socket and its plug travels to the computer's socket. Success: the plug seats (140 ms), one **Call** light runs down the cord (900 ms), the lamp turns green and says `hazır`. Failure: the cord pulls back (450 ms), the lamp turns red, the window shows the message, the log path and `Yeniden dene`. Uninstalling runs the cord backwards. Lamp words: bekliyor · kuruluyor / güncelleniyor / kaldırılıyor · hazır / kaldırıldı · kurulamadı / güncellenemedi / kaldırılamadı.
- Success and failure stay in the window (no message boxes). The window cannot be closed while the work runs and says why in its detail line.
- Footer: a hairline rule, the developer's **maker plate** on the left — a small riveted brass plate engraved `GELİŞTİREN · TAYLAN SOYLU`, the way old switchboards carried their maker's name, kept quiet on purpose ("çok abartmadan"); screen readers hear "Geliştiren: Taylan Soylu" — and the ink buttons on the right (filled for the main action, outlined for the other).
- Theme: the Control Center's own choice (`theme-preference.json`), otherwise Windows' app mode; the Windows 11 title bar takes the panel colour. High contrast uses system colours without texture, brass or motion; reduced motion changes states at once.
- `NativeInstallerSourceRegression.ps1` (`SetupWindowKeepsSantralLookAndDeveloperPlate`) pins the plate, the title, the close guard, the theme sources and the DPI mode.
