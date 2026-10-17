# Minimal text bindings and boundary repair — 2026-10-11

Supersedes the per-binding outcome experiments in `272b82b` and `6d8783a`. The user rejected duplicating pending/error presentation at each text binding. Passing tests for that architecture did not establish that it was appropriate.

## Current contract

`Typed.reactiveText ops read` accepts `unit -> string` and constructs `ops.Bind (=) read`. Tree and Program carry the same small operation. The synchronous mounting path uses one ordinary effect attached to the enclosing owner, a committed string cache, and untracked comparison/write. It creates no extra binding root or outcome policy record.

`BindWith`, `Binding.Policy`, per-binding pending/failed/status/report callbacks, and `Html.renderBindings` are removed. Ordinary HTML rendering escapes the string supplied by the reader. A DOM setter failure during ordinary mount construction is captured and rethrown after effect creation, allowing the existing mount transaction to release component resources and DOM claims. This construction check is shared by the mount.

`mountWith` selects scheduling for ordinary `Bind` as well as the typed helper. A queued binding additionally reserves a versioned slot and cancels older work before reading, including when the next read suspends or fails. Initial settled text commits immediately; subsequent writes are deferred. Nested keyed mounts share the containing queue; removed rows cancel their work. Queue error handling remains a scheduler concern.

The playground uses a core `createBoundary` over an async source. Result, waiting status and caught-error text read that boundary; row text reads strings directly. Tests prove two text nodes share a single boundary evaluation/fallback through pending, failure, recovery and disposal, using Tree/Program and synchronous/queued mounts.

## Boundary limit

Core boundaries catch their computation's reads. They do not catch failures raised later by descendant effects, and this repair does not implement a structural view boundary or coordinated reveal. An unhandled pending/failed binding read follows ordinary core effect isolation and reaches no DOM write; a failed live synchronous setter is likewise recorded by the core effect. Deferred setter failures are exposed by the queue's `CommitErrors`. Production reporting and structural boundary behavior remain separate work. The shared-boundary tests do not claim coverage of descendant-effect error routing.

Mounting inside a core batch or creating a keyed row during a graph flush can defer its first effect until after attachment. Those first setter failures follow live-effect handling rather than construction rollback. This existing core scheduling limitation is outside the repair's tested setup-unwind contract.

## Performance evidence

The [raw results](binding-minimal/browser-results.json) contain 1,650 samples, three independent Chromium processes, three warmups and eleven measured rounds per case. Fable 5.18.0 Release, Chromium 153.0.8010.12, Node 26.7.0, Ryzen 9 9900X, Windows 10.0.26200, isolated high-resolution timers.

The frozen benchmark-only `BindingBaselineMounting.fs` is the mounting source from `79b9e96`, with its module name changed. Both implementations use the same current core and identical benchmark handle records. This isolates the renderer comparison; it does not recreate historical core performance. The baseline source is excluded from the application project. Run `rtk npm run bench:binding` in the playground to reproduce.

Median microseconds per operation:

| Width | Phase | Original synchronous | Corrected direct synchronous | Typed helper synchronous | Corrected direct queued |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | Mount | 6.250 | 6.172 | 6.016 | 6.172 |
| 1 | Update | 0.258 | 0.257 | 0.260 | 1.070 |
| 1 | Batch 100 writes | 0.875 | 0.850 | 0.825 | 1.875 |
| 1 | Dispose | 1.172 | 1.172 | 1.250 | 1.328 |
| 64 | Mount | 48.281 | 46.875 | 46.875 | 62.344 |
| 64 | Update | 11.870 | 12.560 | 12.870 | 25.190 |
| 64 | Batch 100 writes | 23.400 | 25.650 | 24.750 | 38.875 |
| 64 | Dispose | 15.781 | 17.812 | 14.766 | 22.578 |

Direct synchronous update ratios are approximately 1.00× and 1.06×; helper ratios are 1.01× and 1.08×. The rejected policy implementation measured 2.08–2.52× in its earlier run. Queued mode still has meaningful overhead. Small timing differences and noisy disposal results are not a stable performance budget or a framework parity claim.

Queue timings include manual flush and exclude microtask latency. The 100-write case uses core batching for all modes and therefore does not isolate queue coalescing across unbatched writes. No retained-allocation, layout, rendering or heap conclusion follows from these results.

## Verification

- Complete playground acceptance: 156 Node tests and 18 Chromium tests passed. Removed policy-specific cases account for the lower count; scheduler/lifetime and meaningful setter/reentry regressions were retained or replaced.
- Canonical Release test pipeline: 938 F# and 81 C# tests passed on each of net8.0/net10.0, plus AOT smoke.
- Portable representation executable: all six groups passed; Release build had zero warnings/errors. Core-boundary HTML cases additionally pass in SageFs, Node and Chromium.
- XML audit of Binding, Representation, Mounting and Typed: zero findings.
- Fresh scoped FCS checks: DomTests, Playground and RepresentationChecks each clean, zero errors/warnings. Rebuilt SageFs session: all 16 portable binding/boundary cases passed. The HTTP client emitted `BrokenResourceError` during SSE teardown after returning the successful evaluation.
- Production build passed: 81.19 kB JS, 22.33 kB gzip. This includes the changed boundary demo and additional core imports; it is not an isolated binding-runtime size measurement.

The known whole-solution Fable-test compilation failures remain outside this repair. Production package integration, structural boundaries, generated bindings and performance/retained-allocation budgets remain open.
