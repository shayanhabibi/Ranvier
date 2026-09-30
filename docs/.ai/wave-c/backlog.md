# Wave C: backlog

Items deferred from Wave B, each with where its context lives.

| Item | Context |
| --- | --- |
| **First.** Take the `Serialised` entry handling off the `Guarded`/`Unchecked` write path: the counter `cutoff` scenario went from 36 to 52 instructions per equal write (+16), with `chain` +4.1% and `shape-diamond` +3.8%. Choose the entry behaviour once per graph instead of branching per call | `docs/.ai/benchmarks/counters/b693a42.md` vs `e13f159.md`; `FOR-REVIEW` tags in `src/Ranvier/Core.fs` (Serialised stale-read and entry bracket) |
| **First.** Failure provenance cost: `async-recover` went from 185,097 to 209,813 instructions per op (+13.4%) and 131 KB to 180 KB per op (+37%). Capture the stack lazily on the first `.Value` again, as before Wave B, and check the `Failure` record allocation | same reports; `docs/.ai/designs/failure-provenance.md` |
| Fable packaging: pack each library's `.fsproj` and `.fs` sources under `fable/` in the nupkg (Ranvier, Ranvier.CSharp, Ranvier.Elmish, and the `.Traced` variants), resolving the `RanvierTrace`/`RanvierCounters` conditional compile items, plus the `fable-javascript` package tag | `docs/content/fable/index.md` (states no Fable package is published) |
| Instruction-counter run for Wave B on Windows (`counters.ps1`, elevated), then fill the placeholders in `docs/content/benchmarks/*` and `docs/.ai/benchmarks/README.md` | `bench/Ranvier.Counters/Scenarios.fs` (Wave B scenarios) |
| Fan-out change 3 (skip `NotifyCheck` with no observers) | `docs/.ai/wave-b/leak-and-fanout.md` |
| Debounce and throttle | `docs/.ai/designs/debounce-throttle.md` |
| Delta readers stages 3 and 4 (value readers, `ApplyDelta`) | `docs/.ai/designs/projection-delta-reader-stage-2.md` §10 |
| Public API surface check for `Ranvier.Elmish` in `tools/verify-trace.fsx` | low priority |
| Good first issue: hand-written `ToString` on the public trace records and unions, so the traced build can be reflection-free | `docs/.ai/designs/aot-trim-analysis.md` |
