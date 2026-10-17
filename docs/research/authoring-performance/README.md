# Authoring performance checkpoint

**Superseded timing interpretation:** the apparent .NET construction penalty below was caused by GC scheduling in the benchmark. See the [GC correction and current results](2026-10-12-gc-correction.md). This file preserves the initial checkpoint; its [original samples](before-gc-control.json) are retained separately.

Measured on 2026-10-12 against base `c425ccf`, with the benchmark additions uncommitted. [Raw results](before-gc-control.json) retain all samples, staged counters, native write counts, runtime/machine metadata and benchmark source hashes.

The overloaded authoring path preserves resource and update costs in this workload. Browser timings do not show a consistent slowdown across processes. This is not evidence that every generated overload is universally free: .NET definition construction is consistently slower despite allocating exactly the same bytes.

## Browser results

Each definition contains either one or sixteen independent components. Components have static id/class attributes, a signal-backed title and input value, a memo-backed disabled property, local reactive text and a click listener. Both paths call the same shared property helpers; the overload path supplies its source adapters and null guards. The component/element/text authoring helpers are common to both.

Pooled medians in microseconds per operation, explicit then overloaded:

- One component: construction 1.12 / 1.26; mount 14.45 / 15.08; update 1.72 / 1.54; disposal 2.73 / 2.89.
- Sixteen components: construction 7.55 / 9.37; mount 138.13 / 139.06; update 21.25 / 21.46; disposal 54.92 / 57.97.

The sixteen-component paired median update ratio is 1.011. Construction varies much more: per-process overloaded/explicit median ratios range from 1.08 to 1.62 in this reference run. Earlier exploratory runs with the same fixture reversed the direction in some processes. These samples do not establish a construction speed advantage or a tight timing regression threshold.

Three independent Chromium processes each run three warm-up rounds and eleven measured rounds, with authoring order rotated. Timing uses an uninstrumented Release Fable build and isolated high-resolution timers. Mount and disposal are batch averages of 64 independent mounts sharing a graph and host, not individual latency measurements. Construction retains definitions during its timed batch; updates alternate values and flush every separate Graph.Run. Cleanup and validation occur outside timing.

## Resources and allocation

Separate counter-enabled builds compare all graph counters at construction, mounting, update and disposal. Native setter/attribute and listener interception runs only during this verification. Both paths match exactly:

- One component: three owners, four effects, five dependency edges and one listener at mount.
- Sixteen components: eighteen owners, sixty-four effects, sixty-five dependency edges and sixteen listeners at mount.
- Listener removal, attribute/property/text write counts and mounted output also match. Update and post-disposal probes preserve node identity and stop disposed callbacks/bindings.

Three independent Release .NET processes measure construction allocations with GetAllocatedBytesForCurrentThread. Both paths allocate 840.0048 bytes per one-component definition and 12,656.024 bytes per sixteen-component definition. These figures include amortized output-array storage and its header. The runner fails on any paired allocation difference.

.NET construction medians are 0.105 / 0.725 microseconds for one component and 5.926 / 8.666 microseconds for sixteen. Tiered compilation is disabled for these runs; this is a construction-only diagnostic, not a server-rendering benchmark. The timing difference is a follow-up to investigate before claiming .NET performance parity. Its cause has not been isolated.

Chromium CDP measures retained JS heap for 512 definitions after forced collection, followed by release and collection. This measures retained heap, not allocation rate. Consult the raw three-process samples; small residual heap differences include VM bookkeeping and should not become exact allocation budgets.

## Reproduce

From the repository root:

```powershell
rtk npm run bench:authoring --prefix examples/Fable.Ranvier.Playground
```

This builds the .NET executable, compiles a separate counter-enabled Fable output, recompiles the ordinary Fable output without counters, verifies parity and writes fresh results. The new .NET project is included in Ranvier.slnx. No renderer or reactive-core implementation changed.

The executable checks exact resource parity and paired .NET allocation parity. Timing samples form a repeatable baseline; broader browser/platform coverage and stable timing budgets remain open.
