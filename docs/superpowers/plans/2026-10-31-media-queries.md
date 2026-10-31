# G01/G15 media queries and host media preferences

## Core and view state

- **Core primitives used:** none inside `Ranvier.Dom`. A view that follows a query uses
  `Ranvier.View.Dom.MediaQueries.matches`: a signal written in `batch` on each change, with the query and its
  subscription disposed through `onCleanup` on the current `Owner`.
- **View-owned state:** none. The service keeps each query's observer list and its `change` listener, which are
  registrations on an engine event target, and the closers of the queries it created.

## Contract

`src/Ranvier.Dom/MediaQueries.fs`: `MediaPreferences` (color scheme, reduced motion, contrast, primary and available
pointers with hover, text scale; `None` keeps the engine's value), `MediaQuery` (owned `MediaQueryList`: `Media`,
`Matches`, `Subscribe` returning an idempotent removal handle, `Dispose`), `MediaPreferenceSupply`,
`MediaQueryProvider` and `MediaQueryService` (`Match`, `CanSupply`, `Supply`, `Supplied`, `Unavailable`, `Dispose`).
`MediaQueries.validate` rejects `CoarseAndFine` as the primary pointer and a text scale outside 0.25 to 8.

Providers: `BrowserMediaQueries.forDocument` (Fable; the platform supplies preferences, so `CanSupply` is false and
`Supply` raises NotSupportedException); the native provider `Ranvier.View.WebCore.MediaQueries.forHost` over
`rv_host_match_media`, `rv_host_set_media_preferences` and `rv_host_media_preferences`.

## Declared limits

The native engine evaluates forced-colors as none and prefers-contrast less and custom as never matching, and has no
reduced-transparency or reduced-data feature: the native `Unavailable` lists them. Text scale changes layout, not the
computed font-size or any media query. Change order across lists is engine-specific.

## Proof

The shared conformance fixture `MediaQueryChecks.fs` in Views runs on native JIT, NativeAOT and Chromium over the steps
of Ranvier.WebCore `tests/fixtures/media-queries.json`. Native engine evidence: Ranvier.WebCore
`docs/media-evidence/final-evidence.json`.
