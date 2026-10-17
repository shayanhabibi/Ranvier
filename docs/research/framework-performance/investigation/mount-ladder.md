# Mount cost ladder

This follows the prepared-template checkpoint at `a431b14`. The ladder attributes production mount and disposal time to individual renderer steps. Four compile-time gates in `Mounting.fs` replace one step each with its naive equivalent; a fifth replaced the renderer lifetime model with core owners. Without defines, the emitted `Mounting.js` is byte-identical to the ungated source.

The owner-lifetime variant has since become production; see [owner lifetime](../../2026-10-13-owner-lifetime.md). The R4 production figures below measure the earlier renderer-scope model, and the R5 variant is no longer built by `ladder.mjs`. `ladder-results.json` now holds the owner-lifetime re-run analysed in [mount rationale](mount-rationale.md); the raw results for the figures below are in commit `8bda3b4`.

| Step | Production | Naive replacement (`LADDER_NAIVE_*`) |
|---|---|---|
| Setup checks | `ensureSetup` around modifiers and appends, active-element check, `validateAttribute` | empty `ensureSetup`; attribute validation skipped (`CHECKS`) |
| Range ownership | `range.Add`/`AppendGenerated`: entries, WeakSet claims, descendant list; attach validation | `appendChild`; top-level roots recorded and removed once (`RANGE`) |
| Component ownership | child `Scope`, `createRoot`, two cleanup links, `untrack` | setup runs inline in the mount scope (`COMPONENTS`) |
| Binding guards | `startBinding`: committed value, comparer, liveness, untracked write, setup failure capture | `createEffect (fun () -> write (read ()))` (`BINDINGS`) |

Each rung adds one step to the previous rung. R0 defines all four symbols; R4 defines none and is production. Step costs therefore sum exactly to R4 minus R0. A step's cost depends on its position in this order; gates interact, and a different order would assign shared costs differently.

## Floor split

Below R0, the floor splits along adapters measured on the R0 page, each adding one layer to the previous:

| Adapter | Adds |
|---|---|
| `native-create` | handwritten DOM construction, listeners, no reactivity |
| `reactive-native` | graph, a local signal per row, one bare effect per binding, one shared root; no renderer |
| `ranvier-direct` | gated backend through direct `Backend` calls: mount scope, range staging and attach, modifier closures, typed listeners with cleanup registration |
| `ranvier` | public authoring: `Program.View` closures, overloaded helpers, modifier arrays |

`native-create` and `reactive-native` bypass the gated renderer and are identical in every bundle, so they also bound page-to-page variance.

## Owner lifetime variant

R5 is production compiled with `LADDER_OWNER_LIFETIME`. It is compared against R4 and is not part of the cumulative ladder.

| | Production (R4) | Owner lifetime (R5) |
|---|---|---|
| Component | child `Scope`, `createRoot`, two cross-links, `untrack` | `createRoot`, `untrack`; setup fails if the owner is disposed |
| Component and listener cleanup | renderer `Scope` callbacks | core owner cleanups; errors are collected and reported through the mount scope; registration after disposal is rejected |
| Generated descendants | WeakSet claim, descendant list, `appendChild` | `appendChild` |
| Top-level roots | claimed and validated | unchanged |
| DOM release | renderer drain after all callbacks | release-only child owner created first under the mount owner, so it releases after every component cleanup |

Every cleanup still runs while its DOM is attached, on explicit and core-driven disposal. Compiled with the gate, the full playground suite passes 196 of 205 Node tests and all 19 playground application tests in Chromium. The 9 failing cases are the guarantee changes, repeated by the Chromium range and investigation specs:

- **Parent-first cleanup:** a parent component's callbacks run before its children's (`range cleanup runs child first while DOM stays attached`).
- **Reverse sibling order:** sibling components clean up in reverse creation order (`128` and `512 components clean up without stopping a sibling mount`).
- **Moved descendants:** a generated descendant moved to another host is neither removed on disposal nor protected against overlapping ownership (`generated descendants remain owned after external reparenting`, `moved generated descendants reject overlap and become reusable after disposal`).
- **Intact detached subtrees:** disposal detaches each root once; descendants remain inside the detached root instead of being unlinked individually (four `structural selection shares a core boundary` cases).

## Validation

Every rung passes the framework contract for all four adapters at widths 1, 16 and 128 in all three processes: 216 checks. The ungated full playground suite passes 205 Node tests and 22 Chromium tests, and its emitted `Mounting.js` is byte-identical to the ungated source. Naive rungs are not run against the full suite; they deliberately omit ownership guarantees.

