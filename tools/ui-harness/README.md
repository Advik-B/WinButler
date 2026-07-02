# UI harness

WinButler has no automated UI test suite — UI changes are verified manually by building,
running, screenshotting, and comparing against the design (see the root `README.md`).
These PowerShell scripts are that loop, made repeatable: launch the app, pin its window
to a fixed size/position, capture it, drive its controls, and zoom in on detail.

Captures land in `out/` (gitignored). Everything is window-relative and the window is
pinned to screen **(40, 40)** at **1320×900**, so coordinates are stable across runs.

## Prerequisites

- A built Debug binary: run `dotnet build` from the repo root first. `shoot.ps1` finds
  `..\..\bin\Debug\net10.0\WinButler.exe` automatically (override with `-Exe`).
- Windows only (UI Automation + GDI+ / `PrintWindow`).
- The app self-elevates via UAC on launch (`app.manifest`), so expect a prompt when
  `shoot.ps1` starts a fresh instance.

## Scripts

| Script | Purpose |
|---|---|
| `shoot.ps1` | Launch (or reuse) WinButler, pin the window, capture it to a PNG via `PrintWindow`. |
| `uia.ps1` | Drive controls via UI Automation: `dump` / `invoke` / `invoketext` / `checkall`. |
| `click.ps1` | Bring the window forward and click at a window-relative `(X,Y)`. |
| `crop.ps1` | Crop a rectangle from a capture and nearest-neighbor-upscale it for inspection. |

## Example: the capture → inspect loop

```powershell
dotnet build                                    # from repo root
./tools/ui-harness/shoot.ps1 -OutName dashboard.png
./tools/ui-harness/uia.ps1 dump                 # list control ids/names
./tools/ui-harness/uia.ps1 invoke "Clean All"   # invoke a button by name
./tools/ui-harness/shoot.ps1 -OutName after.png
./tools/ui-harness/crop.ps1 -In dashboard.png -X 760 -Y 238 -W 520 -H 100 -Scale 2.5 -Out readout.png
```

### `shoot.ps1`
```
-OutName <name.png>   output filename (default dashboard.png)
-WaitSeconds <n>      settle time before capture (default 2)
-OutDir <path>        capture dir (default ./out)
-Exe <path>           override the exe path (default ../../bin/Debug/net10.0/WinButler.exe)
```
Reuses a running WinButler if one exists; otherwise starts the exe. Prints the saved
path, dimensions, `PrintWindow` result, and the center pixel (a sanity check that the
capture isn't blank).

### `uia.ps1`
```
uia.ps1 dump                    list every Button/RadioButton/CheckBox (AutomationId + Name)
uia.ps1 invoke "<text>"         invoke the first Button/RadioButton whose Name contains <text>
uia.ps1 invoketext "<text>"     invoke the first Button with a descendant Text matching <text>
uia.ps1 checkall                toggle every CheckBox to checked
```

### `click.ps1`
```
click.ps1 -X <x> -Y <y>         click at window-relative (x,y); the window is pinned to (40,40)
```
Prefer `uia.ps1 invoke` when a control has a name — it's coordinate-independent. Use
`click.ps1` only for things UI Automation can't reach.

### `crop.ps1`
```
crop.ps1 -In <name.png> -X <x> -Y <y> -W <w> -H <h> [-Scale <f>] [-Out <name.png>]
```
`-In`/`-Out` resolve against `-OutDir` (default `./out`) unless absolute. `-Scale`
defaults to 2.0; `-Out` defaults to `crop_<In>`.
