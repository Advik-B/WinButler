# Changelog

Notable changes to WinButler, by internal milestone. The current shipping version is **1.0.1**.

## v1.0.2 — 2026-07-29 · Ghost-device cleanup + Activision/CoD fix

- **Ghost-device removal on the System Tools page**: "List ghost devices" (read-only) surfaces
  non-present PnP device nodes via `pnputil.exe`. "Remove ghost devices" (Advanced) is
  **permanent and cannot be undone** — to avoid touching live hardware that can also show as
  "disconnected" (disks, GPU-integrated controllers, VSS snapshots, virtual/software device
  stubs — all observed on real hardware during development), it only ever removes an
  allow-listed shape of device (USB/HID devices by vendor+product ID, Bluetooth, audio
  endpoints), explicitly excluding USB root hubs. The removal action runs an embedded PowerShell
  script entirely in memory (`-EncodedCommand`, never written to disk) — see
  `Services/EmbeddedScript.cs`. Credit to the original "remove ghost devices natively with
  PowerShell" concept from theorypc.ca (2017) — see README Acknowledgements.
- **Script-backed System Tools actions are now data-driven**: they're declared in
  `Scripts/scripts.json` and auto-register, so adding one is a drop-in — write a `.ps1`, add an
  entry, no code change (see `Scripts/README.md`). The manifest only ever *names* a script embedded
  in the binary plus a bare-identifier mode; it can't carry a command line, and it's loaded outside
  the definitions merge path so a future remote-definitions rollout could never reach it. Built-in
  Windows-tool actions (DISM, SFC, WMI reset, …) stay defined in code for the same reason.
- **Fixed a data-loss bug in the Activision/Call of Duty cleanup rule**: the old
  `activision-crashes` entry treated every immediate child of `%LocalAppData%\Activision` —
  including all of `Call of Duty`, which can hold `Call of Duty\players` (real user settings) —
  as permanently-deletable junk. Replaced with two narrower entries: one scoped to
  `Call of Duty` itself (now Recycle-Bin risk, not permanent, and excludes `players` via a new
  `exclude` field on known-location rules), and one scoped to the bootstrapper's crash-reports
  folder specifically.

## v1.0.1 — 2026-07-18 · Program Files installer

- **The installer is now a per-machine MSI** (`WinButler-win.msi`) that installs to
  `C:\Program Files\WinButler` with a proper UAC prompt. The v1.0.0 `Setup.exe` was per-user
  (`%LocalAppData%`) and could never launch the app it had just installed — WinButler requires
  administrator rights, so non-elevated Setup ended in "the requested operation requires
  elevation" and a partially-completed install. `Setup.exe` is no longer shipped; the portable
  zip remains for no-install use.
- Auto-updates work unchanged in Program Files: the app always runs elevated, so the updater
  it spawns can write there.

## v1.0.0 — 2026-07-18 · First public release

- **Auto-updates via Velopack**: releases ship as a `WinButler-win-Setup.exe` installer on
  [GitHub Releases](https://github.com/Advik-B/WinButler/releases); installed copies check for
  updates on launch, download them in the background (delta updates after the first release),
  and offer a one-click "restart to update" — never a silent forced restart. Dev runs
  (`dotnet run`) skip all of this.
- **Windows 10/11 only, now enforced at build time**: the project targets `net10.0-windows`
  (building on non-Windows hosts fails with a clear error), and the manifest declares only the
  Windows 10/11 compatibility GUID.
- New tag-driven release pipeline: pushing a `v*` tag cut from master packs and publishes the
  installer + update feed automatically.

## v7 — 2026-07-18 · UI bugfix & polish pass

- Fixed the indeterminate ProgressBar: busy spinners now animate (the template previously rendered a static empty track).
- Fixed the ScrollBar twice over: vertical thumb-drag direction was inverted, and the track outside the thumb was a dead zone — page up/down now works like a native scrollbar. Thumbs also keep a usable minimum size on huge lists.
- Raised muted-text contrast to meet WCAG AA at small sizes; restored the red danger-button hover.
- Disk Explorer: header/row column alignment, first-visit empty state, treemap tooltips that actually appear, luminance-aware treemap labels.
- Unified page conventions: consistent empty states with a SCAN button, hover states across cards/rows, combo-box placeholders, text trimming on clip-prone labels, first tooltips/automation names.
- Confirm dialog gained a severity model: destructive actions stay red; safe operations (redirect move/undo) get an accent variant.
- Removed the vestigial red/green accent-swap machinery and its duplicated color tokens — the theme now has a single green LED accent.

## v6 — 2026-07-06 · Four new cleaning surfaces

This milestone absorbed [FocusedWolf](https://www.reddit.com/user/FocusedWolf/)'s Windows
cleanup batch script into WinButler's native rule engine — credit to them for the original
catalog of cleanup locations.

- Split the rule catalog into per-domain files under `Data/definitions/` (cache, redirect, apps, browsers, drivers, launchers, games, windows), merged fail-closed at load.
- **App & Game Leftovers**: a curated known-locations catalog (~55 entries) for logs, dumps, and installer leftovers outside the cache scanners' territory, with a real-I/O test pinning the two scanners' disjointness.
- **Steam-aware cleaning**: locates every Steam library via `libraryfolders.vdf` and offers shader/download/temp caches, dumps, and logs per library.
- **System Tools**: one-click DISM, SFC, and Windows Update cache flush with live streamed output; dry-run prints the exact command sequence without launching anything.
- **Privacy sweep**: clears Explorer Recent/MRU history and 7-Zip file-manager history, with registry access behind a testable seam.

## v5 — 2026-07-03 · Production hardening

- Crash protection everywhere: guarded async commands, structured logging to `%APPDATA%\WinButler\logs`, AppDomain/TaskScheduler backstops, a startup error window.
- MFT parser hardened against malformed volumes (bounds-checked fixups, overflow-safe attribute walks, per-record recovery, fuzz tests).
- Cancellation support across every long operation, with CANCEL buttons on each page.
- Confirm dialog wired into every real destructive action; dry-run never prompts.
- Redirect safety: reparse-point guards on delete paths, atomic undo-ledger writes, orphaned-redirect reconciliation.
- Roughly 3× lighter MFT tree build and ~4× faster directory walks.

## v4 — 2026-07-01 · "Duly Doted" redesign, Dev Junk, Dashboard

- Full custom theme: true-black canvas, embedded fonts, LED accent, custom control themes throughout.
- **Dev Junk**: per-tool cards (JetBrains, Android SDK, npm, Cargo, …) showing on-disk vs. reclaimable size; live git checkouts are auto-locked out of bulk cleaning.
- **Dashboard**: whole-app overview with per-category cards and Clean All.
- Headless UI test suite (`Avalonia.Headless`) plus the `tools/ui-harness/` capture/automation scripts.

## v3 — 2026-06-30 · Disk Explorer

- Real NTFS `$MFT` parser (WizTree-style) with a virtualized list and a squarified treemap; recursive-walk fallback for non-NTFS volumes.

## v2 — 2026-06-30 · Redirect to Drive

- Directory-junction redirection: copy → verify → delete → junction, with an undo ledger.
- Rule-driven cache classification (`SafeCaches`) and the externalized JSON definitions catalog.

## v1 — 2026-06-30 · Core cleaner

- Electron leftover, temp-file, and cache scanners with risk classification.
- Dry-run on by default as a true no-op; hybrid delete (Safe → permanent, Caution/Risky → Recycle Bin); credential/key deny-list.
