# Talvora Kontrol Merkezi — Redesign spec "Gözlemevi"

> Author: Claude (design direction), 2026-10-18. Implementer: Codex, cards `UI1`–`UI8` (bottom of this file).
> Language: this spec is English; every visible string in the product is ordinary Turkish (copy deck in §9 is authoritative).
> This is a **cosmetic and information-architecture** redesign. All existing capabilities stay (health, lifecycle start/stop/restart/repair, setup, events, raw logs, embedded integrations, search/filter, window placement, tray). No new data sources, no fabricated telemetry: every number, time, latency or count on screen comes from an existing service (`ControlCenterDashboardService`, `ControlCenterComponentHealthService`, `ControlCenterEventStore`, `ControlCenterRawLogService`, `ControlCenterVersionService`, `EmbeddedMcpIntegrationHealth`).

---

## 1. Why redesign (what is wrong today)

Observed on the installed build and in `docs/media/control-center.png`:

- It reads like a generic SaaS admin template (pastel light cards, big "1 / 1 / 3 / 5" numbers) — it does not say *what is connected to what*, which is the one thing a user of Talvora needs to understand at a glance.
- Four equal metric cards take the whole first screen; the service list starts below the fold.
- Jargon leaks into the main surface ("Runtime API key ve tunnel-client kurulumu bulunamadı").
- Three names for one product: window title "Talvora Yönetim Merkezi", header "Kontrol Merkezi", rail "CONTROL CENTER".
- Theme bug: `ControlCenterTheme.xaml` merges WPF-UI `Theme="Light"` while `ControlCenterApplication` applies `ApplicationTheme.Dark` → mixed control templates.
- Primary-button hover text `#0D1118` on `#455DD5` fails WCAG contrast.
- UI is constructed imperatively in C# (77 hard-coded `FontSize` values, no styles for type ramp), so consistency is impossible to keep.
- No dark mode for the window although the desktop worklog card is dark/neon — the two Talvora surfaces feel like two products.

## 2. Concept — "Gözlemevi" (The Observatory)

Talvora quietly watches a workstation the way an observatory watches the sky: a calm, dark field; each component is a point of light; connections are orbit lines; trouble is a star that dims or flickers. The screen should feel like **mission control at night** — precise, quiet, trustworthy — and it should share DNA with the existing dark terminal/neon worklog card so Talvora becomes one recognisable brand.

Two signature elements, used sparingly:

1. **Nabız (Pulse) ring** — a circular gauge in the header area. It is split into one arc segment per managed component; each segment takes the component's state colour. When everything is ready, the ring carries the only gradient in the product (the *Aurora*: turquoise → violet) and breathes very slowly (opacity 0.85 ↔ 1.0, 6 s). When something needs attention, the gradient disappears, the affected segment shows its state colour and the breathing stops (stillness = "look here").
2. **Bağlantı zinciri (Connection chain)** — a horizontal constellation that draws the real topology: *Bu bilgisayar → Talvora çekirdeği → Dev / Admin yüzeyleri → Güvenli tüneller → ChatGPT Business*, with branches for the Gitea chain and Penpot. Lines between healthy nodes carry a slow dash flow (direction = data direction); a broken link shows a gap with a small state glyph; on-demand components are drawn as hollow nodes (neutral, not red).

Everything else is restrained: flat surfaces, 1 px borders, generous spacing, one accent.

Awwwards evidence (required by the delivery gate) is **evidence only** — it never changes this spec (AGENTS.md → EXECUTION MANDATE rule 9). For each UI card, review Awwwards *Dashboard/Data*, *Technology* and *Dark* collections, record 3–5 concrete page URLs in `.talvora/awwwards-verification.json` `references`, and fill `designDecisions` with the spec sections implemented by that card (e.g. "DESIGN-CONTROL-CENTER §5.1 overview hero band"). If a reference suggests a change you think is better, write a proposal; do not implement it.

## 3. Design tokens

All tokens live in XAML resource dictionaries (`Themes/Tokens.Gece.xaml`, `Themes/Tokens.Gun.xaml`) with **identical keys**; components only reference keys via `DynamicResource` so the theme can switch live.

### 3.1 Colour — Gece (dark, default when Windows is dark)

| Key | Hex | Use |
|---|---|---|
| `Tv.Bg` | `#0A0E15` | window background (under Mica tint) |
| `Tv.Surface` | `#101722` | cards, rail |
| `Tv.SurfaceRaised` | `#162030` | hover, selected rows, inputs |
| `Tv.SurfaceSunken` | `#070A10` | evidence / log panels |
| `Tv.Border` | `#1F2A3B` | 1 px separators |
| `Tv.BorderStrong` | `#2C3B52` | focusable outlines |
| `Tv.Text` | `#E9EEF6` | primary text |
| `Tv.TextMuted` | `#A3B0C4` | secondary text |
| `Tv.TextFaint` | `#7C899E` | captions, timestamps (P-31: ≥ 4.6 : 1 on Bg, Surface, SurfaceRaised, SurfaceSunken) |
| `Tv.Accent` | `#5BD8FF` | Talvora turquoise (same as worklog `Trace`) — focus ring, links, selection bar |
| `Tv.AccentInk` | `#04121A` | text on accent fills |
| `Tv.AccentSoft` | `#0F2B38` | selected nav background |
| `Tv.Aurora.Start` / `End` | `#5BD8FF` / `#9B8CFF` | Nabız ring when all ready; logo mark only |
| `Tv.Ready` | `#3DDC97` | hazır |
| `Tv.Attention` | `#F5B84B` | dikkat |
| `Tv.Offline` | `#FF6B81` | ulaşılamıyor / hata |
| `Tv.Neutral` | `#7C8AA3` | isteğe bağlı kapalı, bilinmiyor |
| `Tv.Repairing` | `#79AFFF` | onarılıyor / işlem sürüyor |

