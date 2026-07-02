---
name: avalonia-resource-lookup-gotcha
description: "Avalonia gotcha — Application.Resources.TryGetResource() doesn't see resources defined in Styles-merged dictionaries"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 6ca719e2-5cd7-4cec-bf9e-5598031c602f
---

In Avalonia, `Application.Current.Resources.TryGetResource(key, theme, out value)` only searches the bare `Application.Resources` dictionary. It does NOT cascade into resources defined inside `Application.Styles` (e.g. a `Styles.Resources` block in a `StyleInclude`d `.axaml` file). To look up a resource that may live in either place, call `TryGetResource` on the `Application` instance itself (or any `IResourceHost`), not on `.Resources` — e.g. `app.TryGetResource(key, theme, out value)` instead of `app.Resources.TryGetResource(...)`.

**Why:** In [[winbutler-status]]'s accent-swap feature (`Services/ThemeService.cs`), the precomputed Red/Green brush palette lived in `Themes/Tokens.Colors.axaml`, merged in via `StyleInclude` into `Application.Styles`. `ThemeService.Apply()` called `app.Resources.TryGetResource(...)` to read the precomputed palette and copy it into the mutable live-accent keys. This silently failed every time (TryGetResource returned false, so the copy loop's `if` body never ran) — accent toggling looked like it worked (command fired, no exception) but the UI never actually changed color. It went undetected through code review and even matched the DynamicResource-everywhere audit (grep found zero `StaticResource` misuse — the bug wasn't in the *binding* layer, it was in the C#-side *lookup* the whole mechanism depended on). Only caught via live UI testing (toggling View > LED Green and screenshotting).

**How to apply:** Whenever writing C# code that programmatically looks up an Avalonia resource defined in a `Styles`/`StyleInclude` file (not a plain `<Application.Resources>` block), use `TryGetResource`/`TryFindResource` on the `Application`/`IResourceHost` object, never on `.Resources` directly. And more generally: a binding-layer audit (grep for `StaticResource` vs `DynamicResource`) is necessary but not sufficient to verify a dynamic-theming feature — always do at least one live toggle-and-screenshot check, since the failure can be entirely on the C# side with no XAML symptom.
