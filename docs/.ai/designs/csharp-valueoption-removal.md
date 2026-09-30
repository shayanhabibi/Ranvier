# Removing `ValueOption` from the C# surface: design

**Status:** proposal, not implemented. Line references are to `c631f23`. The change is small; the maintainer's
review is needed for one choice, whether to keep the voption members or replace them (§6).

## 1. Goal

A C# caller of `Ranvier.CSharp`, and of the core types that package returns, will not need to name
`FSharpValueOption` or `FSharpOption`. `concepts/ecosystem.md:102-103` lists this as a gap, and research §12 says
the C# surface should be designed up front.

## 2. Inventory

The baseline (`docs/.ai/public-api-baseline.txt`) covers `Ranvier` only. The facade itself is already clean: it
hides voption behind `Reactive.Memo(compute, seed)` (`Reactive.fs:48-49`), and uses voption only internally
(`Bindings.fs:291, 413`). The traced `Tracing` class returns strings, `Nullable<int>` (`Tracing.fs:98-101`) and
`TraceEvent[]`, whose fields hold no options (`TraceEvents.fs:206-217`). The records that do hold options,
`TraceOrigin.Label: string option` (`TraceEvents.fs:227`), are never returned to C#.

The leaks, all in core, are the members below that C# reaches:

| # | Member | Where | How C# reaches it | Proposed replacement |
| --- | --- | --- | --- | --- |
| 1 | `Previous<T>.Settled : Task<T voption>` | `Core.fs:3203` | `Reactive.Async((previous, token) => …)` (`Reactive.fs:79`); `csharp.md` tells the reader to "test `IsSome`, then read `Value`"; `AsyncTests.cs:191-192` | Extensions `ValueTask<T> SettledOr(T seed)` and `ValueTask<(bool HasValue, T Value)> TrySettled()` in `Ranvier.CSharp` (§3) |
| 2 | `Graph.TryCurrent : Graph voption` | `Core.fs:1241-1244` | Called directly | `static bool TryGetCurrent(out Graph graph)`, .NET only, shaped like `Reading.TryGetValue` (`Types.fs:311-319`) |
| 3 | `Memo<T>(Graph, Func<T voption, T>)` and its `owning` overload | `Core.fs:2258, 2266` | `csharp.md` Limits points C# readers to `new Memo<int>(graph, previous => …)` | Overloads `(Graph, Func<T>)`, `(Graph, Func<T>, bool owning)` and `(Graph, Func<T, T> compute, T seed)` |
| 4 | `Boundary<T>.Suspense`, `Errors` and `Catching` with `Func<T voption, …>` | `Core.fs:4093-4107` | `csharp.md` sends readers who need the previous value here | Overloads that take `Func<T, T>` with a `T seed`; the same overloads on `Reactive.Suspense`, `ErrorBoundary` and `Boundary` |
| 5 | `GraphOptions.Dispatcher : IGraphDispatcher option`, and the record constructor | `Types.fs:263`; baseline:110 | Reading the options from C# | None. C# builds options with the `With…` methods (`Types.fs:281-297`) and reads the resolved `Graph.Dispatcher` (`Core.fs:1233`) |
| 6 | `Projection.TryGet`, `Lookup.TryGet : V option` | `Projections.fs:1032, 1889` | Called directly | Already in place: `TryGetValue` (`Projections.fs:1055, 1908`) |
| 7 | `AsyncMemo<T>(Graph, Func<Previous<T>, …>)` | baseline:40 | Constructor | Fixed by #1 |

Other F#-shaped members are visible to C# but are not options. The generated members of the `Reading<T>` union
(`Tag`, `NewReady`, `get_value`; baseline:198-215) and `FlightPolicy.IsQueue` are out of scope for this note.

## 3. `Settled` in detail

`Settled` is a task because under `Queue` the previous value can be unknown when the body runs
(`Core.fs:3186-3196`). That rules out two shapes:

- `bool TryGet(out T)` cannot be awaited.
- `HasValue` and `Value` properties would have to block or throw under `Queue`.

The replacement therefore returns an awaitable:

```csharp
public static ValueTask<T> SettledOr<T>(this Previous<T> previous, T seed);
public static ValueTask<(bool HasValue, T Value)> TrySettled<T>(this Previous<T> previous);

// AsyncTests.cs:191-192 becomes
return await previous.SettledOr(0) + by;
```

- Under `CancelPrevious` and `KeepLatest`, `Settled` is already complete when the body runs
  (`Core.fs:3186-3187`). The extension then wraps the result in a `ValueTask` and allocates nothing.
- Under `Queue`, when a predecessor's result is still unapplied, the extension allocates one continuation task, in
  addition to the `TaskCompletionSource` that core already allocates.
- On .NET, awaiting a completed `ValueTask` continues synchronously, so dependency tracking is the same as awaiting
  `Settled`. The rule "read every input, then await" stays as it is.

## 4. Cost model

