# CLAUDE.md

Guidance for working in this repo. `README.md` is the user-facing tour; `docs/DEVELOPMENT.md`
is the full developer guide. This file is the condensed agent quick reference.

WinButler is a Windows disk-cleaner + space-reclaim toolkit: an MFT-based disk scanner,
rule-driven cleaners (Electron leftovers, temp, caches, dev junk), and a directory-junction
"redirect" feature. Avalonia 12 on .NET 10 (`net10.0-windows` — Windows 10/11 only, enforced
at build time), MVVM via CommunityToolkit.Mvvm.

## Build / run / test

```bash
dotnet build
dotnet run --project WinButler.csproj
dotnet test Tests/WinButler.Tests.csproj
```

- The app **self-elevates via UAC** on launch (`requireAdministrator` in `app.manifest`) —
  it needs all-user temp locations and NTFS junction privileges. Expect a UAC prompt.
- `dotnet test`: service/parser tests plus a headless ViewModel suite (`Tests/Headless/`).
  Takes a few minutes — several service tests hit **real** I/O (the redirect scan sizes real
  dev-tool folders; MFT tests read the real `$MFT` on `C:`), not mocked filesystems. The
  headless tests touch no disk and need no admin.
- The test project is **xunit v3** (`xunit.v3`), required by `Avalonia.Headless.XUnit` 12.x.
- CI (`.github/workflows/build.yml`) runs build + the sandboxed test subset only — it excludes
  `MftReaderTests`, `PerfProbeTests`, `ScannerOverlapTests`, `RedirectionServiceTests` (real
  machine I/O). Keep new machine-dependent tests out of the CI filter the same way.
- **Releases are tag-driven** (`.github/workflows/release.yml`): push a `v*` tag cut from
  master → self-contained win-x64 publish → `vpk pack` (pinned 1.2.0) → GitHub Release with a
  **per-machine MSI** (patched to install to Program Files via
  `tools/release/patch-msi-installdir.vbs`) + update feed. Velopack's per-user Setup.exe is
  deleted from the assets — it can't launch a `requireAdministrator` app after installing
  (see docs/DEVELOPMENT.md "Releasing"). The in-app updater (`Services/UpdateService.cs`,
  checked once per launch from `MainWindow.Opened`) reads that feed; it no-ops on non-Velopack
  launches (dev runs, tests). `VelopackApp.Build().Run()` must stay the first statement in `Main`.

## UI verification

Two complementary layers — neither replaces the other:

- **Headless interaction/logic tests** (`Tests/Headless/`, `Avalonia.Headless.XUnit`): boot the
  real `App` on a windowless backend and assert ViewModel behavior/state — selection math,
  command `CanExecute` gating, dry-run clean, dashboard aggregation, navigation, toast/confirm
  slots. Fast, deterministic, no window/UAC. Use `[AvaloniaFact]`/`[AvaloniaTheory]` so bodies
  run on the UI thread (needed for the `Dispatcher`/`DispatcherTimer` cases). **Gotchas:**
  `WeakReferenceMessenger.Default` is a static singleton — reset it per test (see
  `MessengerIsolatedTest`); pump `Dispatcher.UIThread.RunJobs()` before asserting on anything a
  handler `Post`ed. This layer catches *logic* regressions, **not** visual/design fidelity —
  headless rendering is a different renderer than the on-screen compositor.
- **Design-fidelity + real elevated run** (`tools/ui-harness/`): launch + PrintWindow capture, UI
  Automation control-driving, crop/zoom — the manual "screenshot and compare against the design"
  loop, and the only way to exercise the real elevated app end-to-end (MFT read + junctions).
  See `tools/ui-harness/README.md`.

## Architecture

MVVM. Views are resolved from ViewModels by name convention (`FooPageViewModel` →
`Views/FooPageView.axaml`) in `ViewLocator.cs`. One ViewModel per screen/component,
one View per ViewModel.

```
Assets/Fonts/    Embedded fonts for the custom theme
Controls/        Custom-drawn controls (TreemapControl for Disk Explorer)
Converters/      XAML value converters
Data/definitions/ per-domain JSON rule files (cache, redirect, known-locations) — see its README
Models/          Plain data types
Services/        Scanners (incl. KnownLocationsScanner), RedirectionService, the MFT parser
                 (Services/Mft/), Steam (Services/Steam/), Privacy, SystemActionRunner, ThemeService
Themes/          "Duly Doted" theme: color/typography/spacing tokens, effects, per-control ControlThemes
ViewModels/      One per screen/component
Views/           One .axaml per ViewModel, plus Views/Shared and Views/Shell
Tests/           xUnit project (WinButler.Tests.csproj) — separate project, excluded from the app build
tools/ui-harness/ PowerShell UI capture/automation scripts
```