## Measurements

`ladder-results.json` contains one bundle per rung in three independent Chromium processes, three warmups and eleven measured rounds of mount and disposal, with rung and adapter order rotated each round. The control adapters are identical in every bundle. At 128 rows they range 355–405 µs (`native-create`) and 448–517 µs (`reactive-native`) for mounting across rungs, and 37–58 µs for disposal. Mount deltas below roughly 50 µs pooled, and single-process deltas below roughly 100 µs, are not resolved. Disposal is far less noisy. Absolute levels shift between runs; two earlier runs measured R4 mounting at 1,044 and 1,084 µs.

At 128 rows, public authoring path, µs per operation:

| Layer or step | Mount | Share | Mount per process | Dispose | Share | Dispose per process |
|---|---|---|---|---|---|---|
| Native DOM (`native-create`) | 404 | 42.8% | 426 / 300 / 378 | 37 | 9.8% | 58 / 35 / 37 |
| + reactive core | 114 | 12.0% | 127 / 193 / 25 | 16 | 4.1% | 11 / 12 / 11 |
| + backend dispatch and mount | 250 | 26.4% | 254 / 224 / 355 | 72 | 19.2% | 77 / 69 / 66 |
| + authoring layer | 27 | 2.8% | 79 / 20 / −24 | −3 | −0.7% | 17 / 1 / −3 |
| = R0 floor | 794 | | 887 / 736 / 733 | 123 | | 164 / 117 / 111 |
| + setup checks | 55 | 5.8% | 38 / 73 / 43 | 6 | 1.6% | 0 / 10 / 11 |
| + range ownership | 86 | 9.1% | 53 / 68 / 114 | 127 | 33.5% | 204 / 116 / 114 |
| + component ownership | 25 | 2.6% | 66 / −22 / −2 | 85 | 22.5% | 127 / 89 / 94 |
| + binding guards | −15 | −1.6% | 47 / 80 / 34 | 38 | 10.0% | −27 / 6 / 16 |
| = R4 production | 945 | | 1,090 / 934 / 923 | 378 | | 469 / 338 / 345 |

Backend dispatch and mount is the largest attributable mount layer in both runs that split the floor: 260 and 250 µs. Range ownership is the largest gated mount step in every run: 127, 115 and 86 µs. For disposal, range ownership and component ownership dominate: together 212 of 378 µs, resolved in every process. The `ranvier-direct` gated deltas are 50, 82, 35 and 17 µs for mounting and 12, 166, 64 and −14 µs for disposal.

Owner lifetime against production, µs per operation:

| Width | Phase | R4 | R5 | Δ | Per process | Control Δ (native / reactive) |
|---|---|---|---|---|---|---|
| 16 | mount | 128 | 112 | −16 (−12.6%) | −14 / −6 / −10 | −2 / −7 |
| 16 | dispose | 59 | 30 | −29 (−48.9%) | −26 / −22 / −35 | 1 / 0 |
| 128 | mount | 945 | 886 | −58 (−6.2%) | −85 / −51 / −75 | −50 / −51 |
| 128 | dispose | 378 | 186 | −192 (−50.9%) | −242 / −171 / −181 | −5 / −4 |

Owner lifetime halves disposal at both widths; the control adapters do not move, so the reduction is resolved. The 128-row mount reduction is not resolved: both controls fell by the same 50 µs on the R5 page. At 16 rows the 16 µs mount reduction exceeds its control drift in every process. `ranvier-direct` agrees: −171 µs disposal and an unresolved −44 µs mount at 128 rows. R5 disposal remains roughly 3.5× Solid and Ripple (49 and 54 µs in the prepared comparison).

At 16 rows every gated mount step is within noise; production mounts in 128 µs against a 49 µs native mount.

## Reproduction

From `examples/Fable.Ranvier.Playground`:

```powershell
rtk node benchmarks/frameworks/ladder.mjs --build
```

`--build` compiles each rung's Fable output into the ignored `generated-ladder/<rung>` directory and bundles it into `generated-ladder-dist/<rung>`; Vite redirects the page's `generated-tests` imports through `RANVIER_LADDER_TESTS`. Without `--build`, the script reuses existing bundles. The artifact records each rung's defines, source and bundle hashes, every raw sample, per-rung summaries, per-step deltas, the floor split and the owner-lifetime comparison, for mounting and disposal.
