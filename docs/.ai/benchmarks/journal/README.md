# Input journal budget (#184, criterion 5)

Baseline: `aae54f2e`, the fork point of `issue/184`. Head: `issue/184` with the out-of-line signal hook
(`Signal.RecordWrite`). Both are untraced Release builds of `src/Ranvier` for `net10.0`, with
`Journal = None`. Machine: AMD64 Family 26 Model 68, 24 logical processors, Windows 10.0.26200,
.NET 10.0.13, a non-elevated shell, shared with up to two other lanes.

The baseline tree is a `git archive` export, not a worktree. `build.fsx -- perf build` and
`tools/verify-trace.fsx` add git worktrees, which this lane does not create, and the counter A/B
harness (`Ranvier.Counters ab`) reads Linux thread counters only. The measurements below replace
them with the same comparisons run by hand.

## IL, method by method

`il-diff.txt` lists every merge-base method whose untraced body changed, from the
`methodBodies` comparison of `tools/verify-trace.fsx` gate 1:

| Method | Change |
| --- | --- |
| `Signal.set_Value` | The guarded hook: `ldfld graph`, `ldfld journal`, `brfalse`, and a call to `RecordWrite` that runs only with a journal. Once per copy of the body (the fast path and the guarded path). |
| `AsyncSource.Settle` and `Fail` closures | The guarded hook, as in the setter. |
| `Graph.RunFlush` | The guarded `Boundary` call after a flush pass. |
| `Graph.RunUntrackedHosted`, `RunUntrackedHostedWith` | The guarded `EnterDerived` / `LeaveDerived` pair. |
| `Graph.EnterContinuation`, `LeaveContinuation` | The guarded `EnterDerived` / `LeaveDerived` pair for an async-memo continuation. |
| `Effect.RunBody`, `EffectOn.Act` | Added by the second review fix of PR #188: `ldfld graph`, `ldfld journal`, `brfalse` before the body runs; the work numbering and its closure run only with a journal. |
| `Graph.Post` | Added by the same fix: on the queued, off-thread path only, a `journal` null check before the inbox enqueue. |
| `Graph..ctor` | Resolving `options.Journal` into the `journal` field. |
| `GraphOptions` members | The record's fifth field, `Journal`. |
| `AsyncMemo.applyResult` | No instruction change: the `ldstr` source path and line literal of a raise site differ between the two trees. |

## Counters, every scenario

The counters below predate the second review fix. Its journal-off additions are the null checks in the table above and allocate nothing; the counters were not rerun for it.

`counters-base-aae54f2e.md` and `counters-head.md`: `Ranvier.Counters --no-pmc` with a
`RanvierCounters` counters worker, base then head, under `gate-lock.ps1 bench`. CPU load 63 % before,
68 % after. Allocations and library counters are deterministic.

Every Ranvier row is identical except `server-render`, which builds a graph per request: 200,560 B/op
at the baseline and 200,568 B/op at head, the same 103 objects/op. The 8 bytes are the `journal`
reference field on `Graph`. Instruction counts are not collected: processor counters need an
elevated shell.

An earlier run of `update`, `chain` and `cutoff`, base and head interleaved twice, gave identical
rows (CPU load 85 % before, 77 % after).

## Time, paired and interleaved

`timing-harness.fs` is compiled once against each `Ranvier.dll`. Each process reports the best of
five passes of 200,000 operations:

- signal write: a signal, a memo over it and an effect over the memo; one write per operation;
- memo chain: a signal and a chain of four memos; one write and a read of the last memo per
  operation.

`paired.ps1` runs the two processes alternately for 20 rounds, flipping the order each round, and
reports the geometric mean of the head/base ratios with a 95 % t interval.

| Run | CPU load | Signal write, head/base | Memo chain, head/base |
| --- | --- | --- | --- |
| A/B (`paired-timing.csv`) | 70 % before, 62 % after | 1.026 [0.954, 1.105] | 0.952 [0.892, 1.017] |
| A/A, the baseline against itself (`aa-timing.csv`) | 82 % before, 66 % after | 1.017 [0.946, 1.094] | 0.965 [0.884, 1.054] |

Both A/B intervals contain 1 and lie within the A/A interval of the same harness: no difference is
detected at this machine's noise. The intervals are about ±8 % wide, so a cost under that is not
ruled out.

An earlier A/B run, with the hook inline in the setter (`graph.RecordInput (JournalEntryKind.Write, id,
box v)` behind the null check), gave a signal write ratio of 1.046 [1.004, 1.090]. Moving the call into
the `NoInlining` `RecordWrite` leaves the field load and null check in the setter.
