# WinButler scripts

PowerShell backing the System Tools page's script-based actions. Every `.ps1` here and
`scripts.json` are **embedded in the assembly** (`WinButler.csproj`) and run in memory — see
"Never written to disk" below.

## Adding a script

Two steps, no code change:

1. Drop `YourScript.ps1` in this folder.
2. Add an entry to `scripts.json`.

It auto-registers on the System Tools page next build (`Services/ScriptCatalog.cs`).

```json
{
  "id": "my-action",              // unique in this file; the key logs and tests use
  "name": "Do the thing",         // button row title
  "description": "What it does.", // button row subtitle
  "warning": "Why it's risky.",   // shown in the confirm modal — REQUIRED unless isReadOnly
  "script": "YourScript.ps1",     // must be a .ps1 embedded from this folder
  "mode": "Remove",               // optional; assigned to $Mode before the script body
  "isReadOnly": false,            // true → runs even in dry-run, never prompts (changes nothing)
  "isAdvanced": true              // true → grouped under the "Advanced" divider
}
```

| Field | Rule |
|-------|------|
| `id` | Required, unique within this file. |
| `name`, `description` | Required, non-empty. |
| `warning` | **Required unless `isReadOnly`.** A destructive action must state its own risk — the confirm modal shows this. |
| `script` | Required. Must match `^[A-Za-z0-9._-]+\.ps1$` **and** resolve to a `.ps1` embedded from this folder. |
| `mode` | Optional. Must be a bare identifier (`^[A-Za-z][A-Za-z0-9]*$`). |
| `isReadOnly`, `isAdvanced` | Optional, default `false`. |

**`isReadOnly` means "changes nothing"**, not "is quick" — it makes the action bypass both the
dry-run guard and the confirm modal. Only set it on an action that genuinely cannot mutate anything.

### One script, several actions

Use `mode` to back several actions with one script — `RemoveGhostDevices.ps1` does this, exposing a
read-only `List` preview and the real `Remove`. Because both run the *same* classification code,
the preview cannot drift out of sync with what the destructive action actually does:

```powershell
if (-not $Mode) { $Mode = 'Remove' }   # default when no mode is declared
```

## Never put commands in this JSON

`scripts.json` carries **metadata plus a reference to a script**. It must never contain a command
line, an executable name, or raw PowerShell. Two reasons, both load-bearing:

- **WinButler always runs elevated** (`requireAdministrator`). Anything expressible in data becomes
  something that runs as administrator.
- Rule definitions under `Data/definitions/` can, by design, be overlaid at runtime from a remote
  URL (`Services/Definitions/RemoteDefinitionSource.cs`, merged via `DefinitionsProvider.AddSource`
  — currently unused, but the plumbing exists and is public). This manifest is deliberately loaded
  by `ScriptCatalog` from its own embedded resource, **outside** that merge path, so it can never
  be reached that way.

The validation above is what keeps that true: `script` must name something already compiled into
the binary, and `mode` is restricted to letters and digits so it cannot escape the `$Mode = '…'`
assignment it is interpolated into. The result is that this file can only ever select among scripts
that shipped with the app — it can never introduce new executable content.

See `Models/SystemAction.cs` for the same rule applied to the built-in Windows-tool actions (DISM,
SFC, `wevtutil`, …), which stay defined in C# for exactly this reason.

## Never written to disk

`Services/EmbeddedScript.cs` runs these via `powershell.exe -NoProfile -EncodedCommand <base64>`,
reading the script straight out of the assembly. It is never extracted to a temp file or to
`%APPDATA%`. Those locations are user-writable, so an unprivileged process could overwrite the
script between write and execute and have WinButler run it as administrator.

## Fail-closed

If `scripts.json` is missing, malformed, or **any** entry fails validation, the whole manifest is
rejected: zero script actions register and the error goes to `%APPDATA%\WinButler\logs\winbutler.log`.
It is all-or-nothing on purpose — a partial load could register a destructive action while dropping
the read-only preview that makes it safe to use. Built-in C# actions are unaffected, so the System
Tools page still works.
