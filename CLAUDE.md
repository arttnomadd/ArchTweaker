# ArchTweaker — repo guide

An original Windows 10/11 performance/latency tweaker (C# WinForms), built on Linux into a single Windows
`.exe`. This file orients a new contributor (human or AI) fast. Decisions behind the design live in
[docs/adr/](docs/adr/README.md). UI tokens, components, accessibility and copy rules live in
[docs/DESIGN.md](docs/DESIGN.md): read it before touching any page; check colours with `python3 docs/palettes.py`.

## Build, test, run

```sh
# Build the Windows exe (Mono mcs → .NET Framework 4.5 refs, with a Mono-only-call audit)
bash tools/build-win.sh <version> /path/out.exe     # e.g. 3.35.1.0; must print "audit OK"

# Run the headless unit tests (pure logic: parsers, path-safety, updater, catalog)
sh tools/run-tests.sh     # must end "N passed, 0 failed"

# Windows UI smoke tests (run on Windows, elevated) — see e2e/README.md
```

Docs: `README.md` is short (what, run, Lite/Pro, sidebar). Release notes go at the top of
`docs/CHANGELOG.md` (`## x.y.z: title`, then the changes under `### New features`, `### Tweaks`, `### Design`, `### Fixes`,
`### Security`, `### Under the hood`; a test rejects other headings), then `python3 tools/gen_changelog.py`. `docs/TWEAKS.md` is generated
from the catalog by `tools/DumpTweaks.cs` (header comment has the command); regenerate it when tweaks change.

The app requires administrator rights and runs only on Windows; data/backups live in
`C:\ProgramData\ArchTweaker\`.

## CRITICAL build constraints (read before editing any .cs)

The exe must run on stock Windows .NET Framework 4.5, so:
- **C# 5 only** — no string interpolation (`$""`), null-conditional (`?.`), expression-bodied members,
  `nameof`, auto-property initializers, pattern variables (`is T x`), or digit separators.
- **No Mono/.NET-Core-only overloads**: `String.TrimStart()/TrimEnd()` (no-arg), `TrimStart(char)`,
  `Split(char, …)` / `Split(char)`, `Contains(char)/StartsWith(char)/EndsWith(char)`, `Array.Empty<T>()`.
  Use `Split(char[])` etc. The `ikdasm` audit in `build-win.sh` fails the build on violations.

## Source map (`src/`)

Files are grouped by feature, not by release (3.25). The compiler takes every `.cs` under `src/`
(`-recurse:'src/*.cs'`, `/recurse:src\*.cs` in `build.bat`). A feature's engine and its page sit in one file
or folder. Each file starts with a one-line comment that says what it holds.

| Folder | What lives there |
|--------|------------------|
| `App/` | `Program.cs` (entry + CLI), `L10n.cs` (language engine), `Update.cs` (self-update; signed manifest, ADR-0008), `LogStore.cs`, `Startup.cs` (/trace, auto-resume, SeDebugPrivilege) |
| `Core/` | the tweak engine only: `Engine.cs` (Hive, Paths, Log), `Reg.cs` (registry + journaled writes), `Journal.cs` (undo journal, State), `ShellRun.cs` (Shell.Run/PS, `RunLines` for tools with a timeout and line output, `HiddenInfo`, tool allowlist; start every process through it), `SvcInfo.cs` (Svc, OSInfo), `TweakModel.cs` (Tweak, Risk, basic ops), `Ops.cs` (NIC, WMI power, service preset, keep, device disable, PLM ops), `Advanced.cs` (power/device-reg ops, timer resolution), `Verify.cs` (preview, drift, AppliedSet, RegSnapshot), `RegText.cs` (.reg lines, hive names), `Units.cs` (sizes as text, System32 paths), `StateCache.cs` (never call `IsApplied()` on the UI thread), `Secure.cs`, `Scan.cs`, `SafeLists.cs`, `SelfTest.cs`, `OSBuilds.cs`, `AfterApplyScan.cs`, `Native.cs`, `Mem.cs` |
| `Catalog/` | `Cat.cs` (category names as constants: use `Cat.X`, not the text), `CoreTweaks.cs` (categories, builder T/Dw/Sz…, Build order), core list in `CoreTweaks.Desktop.cs` + `CoreTweaks.System.cs`; tweak lists by origin: `More.cs`, `DisplayTweaks.cs`, `UltimateTweaks.cs`, `SynezUltimate.cs`, `NetInputNvidia.cs`, `Allshi.cs`, `BuildValidity.cs`, `RecoveryAndPlm.cs`, `TasksServices.cs`, `Audio.cs`, `Danger.cs`, `HwTweaks.cs`; per-tweak text: `Gains.cs`, `Effects.cs`, `Evidence.cs`; `Recommend.cs`, `Adaptive.cs`, `AdvisorRules.cs`; `CustomSettings.cs` (Customize page values: choices and numbers, journaled) |
| `Modes/` | `LiteProMode.cs` (Mode, LiteTweak rule, ProPages, disclaimer, sidebar switch), `ProSafety.cs` (recovery backfill, PIN, export, Optimize safely) |
| `Safety/` | `RecoveryKit.cs`, `RestoreCheck.cs`, `DriftGuard.cs` (+ DriftWatch while running), `BootGuard.cs`, `Snapshot.cs`, `ChangeHistory.cs`, `ChangesPage.cs`, `Tools.cs` + `Repairs.cs` (Windows fixes: repair chain, WMI, runtimes, named restore points), `Maint.cs` (scheduled tasks), `UndoFull.cs` |
| `Measure/` | `Measure.cs` (PresentMon, A/B stats), `LatencyEngine.cs` (PDH + kernel ETW DPC/ISR), `WakeLatency.cs`, runs + pages: `LatencyRuns.cs`, `LatencyPage.cs`, `MeasureRuns.cs`, `MeasurePage.cs`, `ABTestPage.cs`, `Benchmark.cs` + `BenchReport.cs` + `BenchmarkPage.cs`, `LiveOverlay.cs`, `TuningAssistant.cs`, `MousePolling.cs`, `BootTimes.cs`, `RebootAB.cs`, `Health.cs`, `LatencyTest.cs` (Sleep(1) precision), `Results.cs` (all saved runs + put back tuner changes), `TuneAll.cs` (the three tuners in a row), `Report.cs` + `ReportHtml.cs` (HTML reports; `ReportHtml.Esc` is the only HTML escaper), `ProofStore.cs` (measured proof per tweak, append-only proof.tsv) |
| `Power/` | `PowerPlanOps.cs` (plan ops used by tweaks), `PowerTuner.cs`, `PowerExplorer.cs` + `PowerSettingsPage.cs`, `PowerPlans.cs`, `PlanFiles.cs` (.pow export / import), `PlanCompare.cs`, `ForegroundBoost.cs` (Win32PrioritySeparation) |
| `Hardware/` | `HwProbe.cs` (topology, probes), `Advisor.cs` (PC advisor), `SysInfo.cs`, `HwAudit.cs`, `Interrupts.cs`, `InterruptTuner.cs`, `Devices.cs`, `DevCtl.cs`, `Ghosts.cs`, `SetupApi.cs` (shared SetupAPI imports), `DriverStore.cs`, `DriversPage.cs`, `DriverAdvice.cs`, `NvDrs.cs`, `NipFiles.cs` |
| `Network/` | `Nagle.cs`, `NetBios.cs`, `NetTest.cs`, `Bufferbloat.cs`, `NetshGlobal.cs`, `NetDpcCheck.cs` |
| `Cleanup/` | `CleanupModule.cs`, `AppSettings.cs` (Discord / Firefox / Spotify files), `HostsModule.cs`, `Winapp2.cs`, `Uninstaller.cs`, `Hosts.cs`, `ContextMenu.cs` + `ContextMenuPage.cs`, `DiskScan.cs` + `DiskPage.cs`, `PackLeftovers.cs`, `ServicesModule.cs` + `ServiceTiers.cs`, `AppsModule.cs` (Store apps, risk badges), `AutorunModule.cs`, `TasksModule.cs` (scheduled tasks, journaled enable/disable) |
| `Iso/` | `IsoBake.cs` (ADR-0006), `IsoPage.cs` |
| `Ui/` | `MainForm.cs` (fields, constructor, sidebar sections) + partials `MainForm.Nav.cs` (sidebar, pages, search, shortcuts, command palette), `MainForm.Apply.cs` (pending changes, presets, apply, undo, details pane, busy overlay), `MainForm.Notices.cs` (notice bars), `MainForm.Appearance.cs`, `MainForm.Shell.cs` (log panel, tray, closing); `Theme.cs` (tokens, DPI, `Note`, `BarLabel`) + `Theme.Palettes.cs` (accent and scheme colour data, read by docs/palettes.py); `Kit/` (one file per control): `Toggles.cs`, `Chips.cs` (impact meter, row chips), `Nav.cs` (sidebar, page glyphs), `Skin.cs` (themes a page, empty and loading states), `SearchField.cs`, `MenuRenderer.cs`, `Toast.cs`, `InfoBar.cs`, `CheckList.cs`, `SettingsCard.cs`, `StatTiles.cs` (page summary tiles), `Graphics.cs` (drawing, motion, dark title bar, accessibility), `ThemeControls.cs` (FocusButton, ThemeSwitch); `TableSkin.cs` (row painter, empty state), `TweakList.cs`, `Appearance.cs`, `ThemeMakers.cs`, `Search.cs`, `ApplyReview.cs`, `Shell.cs` (palette, health strip, tray), `SetupGuide.cs`, `WhatsNewTour.cs`, `WhyArchTweaker.cs` (the Why ArchTweaker panel), `TrendChart.cs`, `NavHistory.cs` (back / forward pages), `Cards.cs`, `AppIcons.cs`, `Dlg.cs`, `DangerUi.cs` (Danger zone banner + gate); `Dialogs/` (one file per dialog): `ListDialog.cs` (shared intro + list + button bar base), `GhostDialog.cs`, `PackLeftoverDialog.cs`, `ForegroundBoostDialog.cs`, `AppSettingsDialog.cs`, `LeftoverDialog.cs` (uninstaller leftovers), `TextDialog.cs`, `PinDialog.cs`; `Pages/` (one file per page): `HomePage.cs`, `AdvisorPage.cs`, `ScanPage.cs`, `InfoPage.cs`, `ServicesPage.cs`, `AppsPage.cs`, `StartupPage.cs`, `TasksPage.cs` (Scheduled tasks), `CustomizePage.cs` (Customize), `CleanupPage.cs`, `CleanPages.cs` (Deep clean + Uninstaller), `HostsPage.cs`, `ToolsPage.cs` (Tools & backup, profiles), `SettingsPage.cs`, `MaintenancePage.cs`, `CardPage.cs` (headings + setting cards base for Tools and Settings) |
| `Generated/` | `Changelog.cs` (`python3 tools/gen_changelog.py` from `docs/CHANGELOG.md`) and `L10nRu.cs` (`python3 tools/gen_l10n.py` from `lang/ru.tsv`). Never edit by hand. |

Rule (3.31): engine folders (`Core/`, `Catalog/`, `Network/`, `Hardware/`) hold no dialogs. Only `Core/StateCache.cs` (UI-thread callback) and `Catalog/Danger.cs` (`DangerZone.Ensure` owner window) still import WinForms.

Sidebar sections (`Ui/MainForm.cs`): Tweaks, Diagnostics, Measure & test, Tune, History, Clean & apps,
Windows tools. A section whose pages are all Pro is hidden in Lite.

Tests: `Tests/` (one file per `src/` area, `Framework.cs` = runner and asserts, `TestHelp.cs` = shared helpers; a test must set up any theme or mode state it needs). E2E: `e2e/` (its `PAGES` list must match the sidebar; `UiTests.E2ePageList_MatchesTheSidebar` checks). Tooling: `tools/` (build-win.sh, mkres.py, run-tests.sh, generators, checks). Every `src/` file must be named in this map (`CoreTests.ClaudeMap_NamesEverySourceFile`).

## Core model

- **Tweak** objects hold a list of **Op**s (VOp = registry value, KeyAdd/Del, Task, Svc, Power, DeviceReg).
  Each Op implements `IsOn/Apply/Revert/Describe`. Fluent builders: `.Dw/.Sz/.Bin/.Pow/.Cmd/.PS/.Service/
  .Tasks/.DetectBy/.Requires/.In(presets)/.NeedsReboot/.R(Risk)/.Info`.
- **Exact undo** is the central guarantee (ADR-0002): every change is journaled; deletes export a `.reg`
  first and refuse to delete if the export didn't write.
- Elevated code paths (cleaner, uninstaller, reg-scan, self-update, SYSTEM tasks) are **security-sensitive** —
  see ADR-0003/0004/0005/0008 before touching them.
- **Release signing (3.35):** every latest.json must be signed: `python3 tools/sign_manifest.py --key <private key file> --pub-xml <public.xml> --exe out/ArchTweaker.exe --version x.y.z --url <https release url>`. The private key never goes into the repo or the zip.
- Tests that need another Windows build set `OSInfo.BuildOverride`; never write HKLM build values from a test.

## Add a tweak

Add a `T("id", Category, "Title", "Desc").Dw(hive, path, name, on, off)…` entry in the relevant `Add*()`
method (e.g. `AddRegOrg()` in `Catalog/UltimateTweaks.cs`), wire its impact in the matching `Gains*` dict, use
`off = null` to mean "delete on revert". Then `run-tests.sh` (catalog-integrity tests assert unique ids).

## DPI rule
Pages are scaled once by `Theme.ScaleTree` after they are built. Build-time sizes, margins and paddings in a page are plain 96-dpi numbers. Use `Theme.S()` only at run time (paint, layout, controls made after the page is shown), or in a control that overrides `ScaleControl` and `ScaleChildren` (TweakRow, CompareCards, StatusTiles, HistoryList, KvCard). `S()` at build time scales twice above 100 % DPI. Check with `AT_DPI=144` and `tools/Shot.cs`.