### 3.2 Colour — Gün (light)

| Key | Hex |
|---|---|
| `Tv.Bg` | `#F4F6FA` |
| `Tv.Surface` | `#FFFFFF` |
| `Tv.SurfaceRaised` | `#EDF1F7` |
| `Tv.SurfaceSunken` | `#E7ECF3` |
| `Tv.Border` | `#DCE2EC` |
| `Tv.BorderStrong` | `#B9C3D3` |
| `Tv.Text` | `#0E1625` |
| `Tv.TextMuted` | `#46546C` |
| `Tv.TextFaint` | `#5D6A81` |
| `Tv.Accent` | `#0779A8` (darker turquoise for contrast on white) |
| `Tv.AccentInk` | `#FFFFFF` |
| `Tv.AccentSoft` | `#E1F3FA` |
| `Tv.Aurora.Start` / `End` | `#0A9BD0` / `#6D5BEA` |
| `Tv.Ready` | `#12805A` |
| `Tv.Attention` | `#9A5B00` |
| `Tv.Offline` | `#C2324A` |
| `Tv.Neutral` | `#5F6C82` |
| `Tv.Repairing` | `#2F6FD6` |

Contrast rule (checked in UI8): body text ≥ 4.5:1, large text/icons ≥ 3:1 against the surface they sit on, in both themes. State is **never colour-only**: every state has a glyph + Turkish word.

### 3.3 Typography

- Display/headings: `Segoe UI Variable Display`; body: `Segoe UI Variable Text`; technical evidence, timestamps, IDs: `Cascadia Mono` (fallback `Consolas`).
- Ramp (keys `Tv.Type.*`, defined once as styles for `TextBlock`):

| Key | Size / line height | Weight | Use |
|---|---|---|---|
| `Display` | 28 / 36 | Semibold | page title |
| `Title` | 20 / 28 | Semibold | section titles, detail header |
| `Subtitle` | 16 / 22 | Semibold | card titles |
| `Body` | 14 / 20 | Regular | default |
| `BodyStrong` | 14 / 20 | Semibold | row names |
| `Caption` | 12 / 16 | Regular | metadata, timestamps (minimum size anywhere) |
| `Overline` | 11 / 14, letter-spacing 80 | Semibold, uppercase | rail group labels only |
| `Mono` | 12.5 / 18 | Regular | evidence, logs |

Numbers in tiles use `Typography.NumeralAlignment="Tabular"`.

### 3.4 Space, radius, elevation, motion

- 4-pt grid: `Tv.Space.1..8` = 4, 8, 12, 16, 20, 24, 32, 48.
- Radius: `Tv.Radius.S` 6 (pills, inputs), `M` 10 (rows, buttons), `L` 14 (cards), `XL` 20 (hero band).
- Elevation: none by default. Cards = surface + 1 px border. Popups/palette: `DropShadowEffect` blur 24, opacity 0.35 (Gece) / 0.12 (Gün).
- Motion tokens: `Tv.Motion.Fast` 120 ms, `Base` 180 ms, `Slow` 260 ms; easing `CubicEase EaseOut`. Breathing/flow animations only when the window is visible, focused or not minimized, and **off** when `SystemParameters.ClientAreaAnimation == false` or high contrast is on.

## 4. Shell

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ◉ Talvora  ›  Genel bakış                 [ ⌘ Ara veya komut çalıştır…  Ctrl+K ]  ◐ ─ □ ✕ │ 48 px title bar (ExtendsContentIntoTitleBar)
├──────┬───────────────────────────────────────────────────────────────────────────────┤
│  ◉   │                                                                               │
│      │                         content host (max width 1440, centred)                │
│  ⌂   │                                                                               │
│  ⛁   │                                                                               │
│  ⟡   │                                                                               │
│  ☰   │                                                                               │
│      │                                                                               │
│  ⚙   │                                                                               │
│ ●3   │                                                                               │
└──────┴───────────────────────────────────────────────────────────────────────────────┘
 72 px rail (collapsed) / 232 px (expanded with labels, toggled by the ◉ logo or width ≥ 1360)
