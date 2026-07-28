# WinButler rule definitions

This folder is WinButler's **single source of truth** for what the app scans and cleans. Every
`*.json` file here is embedded in the build and **folded together at load** (see
`Services/Definitions/BundledDefinitionSource.cs`). You can add or tune rules by editing JSON — no
code changes, no recompile of logic required.

## Why several files

Each file is a *partial* set of rules — same schema, only the sections it cares about are filled in.
Splitting by domain keeps diffs small and lets contributors touch one area without wading through the
whole catalog. Files are merged in filename order, so ordering never changes behavior.

| File | What it holds |
|------|---------------|
| `cache.json` | Cache-classification lists for `CacheScanner` — **including the deny-list** (`denyFragments`). |
| `redirect.json` | The redirect catalog (`RedirectionService`). |
| `apps.json`, `browsers.json`, `drivers.json`, `launchers.json`, `games.json`, `windows.json` | Known-location cleanup entries (`KnownLocationsScanner`), grouped by domain. |

## How merging works

- **Cache lists** (`alwaysSafeNames`, `denyFragments`, …) are **unioned** case-insensitively.
- **Redirect entries** merge by `targetName` — a later file with the same `targetName` replaces the earlier one. Keep `targetName` unique.
- **Known-location entries** merge by `id` — same rule, keyed on `id`. Keep `id` unique.

`//` and `/* */` comments and trailing commas are allowed. Keys prefixed `_comment` are ignored.

## Fail-closed

If **any** file here is missing or won't parse, WinButler loads **nothing**, disables cleaning, and
shows an error banner naming the offending file (in the log). This is deliberate: a lost `cache.json`
would mean a lost deny-list, and scanning without a deny-list could offer credential stores for
deletion. Partial loads are never accepted — validate your JSON before committing.

## Known-location entry schema (`knownLocations.entries`)

```json
{
  "id": "discord-cache",              // unique, kebab-case; the merge key
  "path": "%AppData%\\Discord\\Cache",// env tokens expanded (see below)
  "mode": "children",                 // children | files | self
  "pattern": "*.dmp",                 // files mode only: wildcard filter
  "recursive": true,                  // files mode only: recurse subdirs
  "exclude": ["players"],             // children mode only: child names to always skip
  "allDrives": false,                 // path is relative to every fixed drive root
  "risk": "safe",                     // safe | caution | risky
  "displayName": "Discord cache",
  "description": "Chromium cache",
  "group": "Apps"                     // UI grouping on the Apps page
}
```

- **`mode: children`** — every immediate child of `path` is a delete target; the directory itself survives.
  Optionally set `exclude` (child *names*, case-insensitive, not full paths) to skip specific children
  even though they'd otherwise match — use this when a folder mixes junk with data that must never be
  offered (e.g. a game's crash-report folder that also holds a `players` settings subfolder). Ignored
  outside `children` mode.
- **`mode: files`** — files under `path` matching `pattern` (optionally `recursive`) are targets.
- **`mode: self`** — `path` itself is the target (a specific junk folder or file).
- **`risk`** drives deletion policy: `safe` → deleted permanently; `caution`/`risky` → sent to the Recycle Bin and never auto-selected. Use `risky` for anything a user might miss (local edit history, package stores that are slow to rebuild).

### Path tokens

Custom tokens are expanded first, then real environment variables:

| Token | Expands to |
|-------|-----------|
| `%Documents%` | the user's Documents folder |
| `%LocalLow%` | `%UserProfile%\AppData\LocalLow` |
| `%AppData%`, `%LocalAppData%`, `%ProgramData%`, `%WinDir%`, `%ProgramFiles%`, `%UserProfile%` | the standard environment variables |

The deny-list (`cache.json` → `denyFragments`) gates **every** entry here: no path containing a
denied fragment is ever offered, regardless of what a rule says. Junctions/symlinks are unlinked,
never followed.
