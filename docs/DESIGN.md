# ArchTweaker design system: Carbon

The one style the app ships (since 2.4.1). Every page reads its colours, fonts and corner radius from `Theme` in `src/Ui/Theme.cs`. Don't hardcode a hex value, a `new Font(...)` or a radius in a page.

## Tokens

### Colour

All pairs below are checked with `palettes.py`. The checks are: text ≥ 7:1, muted text ≥ 4.5:1, text on the accent ≥ 4.5:1, accent and status colours ≥ 3:1. Status colours used as small text in light mode are ≥ 4.5:1.

| Token | Dark | Light | Use |
|---|---|---|---|
| `Bg` | #0B0B0C | #F4F4F6 | Window and page background |
| `Panel` | #161618 | #FFFFFF | Rows, cards, inputs |
| `PanelHover` | #1F1F22 | #F0F0F3 | Hover step for rows and secondary buttons |
| `Side` | #070708 | #EAEAEE | Sidebar, Apply bar |
| `Accent` | #E11D2E | #C8102E | Primary button, toggle on, active nav edge |
| `AccentDark` | #B5121F | #9E0C24 | Primary hover |
| `OnAccent` | #FFFFFF | #FFFFFF | Text on the accent |
| `Border` | #2A2A2E | #D9D9DE | Secondary button outline |
| `PendingBg` | #3A1418 | #FDE3E6 | Row with an unapplied change, active nav item |
| `Text` | #ECECEE | #16161A | Body text |
| `Muted` | #A1A1A8 | #5C5C66 | Descriptions, captions, toggle off |
| `Strong` | #FFFFFF | #000000 | Titles of applied tweaks, focus ring |
| `Ok` | #34C759 | #0E7434 | High gain, success |
| `Warn` | #FFB020 | #7E4A00 | Moderate risk, pending edge |
| `Bad` | #FF6B6B | #AE1219 | Advanced risk, errors |
| `Grid` | #232327 | #E4E4E9 | Grid lines, press step |
| `Sel` | #4A1219 | #FCD2D8 | Selected grid row |
| `AltRow` | #121214 | #F9F9FB | Alternate grid row |
| `LogBg` / `LogFg` | #050506 / #D9A0A4 | #FCFCFD / #8E1B29 | Activity log |
| `AccentOff` | #2E2E33 | #CFCFD6 | Inactive bars (impact meter) |

### Accent colours (3.37)

