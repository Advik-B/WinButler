# Developing WinButler

This is the developer companion to the [user-facing README](../README.md): architecture,
build/test workflow, and the invariants a change must not break. For the contribution
workflow itself (what makes a good rule PR, review expectations), see
[CONTRIBUTING.md](../CONTRIBUTING.md).

WinButler is an Avalonia 12 desktop app on .NET 10 (`net10.0`), MVVM via
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).

## Build, run, test

```bash
dotnet build
dotnet run --project WinButler.csproj
dotnet test Tests/WinButler.Tests.csproj
```

Two things to know before your first run:

- **The app self-elevates via UAC** (`requireAdministrator` in `app.manifest`). It needs
  all-user temp locations and NTFS junction privileges; expect a prompt.
- **The full test suite hits real I/O.** Several service tests read the real `$MFT` on
  `C:` or size real dev-tool folders on the machine, so `dotnet test` takes a few
  minutes and results depend on the box. The CI workflow
  ([`.github/workflows/build.yml`](../.github/workflows/build.yml)) runs the sandboxed
  subset — everything except `MftReaderTests`, `PerfProbeTests`, `ScannerOverlapTests`,
  and `RedirectionServiceTests` — which touches only temp directories and finishes in
  seconds.

The test project is **xunit v3** (`xunit.v3`), required by `Avalonia.Headless.XUnit` 12.x.

The project targets **`net10.0-windows`** on purpose — junctions, the `$MFT` reader, and
UAC self-elevation are Windows-only, and the build fails fast (with a clear error) on any
non-Windows host. Windows 10/11 are the only supported OSes.

## Releasing