```

- **Rail items** (icon + label when expanded, tooltip when collapsed): `Genel bakış` (⌂), `Hizmetler` (⛁), `Çalışma günlüğü` (✎ — session history of the desktop worklog, spec in `DESIGN-WORKLOG.md` §8, card N7), `Bağlantı zinciri` (⟡), `Olaylar` (☰); bottom: `Ayarlar ve kurulum` (⚙) and a tiny **Nabız dot** with the attention count. Icons (WPF-UI `SymbolRegular`, exactly these): Genel bakış `Home24`, Hizmetler `Server24`, Çalışma günlüğü `Notebook24`, Bağlantı zinciri `Organization24`, Olaylar `TextBulletListLtr24`, Ayarlar ve kurulum `Settings24`. Verify each name exists in WPF-UI 4.3 (Context7/source); if one does not exist, stop with a proposal — do not substitute. Keyboard: `Ctrl+1..5` follow the rail order.
- **Theme decision (user delegated Q4 to design, 2026-10-18):** default preference = **Sistem** (follow Windows app theme, live). If the system theme cannot be read, use **Gece**. The user can override in Ayarlar → Görünüm. The desktop worklog card always uses Gece (terminal identity).
- Selected item: `Tv.AccentSoft` background, 3 px `Tv.Accent` bar on the left edge, text `Tv.Text`.
- **Title bar**: logo mark (Aurora gradient "T" glyph, 20 px), breadcrumb, command palette box, theme toggle (◐: Sistem / Gece / Gün), window buttons. Window title (taskbar) = **"Talvora Kontrol Merkezi"** everywhere.
- **Command palette** (`Ctrl+K`): fuzzy list of navigation targets, services ("Talvora Dev — ayrıntılar"), and actions that already exist ("Yenile", "Talvora Admin'i yeniden başlat", "Ham günlüğü aç"). Destructive actions open the same confirmation dialog as their buttons.
- Keyboard: `Ctrl+1..5` navigation, `F5` refresh, `Ctrl+F` focuses the page search, `Esc` closes palette/dialogs, `Alt+←` back from detail.
- Window: `WindowBackdropType.Mica` on Windows 11 (build ≥ 22000) with `Tv.Bg` at 0.85 opacity over it; solid `Tv.Bg` on Windows 10. Rounded corners as today.

## 5. Screens

### 5.1 Genel bakış (Overview) — 1280 × 800

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  ╭──────────╮   Her şey yolunda.                                    [ Yenile ]        │ hero band (XL radius)
│  │  ◜‾‾◝    │   5 bileşenin 4'ü hazır, Penpot isteğe bağlı kapalı.                    │
│  │ (  4/5 ) │   Son kontrol 14:32 · Talvora 3.1.0                                    │
│  │  ◟__◞    │                                                                        │
│  ╰──────────╯   (attention variant)  Talvora Admin tüneli yeniden bağlanıyor.         │
│   Nabız 112px                        [ Sorunu onar ]  [ Ayrıntılar ]                  │
├──────────────────────────────────────────────────────────────────────────────────────┤
│  BAĞLANTI ZİNCİRİ                                                    Tümünü göster →  │
│   ●────────●═══════●───────────●- - - -◌                                              │
│  Bu bilgisayar  Talvora  Dev/Admin   Güvenli     ChatGPT                              │
│                 çekirdeği yüzeyleri  tüneller    Business                             │
│                    └──●──●──● Gitea · Proxy · Gitea MCP     ◌ Penpot (isteğe bağlı)   │
├───────────────────────────────┬──────────────────────────────────────────────────────┤
│  Hazır   4   Dikkat 1   Kapalı 1   Son 24 saatte 2 otomatik onarım                    │ one compact tile row (not 4 big cards)
├───────────────────────────────┴──────────────────────────────────────────────────────┤
│  HİZMETLER                                         [ Bileşen ara… ] [ Tümü ▾ ]       │
│  ┌────────────────────────────────────────────────────────────────────────────────┐  │
│  │ [logo] Talvora Çekirdek   Yerel MCP     ● Hazır        2 dk önce        ⋯      │  │
│  │ [logo] Talvora Dev        Tünel         ● Hazır        2 dk önce        ⋯      │  │
│  │ [logo] Talvora Admin      Tünel         ▲ Dikkat       az önce          ⋯      │  │
│  │ [logo] Gitea              Yerel zincir  ● Hazır        1 dk önce        ⋯      │  │
│  │ [logo] Penpot             İsteğe bağlı  ○ Kapalı       —                ⋯      │  │
│  └────────────────────────────────────────────────────────────────────────────────┘  │
│                                                         (≥1360 px: right column     │
│                                                          "Son olaylar", 6 items)     │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

- Status sentence is generated from the snapshot in plain Turkish (copy deck §9); it names the most important problem first.
- "Sorunu onar" appears only when `ControlCenterRepairService` reports a repairable state; it runs the same repair path as today.
- Tile row values come from the snapshot and the event store; "otomatik onarım" count = recovery events in the last 24 h (only if the store has them; otherwise hide the tile).
- Service row: 56 px tall, logo from `McpLogoResolver`, name (`BodyStrong`), role chip (`Caption`, `SurfaceRaised` pill), state pill (glyph + word), relative last-check time (absolute time in tooltip), overflow `⋯` with lifecycle actions. Entire row clickable → detail. Keyboard: arrow keys move, Enter opens.
- Width < 1000 px: tile row wraps to 2×2, chain becomes vertical, right column hidden.

### 5.2 Bağlantı zinciri (full page)

The same constellation, full size, laid out left→right per chain with real per-component health from `ControlCenterComponentHealthService` (control plane, response delivery, queue, dispatcher, backend, proxy, MCP process, tunnel). Node click → side sheet (360 px) with the component's plain-language state, last check time, and "Bu katmanı onar" when the existing repair service supports layer-level repair. Legend at the bottom: ● hazır, ▲ dikkat, ✕ ulaşılamıyor, ○ isteğe bağlı, ↻ onarılıyor.

### 5.3 Hizmet ayrıntısı (Service detail)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ← Hizmetler                                                                           │
│ [logo 40]  Talvora Admin                     ▲ Dikkat istiyor                         │
│            ChatGPT Business için yönetim yüzeyi · Tünel                               │
│            [ Onar ]  [ Yeniden başlat ]  [ Durdur ]                                   │
│  ⟶ operation banner (Repairing colour) while an action runs, with real elapsed time  │
├──────────────────────────────────────────────────────────────────────────────────────┤
│  Durum │ Olaylar │ Teknik kanıt                                                       │ tabs
├──────────────────────────────────────────────────────────────────────────────────────┤
│  Bu hizmetin zinciri:  ● Talvora çekirdeği ─ ● Admin yüzeyi ─ ▲ Güvenli tünel         │
│  Ne oldu?   OpenAI tarafı yavaş yanıt veriyor. Talvora yerel tarafı sağlam tutuyor     │
│             ve 5 dakika sonra yeniden deneyecek.                                       │
│  Son kontrol 14:31:12 · Son başarılı bağlantı 13:58                                    │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

- "Teknik kanıt" tab = today's technical details + raw log for this service, in `Tv.SurfaceSunken`, `Mono`, with copy button; content passes the existing redaction (unchanged services).
- Manual stop intent is shown explicitly: "Bu hizmeti siz durdurdunuz; Talvora otomatik başlatmayacak."

### 5.4 Olaylar (Events)

Day-grouped timeline ("Bugün", "Dün", "15 Ekim Çarşamba"). Each item: time (`Mono` caption), severity glyph, one-line Turkish summary, service chip; expand for detail. Filter chips: `Tümü · Sorunlar · Onarımlar · Yaşam döngüsü`, search box, service filter. "Canlı ham günlük" moves into a secondary toggle at the top right ("Ham günlüğü göster") and opens the sunken mono panel with live tail (existing 2 s timer only while visible).

### 5.5 Ayarlar ve kurulum

- **Kurulum stepper** card (replaces the yellow banner): steps `1 Talvora çekirdeği · 2 Güvenli tüneller · 3 ChatGPT Business bağlantısı`, each with a state glyph and one sentence. Inputs (tunnel IDs, admin key) appear inside the active step only. Copy: "Bu anahtar yalnızca kurulum sırasında kullanılır ve saklanmaz." (S5). Technical names (tunnel-client, runtime key) only inside a `Teknik ayrıntılar` expander.
- **Görünüm**: Tema (Sistem / Gece / Gün), Hareket (Sistem ayarını izle / Azalt), Kenar çubuğu (Otomatik / Daralt / Genişlet). Persist per user next to the existing window-placement file.
- **Güvenlik** (after S2): "Bağlantı anahtarını yenile" with confirmation.
- **Hakkında**: version string from R5, source commit (short), links to README/SECURITY (open in browser).

### 5.6 Empty, loading and error states

- Loading: skeleton rows (SurfaceRaised blocks with a 1.2 s shimmer; static when reduced motion) — never "Kontrol ediliyor..." text alone.
- Service unreachable: row stays, pill "✕ Ulaşılamıyor", sentence explains what is protected.
- Control Center cannot reach the service at all: full-page calm state with the Nabız ring grey: "Talvora hizmetine şu anda ulaşılamıyor. Bilgisayarınızdaki dosyalar etkilenmedi. [Yeniden dene] [Hizmeti başlat]" (start uses the existing lifecycle path).

## 6. Components (build once, reuse)

| Component | Notes |
|---|---|
| `StatusPill` | glyph + word, background = state colour at 14 % opacity, text = state colour (Gece) / darker state colour (Gün). States: Ready, Attention, Offline, Neutral, Repairing. |
| `NavRailItem` | `RadioButton`-based (one selected), icon + label, accent bar. |
| `PulseRing` | custom `FrameworkElement` (sample §7.3). Properties: `Segments` (state list), `ReadyCount`, `TotalCount`, `IsAllReady`. Exposes `AutomationProperties.Name` = status sentence. |
| `ChainView` | `ItemsControl` with a `Canvas`-based panel; nodes = `ChainNode` (circle 14 px, hollow for optional), links = `Path` with `StrokeDashArray="2 6"` and animated `StrokeDashOffset` when both ends ready. |
| `ServiceRow` | template described in §5.1. |
| `MetricInline` | label + tabular number + optional caption. |
| `EvidencePanel` | sunken, mono, copy, wrap toggle, max height + scroll. |
| `CommandPalette` | popup, `TextBox` + `ListBox`, fuzzy match (simple subsequence scoring is enough). |
| `StepperCard` | vertical steps with connectors. |

## 7. Sample code (starting points — verify APIs with Context7 for WPF-UI 4.3 and .NET 10 WPF)

### 7.1 Token dictionary (excerpt, `Themes/Tokens.Gece.xaml`)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="Tv.Bg.Color">#0A0E15</Color>
    <Color x:Key="Tv.Surface.Color">#101722</Color>
    <Color x:Key="Tv.Accent.Color">#5BD8FF</Color>
    <Color x:Key="Tv.Ready.Color">#3DDC97</Color>
    <Color x:Key="Tv.Attention.Color">#F5B84B</Color>
    <Color x:Key="Tv.Offline.Color">#FF6B81</Color>
    <Color x:Key="Tv.Neutral.Color">#7C8AA3</Color>
    <Color x:Key="Tv.Aurora.Start.Color">#5BD8FF</Color>
    <Color x:Key="Tv.Aurora.End.Color">#9B8CFF</Color>

    <SolidColorBrush x:Key="Tv.Bg" Color="{StaticResource Tv.Bg.Color}" />
    <SolidColorBrush x:Key="Tv.Surface" Color="{StaticResource Tv.Surface.Color}" />
    <SolidColorBrush x:Key="Tv.Accent" Color="{StaticResource Tv.Accent.Color}" />
    <LinearGradientBrush x:Key="Tv.Aurora" StartPoint="0,0" EndPoint="1,1">
        <GradientStop Offset="0" Color="{StaticResource Tv.Aurora.Start.Color}" />
        <GradientStop Offset="1" Color="{StaticResource Tv.Aurora.End.Color}" />
    </LinearGradientBrush>
    <!-- …all keys from §3.1, same names in Tokens.Gun.xaml … -->
</ResourceDictionary>
```