**Theming:** the UI is a fully custom theme ("Duly Doted"), not a stock control library —
per-control `ControlTheme`s in `Themes/` over base Avalonia `FluentTheme` primitives, with a
single green LED accent. Screens bind color tokens via `DynamicResource`; C# call sites resolve
them through `ThemeService.Brush` (see the resource-lookup gotcha below).

## Conventions & gotchas

- **`Data/definitions/` is the single source of truth** for cache-classification rules, the
  redirect catalog, and the known-location cleanup catalog. It's a folder of per-domain JSON files
  (`cache.json`, `redirect.json`, `apps.json`, `browsers.json`, …), each a *partial*
  `WinButlerDefinitions` embedded via a glob and folded together at load
  (`BundledDefinitionSource.Load`). See `Data/definitions/README.md` for the schema. Add/adjust
  rules there — do **not** hardcode paths in code. **Fail-closed:** any file missing or unparseable
  aborts the whole load (a lost `cache.json` = a lost deny-list).
- **Dry-run is the default everywhere** and is a true no-op: `Services/Cleaner.cs` returns
  before any filesystem mutation when dry-run is on. Keep it that way.
- **Deny-list paths** (SSH/GPG keys, credential stores, browser login data, etc.) are never
  touched and never even offered as suggestions, regardless of what else matches. Enforced on
  **every** scan path — `CacheScanner`, `DevJunkAggregator`, `TempScanner` and
  `ElectronLeftoverScanner` all funnel candidates through `SafeCaches.IsDenied`.
- **Hybrid delete:** `Safe` items are deleted permanently; `Caution`/`Risky` items go to the
  Recycle Bin (recoverable). `RecycleBin.Send` and `Cleaner.DeletePermanently` both guard against
  reparse points — a junction is unlinked, never followed into its target.
- **Confirm before real deletes:** every dry-run-**off** clean/redirect/undo routes through the
  shell's confirm modal (`ViewModelBase.ConfirmInteraction`, wired in `MainWindowViewModel`);
  dry-run never prompts. An unset delegate auto-confirms, so headless tests drive commands directly.
- **Fail-closed definitions:** if any file under `Data/definitions/` won't load,
  `DefinitionsProvider.LoadFailed` is set, the shell builds **zero** scanners and shows a persistent
  error banner — never scans against an empty (⇒ empty deny-list) ruleset. The load is all-or-nothing
  across the folder (`BundledDefinitionSource.Load` throws if any single file fails to parse).
- **`Tests/**` is excluded from the app build** (explicit `Remove` items in `WinButler.csproj`).
  `InternalsVisibleTo("WinButler.Tests")` exposes internal MFT helpers (USA fixup, data-run
  decoder) to the test project.
- **Avalonia resource-lookup gotcha:** `Application.Resources.TryGetResource(...)` does **not**
  find resources merged in via `Styles`/`ControlThemes`. Look them up from a control in the
  visual tree, or from the specific `ResourceDictionary`, instead. `ThemeService.Brush(key, hex)`
  wraps this correctly for C# call sites (converters, custom-drawn controls).
- **Diagnostics & state on disk** (all under `%APPDATA%\WinButler\`): `logs\winbutler.log`
  (`Services/Log.cs` — append-only, rotates once per session at 2 MB, never throws; logs every
  destructive action); `redirects.json` (undo ledger, atomic write); `settings.json`
  (`Services/SettingsStore.cs`). **`settings.json` persists the target drive only — never
  `IsDryRun`**, so every launch starts dry-run ON (a past dry-run-off session must not carry over).
- **Crash-protection:** async command bodies run through `ViewModelBase.RunGuardedAsync`
  (catch → log + `StatusText`, `OperationCanceledException` → "Cancelled."); without it,
  CommunityToolkit's `AsyncRelayCommand` rethrows on the UI thread and crashes the process.
  `Program.cs` adds `AppDomain`/`TaskScheduler` backstops (log-only).
