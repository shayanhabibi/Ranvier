# DOM framework performance comparison

> This record's Solid figures are Solid 1.9.17 and are kept as an earlier measurement. The harness now builds Solid 2 (2.0.0-rc.14 with `@solidjs/compiler`); current comparisons are in [owner lifetime](../2026-10-13-owner-lifetime.md) and the [mount cost rationale](investigation/mount-rationale.md).

Measured on 12 October 2026 in three independent Chromium 153.0.8010.12 processes on an AMD Ryzen 9 9900X. The [raw results](results.json) contain all 1,188 samples, per-process medians, runtime/package versions, source hashes and 36 retained-heap readings. This is one small component workload, not a general framework ranking.

Ranvier's update path is competitive with the two fine-grained libraries, but mounting, teardown and retained heap need attention. At 128 components it updates about 17% slower than Solid/Ripple and 45% faster than React's synchronous commits. It mounts about 3.2 times slower than Solid and twice as slowly as Ripple; disposal is about 7.4–7.8 times slower than those two.

## Workload and timing

Each row has a static ID and class, a reactive title, a live input value, a memo-derived disabled button, a component-local counter and a click handler. One shared source alternates between `a` and `blocked`, changing title/value/disabled in every row. The counter has its own state and updates independently. All frameworks render a section containing the same number of rows.

- Ranvier: the existing overloaded authoring fixture, component/fragment ownership and native DOM backend, compiled with Fable 5.18.0 in Release with counters disabled.
- Solid: `solid-js` 1.9.17 with production-compiled JSX and its template optimization, signals, a memo, per-row signals and `render` disposal. This uses the normal compiled DOM path rather than handwritten native DOM mutations.
- Ripple: published `Fable.Ripple.Dom` 1.0.0-beta.9 and `Fable.Ripple` 1.0.0-beta.8, compiled by the same Fable version in Release; the public HTML/attribute DSL and one owned signal scope per row.
- React: React/React DOM 19.3.0 production, `createRoot`, parent and row `useState`, a `useMemo` derived value and controlled input. Each update uses `flushSync` to include completed DOM work in timing. This measures synchronous commit cost, not normal concurrent scheduling or batched event throughput.

All four run in the same minified Vite production bundle and Chromium environment. Hosts are attached before timing. Complete mount includes state, memo, component setup, view construction and insertion; host creation is excluded. Disposal includes each framework's public teardown and DOM removal, with host removal outside timing. Updates commit independently, with no batch across iterations. No layout, painting or animation frame is forced.

There are three discarded warmup rounds, eleven measured rounds per process and three independent browser processes. Framework order rotates each round and process. Mount/disposal batches use 256 independent roots at width 1, 128 at width 16 and 32 at width 128. Values below are pooled sample medians in microseconds per operation, not isolated single-operation latency or confidence intervals. Mount/disposal numbers are sensitive to root count, GC and warmup; consult per-process readings before assigning a tight budget.

View-definition construction is deliberately excluded as a separate ranked phase: Ripple's authoring eagerly creates DOM, while Ranvier builds reusable definitions. Complete mounting includes the work needed by each model.

## Results

Framework order in every line: **Ranvier / Solid / Ripple / React**. Units: **microseconds per operation**, lower is better.

- **1 row:** mount 16.72 / 4.57 / 6.43 / 62.54; update 1.44 / 1.31 / 1.24 / 3.59; disposal 3.96 / 0.57 / 0.70 / 17.77.
- **16 rows:** mount 146.76 / 41.76 / 57.89 / 116.37; update 18.07 / 15.42 / 15.85 / 34.12; disposal 51.25 / 6.52 / 10.86 / 23.98.
- **128 rows:** mount 1048.12 / 331.09 / 520.62 / 580.78; update 147.65 / 125.72 / 126.48 / 267.37; disposal 369.37 / 49.84 / 47.19 / 87.97.

For 128-row updates, per-process medians were 137–159 microseconds for Ranvier, 119–127 for Solid, 119–131 for Ripple and 254–277 for React. The update ordering was consistent across processes. Ranvier's larger mounts are also slower than React's in this workload, despite its faster updates; there is no universal winner.

GC-stabilized retained JavaScript heap per 128-row mounted root was approximately 453 KiB for Ranvier, 130 KiB for Solid, 163 KiB for Ripple and 189 KiB for React (median across processes). This includes retained adapter/host references. It excludes total native DOM memory and measures retained heap, not allocation rate. Small-root heap readings and release deltas are noisy and can include runtime caching; they do not establish a leak or an exact memory budget.

## Correctness and limits

Before timing, every framework is checked at every width for initial live DOM state, a working local click, both source states, preserved input/button identity, empty hosts after disposal and idempotent disposal. Calling the exposed update API after disposal must leave the detached subtree's state unchanged. The raw `correctness.disposal` field means those DOM/API checks; it does not prove every subscription or listener was released. The current verifier labels this `disposalDom`; that label and its error wording were clarified after review, with timing behavior unchanged. Ranvier's adapter suppresses writes after its graph is disposed. Its separate [authoring resource checkpoint](../authoring-performance/2026-10-12-gc-correction.md) covers counters and listener cleanup within Ranvier.

Live DOM state is compared semantically, not by serialized HTML bytes. React's controlled input maintains a default value attribute and uses event delegation; Solid also delegates click events. Those framework behaviors are retained. Ranvier's ownership anchors and per-node teardown remain enabled. This benchmark does not compare identical internal allocations or listener counts across frameworks.

The results support profiling Ranvier's mounting and disposal paths before further authoring abstractions. Component ownership, intermediate view construction, DOM insertion and teardown granularity are candidates to inspect, not demonstrated causes. Solid's static template optimization is present in this comparison; Ranvier's equivalent static-view path remains future work. Keyed reconciliation, branch switching, forms/composition, async lifecycle, server rendering and whole-application throughput are outside this workload.

## Repeat

From `examples/Fable.Ranvier.Playground`:

```powershell
rtk npm run bench:frameworks
rtk npm run bench:frameworks:verify
rtk node --test benchmarks/frameworks/contract.test.mjs
```

The full command rebuilds the Fable adapters and production bundle, verifies correctness, measures all phases and writes `results.json`. The verify command runs correctness without collecting timing. It requires the existing installed Playwright Chromium browser. Benchmark dependencies are development-only. The Ripple comparison project is grouped with the playground in `Ranvier.slnx`.

Verification: all 36 preflight scenarios passed across the timed browser processes; the final production correctness run passed all 12 framework/width combinations after the label clarification. The benchmark contract rejection test passed. The existing full playground suite passed 193 Node cases and 21 Chromium tests. Both F# adapters built in Release with zero warnings/errors, and their XML documentation audit returned zero findings.

Primary API references: [Solid render](https://docs.solidjs.com/reference/rendering/render), [Solid batch](https://docs.solidjs.com/reference/reactive-utilities/batch), [Ripple source and package overview](https://github.com/fable-hub/Fable.Ripple), [React createRoot](https://react.dev/reference/react-dom/client/createRoot), [React flushSync](https://react.dev/reference/react-dom/flushSync). React documents that `flushSync` can hurt performance; the synchronous test deliberately includes that cost.