### 7.2 Theme manager (follow Windows, manual override, keep WPF-UI in sync)

```csharp
internal enum TalvoraThemePreference { System, Gece, Gun }

internal static class TalvoraThemeManager
{
    private static readonly Uri GeceUri = new("pack://application:,,,/Talvora.Tray;component/Themes/Tokens.Gece.xaml");
    private static readonly Uri GunUri  = new("pack://application:,,,/Talvora.Tray;component/Themes/Tokens.Gun.xaml");

    public static void Apply(Application app, TalvoraThemePreference preference)
    {
        var dark = preference switch
        {
            TalvoraThemePreference.Gece => true,
            TalvoraThemePreference.Gun => false,
            _ => ApplicationThemeManager.GetSystemTheme() is SystemTheme.Dark or SystemTheme.HCBlack, // verify API name
        };

        // 1) WPF-UI control templates must match the Talvora palette (fixes today's Light/Dark mismatch).
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica, updateAccent: false);

        // 2) Swap the Talvora token dictionary in place (components use DynamicResource).
        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source == GeceUri || d.Source == GunUri);
        var next = new ResourceDictionary { Source = dark ? GeceUri : GunUri };
        if (current is null) dictionaries.Add(next); else dictionaries[dictionaries.IndexOf(current)] = next;
    }
}
```

