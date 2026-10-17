# Remaining mount cost after cleanup

The production comparison remains **980 µs Ranvier / 533 µs Ripple / 313 µs Solid** at 128 rows: **1.84× Ripple** and **3.13× Solid**. This diagnostic capture profiles the cleaned-up code at `b1de7f5`; it does not replace the minified production comparison.

The strongest current explanation for the Ripple gap is extra renderer/authoring setup and allocation, with per-node ownership claims adding work. The Solid gap also includes its prepared-template advantage. These measurements do not establish an unavoidable cost of core guarantees or an exact percentage attributable to each mechanism.

## Allocation evidence

CDP allocation sampling estimates **689 KiB per Ranvier mount**, **369 KiB Ripple** and **172 KiB Solid** at 128 rows, approximately **1.87× Ripple** and **4.00× Solid**. The direct-owned-backend diagnostic estimates **645 KiB**, only about 6% below the normal Ranvier authoring path. Removing Program construction alone will not close the gap.

Ranvier's largest allocating source groups are approximately **172 KiB mounting**, **152 KiB authoring fixture**, **132 KiB core**, and **83 KiB typed modifiers**. Binding setup alone accounts for approximately **112 KiB** in the sampled stacks. This makes callback construction, binding state, component setup, modifier recipes and cleanup registration concrete targets. Core allocation is only one part of the total; these results do not justify blaming the complete gap on reactive guarantees.

Arrays removed linked-list churn, but each independent mount still constructs component definitions, child arrays, bound readers/writers and listeners. A promising next step is sharing identical source readers and modifier recipes across rows where their semantics permit it, and simplifying the binding/element setup path. Those changes must retain owner context, untracked writes, setter failure handling and reentrant disposal.

Allocation sampling used a 4096-byte interval, one browser process, and included collected objects. These are estimates of transient JavaScript allocation, not exact object counts or total native DOM memory. The earlier baseline allocation sample was about 1.04 MiB, so the cleanup substantially reduces transient allocation despite the roughly 5% higher retained heap reported separately.

## CPU and DOM evidence

Ranvier and Ripple both create **385 elements and 128 text nodes**. Ranvier additionally creates a staging fragment and two anchors, claims **515 nodes**, and retains ownership references. Its node-claim function is a visible CPU hotspot. Equal element counts rule out DOM count as an explanation of the Ripple gap; insertion timing and other native behavior can still differ.

Solid uses **129 template clones** instead of those individual creation calls. Prepared DOM templates therefore remain a concrete client-side optimization opportunity for Ranvier.

The Ranvier CPU capture also shows function-arity adapters (`uncurry2`) and substantially more GC samples than Ripple/Solid. The prominent generated input-value setter frame is common DOM work: Ripple has a similar setter hotspot. It is not evidence that generated bindings themselves dominate the extra cost.

The CPU profiles request a 100-microsecond interval, but Windows sampling is coarser. `(program)` includes unattributed native/event-loop work and activity around the CDP call. Profiled timings are unminified and instrumented. Exact source percentages and additive time budgets would overstate this evidence.

## Repeat

First rebuild counter-free Release Fable adapters using `rtk node benchmarks/frameworks/run.mjs --build --verify-only`. Then run, sequentially from the playground:

```powershell
rtk node benchmarks/frameworks/diagnose.mjs --build --cleanup
rtk node benchmarks/frameworks/summarize-profiles.mjs --cleanup
rtk node benchmarks/frameworks/allocations.mjs --cleanup
```

The cleanup flag keeps these captures separate from the archived baseline. CPU profiles include their matching compressed source map; allocation stacks are mapped during collection. No production code changes accompany this diagnosis.
