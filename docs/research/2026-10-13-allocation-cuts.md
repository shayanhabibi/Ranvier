# Allocation cuts

This removes allocations from the mount path that carry no semantic purpose, following the [mount cost rationale](framework-performance/investigation/mount-rationale.md). Handler ownership, rollback and equal-write skipping are unchanged.

| Change | Allocation removed |
|---|---|
| `Backend.ElementWith` runs an element's children array directly; its default implementation calls `Element`, so other backends are unchanged | one children closure per element |
| `Typed.listener` and `Typed.reactiveProperty` pass the caller's handler, reader and setter through; `ReactiveProperty` takes its writer as `Func<obj, obj, unit>` | one adapter closure per event or property modifier, and a `curry2`/`uncurry2` chain per property |
| `Listen` checks delegated event names with an index loop | one comparer object per element |
| Component setup builds its cleanup function as one closure | a curried adapter and its partial application per component |
| Synchronous bindings commit through one closure per binding | one closure per effect run, on mount and every update |

Fable inserts conversion closures when a curried function value crosses a boxing or curried parameter boundary; `unbox` does not suppress them. The pass-through helpers are `Emit("$0")` functions typed with the exact source and target function types, and the writer is a delegate, which Fable passes as a plain function.

## Measurements

Allocation sampling ([raw](framework-performance/investigation/allocation-cuts-profile/allocations.json)), one browser process, 4096-byte interval: Ranvier allocates 509–519 KiB per 128-row mount across three samples, from 568 KiB after [delegated listeners](2026-10-13-delegated-listeners.md); Solid 2 allocates 136–146 KiB and Ripple 367–374 KiB. Samples taken without the binding change measured 510–517 KiB, so its effect on mounting is within sampling noise; it removes a closure from every update.

[Framework results](framework-performance/investigation/allocation-cuts-framework-results.json) against Solid 2.0.0-rc.14, all 36 correctness checks passing. Every framework measured slower than in the previous run, so the change is read against the frameworks measured in the same processes. At 128 rows, µs per operation:

| | Ranvier | Solid 2 | Ripple | React |
|---|---|---|---|---|
| Mount | 885 | 465 | 675 | 820 |
| Update | 162 | 153 | 168 | 377 |
| Dispose | 154 | 47 | 87 | 140 |
| Retained JS heap | 341 KiB | 115 KiB | 163 KiB | 189 KiB |

Mounting moves from 2.16× to 1.90× Solid 2 and from 1.53× to 1.31× Ripple; an earlier run during the same slow period measured 1.93× and 1.39×. Disposal moves from 3.6× to 3.3× Solid 2 and from 2.0× to 1.8× Ripple. Retained heap falls from 349 to 341 KiB.

The playground suites pass 211 Node tests and 22 Chromium tests; the portable .NET representation checks pass.
