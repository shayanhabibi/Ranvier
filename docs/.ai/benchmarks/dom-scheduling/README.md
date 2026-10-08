# Fable.Ranvier DOM scheduling benchmark

Measured 2026-10-02T13:02:34.622Z at commit f14d8eba4d11150dea6f4936175de3250b3fdfd9.

Host: AMD Ryzen 9 9900X 12-Core Processor, win32 10.0.26200, Node v26.7.0, Chromium 153.0.8010.12 (headless).

## Method

Release-compiled F# scheduling fixtures run on attached real Chromium DOM nodes. Four modes isolate the queue from core batching. Each row has its own graph and mount; this is a multi-widget workload, not a single shared-graph dashboard. Core batching wraps each row's writes using the fixture's Batch function; the input case dispatches synthetic input events.

Five warm-up runs per workload/mode, then 21 measured samples with rotating mode order in one browser process. Setup/mounting and disposal are excluded. The timer includes signal/event work, two Promise microtask boundaries after every wave (in all modes), and an offsetHeight read after each wave to settle layout. It excludes animation-frame scheduling and paint; waves are drained immediately rather than paced at real frame or typing intervals. Ratios are ratios of median elapsed times for identical work, not absolute frame rates. No GC control, confidence intervals or cross-browser runs. Samples within a run share a browser process; an earlier separate browser-process run from this investigation is retained in [replicate-1.json](replicate-1.json).

Final text, attributes, derived values, input connectivity, error state and expected setter/memo counts are asserted. The setter counter measures one counter-property binding, not all DOM writes. An input-only run therefore reports zero counter setter/memo calls. Input setters compare the live DOM value and skip identical assignments in both modes. The layout-reading case additionally calls offsetHeight inside every counter setter; it deliberately exposes interleaved writes/layout reads and is not representative of cheap setters.

## counter-burst

1 independently mounted rows × 100 writes per row per wave × 400 waves.

- **sync**: 62.20 ms median (middle 50%: 60.60–66.30 ms); 1.00× baseline throughput. Counter property setter calls: 40000; derived memo computations: 40000.
- **microtask**: 58.60 ms median (middle 50%: 55.60–61.90 ms); 1.06× baseline throughput. Counter property setter calls: 400; derived memo computations: 40000.
- **sync+batch**: 5.40 ms median (middle 50%: 5.00–5.50 ms); 11.52× baseline throughput. Counter property setter calls: 400; derived memo computations: 400.
- **microtask+batch**: 6.20 ms median (middle 50%: 5.70–6.80 ms); 10.03× baseline throughput. Counter property setter calls: 400; derived memo computations: 400.

Adding the queue to core batching: 0.87× throughput versus sync+batch.

## dashboard-burst

40 independently mounted rows × 10 writes per row per wave × 100 waves.

- **sync**: 57.20 ms median (middle 50%: 56.20–59.00 ms); 1.00× baseline throughput. Counter property setter calls: 40000; derived memo computations: 40000.
- **microtask**: 81.20 ms median (middle 50%: 79.50–87.20 ms); 0.70× baseline throughput. Counter property setter calls: 4000; derived memo computations: 40000.
- **sync+batch**: 25.30 ms median (middle 50%: 24.10–26.10 ms); 2.26× baseline throughput. Counter property setter calls: 4000; derived memo computations: 4000.
- **microtask+batch**: 36.90 ms median (middle 50%: 35.40–37.60 ms); 1.55× baseline throughput. Counter property setter calls: 4000; derived memo computations: 4000.

Adding the queue to core batching: 0.69× throughput versus sync+batch.

## dashboard-single

40 independently mounted rows × 1 writes per row per wave × 200 waves.

