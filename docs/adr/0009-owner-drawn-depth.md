# ADR-0009: Depth from derived tokens and one surface painter; buttons drawn by the app

**Date**: 2026-11-16
**Status**: accepted
**Deciders**: Archie

## Context

The app looked flat: every card, tile and row was a filled rectangle with a border, buttons were WinForms flat buttons,
and state was shown with coloured edge stripes. Eleven colour schemes, custom accents and High Contrast all have to keep
working, and pages set colours from `Theme` tokens.

## Decision

- Depth tokens (`Line`, `Raised`, `Hi`, `ShadowA`, `AccentSoft`, `AccentInk`) are derived from the active colours in
  `Theme.Derive()`, which runs after every colour change (scheme, High Contrast, `Preview` restore). No scheme table grows.
- `Gfx.Surface` is the one painter for raised surfaces: stacked soft shadow, fill, light top edge (dark only), hairline.
- `FocusButton` draws itself (UserPaint) from the colours `Theme.Btn` / `Primary` / `Lead` already set, so every
  existing button changes without touching its page, and pages that recolour a button keep working.
- Read-out numbers use `Theme.Num` (Bahnschrift, shipped with Windows), with a fallback when a face lacks the style.

## Alternatives Considered

- **New colour per scheme for raised surfaces:** 11 tables to extend and test; seams where child controls use `Panel`.
- **A third-party control library:** breaks the single small exe and the C# 5 / .NET 4.5 build.

## Consequences

- Positive: one place sets the depth; all schemes and High Contrast follow; buttons get real hover and press states.
- Negative: a button with an image is still drawn by WinForms (no such button exists today).
- Risk: owner-drawn buttons must keep focus cues and disabled contrast; `FocusButton` draws the 2 px focus ring.
