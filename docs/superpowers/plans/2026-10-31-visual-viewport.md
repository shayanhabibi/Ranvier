# G03 visual viewport service (DOM alpha.40)

## Core and view state

- **Core primitives used:** none inside `Ranvier.Dom`. A view that subscribes calls `VisualViewportService.Subscribe`
  under an `Owner` and registers the handle's `Dispose` with `onCleanup`; values derived from changes are written to
  signals inside `batch`.
- **View-owned state:** none. The service keeps its subscriber list and its two listeners on the engine's
  `VisualViewport` event target.

## Contract

`src/Ranvier.Dom/VisualViewport.fs`:

- `VisualViewportState` holds the seven `VisualViewport` members. `VisualViewportInsets` holds the obscured edges.
- `VisualViewportUnavailable` is `Detached | Unsupported of reason`. `VisualViewportChange` is
  `Resized | Scrolled`, each carrying the state read when the event fired.
- `VisualViewportProvider` holds the shared `VisualViewport` object, `Read`, `SetPageScale` and `SetObscuredInsets`.
- `VisualViewportService` has `Current`, `SetPageScale`, `SetObscuredInsets`, `Subscribe` and `Dispose`. Its inputs
  raise `ArgumentException` for a scale that is non-finite or at most 0, and for an inset that is
  non-finite or negative.

`VisualViewport` is the generated shared interface of `Generated/Dom.fs`, copied whole from Ranvier.WebCore
`gen/Ranvier.Dom.fs`.

Providers:

- `BrowserVisualViewport.forDocument` (Fable) reads the document's `window.visualViewport`. A page cannot set the
  page scale or the obscured insets, so both inputs return `Unsupported`. A document without a Window reads
  `Detached`, rather than Chromium's all-zero members.
- The native provider in Views `Ranvier.View.WebCore/VisualViewport.fs` (Views 0.1.30) is built over
  `rv_dom_window_get_visual_viewport`, the `rv_dom_visual_viewport_*` getters (`RV_ERR_DETACHED` maps to
  `Detached`), `rv_host_set_page_scale` and `rv_host_set_obscured_insets` (Native37/ABI45).

## Proof

The shared conformance fixture `VisualViewport.fs` in Views runs on native JIT, NativeAOT and Chromium. Native
engine evidence: Ranvier.WebCore `docs/visual-viewport-evidence`.