Releases are tag-driven and packaged with [Velopack](https://velopack.io):

1. Bump `<Version>` in `WinButler.csproj` and add a `CHANGELOG.md` entry.
2. Commit on master, then `git tag vX.Y.Z && git push origin master vX.Y.Z`.
3. [`release.yml`](../.github/workflows/release.yml) verifies the tag is on master, publishes
   a self-contained win-x64 build, packs it with `vpk`, and creates the GitHub Release with
   `WinButler-win-Setup.exe`, full + delta `.nupkg` packages, and `releases.win.json` — the
   feed `Services/UpdateService.cs` reads at app launch.

Installed copies check that feed once per launch (`MainWindowViewModel.CheckForUpdatesAsync`),
download in the background, and surface a "restart to update" button in the status bar; dev
runs aren't Velopack installs, so the whole path no-ops (`update` log lines say why). To
exercise the updater without publishing anything, set `WINBUTLER_UPDATE_URL` to a local
folder of `vpk pack` output and install from there.

## Project layout

```
Assets/Fonts/      Embedded fonts for the custom theme
Controls/          Custom-drawn controls (TreemapControl for Disk Explorer)
Converters/        XAML value converters
Data/definitions/  Per-domain JSON rule files — the single source of truth for all rules
Models/            Plain data types
Services/          Scanners, RedirectionService, the MFT parser (Services/Mft/), Steam,
                   Privacy, SystemActionRunner, ThemeService, Log, SettingsStore
Themes/            "Duly Doted" theme: tokens, effects, per-control ControlThemes
ViewModels/        One per screen/component
Views/             One .axaml per ViewModel, plus Views/Shared and Views/Shell
Tests/             xUnit project (separate csproj, excluded from the app build)
tools/ui-harness/  PowerShell UI capture/automation scripts
```

**View resolution is by name convention**: `FooPageViewModel` →
`Views/FooPageView.axaml`, resolved in `ViewLocator.cs`. One ViewModel per screen,
one View per ViewModel — no exceptions.

**The volume index is shared.** `DiskIndexService` performs one MFT read per drive and
every feature (cleaners' size numbers, redirect sizing, Dashboard, Disk Explorer) reads
from that shared `DiskNode` tree. Don't add a feature that does its own recursive walk
of a whole drive.

**Theming** is a fully custom theme ("Duly Doted"), not a stock control library — the
per-control `ControlTheme`s in `Themes/` sit on top of base Avalonia `FluentTheme`
primitives. Screens bind colors via `DynamicResource` tokens.

## The rules engine

`Data/definitions/` is a folder of per-domain JSON files (`cache.json`,
`redirect.json`, `apps.json`, `browsers.json`, …). Each file is a *partial*
`WinButlerDefinitions`; all are embedded resources, loaded in filename order, and folded
together by `BundledDefinitionSource.Load`. The schema and how to add rules are
documented in [`Data/definitions/README.md`](../Data/definitions/README.md).

Add or adjust rules **there** — never hardcode paths in scanner code.

The load is **fail-closed and all-or-nothing**: if any single file is missing or
unparseable, `DefinitionsProvider.LoadFailed` is set, the shell builds zero scanners,
and a persistent error banner is shown. This is deliberate — a lost `cache.json` is a
lost deny-list, and scanning against an empty deny-list is worse than not scanning.

## Safety invariants (do not break these)

1. **Dry-run is a true no-op.** `Services/Cleaner.cs` returns before any filesystem
   mutation when dry-run is on. Every new destructive code path must respect the same
   chokepoint.
2. **The deny-list is enforced on every scan path.** `CacheScanner`,
   `DevJunkAggregator`, `TempScanner`, and `ElectronLeftoverScanner` all funnel
   candidates through `SafeCaches.IsDenied`. A new scanner must too.
3. **Hybrid delete.** `Safe` items are deleted permanently; `Caution`/`Risky` items go
   to the Recycle Bin. Both `RecycleBin.Send` and `Cleaner.DeletePermanently` guard
   against reparse points — a junction is unlinked, never followed into its target.
4. **Confirm before real deletes.** Every dry-run-off clean/redirect/undo routes
   through the shell's confirm modal (`ViewModelBase.ConfirmInteraction`, wired in
   `MainWindowViewModel`). Dry-run never prompts. An unset delegate auto-confirms,
   which is what lets headless tests drive commands directly.
5. **`settings.json` never persists `IsDryRun`.** `Services/SettingsStore.cs` saves the
   target drive only — every launch starts with dry-run ON, so a past dry-run-off
   session can never carry over.
6. **Async commands go through `ViewModelBase.RunGuardedAsync`.** CommunityToolkit's
   `AsyncRelayCommand` rethrows on the UI thread and would crash the process; the guard
   catches, logs, and surfaces the error via `StatusText`. `Program.cs` adds
   AppDomain/TaskScheduler backstops, but those are log-only last resorts.

## Testing strategy

UI verification is two complementary layers — neither replaces the other:

- **Headless interaction/logic tests** (`Tests/Headless/`, `Avalonia.Headless.XUnit`)
  boot the real `App` on a windowless backend and assert ViewModel behavior: selection
  math, command gating, dry-run clean, dashboard aggregation, navigation, toast/confirm
  flows. Fast, deterministic, no admin. Use `[AvaloniaFact]`/`[AvaloniaTheory]` so test
  bodies run on the UI thread.
  - `WeakReferenceMessenger.Default` is a static singleton — reset it per test (see
    `MessengerIsolatedTest` in `Tests/Headless/Fakes.cs`).
  - Pump `Dispatcher.UIThread.RunJobs()` before asserting on anything a handler
    `Post`ed.
- **Design fidelity + the real elevated run** (`tools/ui-harness/`): launch +
  `PrintWindow` capture, UI Automation control-driving, crop/zoom — the manual
  screenshot-and-compare loop, and the only way to exercise the real elevated app end
  to end (MFT read + junctions). See [`tools/ui-harness/README.md`](../tools/ui-harness/README.md).

Headless rendering uses a different renderer than the on-screen compositor, so the
headless layer catches *logic* regressions, not visual ones.

`Tests/**` is excluded from the app build (explicit `Remove` items in
`WinButler.csproj`); `InternalsVisibleTo("WinButler.Tests")` exposes the internal MFT
helpers (USA fixup, data-run decoder) to the test project.

## Sharp edges (Avalonia)

- **`Application.Resources.TryGetResource(...)` does not see resources merged via
  `Styles`/`ControlThemes`.** Look resources up from a control in the visual tree, or
  call `TryGetResource` on `Application.Current` itself. `ThemeService.Brush(key, hex)`
  wraps this correctly for C# call sites (converters, custom-drawn controls) — use it.
- **An inline `ControlTemplate` nested inside another ControlTheme's template silently
  produces a zero-size control.** Define the inner theme as a resource and assign it
  via `Theme=` instead (this bit the ScrollBar's page up/down buttons).

## Diagnostics & state on disk

Everything lives under `%APPDATA%\WinButler\`:

| File | What |
|---|---|
| `logs\winbutler.log` | Append-only log (`Services/Log.cs`); rotates once per session at 2 MB; never throws; records every destructive action |
| `redirects.json` | The redirect undo ledger — written atomically (tmp + `File.Replace`) |
| `settings.json` | Persisted prefs (`Services/SettingsStore.cs`) — never `IsDryRun` |
