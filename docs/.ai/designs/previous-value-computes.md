# Previous-value computes: design

Line references are to `src/Ranvier/Core.fs` and `src/Ranvier/Api.fs` at `9da5e1a`. Costs are read from the
code; §7 names the measurement that checks them.

## 1. Goal

A derived value's compute receives the value it last published, so a run can build on the previous one:

- **Accumulation.** Fold the new inputs into the previous result (a running total, a page appended to a list).
- **Reuse.** Return parts of the previous value unchanged for structural sharing, or the previous value itself so
  the equality cutoff stops propagation.

Ranvier is unreleased, so the existing signatures change. There is no parallel API.

## 2. Proposed API

```fsharp
val createMemo     : compute: ('T voption -> 'T) -> Memo<'T>
val createMemoWith : compute: ('T voption -> 'T) -> Memo<'T>

val createAsync     : compute: (Previous<'T> -> CancellationToken -> Task<'T>) -> AsyncMemo<'T>
val createAsyncWith : compute: (Previous<'T> -> CancellationToken -> Task<'T>) -> AsyncMemo<'T>
```

`ValueNone` means the node has never published a value. The same `'T voption` convention already feeds a
boundary's `fallback` and `recover` (`Core.fs:3661`).

`Previous<'T>` is a struct handle with one member, `Settled : Task<'T voption>`. A body reads it with
`let! prev = previous.Settled`, the same form on .NET and under Fable. §4 explains why the async parameter is a
handle and not a bare task.

Existing call sites become `fun _ -> ...`. `createOptionMemo` (`Api.fs:360`) drops its `ref` cell and reads the
parameter. Every derived value has one form.

`createEffect`, `createEffectOn` and boundary bodies keep their signatures. An effect's compute feeds `act` by
equality; a fold kept there is readable by nothing else, and belongs in a memo the effect reads.

## 3. `Memo`

### 3.1 Semantics

- **`prev` is the last settled value.** It equals `Peek`, the retained value.
- **One step per run.** A run is one recompute that sees changed inputs. Each write to an observed memo outside a
  batch triggers a flush, so the fold steps once per write. Writes inside a batch, or to an unobserved memo, collapse
  into one run that folds the final inputs once. A fold counts runs, not writes. Solid's collapse of same-tick
  writes gives the same behaviour.
- **A run that suspends or fails leaves `prev` unchanged.** The body re-runs from the top and publishes nothing, so
  writes made while a source is in flight are folded once, together, when the run completes:

  | Step | Event | `prev` in the run |
  | --- | --- | --- |
  | 1 | `c` runs, reads `s`, suspends on `a` | `v0` |
  | 2 | `s` written twice; each re-run suspends | `v0` |
  | 3 | `a` settles; `c` runs with the current `s` and `a` | `v0` |

  `prev` never holds a value computed from a combination of inputs the graph never published. This follows from
  re-running rather than resuming (`concepts/suspension.md`, *Re-running versus resuming*).
- **History belongs to the node.** A recreated memo starts from `ValueNone`: a projection row, or a memo created
  inside an owning body.

### 3.2 Cost

- **State.** `value` already holds the last settled value: `Run` overwrites it only on success and reads it as
  `previous` for the cutoff (`Core.fs:2250`). One bool marks "has published". The `Uninitialized` status bit alone
  cannot tell a never-run memo from one whose first run failed. The bool sits beside `disposed` and `violated` and is
  expected to fit in the object's existing padding.
- **Per run.** One struct argument, no allocation. `Graph.RunHosted` (`Core.fs:1729`) takes `unit -> 'T`; wrapping
  the compute as `fun () -> compute prev` would allocate a closure per run, so `RunHosted` gains an overload that
  takes the argument: `RunHosted(host, body: 'A -> 'T, arg: 'A)`.
- **Fable.** A generic `ValueSome x` compiles to a `some(x)` helper call per run.

## 4. `AsyncMemo`

### 4.1 Semantics

A flight's `prev` is the value published before the flight **starts**; its result is applied when it **settles**.

- **`CancelPrevious` and `KeepLatest`.** Only the newest flight publishes. Overlapping flights all launch against
  the same published value, and the superseded ones are discarded (`gen <> generation`, `Core.fs:3134`), so the
  history equals what the graph showed. `prev` is known at launch.
- **`Queue`.** Every result applies, in start order. For a fold to chain, flight *n*'s `prev` must be flight
  *n − 1*'s result, which does not exist when flight *n*'s body runs. The body cannot run later: dependencies are
  tracked only before its first `await` that suspends. `prev` is therefore a task, completed when flight *n − 1*'s
  result is applied.
