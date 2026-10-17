# Delegated listeners

Bubbling DOM events are dispatched from one listener per mount host instead of one listener per element. `Backend.Listen (eventName, handler)` replaces the generic browser hook for event modifiers. The owned mount backend stores each owner-aware handler on its element and registers the event name on the mount's host after attach; other backends attach direct listeners (`Browser.fs`), omit them (server HTML) or reject them (prepared skeletons).

The [mount cost rationale](framework-performance/investigation/mount-rationale.md) attributed 118 µs of 128-row mount time and 57 µs of disposal to per-element listeners, with no guarantee lost by delegation.

## Contract

- Handlers run in their owner's graph, untracked, and do nothing once that owner is disposed, as before.
- Delegated events: `beforeinput`, `click`, `contextmenu`, `dblclick`, `focusin`, `focusout`, `input`, `keydown`, `keyup`, the `mouse*` and `pointer*` down, move, out, over and up events, and `touchstart`, `touchmove`, `touchend`. Every other event keeps a direct listener removed on disposal.
- The host listener walks from the target to the host and presents each handled element as `currentTarget`. `stopPropagation` stops the walk. A host inside another mount records itself on the event, so the outer host resumes above it and each handler runs once.
- Detached hosts dispatch normally. A moved mount registers on its new host; each host listener is removed when the last mount using it is disposed.
- Delegated handlers run when the event reaches the host, after native listeners on elements between the target and the host.

The playground suites pass 211 Node tests, including six delegation cases, and 22 Chromium tests.

## Measurements

[Raw results](framework-performance/investigation/delegation-framework-results.json) use the framework harness against Solid 2.0.0-rc.14: three Chromium processes, all 36 correctness checks passing. At 128 rows, µs per operation:

| | Ranvier | Solid 2 | Ripple | React |
|---|---|---|---|---|
| Mount | 672 | 311 | 439 | 545 |
| Update | 120 | 113 | 123 | 267 |
| Dispose | 103 | 29 | 52 | 93 |
| Retained JS heap | 349 KiB | 115 KiB | 163 KiB | 189 KiB |

Every framework measured faster than in the [owner-lifetime run](2026-10-13-owner-lifetime.md), so the change is read against the frameworks measured in the same processes. Mounting falls from 2.50× to 2.16× Solid 2 and from 1.73× to 1.53× Ripple; disposal falls from 4.5× to 3.6× Solid 2 and from 2.8× to 2.0× Ripple. Scaled to the earlier Solid 2 timing, mounting moves from 884 to about 763 µs, matching the ladder's 118 µs estimate. Retained heap falls 12%, from 397 to 349 KiB.
