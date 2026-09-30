# Wave C: backlog

Items deferred from Wave B, each with where its context lives.

| Item | Context |
| --- | --- |
| Fable packaging: pack each library's `.fsproj` and `.fs` sources under `fable/` in the nupkg (Ranvier, Ranvier.CSharp, Ranvier.Elmish, and the `.Traced` variants), resolving the `RanvierTrace`/`RanvierCounters` conditional compile items, plus the `fable-javascript` package tag | `docs/content/fable/index.md` (states no Fable package is published) |
| Instruction-counter run for Wave B on Windows (`counters.ps1`, elevated), then fill the placeholders in `docs/content/benchmarks/*` and `docs/.ai/benchmarks/README.md` | `bench/Ranvier.Counters/Scenarios.fs` (Wave B scenarios) |
| Fan-out change 3 (skip `NotifyCheck` with no observers) | `docs/.ai/wave-b/leak-and-fanout.md` |
| Debounce and throttle | `docs/.ai/designs/debounce-throttle.md` |
| Delta readers stages 3 and 4 (value readers, `ApplyDelta`) | `docs/.ai/designs/projection-delta-reader-stage-2.md` §10 |
| Public API surface check for `Ranvier.Elmish` in `tools/verify-trace.fsx` | low priority |
| Good first issue: hand-written `ToString` on the public trace records and unions, so the traced build can be reflection-free | `docs/.ai/designs/aot-trim-analysis.md` |
