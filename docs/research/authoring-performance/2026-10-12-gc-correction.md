# Corrected authoring performance check-in

The initial .NET construction penalty was a benchmark artefact. [Collection diagnostics](gc-diagnostic.json) show every slow one-component overload batch had a collection; collection-free batches matched the explicit path. Equal batch allocations and deterministic ordering placed collections repeatedly in one variant's timed batches.

The corrected harness starts a 64 MiB no-GC region before each timed .NET construction sample, records all generation collection counts and rejects contaminated samples. Setup/teardown and collection counters are outside timing. Tiered compilation is disabled. This isolates construction cost and excludes real-world GC latency; it is not a server-rendering benchmark.

[Corrected raw results](browser-results.json) contain three independent Chromium runs and three independent .NET processes, with three warm-up rounds and eleven measured rounds per combination. Every .NET sample has zero collections and exact paired allocation parity.

Corrected .NET medians, explicit then overloaded:

- One component: 0.161 / 0.160 microseconds; both allocate 840.0048 bytes.
- Sixteen components: 3.249 / 3.139 microseconds; both allocate 12,656.024 bytes.

No renderer or generated helper change was needed.

Browser medians in microseconds per operation, explicit then overloaded:

- One component: construction 1.00 / 1.07; mount 13.36 / 13.20; update 1.49 / 1.46; disposal 3.28 / 3.20.
- Sixteen components: construction 6.61 / 8.32; mount 115.08 / 115.86; update 19.12 / 18.99; disposal 43.36 / 39.77.

The sixteen-component paired median update ratio is 0.998. Construction is slower in this reference run: per-process overloaded/explicit median ratios range from 1.11 to 1.55. Earlier runs reversed direction in some processes. This does not establish universal zero-cost authoring or a tight timing regression threshold.

Graph owner/effect/edge counts, listener registrations/removals and native DOM write counts match exactly. Instrumentation runs separately from timing. Mount/disposal are batch averages sharing a graph and host; construction retains definitions during its batch; updates flush each separate Graph.Run. CDP heap results measure retained JS heap, not allocation rate.

Reproduce from the repository root:

```powershell
rtk npm run bench:authoring --prefix examples/Fable.Ranvier.Playground
```

The command builds separate counter and non-counter Fable outputs, verifies exact resource and .NET allocation parity, and writes fresh results. Running the .NET Release DLL with `--allow-gc` reproduces the diagnostic behavior.