- **Write, recompute and flush:** unchanged. Every replacement is a new overload or an extension member.
- **`SettledOr`:** measured with `dotnet fsi --optimize+` on net10.0, reading a completed `Task<int voption>` that
  holds a result outside the runtime's small-int task cache:

  | Wrapper | B/op |
  | --- | --- |
  | F# `task { }` returning `Task<int>` | 72 |
  | `ValueTask<int>` returned from the completed task | 0 |

  This measurement is why the extensions return `ValueTask`.
- **Memo constructors (`Func<T>` and seed forms):** one adapter closure at construction and one extra delegate
  call per run. `Reactive.Memo` pays the same today (`Reactive.fs:43, 49`).
- **`Graph.TryGetCurrent`:** the same work as `TryCurrent`, with no allocation.
- **Code that does not use the new members:** pays nothing.
- **Benchmark that would settle it:** `ConstructionBenchmarks.CreateAndDisposeMemo` next to a new
  `CreateAndDisposeMemoFunc` case. The two are expected to match the facade's cost.

## 5. Fable, AOT and trimming

- The extensions live in `Ranvier.CSharp`, which is .NET only.
- The core overloads sit under `#if !FABLE_COMPILER`, as the existing `Memo` constructors do
  (`Core.fs:2254-2270`), as does `Reading.TryGetValue` (`Types.fs:311`). `out` parameters have no meaning under
  Fable.
- `ValueTask` and `ValueTuple` ship in netstandard2.1. The design uses no reflection.

## 6. Breaking-change impact

- **Option A, additive (recommended).** Keep every voption member and add the replacements. Nothing breaks. The
  baseline gains `Graph.TryGetCurrent`, three `Memo` constructors and three `Boundary` statics. C# IntelliSense
  still lists `Settled` and `TryCurrent`, and the guide stops using them.
- **Option B, replace.** Remove the voption forms that C# sees. This breaks F# callers:
  - `Graph.TryCurrent` has 9 uses in `src` and `tests`.
  - F# docs call `Boundary<T>.Suspense` directly (`guide/troubleshooting.md:476`).
  - `Bindings.fs:413` uses the voption `Memo` constructor.

  `Settled` must stay under either option, because it is the F# API.

The documentation changes are the same under both options:

- `csharp.md`: the `Settled` paragraph, the boundary note (lines 83-84) and Limits (lines 286-287).
- `concepts/ecosystem.md:102-103`: drop the gap.

## 7. Alternatives

- **Change `Settled` to `Task<(bool, T)>` in core.** F# would lose its voption idiom, and every F# caller would
  break.
- **A `Reactive.Async(Func<T, CancellationToken, Task<T>>, T seed)` overload that awaits `Settled` before calling
  the body.** Under `Queue` the await suspends before the body reads its inputs, which leaves those reads untracked.
- **A Ranvier-owned `Optional<T>` struct.** It adds a new type, while `ValueTask` and `ValueTuple` already cover the
  C# use.

## 8. Recommendation

**Do**, as option A. The work is additive and small, and it closes the last voption gap listed in the ecosystem
page.

## 9. Questions for the maintainer

1. Keep the voption overloads beside the new ones? (keep/replace)
2. Use a seed (`Func<T, T>` plus `T seed`) for previous values in `Memo` and `Boundary`, matching
   `Reactive.Memo(compute, seed)`? (yes/no)
3. Should `SettledOr` and `TrySettled` live in `Ranvier.CSharp` rather than core? (yes/no)


## Reviewer corrections (not yet applied)

Verdict: needs fixes

- Severity: moderate. §6 'Option A, additive (recommended). ... Nothing breaks.' Adding `Memo<T>(Graph, Func<T>)` beside `Memo<T>(Graph, Func<T voption, T>)` makes an F# call with a `fun _ -> ...` lambda ambiguous. Reproduced in dotnet fsi: FS0041 'A unique overload for method could not be determined', with both constructors as candidates. In C#, `(Graph, Func<T,T>, T seed)` beside `(Graph, Func<T voption,T>, bool owning)` is ambiguous for `Memo<bool>` when the lambda ignores its parameter (e.g. `new Memo<bool>(graph, _ => true, false)`, CS0121). No call in the repo hits this (Bindings.fs:413 uses an explicit Func), but user code can. Correct: 'source-compatible for existing repo code; F# callers passing `fun _ -> …` to the Memo constructor and C# `Memo<bool>` seed calls become ambiguous'.
- Row 5 '`GraphOptions.Dispatcher : IGraphDispatcher option` ... `Types.fs:263`' → Types.fs:267 (263 is the start of its doc comment).
- §6 '`Graph.TryCurrent` has 9 uses in `src` and `tests`': there are 8 call sites (TraceApi.fs:40, Bindings.fs:291, tests/Api.fs:81, 85, 94, 96, tests/Threading.fs:73, 579) plus the definition at Core.fs:1241.
- §5 'The core overloads sit under `#if !FABLE_COMPILER`, as the existing Memo constructors do': that fits the Memo constructors and `TryGetCurrent` (out parameter). The existing `Boundary.Suspense/Errors/Catching` statics (Core.fs:4093-4107) are not under any `#if`, and seed overloads take no out parameter, so the new Boundary overloads need not be .NET-only.