- **A body that suspends before starting a flight** leaves `prev` unchanged, as on `Memo`.
- **A queued result applied while the node is suspended** (`Completed v when suspended`, `Core.fs:3145`) writes
  `value`, so it becomes the next `prev`.

Tracking still stops at the first `await` that suspends. Under `Queue`, awaiting `prev` before a tracked read loses
that read's edge. Under the default policies `prev` is already complete and the mistake stays hidden, so the
documentation states the rule: read every input, then await `prev`.

### 4.2 Why a handle, not a bare `Task<'T voption>`

A bare task parameter has to exist whether or not the body reads it:

| Case | Cost of a bare task |
| --- | --- |
| Default policy, before the first value | A cached `Task.FromResult ValueNone` per `'T` |
| Default policy, after a publish | One `Task<'T voption>` per publish, cached on the node |
| `Queue`, no earlier flight unapplied | The cached completed task |
| `Queue`, earlier flight unapplied | One `TaskCompletionSource` per overlapping flight |

`Previous<'T>` holds the node and the flight's generation, and creates the task on first access:

- **Default policies.** Returns the cached completed task.
- **`Queue`, earlier flight unapplied** (`queued > 0` at launch). Creates a `TaskCompletionSource`, stored in a
  waiter slot on the node. The next `applyResult` completes it with `value`.

An unread `Previous<'T>` costs a struct field in the flight closure `Launch` already allocates (`Core.fs:3289`),
one 8-byte waiter field on `AsyncMemo`, and one null check per `applyResult`. For comparison, every flight already
allocates the flight `Task`, a `CancellationTokenSource` under `CancelPrevious`, two closures, a continuation, a
`FlightOutcome` and a dispatch closure.

### 4.3 The `Queue` branch

The runtime cost is small. The branch adds correctness surface:

1. **Reentrancy.** `applyResult` runs on the graph thread. A `TaskCompletionSource` completed there on .NET runs the
   waiting body's continuation inline, inside the apply, so it is created with
   `TaskCreationOptions.RunContinuationsAsynchronously`. A Fable promise resumes asynchronously.
2. **A predecessor that faults or is dropped.** The waiter completes with the value after that apply (the last
   settled value), so the chain continues (`Core.fs:3145–3152`).
3. **Disposal.** Disposing the node completes the waiter, consistent with a pending node becoming Failed with
   `ObjectDisposedException` on disposal. A body awaiting `prev` then finishes and its result is dropped.
4. **Dispatch.** The waiter completes when `applyResult` runs, which under `ManualDispatcher` is `Graph.Pump`. A
   queued chain that reads `prev` advances once per pump.
5. **Fable.** `Platform` exposes `completedTask`, `whenSettled`, `outcomeOf`, `apply` and `after`; a deferred promise
   is a new helper. `DispatchApplied` (`Core.fs:1417`) shows the split: a `TaskCompletionSource` on .NET, a completed
   task under Fable.

## 5. Shared rules

- **`prev` after a failure.** The last settled value, matching `Peek` and a boundary's fallback. The alternative,
  `ValueNone`, would restart a fold after every failure.
- **Owning computes.** `createMemoWith` and `createAsyncWith` discharge the previous run's scope before the next run
  (`RecomputeScoped`, `Core.fs:2220`; `Start`, `Core.fs:3219`). Nodes created by the previous run are disposed when
  the body receives `prev`; `prev` is safe for its data only.
- **Threads.** `prev` is captured by value. A mutable object in `prev` that the body mutates off the graph thread is
  a hazard the thread guard does not detect, as with any other capture.

## 6. Tests

- **`Memo`.** First run receives `ValueNone`. A fold steps once per unbatched write and once per batch. A suspended
  run leaves `prev` unchanged. A failed run leaves `prev` at the last settled value. A failed first run passes
  `ValueNone` to the next run. Returning `prev` triggers the cutoff.
- **`AsyncMemo`, default policies.** `prev` is complete at launch. A superseded flight's result never appears as
  `prev`.
- **`AsyncMemo`, `Queue`.** Three or more overlapping flights chain in start order. A faulted predecessor and a
  dropped predecessor both resolve the waiter. Disposal while waiting completes the waiter. A body awaiting `prev`
  before a tracked read loses that edge (pins the documented rule). The waiter continuation runs outside
  `applyResult`.
- **Fable.** The same cases under the Fable test run.

## 7. Measurement

Construction (projection rows build memos in bulk), memo recompute, and flight throughput under each policy, before
and after, with the CPU op counters (`counters.ps1`).