`Theme.AccentIndex` picks one row of `AccentSets`: Red (Carbon Red 60, default), Blue (Carbon Blue 60), Teal, Graphite, Pink, Neon cyan; then Windows (the system accent, made readable by `AccentMaker`) and Custom. It replaces Accent, AccentDark, OnAccent, PendingBg, Sel and LogFg. Never use a raw accent hex in a page. Anything drawn on the accent uses `OnAccent`, which is black on teal, graphite, pink and neon cyan. 3.37 removed the older Red (#E11D2E) and Blue (#2F6FEB), near copies of the Carbon ones; `Theme.AccentFromSaved` maps a saved accent of any settings version to this order (`AccentV` "4"). `python3 docs/palettes.py` checks every accent.

### Type

There is one family per role. Fallbacks are picked at startup with `Theme.Pick`. Sizes are cached, so calling `Theme.Semi(x)` or `Theme.Body(x)` in `OnPaint` is safe.

| Token | Face | Size | Use |
|---|---|---|---|
| `FH` | Segoe UI Semibold | 15 | Page title |
| `Semi(12)` | Segoe UI Semibold | 12 | Dialog title |
| `FB` | Segoe UI Semibold | 10 | Tweak title, group label, primary button |
| `F` | Segoe UI | 9.5 | Body, buttons, inputs |
| `FS` | Segoe UI | 8.5 | Descriptions, gain text |
| `Semi(8)` | Segoe UI Semibold | 8 | Chips |
| `Mono` | Cascadia Mono → Consolas | 9.5 | Log, dialog details, raw values |
| `LogoFont` | Segoe UI Black | 15 | Wordmark only |

### Scale (DPI)

- Write every pixel value at 96 dpi. Code that builds UI calls `Theme.S(px)` (also `S(w, h)` and `S(Padding)`), or leaves the value raw and lets `Theme.ScaleTree` scale the tree. Chrome is scaled once in the `MainForm` constructor; each page is scaled after its first `OnShow`.
- Controls that lay themselves out in `S()` units implement `Theme.IDpiSelfScaled` so they aren't scaled twice (TweakRow does).
- Give font sizes in points at 96 dpi (`Theme.Semi(10f)`); the cache scales them to the monitor.
- A monitor change raises `WM_DPICHANGED`; `MainForm` rebuilds itself at the new scale.

### Shape and space

- **Radius:** `Theme.Radius = 4` for tiles, cards and focus rings. Pills (toggle, chips, theme switch) use half their height.
- **Spacing scale:** 2 / 4 / 6 / 8 / 12 / 16 / 20 px. Page padding is 20 px. A row's inner left edge for text is 70 px (after the toggle). Gaps between rows are 6 px, and buttons sit 8 px apart. Older pages still have a few off-scale values (7 px chip padding, 22 px dialog padding); use the scale in anything new.
- **Accent strip:** a 2 px `Accent` line under the page title. It is the only decorative use of colour.

### Depth and read-outs (3.36: "precision instrument")

The look is an instrument panel: exact, quiet, and red only where something needs attention. Depth comes from light and edges, never from new colours.

- **Depth tokens** (`Theme.Derive`, from whatever scheme is active, so every scheme and High Contrast get them; never set them by hand):
  - `Line` is the hairline around and between surfaces (Panel mixed 11 % toward Text in dark, 14 % in light).
  - `Raised` is the card fill. It equals `Panel`, so child controls on a card never show a seam.
  - `Hi` is the 1 px light top edge of a raised surface, in dark schemes only.
  - `ShadowA` is the shadow strength: 110 in dark, 34 in light, 0 in High Contrast.
  - `AccentSoft` is a quiet accent tint, used only for the halo of the weakest link.
  - `AccentInk` is the accent as small text, lifted until it is ≥ 4.5:1 on `Panel`.
- **`Gfx.Surface(g, rect, fill, shadow)`** is the only way to draw a card, tile, row or panel. It draws a stacked soft shadow (offset down, three layers), the fill, the `Hi` top edge and a `Line` border. Leave 3 px under a surface for its shadow.
- **`Gfx.Hair`** draws dividers inside a surface. Columns of read-outs share one surface split by hairlines; they are not separate cards.
- **Read-outs:** a caption label (`Theme.FT`, upper case in English through `Theme.Cap` / `Theme.CapFit`, `Muted`), then the number in `Theme.Num(size)`. That face is Bahnschrift, the DIN face Windows ships, with even-width figures; it falls back to Segoe UI Variable Display. A unit or a denominator ("/100", "/14") is set smaller and `Muted`.
- **Scales:** `Gfx.Segments` draws a segmented meter (20 cells for the health score). Thin rules (2–3 px) show a share applied.
- **Buttons** are drawn by `FocusButton`: rounded with `Radius`, a `Line` border (the accent for `Lead`), and a light top edge. Hover is `PanelHover`, press is `Grid` with the label 1 px lower. `Chevron = true` adds a "›" for a button that opens another page.
- **State never uses a coloured edge stripe.** The tweak switch, title weight and chips carry a row's state; a pending row has the pending tint and an amber hairline. Info and Pro bars carry their tone in the tint, the glyph and hairlines. Advisor findings use a coloured dot and a caption label.
- **Motion:** one authored moment. Home's health score counts up and its scale fills (≈ 600 ms, ease-out quart) from the last value shown. The chain wave from 3.3 still runs after a change. Both are off when Windows animations are off.
- **Red** is the brand and the signal: the main action (one filled red button per view), the active page, the weakest link. It is never decoration.

## Components

| Component | Where | States | Keyboard | Screen reader |
|---|---|---|---|---|
| **Primary button** `Theme.Primary` | One per bar: the main action | Default `Accent` → hover `AccentDark` → press darker; disabled faded | Tab, Enter/Space | Button, named by its label |
| **Secondary button** `Theme.Btn` | Every other action | `Panel` → hover `PanelHover` → press `Grid` | Same | Same |
| **FocusButton** (base of both) | Automatic | Adds a 2 px `Strong` focus ring when focused from the keyboard | | |
| **Toggle** `UiKit.Toggle` | Tweak on/off | Off: outlined `Muted` track and knob. On: filled `Accent` track, white knob. Hover darkens, disabled fades | Tab, Space | Check button named after the tweak, with state Checked. Its description gives what it does, gain, risk and restart need |
| **NavItem** | Sidebar page | Default `Side`, hover `PanelHover`, active `PendingBg` + 3 px accent edge + bold | Tab, ↑/↓ between items, Enter/Space opens | Page tab, Selected when active, count as description |
| **NavItem extras** (2.8) | Sidebar | Icon from Segoe Fluent Icons / MDL2 Assets (`Muted`, `Accent` when active); count pill: neutral at 0, `Accent` track for partial, solid `Accent` + `OnAccent` text when complete | Tooltip = full page name + "a of b applied" | Description "a of b applied" |
| **Page subtitle** | Top bar, under the title | `FS`, `Muted`, ellipsis; tweak pages "n tweaks · a applied" | Tooltip = page purpose | Label "pageSubtitle" |
| **Empty / loading overlay** `Skin.Loading` / `Skin.Loaded` / `Skin.SetEmpty` | Any ListView / DataGridView | `F`, `Muted`, centred on `Panel` | n/a | Sets the list's AccessibleDescription |
| **Toast** (2.9) `MainForm.Notify(msg, kind)` | Bottom-right over the content | `Panel` card, `Border` outline; edge/glyph `Accent` (ok), `Warn` (restart / sign-out), `Bad` (failure); closes after 6 s or on click | n/a | Alert role, announced |
| **Compact rows** (2.9) `Theme.Compact` | Tweak lists | One line per tweak; the selected row and search matches expand | Focusing a row expands it | Unchanged |
| **NavHeader** | Sidebar section | Accent caret; hover turns `Strong` | Enter/Space toggles | Outline button, Expanded/Collapsed |
| **Chip** | Risk/restart tags | Text in its status colour on a 10 % tint | n/a | Read as text |
| **ImpactMeter** + word | Tweak gain | 0–3 bars; the word label always sits beside it, so meaning never relies on colour alone | n/a | Named "High gain" etc. |
| **TweakRow** (one owner-drawn control since 2.6) | Tweak list | Normal `Panel`; hover `PanelHover`; pending `PendingBg` + `Warn` edge; applied `Accent` edge; *checking* (state not read yet): outlined switch with three dots | Tab to the row; Space toggles, Enter shows details; focus opens details and scrolls into view | Check button named after the tweak; Checked / Unavailable / Busy states |
| **SearchField** | Top bar | `Panel` fill, `Border` frame → `Muted` on hover → 2 px `Accent` when focused; magnifier in `Muted` | Ctrl+F, Esc clears | Edit "Search all tweaks" |
| **Skin** (`Skin.Apply`, automatic via `Chrome.Polish`) | Every ComboBox, CheckBox, RadioButton, DataGridView, ListView | Drop-down: owner-drawn items (`Sel` highlight), frame `Border`/`Accent` on focus, `Muted` chevron. Checkbox: flat, `Muted` border, `Accent` when ticked. Grid/list headers: `Side` + `Muted` semibold | System default | System default |
| **ThemeSwitch** | Top bar | Dark: filled accent. Light: outlined, like the toggle | Tab, Space/Enter | Check button "Dark mode" |
| **Dlg** (`src/Ui/Dlg.cs`) | All questions and messages | Info (accent edge), Warn (`Warn` edge), Error (`Bad` edge + sound) | Enter = action, Esc = cancel. Destructive dialogs focus Cancel | Dialog named by its title. Details sit in a read-only box you can Tab into |

## Accessibility rules

- Every custom control is a tab stop and draws `Gfx.FocusRing` (2 px, `Strong`), never a dotted rectangle.
- Every input has an `AccessibleName`. `A11y.Fill` names any control a page left unnamed: inputs from the label before them, lists from the page.
- Main tab order follows reading order: sidebar → top bar → restart notice → page → details → Apply bar.
- State is never shown by colour alone: toggles change shape (outline vs fill), the meter has a word, and pending rows also get an edge.
- Shortcuts: Ctrl+F search, Ctrl+Enter apply, Esc clears search.

## Status line rule

`Log.Say()` is for results the user should see (applied/undone counts, presets, failures). It goes to the log and to the status line under the page. `Log.W()` is for everything else, and only reaches the log.

## Copy rules

- **Buttons:** verb + object ("Open Device Manager", "Remove 3 apps"). Never just "OK"/"Yes" for an action. Use "and", not "&" or "+".
- **Confirm dialogs:** the title is the question ("Remove 3 apps?"), the body is the consequence and how to undo it, and the buttons name the action ("Remove 3 apps" / "Cancel"). Use `destructive: true` when it can't be undone by ArchTweaker.
- **Errors:** title = what happened ("Couldn't check for updates"), body = what to do next, details = the raw error.
- **Empty states:** say why the list is empty and how to change that.
- **Tweak titles** describe the state when the toggle is **on** ("Disable Game DVR", "Display never dims"). Descriptions say what changes, then any trade-off.
- **Terms:** *restart* (not reboot) in prose, *apply* / *undo*, *tweak*, *PC*. Write numbers with units ("0.5 ms", "4096 icons").

## Language (3.0)

- Write every interface string in English in the code. `L.Apply` (run once on the main window) translates control text, accessible names, list and grid headers and combo items. It also translates text set later, through TextChanged and ControlAdded hooks.
- Owner-drawn controls call `L.T` when they paint and when they measure: always measure the translated text, never the English.
- Text built from pieces needs a pattern in `lang/ru.tsv`:
  - `Freed {0}.` → `Освобождено {0}.`
  - Counts use Russian plural forms: `{0} {0|изменение|изменения|изменений}`.
- Never translate data: registry values, paths, preset keys, page keys, log lines. Mark controls that show data with `Tag = "notr"`.
- Russian text is about 20–30 % longer: buttons size to their text, and fixed-width labels must ellipsize.
- Keep Russian terms consistent:
  - tweak = твик
  - Apply = Применить
  - Undo everything = Отменить всё
  - restore point = точка восстановления
  - power plan = схема электропитания

## Colour roles and view options (3.1–3.2)

- Red (Accent) marks the brand, the selected page and the main action only. "On / applied" uses `Theme.On` (status green); section titles are neutral; the weakest latency stage is amber.
- A Windows high-contrast theme overrides every token with `SystemColors` (`Theme.HighContrast`). Never hard-code a colour outside `Theme`, or it will ignore high contrast.
- Text size (`Theme.TextSize`: 100/115/130 %) scales fonts only. Size anything that holds text by measuring it, not by a fixed height. The logo keeps its size.
- Slim sidebar (`Theme.Slim`): icons only; every nav control keeps its name in a tooltip and its accessible name.
- A button that can't act right now is hidden, except the primary action (Apply), which is shown disabled so its place never moves.

## Colour schemes (3.37)

Four schemes: **Carbon dark** (default), **Carbon light**, **OLED black** (true black for OLED screens) and **Custom** (one
background colour; `SchemeMaker` builds the rest and keeps it readable). The accent is chosen separately and a scheme never
changes it. 3.37 removed Slate, Rose, Blossom and the four 3.29 design packages (Carbon Performance, Carbon Strict, Fluent
Native, Gaming HUD): they were near copies of Carbon dark with another accent. A saved removed scheme opens as Carbon dark
(Blossom as Carbon light) through `Theme.SchemeFromKey`.

Colour roles that hold in every scheme:
- **Red is brand and selection only.** High risk (Advanced, Danger zone) is `Theme.Risk`: Carbon Magenta 40 #FF7EB6 dark,
  Magenta 70 #9F1853 light. Errors stay `Theme.Bad`. Experimental is `Theme.Exp`: Purple 40 #BE95FF / Purple 70 #6929C4.
  The PRO chip is neutral (`Theme.Muted`). Every chip has a text label, so no state is colour only.
- **Lite and Pro look different.** Lite shows a red accent (Red, Carbon red) as Carbon blue (`Theme.EffectiveAccent`).
  Pro keeps the chosen accent. Pro-only pages and the Danger zone have an expert notice bar (Warn stripe).
  Experimental tweaks ask once more before Apply.
- **Focus** is a 2 px `Theme.Strong` ring (white on dark), never the accent, so focus never looks like an error.
- Carbon Red 60 on Carbon layer 01 (#262626) is exactly 3:1; a lighter card fails. `docs/palettes.py` checks every scheme
  with every accent; magenta and purple are ≥ 3:1 on every surface and tint.

## Don't

- Hardcode `Color.FromArgb`, hex, `new Font` or radius in a page (white knobs on the accent are the one exception).
- Call `MessageBox.Show` outside startup/crash paths in `Program.cs`; use `Dlg`.
- Put two primary buttons in one bar.
- Add a second style or palette; Carbon is the system.
- Draw a card, tile or row with `FillPath` + `Border`; use `Gfx.Surface` (3.36).
- Mark state with a coloured stripe on the edge of a card, row or bar (3.36).