- **sync**: 52.50 ms median (middle 50%: 49.30–54.50 ms); 1.00× baseline throughput. Counter property setter calls: 8000; derived memo computations: 8000.
- **microtask**: 66.70 ms median (middle 50%: 65.70–74.90 ms); 0.79× baseline throughput. Counter property setter calls: 8000; derived memo computations: 8000.
- **sync+batch**: 47.30 ms median (middle 50%: 46.20–48.80 ms); 1.11× baseline throughput. Counter property setter calls: 8000; derived memo computations: 8000.
- **microtask+batch**: 68.90 ms median (middle 50%: 67.20–70.60 ms); 0.76× baseline throughput. Counter property setter calls: 8000; derived memo computations: 8000.

Adding the queue to core batching: 0.69× throughput versus sync+batch.

## input-single

1 independently mounted rows × 1 writes per row per wave × 3000 waves.

- **sync**: 48.90 ms median (middle 50%: 44.70–52.50 ms); 1.00× baseline throughput. Counter property setter calls: 0; derived memo computations: 0.
- **microtask**: 54.90 ms median (middle 50%: 51.90–59.80 ms); 0.89× baseline throughput. Counter property setter calls: 0; derived memo computations: 0.
- **sync+batch**: 49.10 ms median (middle 50%: 46.90–52.70 ms); 1.00× baseline throughput. Counter property setter calls: 0; derived memo computations: 0.
- **microtask+batch**: 53.70 ms median (middle 50%: 51.00–62.40 ms); 0.91× baseline throughput. Counter property setter calls: 0; derived memo computations: 0.

Adding the queue to core batching: 0.91× throughput versus sync+batch.

## layout-reading-setter

20 independently mounted rows × 10 writes per row per wave × 100 waves.

- **sync**: 272.40 ms median (middle 50%: 270.10–275.10 ms); 1.00× baseline throughput. Counter property setter calls: 20000; derived memo computations: 20000.
- **microtask**: 57.20 ms median (middle 50%: 55.40–60.20 ms); 4.76× baseline throughput. Counter property setter calls: 2000; derived memo computations: 20000.
- **sync+batch**: 27.60 ms median (middle 50%: 26.30–27.80 ms); 9.87× baseline throughput. Counter property setter calls: 2000; derived memo computations: 2000.
- **microtask+batch**: 35.30 ms median (middle 50%: 33.90–36.20 ms); 7.72× baseline throughput. Counter property setter calls: 2000; derived memo computations: 2000.

Adding the queue to core batching: 0.78× throughput versus sync+batch.

## Interpretation

The queue alone is not a general speedup in these flows. Across the two browser-process runs:

- The cheap 100-write counter burst was approximately tied to 6% faster with the queue. This is a small, variable effect despite 100× fewer counter setter calls.
- The multi-widget burst took 42–48% longer with the queue alone; single updates took 27–35% longer. Input echoes took 11–12% longer, with overlapping timing distributions.
- The deliberately layout-reading setters ran 4.5–4.8× faster with the queue alone. Core batching ran that same workload 9.8–9.9× faster than unbatched synchronous updates.
- Core batching alone ran counter bursts 11.0–11.5× faster and multi-widget bursts about 2.2× faster. Adding the queue on top of core batching increased elapsed time in every tested workload in both runs.

For these fixtures, prioritize grouping state writes with core batching and retain synchronous DOM updates as the default. Keep the queue opt-in for expensive setters or flows that cannot group their state writes. Its memo and queue overhead needs optimization before claiming broad performance gains.

Fewer DOM mutations do not imply less total work: the queue adds a memo, ownership handling and queue operations, while unbatched source writes still recompute the graph. Core batching removes those intermediate computations as well as intermediate DOM writes. Read the microtask versus sync comparison separately from microtask+batch versus sync: the latter combines two optimizations. Compare microtask+batch with sync+batch to isolate whether the queue adds value once state writes are already batched.

This is a local naive benchmark of the current PoC fixtures, not a general renderer or production-app performance claim. No production runtime was changed to obtain these results. Raw samples are in [results.json](results.json).

## Reproduce

From the repository root, with playground dependencies and Playwright Chromium installed:

```powershell
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run bench:dom
```

The command starts its own Vite server on port 5179 and closes that process and its browser on completion. It overwrites this report and results.json.
