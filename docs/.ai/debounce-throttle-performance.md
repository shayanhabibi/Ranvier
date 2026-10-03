# Debounce and throttle performance evidence

Measured on 2026-10-03 on Windows 11, AMD Ryzen 9 9900X (12 cores, 24 logical processors), .NET SDK 10.0.401 and .NET runtime 10.0.12. These are implementation measurements, not a claim that one policy is universally fastest.

## Selected policy

Debounce uses lazy deadline extension with one reusable timer per node, created only when needed. Changing the candidate extends a monotonic deadline without rearming an already armed timer. An early wake checks that deadline on the graph owner and rearms for the remaining time. Leading throttle uses a timestamp and creates no timer; trailing modes use a fixed window. Timer callbacks carry no candidate and coalesce owner inbox posts.

This choice reduces backend timer changes during a burst, at the cost of intermediate callbacks and owner wakes. A deterministic JavaScript policy harness (`fable/Ranvier.Counters/TimedPolicy.mjs`) compares the same admissions with 100 ms windows. For inputs spaced 1 ms apart, 64 inputs required 64 arms and one callback with per-input rearm, versus two arms and two callbacks with lazy extension. At 4,096 inputs those counts were 4,096/1 versus 43/43. Sparse inputs spaced 150 ms apart used one arm and callback per input with either policy. Assertions check identical winning values and admissions.

The harness is a counting timer simulation, not a native `setTimeout` latency measurement. Its throughput varied with burst size and sparsity; lazy extension was slower for the largest sparse case. The .NET policy benchmark likewise uses a counting backend. Its six Short-run cases had overlapping confidence intervals: dense 4,096-input bursts averaged 10.92 µs for rearm and 10.55 µs for lazy extension, with 464 bytes per constructed burst in either case. Backend operation counts and semantic tests support retaining lazy extension; the simulated timings do not establish a native timer performance advantage.

## Actual graph measurements

`TimedBenchmarks` ran 24 BenchmarkDotNet Short cases: changed capture and capture plus admission, all four modes, at 1, 64 and 4,096 nodes. Three warmups and three measurement iterations make these smoke measurements; use a longer job for release comparisons.

- Changed capture allocated zero bytes in every case. One-node means were 21.12–24.05 ns; 64-node means were 1.03–1.13 µs; 4,096-node means were 72.40–80.40 µs.
- Debounce capture plus admission averaged 53.88 ns for one node, 2.72 µs for 64, and 196.77 µs for 4,096. Trailing throttle averaged 53.53 ns, 2.71 µs, and 187.07 µs respectively.
- Timer-backed admissions allocated 24 bytes per node in this synchronous counting-clock workload. Capture itself remains allocation-free after warmup. This is not a zero-allocation expiry claim.
- Leading throttle capture plus admission allocated zero bytes. The `both` benchmark spaces each changed value by a full window, exercising leading admissions; it does not measure trailing admissions for that mode. The separate continuous-input counter scenario does exercise its trailing path and records 24 bytes per timer-backed admission.
- The benchmarks use primitive integers and shared preallocated source delegates. Construction, user delegate allocation, OS callbacks, scheduler latency and contention are outside these measured cases.

Raw Short-run results and uncertainty are retained under `docs/.ai/benchmarks/results/Ranvier.Benchmarks.Timed.*`. `EqualWrite` and `Construct` benchmark entry points are provided for further measurement; their results are not included in the 24-case run.

These Short runs preceded the final cleanup moving two trace-only predicates into conditional hooks. Final allocation regression tests pass and existing-method/hook-erasure IL gates pass on the resulting tree; the listed timing means are measurements of that earlier feature tree, not a new timing run after the cleanup.

## Existing-path checks

Allocation and graph counters were run three times with `counters.ps1 -NoPmc` at the clean merge-base `506c8d2` and at the feature tree. All 40 existing .NET scenario rows had exactly unchanged allocated bytes, object counts and graph counters. New 64-node capture scenarios allocated zero bytes in all four modes. Timer-backed admissions allocated 1,536 bytes, or 24 bytes per node; leading admissions allocated zero bytes. JavaScript heap deltas are noisy and are not evidence of exact allocation counts.

The trace verifier separately compares existing untraced method bodies against the merge-base and compares the feature assembly against a same-feature build with timed trace hooks removed. These checks protect existing paths and trace erasure independently of benchmark timing. Packed API verification permits only the explicit 52-member addition manifest, with no existing signature removals.

## Outstanding performance acceptance

The required retired-instruction measurement is blocked on this host: ETW/PMC collection requires an elevated administrator process. Unrestricted filesystem permissions do not provide Windows elevation. `-NoPmc` does not satisfy this gate. Run the full verifier and `counters.ps1 -Repeat 5` from an elevated suitable host before declaring performance acceptance.

Native timer rearm versus lazy-extension throughput, separate callback/owner-dispatch costs, contention and real deadline latency distributions remain unmeasured. The simulation results must not be presented as those measurements. No timing wheel or shared timer scheduler is justified by the evidence currently collected.