Use WPF-UI's system-theme watcher (verify `SystemThemeWatcher.Watch(window)` in 4.3) when the preference is `System`. Remove `<ui:ThemesDictionary Theme="Light" />` from XAML and let the manager own it.

### 7.3 Nabız ring

```csharp
public sealed class PulseRing : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<ComponentVisualState>), typeof(PulseRing),
        new FrameworkPropertyMetadata(Array.Empty<ComponentVisualState>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ComponentVisualState> Segments
    {
        get => (IReadOnlyList<ComponentVisualState>)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || Segments.Count == 0) return;
        const double thickness = 8, gapDegrees = 6;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - thickness;
        var allReady = Segments.All(s => s == ComponentVisualState.Ready);
        var sweep = 360.0 / Segments.Count;

        for (var i = 0; i < Segments.Count; i++)
        {
            var start = -90 + i * sweep + gapDegrees / 2;
            var end = start + sweep - gapDegrees;
            var brush = allReady ? (Brush)FindResource("Tv.Aurora") : BrushFor(Segments[i]);
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, Arc(center, radius, start, end));
        }
    }

    private Brush BrushFor(ComponentVisualState state) => (Brush)FindResource(state switch
    {
        ComponentVisualState.Ready => "Tv.Ready",
        ComponentVisualState.Attention => "Tv.Attention",
        ComponentVisualState.Offline => "Tv.Offline",
        ComponentVisualState.Repairing => "Tv.Repairing",
        _ => "Tv.Neutral",
    });

    private static Geometry Arc(Point c, double r, double startDeg, double endDeg)
    {
        static Point P(Point c, double r, double deg) =>
            new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = P(c, r, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment(P(c, r, endDeg), new Size(r, r), 0,
            isLargeArc: endDeg - startDeg > 180, SweepDirection.Clockwise, isStroked: true));
        var g = new PathGeometry([figure]);
        g.Freeze();
        return g;
    }
}
```

