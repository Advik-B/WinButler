---
name: avalonia-inline-template-gotcha
description: "Avalonia gotcha — an inline ControlTemplate nested on a child control inside a ControlTheme's template silently produces a zero-size control; assign a ControlTheme via Theme= instead"
metadata:
  type: feedback
---

In Avalonia (verified on 12.0.2), declaring an **inline `<X.Template><ControlTemplate>…`** on a child control that sits *inside another ControlTheme's ControlTemplate* can silently break: the control instantiates (it appears in the logical/UIA tree, events can be wired to it) but renders with **empty bounds — zero size, invisible, un-hit-testable**. No exception, no binding error, nothing in logs.

**Why:** WinButler's custom ScrollBar theme (`Themes/Controls.ScrollBar.axaml`) added `Track.DecreaseButton`/`Track.IncreaseButton` RepeatButtons ("PART_PageUpButton"/"PART_PageDownButton") with inline `<RepeatButton.Template>` — exactly mirroring Fluent's structure *except* Fluent assigns the buttons `Theme="{StaticResource FluentScrollBarPageButton}"`. Symptom: clicking the scrollbar track did nothing (user-reported), yet `uia.ps1 invokeid PART_PageDownButton` paged correctly (Click was wired) and UIA showed `BoundingRectangle=Empty` for the buttons while the sibling Thumb had real bounds. Swapping the inline template for a `ControlTheme` resource assigned via `Theme=` made the buttons fill the track and receive real clicks — the only change.

**How to apply:** Inside a ControlTheme's ControlTemplate, never give a child control an inline nested `ControlTemplate`. Extract a small `<ControlTheme x:Key="..." TargetType="...">` with the Template setter into the same ResourceDictionary and assign it with `Theme="{StaticResource ...}"` (this is also what Fluent does everywhere, e.g. FluentScrollBarPageButton/FluentScrollBarThumb). Debug this class of bug by (1) UIA `BoundingRectangle` (Empty = never arranged) vs. tree presence, and (2) temporarily giving the template a loud `Background="Red"` — if no red renders, it's layout/template, not hit-testing. Related: [[avalonia-resource-lookup-gotcha]] — both are silent C#-/theme-side failures invisible to XAML binding audits; only live driving catches them.
