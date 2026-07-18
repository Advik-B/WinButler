# Contributing to WinButler

Thanks for your interest! WinButler is a Windows-only Avalonia app that deletes files for a
living, so contributions are judged first on safety, then on everything else. This page covers
the workflow; the deep technical tour lives in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## The easiest contribution: rules, not code

Most of WinButler's usefulness comes from its JSON rule catalog — which caches are safe to
clean, which folders are worth redirecting, where apps leave junk behind. All of it lives in
[`Data/definitions/`](Data/definitions/), and adding an entry is a JSON edit, not a code
change. The schema is documented in [`Data/definitions/README.md`](Data/definitions/README.md).

Good rule PRs:

- Name the app/tool and what the folder actually contains, in the entry's description.
- Classify conservatively. `safe` is only for content that is *regenerated automatically and
  worthless* (shader caches, thumbnail caches). When in doubt, use `caution` — it routes
  deletions to the Recycle Bin.
- Never add a rule that could match credential material. If your rule's pattern could brush
  against key stores, tokens, or browser profile data, it belongs on the deny-list instead.

## Reporting bugs

Please include:

- What you did, what you expected, what happened.
- The log: `%APPDATA%\WinButler\logs\winbutler.log` — every scan and destructive action is
  recorded there. Trim it to the relevant session if it's long.
- Whether dry-run was on or off at the time.

If WinButler deleted something it shouldn't have: `Caution`/`Risky` items go to the Recycle
Bin, so check there first — then file the issue anyway, because a bad classification is a bug
even when it's recoverable.

## Code contributions

### Setup

Windows 10+ and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project WinButler.csproj   # self-elevates via UAC
dotnet test Tests/WinButler.Tests.csproj
```

The full test suite hits real I/O (it reads the real `$MFT` on `C:` and sizes real dev-tool
folders) and takes a few minutes. CI runs the sandboxed subset; see
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#build-run-test).

### The rules that are not negotiable

These invariants are what make WinButler trustworthy. A PR that weakens one won't be merged,
however nice the feature:

1. **Dry-run is a true no-op.** No filesystem mutation of any kind while dry-run is on.
2. **Every scanner funnels through the deny-list** (`SafeCaches.IsDenied`). New scan paths
   included.
3. **Reparse points are never followed into their targets.** A junction gets unlinked, never
   traversed, on every delete path.
4. **Real destructive actions confirm first and are logged.** Route them through the existing
   confirm modal and `Services/Log.cs`.
5. **Definitions load fail-closed.** A missing or unparseable rule file must abort scanning
   entirely, never degrade to a partial ruleset.
6. **`IsDryRun` is never persisted.** Every launch starts with dry-run ON.

The full list with file references is in
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#safety-invariants-do-not-break-these).

### Conventions

- MVVM, strictly paired: one ViewModel per screen, one View per ViewModel, resolved by name
  convention (`FooPageViewModel` → `Views/FooPageView.axaml`).
- Async command bodies go through `ViewModelBase.RunGuardedAsync` — an unguarded
  `AsyncRelayCommand` exception crashes the process.
- No hardcoded cleanup paths in code — paths belong in `Data/definitions/`.
- Match the existing comment style: comments explain *why* (invariants, ordering, edge cases),
  not *what* the next line does.

### Tests

- ViewModel/interaction changes get headless tests (`Tests/Headless/`, `[AvaloniaFact]`).
  They boot the real app windowless, need no admin, and run in seconds.
- Service changes get unit tests that sandbox in `%TEMP%` where possible. If a test genuinely
  needs real machine state (the MFT, your actual profile), keep it out of the CI filter — see
  `.github/workflows/build.yml`.
- Visual changes can't be asserted headlessly; verify with the capture loop in
  [`tools/ui-harness/`](tools/ui-harness/README.md) and include a before/after screenshot in
  the PR.

### Pull requests

- Keep PRs focused — one feature or fix, plus its tests and docs.
- `dotnet build` and the CI test subset must pass; run the full suite locally when your change
  touches scanners, the MFT parser, or redirection.
- Update `CHANGELOG.md` for anything user-visible.
- Write commit messages in the imperative, saying what the change does and why it's safe if
  it touches a delete path.

## Questions

Open an issue — design questions are welcome before you write any code, and usually save a
round of rework.