The centre text ("4/5", `Title` style) and the breathing animation are XAML on top of the control. Re-render when the theme dictionary changes (`DynamicResource` brushes are looked up in `OnRender`, so call `InvalidateVisual()` on theme change).

### 7.4 Chain link flow (only when both ends are ready and motion is allowed)

```xml
<Path Data="{Binding Geometry}" Stroke="{DynamicResource Tv.Accent}" StrokeThickness="2"
      StrokeDashArray="2 6" StrokeDashCap="Round">
    <Path.Style>
        <Style TargetType="Path">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsFlowing}" Value="True">
                    <DataTrigger.EnterActions>
                        <BeginStoryboard x:Name="Flow">
                            <Storyboard RepeatBehavior="Forever">
                                <DoubleAnimation Storyboard.TargetProperty="StrokeDashOffset"
                                                 From="0" To="-16" Duration="0:0:1.6" />
                            </Storyboard>
                        </BeginStoryboard>
                    </DataTrigger.EnterActions>
                    <DataTrigger.ExitActions><StopStoryboard BeginStoryboardName="Flow" /></DataTrigger.ExitActions>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Path.Style>
</Path>
```

`IsFlowing = bothEndsReady && motionAllowed && windowActive`. Pause on `Deactivated`/minimize to keep idle CPU near zero (measure in UI8: < 1 % CPU idle on the overview).

### 7.5 Status pill style

```xml
<Style x:Key="Tv.StatusPill" TargetType="Border">
    <Setter Property="CornerRadius" Value="{StaticResource Tv.Radius.S}" />
    <Setter Property="Padding" Value="8,3" />
    <Setter Property="Background" Value="{DynamicResource Tv.Neutral.Soft}" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding State}" Value="Ready">
            <Setter Property="Background" Value="{DynamicResource Tv.Ready.Soft}" />
        </DataTrigger>
        <!-- Attention, Offline, Repairing … -->
    </Style.Triggers>
</Style>
```
(`*.Soft` brushes = state colour at 14 % opacity, defined in both token files.)

## 8. Architecture of the new UI

- Move views to XAML: `Views/ShellWindow.xaml`, `Views/OverviewView.xaml`, `Views/ServicesView.xaml`, `Views/ServiceDetailView.xaml`, `Views/ChainView.xaml`, `Views/EventsView.xaml`, `Views/SettingsView.xaml`; components under `Controls/`; styles under `Themes/`.
- ViewModels: thin, testable, no WPF types except `ICommand`; they wrap the existing services (do not change service behaviour). Use **CommunityToolkit.Mvvm** (MIT) for `ObservableObject`/`RelayCommand`/source generators — take the current stable version from Context7/NuGet, add it to `Directory.Packages.props`, lock files and `THIRD-PARTY-NOTICES.md`. If it cannot be used (licence, .NET 10 incompatibility, analyzer conflict with `TreatWarningsAsErrors`), stop with a proposal — no hand-written replacement without Claude's decision.
- Keep: UI-thread responsiveness guarantees (all I/O off the dispatcher; existing tests in `ControlCenterUiResponsivenessSourceRegression.ps1` must be re-expressed as behaviour tests on ViewModels — e.g., "refresh does not run on the dispatcher thread").
- Delete the old imperative builders only in UI8, after every screen is replaced. **Amended (Claude, audit 2026-10-24 (17)):** a card may delete the legacy builder of the screen it replaces once no code references it (UI5 → `ControlCenterWindow.Events.cs`); UI8 removes whatever remains.
- Desktop worklog card (`DesktopProgressNotificationWindow*`): its layout and behaviour are owned by `docs/codex/DESIGN-WORKLOG.md` (cards N1–N8). UI cards never change it; UI7 only checks that the worklog uses the shared Gece token values listed in DESIGN-WORKLOG §7.

## 9. Copy deck (authoritative Turkish strings)

