# CLAUDE.md

Guidance for working in this repo. See `README.md` for the full feature/user-facing tour;
this file is the developer/agent quick reference.

WinButler is a Windows disk-cleaner + space-reclaim toolkit: an MFT-based disk scanner,
rule-driven cleaners (Electron leftovers, temp, caches, dev junk), and a directory-junction
"redirect" feature. Avalonia 12 on .NET 10 (`net10.0`), MVVM via CommunityToolkit.Mvvm.

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
Data/            definitions.json — single source of truth for cache/redirect rules
Models/          Plain data types
Services/        Scanners, RedirectionService, the MFT parser (Services/Mft/), ThemeService
Themes/          "Duly Doted" theme: color/typography/spacing tokens, effects, per-control ControlThemes
ViewModels/      One per screen/component
Views/           One .axaml per ViewModel, plus Views/Shared and Views/Shell
Tests/           xUnit project (WinButler.Tests.csproj) — separate project, excluded from the app build
tools/ui-harness/ PowerShell UI capture/automation scripts
```

**Theming:** the UI is a fully custom theme ("Duly Doted"), not a stock control library.
`Services/ThemeService.cs` swaps a precomputed Red or Green brush palette into mutable
resource keys that screens bind to via `DynamicResource`, so the whole app re-colors live
(View menu) with no restart.

## Conventions & gotchas

- **`Data/definitions.json` is the single source of truth** for cache-classification rules
  and the redirect catalog. It's an embedded resource (see `WinButler.csproj`) and can be
  edited without recompiling. Add/adjust rules there — do **not** hardcode paths in code.
- **Dry-run is the default everywhere** and is a true no-op: `Services/Cleaner.cs` returns
  before any filesystem mutation when dry-run is on. Keep it that way.
- **Deny-list paths** (SSH/GPG keys, credential stores, browser login data, etc.) are never
  touched and never even offered as suggestions, regardless of what else matches.
- **Hybrid delete:** `Safe` items are deleted permanently; `Caution`/`Risky` items go to the
  Recycle Bin (recoverable).
- **`Tests/**` is excluded from the app build** (explicit `Remove` items in `WinButler.csproj`).
  `InternalsVisibleTo("WinButler.Tests")` exposes internal MFT helpers (USA fixup, data-run
  decoder) to the test project.
- **Avalonia resource-lookup gotcha:** `Application.Resources.TryGetResource(...)` does **not**
  find resources merged in via `Styles`/`ControlThemes`. Look them up from a control in the
  visual tree, or from the specific `ResourceDictionary`, instead.