| Situation | Text |
|---|---|
| Product name (title bar, taskbar, tray tooltip) | Talvora Kontrol Merkezi |
| All ready | Her şey yolunda. |
| All ready, sub-line | {ready} bileşenin hepsi hazır. Son kontrol {time}. |
| Some optional off | {ready}/{total} bileşen hazır; {name} isteğe bağlı olarak kapalı. |
| One needs attention | {name} dikkat istiyor: {plainReason} |
| Several need attention | {count} bileşen dikkat istiyor. En önemlisi: {name}. |
| Upstream slow (R4) | OpenAI tarafı yavaş yanıt veriyor. Talvora yerel tarafı sağlam tutuyor ve {minutes} dakika sonra yeniden deneyecek. |
| Repairing | {name} onarılıyor… ({elapsed}) |
| Repair ok | {name} yeniden hazır. |
| Repair failed | {name} onarılamadı. Yerel dosyalarınız etkilenmedi. Talvora {minutes} dakika sonra tekrar deneyecek. |
| Manual stop | Bu hizmeti siz durdurdunuz; Talvora otomatik başlatmayacak. |
| Service unreachable | Talvora hizmetine şu anda ulaşılamıyor. Bilgisayarınızdaki dosyalar etkilenmedi. |
| State words | Hazır · Dikkat istiyor · Ulaşılamıyor · İsteğe bağlı kapalı · Onarılıyor · Bekliyor |
| Role chips | Yerel MCP · Tünel · Yerel zincir · Tasarım · İsteğe bağlı |
| Buttons | Onar · Yeniden başlat · Başlat · Durdur · Yenile · Kopyala · Ham günlüğü göster · Ayrıntılar |
| Confirm stop | {name} durdurulsun mu? ChatGPT bu hizmete siz yeniden başlatana kadar ulaşamaz. [Durdur] [Vazgeç] |
| Setup step 2 | Güvenli tüneller ChatGPT'nin bu bilgisayardaki Talvora'ya güvenle ulaşmasını sağlar. |
| Admin key note | Bu anahtar yalnızca kurulum sırasında kullanılır ve saklanmaz. |
| Rail groups | (no overline labels in collapsed mode; expanded: "ÇALIŞMA ALANI", "SİSTEM") |

Rules: no "MCP", "tunnel-client", "runtime", "control plane" in main surfaces — only inside "Teknik kanıt"/"Teknik ayrıntılar". Use Turkish quotation and number formatting (`tr-TR`): "14:32", "2 dk önce", "15 Ekim Çarşamba".

## 10. Accessibility & quality bars

- Every interactive element: `AutomationProperties.Name` (Turkish), keyboard reachable, visible focus ring 2 px `Tv.Accent` with 2 px offset.
- Hit targets ≥ 32 × 32 px (rows 56 px).
- Works at 100/125/150/200 % DPI and mixed-DPI multi-monitor (window placement already DPI-aware — keep it).
- High contrast: fall back to `SystemColors` (WPF-UI handles HC templates; tokens switch to system brushes when `SystemParameters.HighContrast`).
- Reduced motion respected (§3.4).
- Idle CPU on overview < 1 %; first paint < 500 ms after window show (measure with a Stopwatch log in smoke mode).

## 11. Implementation cards

Each card = one commit, Awwwards evidence refreshed (cite pages actually reviewed), Context7 IDs for WPF-UI/WPF/CommunityToolkit used, before/after PNGs of **both themes** (Q5 smoke) attached to the Gitea issue, `Invoke-QualityGate.ps1` green, installed-runtime check of the tray.

### UI1 — Tokens, theme manager, typography

> **Amended by P-18 (Claude, 2026-10-20)** — read the decision text in `docs/codex/PROPOSALS.md` → "Decided" before starting; where it differs from this card, the decision wins.

**Files:** new `src/Talvora.Tray/Themes/Tokens.Gece.xaml`, `Tokens.Gun.xaml`, `Typography.xaml`, `Controls.xaml`; `ControlCenterTheme.xaml` (reduce to merges), `ControlCenterApplication.cs`, new `TalvoraThemeManager.cs`, settings persistence next to window placement.
**Do:** §3 + §7.1–7.2; fix the Light/Dark mismatch; Mica on Windows 11; replace hard-coded colours in Control Center code with resource lookups (the layout code is replaced later — only colours/fonts now); fix the primary-button hover contrast.
**Don't:** change layout; touch the desktop worklog window.
**Acceptance:** both themes render (Q5 PNGs), live switch without restart, contrast checks pass for text tokens (write a tiny test that computes WCAG ratios from the token files).

### UI2 — Shell, navigation rail, title bar, command palette

**Files:** new `Views/ShellWindow.xaml(.cs)`, `ViewModels/ShellViewModel.cs`, `Controls/NavRailItem`, `Controls/CommandPalette`; `ControlCenterApplication.cs` (show the new shell), `ControlCenterWindow.Chrome.cs` (placement logic moves into the shell, unchanged behaviour), `Directory.Packages.props` (+CommunityToolkit.Mvvm if adopted).
**Do:** §4; existing pages are hosted temporarily inside the new content host (adapter) so nothing is lost mid-migration; title "Talvora Kontrol Merkezi".
**Don't:** break window placement persistence or tray "open/close" behaviour.
**Acceptance:** keyboard map works; UI Automation tree lists rail items with Turkish names; placement restore still correct on mixed DPI (manual user check listed in STATUS).

### UI3 — Overview with Nabız ring and chain strip

**Files:** `Views/OverviewView.xaml`, `ViewModels/OverviewViewModel.cs`, `Controls/PulseRing.cs`, `Controls/ChainView.*`, status-sentence builder `ViewModels/StatusNarrator.cs` (+ unit tests with snapshot fixtures).
**Do:** §5.1, §6, §7.3–7.5, §9 strings; data only from existing services. **Amended by P-33 (Claude, 2026-10-24):** also build the §5.2 full page (`Views/ChainView.xaml`, same `ChainView` control at full size, side sheet, legend) and replace UI2's "Bağlantı zinciri" placeholder with it.
**Acceptance:** narrator tests for: all ready, optional off, one attention, several, unreachable; PNGs both themes; idle CPU measurement logged.

### UI4 — Services list and service detail

**Files:** `Views/ServicesView.xaml`, `Views/ServiceDetailView.xaml`, ViewModels, `Controls/StatusPill`, `Controls/ServiceRow`, `Controls/EvidencePanel`; remove the corresponding imperative code paths in `ControlCenterWindow.cs`/`.Detail.cs` once replaced.
**Do:** §5.3; lifecycle actions call the existing `ControlCenterLifecycleService`/`ControlCenterRepairService` with the existing confirmation semantics; operation banner shows real elapsed time.
**Acceptance:** start/stop/restart/repair on a test MCP from the UI behave as before (smoke + one live check); manual-stop message shown.

### UI5 — Events and raw log

**Files:** `Views/EventsView.xaml`, ViewModel, reuse `EvidencePanel`; `ControlCenterWindow.Events.cs` paths removed when replaced.
**Do:** §5.4; raw log tail timer runs only while the panel is visible.
**Acceptance:** day grouping and filters unit-tested; copy-to-clipboard still redacted.

> **Amended (Claude, audit 2026-10-25 (18)) — apply before committing UI5:** (1) empty state only after the first read finished (`IsLoading == false`): with no search text, category `Tümü` and service `Tümü` show "Son 7 günde kaydedilmiş olay yok."; otherwise show "Bu filtrelerle eşleşen olay yok." (both `Tv.Type.Body`, `Tv.TextMuted`, centred at the top as now). (2) The service pill is collapsed when the event has no service name. (3) "Yaşam döngüsü" matches categories `operation` **and** `startup`. Extend the UI5 unit test for all three.

### UI6 — Settings and setup stepper

**Files:** `Views/SettingsView.xaml`, `Controls/StepperCard`, ViewModel; `ControlCenterWindow.Setup.cs` replaced.
**Do:** §5.5; implements S5 copy; theme/motion/rail preferences persisted; S2 rotate button only if S2 is done (otherwise hidden).
**Acceptance:** setup can be completed end-to-end (user step with real tunnel IDs listed in STATUS); jargon only inside the expander.

### UI7 — Tray icon, tray menu, worklog colour alignment, About

> **Amended by TI1 (Claude, 2026-10-24):** the tray icon itself is delivered by TI1 (`TrayIconArt`). UI7 must not change `TrayIconArt.cs`, `TrayIconFactory.CreateStatusIcon` or the icon timer; the "single-colour silhouette" and "state dot" sentences below are void. UI7 keeps the menu, tooltip, worklog colour alignment and About.

**Files:** `TrayIconFactory.cs`, `TrayApplicationContext.cs` (menu), `DesktopProgressVisualTheme.cs` (colour mapping only), Settings "Hakkında".
**Do:** tray icon = the existing Talvora "T" mark from `assets/Talvora.png`/`Talvora.ico`, rendered as a single-colour silhouette: `#FFFFFF` when the taskbar is dark, `#1A1A1A` when light (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`, live). State dot: circle, diameter 37.5 % of the icon size, bottom-right, 1 px ring in the taskbar colour; colours Ready `#3DDC97`, Attention `#F5B84B`, Offline `#FF6B81`; Neutral/unknown = no dot. Tray menu items in this order: "Kontrol Merkezini aç" (bold, default), "Durum: {§9 status sentence}" (disabled text), separator, "Yenile", separator, "Çıkış". Tooltip: "Talvora Kontrol Merkezi — {§9 status sentence}". The same "T" mark is the 16 px glyph in the worklog header (DESIGN-WORKLOG §7). No change to the worklog card here.
**Acceptance:** icon renders crisp at 16/20/24/32 px (PNG proof), notification regression suite green.

### UI8 — Polish, accessibility, cleanup, proof

> **Amended (Claude, audit 2026-10-24 (15)):** the Control Center smoke must take `*-loaded.png` only after the first real status arrived (the summary no longer shows the loading text); on a cold start it was captured too early.
>
> **Amended (Claude, audit 2026-10-24 (17)):** idle CPU (§10, < 1 %) must be measured on the **installed tray with the Control Center window open** on Genel bakış, sampled over 10 s starting 30 s after the window loaded — not inside the smoke process, whose own start-up work showed 7–10 %. If the real window is ≥ 1 %, find the cause (profile) and fix it in this card.

**Do:** motion pass + reduced motion + high contrast; DPI matrix; accessibility audit (Accessibility Insights or UIA test listing names/roles); delete dead imperative builders (`ControlCenterWindow*.cs` parts no longer used) and obsolete UI regex tests; refresh `docs/media/control-center.png` (both themes) and README screenshots.
**Acceptance:** Q5 smoke green in both themes; no unused UI code (build with analyzers for unused private members); README shows the new design.
