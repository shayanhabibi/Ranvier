# Reactive commands for C#: design

**Status:** implemented on `worktree-wf_c46b5816-3af-4`, pending benchmark gate. Line references are to `c631f23`.
Decisions taken (`docs/.ai/wave-b/decisions.md`): default policy `Disable`; public `Graph.TrackStatus(INode)` in core;
`Queue` and `Parallel` not in the first cut. §11 records where the implementation departs from this note.

## 1. Goal

This note specifies a C# command whose `CanExecute` is derived, whose busy state is observable, and whose async
body declares what happens when executions overlap. It needs no hand-kept `IsBusy` and no `NotifyCanExecuteChanged`
calls. It covers:

- CommunityToolkit#959: `CanExecute` updates when the state it reads changes.
- CommunityToolkit#826: a command is disabled while any other command runs.
- CommunityToolkit#1168: a notification when `IsRunning` changes.
- CommunityToolkit#536 and Avalonia#20729: `CanExecuteChanged` is raised on the UI thread even when the state
  changed on another thread.

Ranvier has no command type today (research §14; `concepts/ecosystem.md:100-101`).

## 2. Proposed API (Ranvier.CSharp)

```csharp
public enum CommandPolicy { Disable, CancelPrevious }   // Queue and Parallel: later, on request

public sealed class ReactiveCommand : ICommand, INotifyPropertyChanged, IDisposable
{
    public bool CanRun { get; }         // tracked read on the graph's thread
    public bool IsRunning { get; }      // tracked read on the graph's thread
    public Exception? Error { get; }    // the last execution's failure; null after a success
    public Memo<bool> Enabled { get; }  // the derived predicate
    public Task ExecuteAsync(object? parameter = null);
    public void Cancel();
    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;  // "CanRun", "IsRunning", "Error"
}

// ReactiveBindings: shares the bindings' notify effect and owner
public ReactiveCommand Command(Func<object?, CancellationToken, Task> execute,
    Func<bool>? canExecute = null, CommandPolicy policy = CommandPolicy.Disable);
public ReactiveCommand Command(Action<object?> execute, Func<bool>? canExecute = null);

// Reactive: standalone, on Graph.Current, with its own effect
public static ReactiveCommand Command(Func<object?, CancellationToken, Task> execute,
    Func<bool>? canExecute = null, CommandPolicy policy = CommandPolicy.Disable);
public static Memo<bool> AnyPending(params INode[] sources);
```

The predicate of `save` reads `load`, which is assigned after `save`. The first evaluation of a predicate is lazy
(§11), so the sample works in this order.

```csharp
save = bindings.Command((_, t) => repo.SaveAsync(draft.Value, t), () => IsValid && !load.IsRunning);
load = bindings.Command((_, t) => repo.LoadAsync(t), () => !save.IsRunning);          // #826
busy = bindings.Computed("IsBusy", () => save.IsRunning || load.IsRunning);
quoting = AnyPending(quote, stock, shipping);
```

## 3. How it works

- **State.** A command holds three nodes:
  - `running`, a `Signal<int>` that counts executions in flight.
  - `error`, a `Signal<Exception>`.
  - `Enabled`, a memo computing `(canExecute?.Invoke() ?? true) && (policy != Disable || running.Value == 0)`.
  `Enabled` becomes pending or failed when `canExecute` reads a pending or failed source, and the command counts
  that as disabled.
- **Notification.** A hosted command is refreshed in the bindings' notify effect (`Bindings.fs:319-347`) after
  the slots. The refresh reads `Enabled.TryValue`, `running` and `error`, compares them with the last-notified
  state, and raises through `ContextHandlers` (`Bindings.fs:16-64`). Each handler runs on the
  `SynchronizationContext` that was current when it subscribed, which covers #536 and Avalonia#20729. A
  standalone command owns one `Effect` that does the same work.
- **`ICommand.CanExecute(object)`** returns the last-notified bool as a volatile field read, the same way
  `BoundValue.IsLoading` does (`Bindings.fs:146`). A UI thread can query it at any time without touching the
  graph.
- **`CanRun`, `IsRunning` and `Error`** are tracked reads of the nodes on the graph's thread, so
  `() => !save.IsRunning` composes. On any other thread they return the last-notified value, as
  `BoundValue.Value` does (`Bindings.fs:137-143`). `IsRunning` is backed by a signal; the class exposes no
  setter for it.
- **`ExecuteAsync(p)`** marshals to the graph's thread through `Graph.Dispatch` (`Core.fs:1441`). On that thread
  it re-reads `Enabled` untracked. When the command is disabled, it returns a completed task. Otherwise it applies
  the policy:
  - **`Disable`:** increments `running`. `Enabled` goes false, and the write outside a batch flushes before the
    body starts, so the button is disabled before a second click can arrive (tested by
    `ADisableCommandIsDisabledBeforeItsBodyStarts` in `tests/Ranvier.CSharp.Tests/CommandTests.cs`).
  - **`CancelPrevious`:** cancels the previous execution's token and starts a new execution.
  - **`Queue`** (not shipped): chains the new execution after the previous execution's task.
  - **`Parallel`** (not shipped): applies no coordination.

  The body runs untracked (`Graph.Untrack`, `Core.fs:1655`). On completion, one batch decrements `running` and
  writes `error`, through `Dispatch` when the completion arrives on another thread. An
  `OperationCanceledException` on the command's own token is recorded as no error.
- **`ICommand.Execute(p)`** discards the task from `ExecuteAsync`. A failure reaches only `Error`, never the
  `SynchronizationContext`: there is no `async void` crash (CommunityToolkit#1205, #714). A caller that awaits
  `ExecuteAsync` sees the fault.
- **`Dispose`** cancels the executions in flight, disposes the nodes and drops the handlers, as
  `ReactiveBindings.Dispose` does (`Bindings.fs:493-496`).
- **`AnyPending`** is a memo that is true while any source is pending. C# can already write it for typed nodes:
  `Memo(() => a.TryValue.IsPending || b.TryValue.IsPending)` works, because `TryValue` is a tracked read that does
  not throw (`Core.fs:2608-2614`). A heterogeneous `INode[]` needs a non-generic tracked read. `ISource` is
  internal (`Core.fs:42`), and `Ranvier.CSharp` has no `InternalsVisibleTo` (`Types.fs:333-336`), so the design
  adds `Graph.TrackStatus(node: INode) : Status` to core:
  - For a source: `UpdateIfNecessary` (`Core.fs:58`), then track the source, then return `Status`.
  - For any other node: return `Status` untracked.

## 4. Policy vocabulary

The command does not reuse `FlightPolicy` (`Types.fs:34-50`), for three reasons:

- `FlightPolicy` is graph-wide: `AsyncMemo` reads it from `graph.Options` (`Core.fs:3344, 3385, 3470, 3498, 3557`).
- `KeepLatest` discards a result, and a command has no result.
- The CommunityToolkit default, `AllowConcurrentExecutions = false`, has no counterpart in `FlightPolicy`.

R3's `AwaitOperation` maps as follows: `Sequential` is `Queue`, `Switch` is `CancelPrevious`, `Parallel` is
`Parallel`, and `Drop` is `Disable` with `CanExecute` false while an execution runs.

## 5. Cost model

The numbers below were measured with an FSI script (`dotnet fsi --optimize+`) against a net10.0 Release build of
`c631f23`. The script counted bytes with `GC.GetAllocatedBytesForCurrentThread` over 10^4 to 10^5 iterations after
10^3 warm-up iterations. They are allocations only; nothing here measures time.

| Case | B/op |
| --- | --- |
| `Root` create and dispose (baseline) | 280 |
| The same root holding `Signal<int>`, a `Memo<bool>` reading it and an `Effect` reading the memo, one flush | 1,336 |
| Two writes to that signal, each flushing the memo and the effect | 192 |

The same script checked `AnyPending` over 8 `AsyncSource<int>` read through `TryValue`: it was true before the
sources settled and false after. Each write triggered its own effect run (202,000 raises for 100,000 op pairs).

- **Construction.** A standalone command costs about 1.05 KB above the root baseline, delegates included. The
  hosted form has no effect of its own. The `error` signal adds one more signal, the amount measured by
  `ConstructionBenchmarks.CreateSignal`.
- **Per execution.** Two signal writes and two notify passes, about 96 B each in the standalone shape, plus:
  - one `CancellationTokenSource`
  - the user's `Task` and its continuation
  - a dispatch closure when the task completes off the graph's thread (`Core.fs:1443-1453`)

  A hosted pass refreshes every slot of its bindings, which is O(slots), as it is today.
- **`CanExecute(object)`.** One volatile read and no allocation.
- **Core hot paths.** Write, recompute and flush are unchanged. `Graph.TrackStatus` runs only when `AnyPending`
  calls it.
- **Code that does not use commands.** Code without commands pays one empty-array length check per notify pass of a
  `ReactiveBindings`; nothing elsewhere.
- **Benchmarks that would settle it.** A new `CommandBenchmarks` class in `bench/Ranvier.Benchmarks` needs a
  `ProjectReference` to `Ranvier.CSharp`; today the project references only `Ranvier`
  (`Ranvier.Benchmarks.fsproj:36`). It would contain:
  - `CreateAndDispose`, standalone against hosted
  - `ExecuteCompleted`, with a body returning `Task.CompletedTask`
  - `ExecuteWithSlots` at 1, 10 and 100 slots

## 6. Fable, AOT and trimming

- `Ranvier.CSharp` targets only net10.0, net8.0 and netstandard2.1 (`Ranvier.CSharp.fsproj:4`). `ICommand`,
  `INotifyPropertyChanged` and `SynchronizationContext` are .NET types, so nothing here reaches Fable. An F# command
  would be a separate design; its signal and memo core would port.
- `Graph.TrackStatus` uses only `ISource` and `Status`. The "for a source" branch is an interface type test, which
  Fable compiles to false, so it needs the same workaround as `Tracer.IdOf` in `Trace.fs`: under `#if FABLE_COMPILER`
  it probes for the `UpdateIfNecessary` member with `Platform.hasMember`. The three `TrackStatus` tests in
  `tests/Ranvier.Tests/Reads.fs` pass under Fable, traced and untraced.
- The design uses no reflection and no dynamic code. `ICommand` ships in netstandard2.1. Neither project sets
  `IsAotCompatible` today, and nothing in this design prevents setting it.

## 7. Breaking?

No. `Ranvier.CSharp` has no entries in `docs/.ai/public-api-baseline.txt`, so the command type touches no baseline.
`Graph.TrackStatus` adds one line to the baseline. That update ships with the member, because
`tools/verify-trace.fsx` compares the packed surface with the baseline.

## 8. Alternatives

- **Implement CommunityToolkit's `IAsyncRelayCommand`.** This adds a package dependency, and it keeps the manual
  `NotifyCanExecuteChanged` model that this design replaces.
- **Use an `AsyncMemo` as the engine.** A flight starts when an input it read changes (`Reactive.fs:69-74`), but an
  execution starts when a user invokes it. Driving a memo from a trigger signal would track the body's reads and
  re-execute the body when those inputs change.
- **Expose `IsRunning` as a public `Signal<bool>`.** Callers could then write it. A property that performs a
  tracked read composes the same way and exposes no setter.
- **Standalone commands only.** This is simpler, but every command costs an effect.
- **A `CanExecute` that depends on the parameter.** A memo takes no parameter. A `Lookup` keyed by parameter
  (`Reactive.fs:177-180`) could provide one later.

## 9. Recommendation

**Do.** Ship the hosted and standalone forms with `Disable` and `CancelPrevious`, together with `AnyPending` and
`Graph.TrackStatus`. Add `Queue` and `Parallel` when a user asks for them.

## 10. Questions for the maintainer

1. Should the default policy be `Disable` or `CancelPrevious`? **Decided: `Disable`.**
2. Should core gain a public `Graph.TrackStatus(INode)` so that `AnyPending` accepts an `INode[]`? (yes/no) **Decided: yes.**
3. Should `Queue` and `Parallel` ship in the first cut? (yes/no) **Decided: no.**


## 11. As implemented

Deviations from the sections above, and choices the note left open:

- **Lazy first evaluation.** A command holds a fourth node, `armed`, a `Signal<bool>`. The notify pass reads nothing
  else of a command until `armed` is true. The first `CanExecute` call, `CanExecuteChanged` or `PropertyChanged`
  subscription, or execution arms the command. This is the fix for the reviewer's sample hazard: the predicate is
  first evaluated after the view model's constructor has assigned every command. `CanExecute` reports `false` before
  arming, and the first arm raises `CanExecuteChanged` when the predicate is true. Tagged `FOR-REVIEW` in
  `Bindings.fs` for the extra signal per command.
- **Only the latest execution writes `Error`.** Under `CancelPrevious`, a superseded execution that completes after
  the newer one leaves `Error` alone, so a late cancellation cannot clear the newer execution's failure.
- **The task from `ExecuteAsync`** completes after `IsRunning` and `Error` are updated, not with the body's task
  itself. It is faulted or cancelled as the body's task was, and completes at once when the command is disabled or
  disposed. `ICommand.Execute` allocates no completion source and so leaves nothing unobserved.
- **A public `Execute(object)`** beside the explicit `ICommand.Execute`, for C# callers holding a `ReactiveCommand`.
- **The synchronous `Action<object>` overload** runs the increment, the body and the completion in one batch: a
  synchronous execution raises only its outcome, not a disabled/enabled pair. `Reactive.Command` has the overload too.
- **`Reactive.fs` compiles after `Bindings.fs`**, so `Reactive.Command` can return the command type.
- **Token callbacks that throw** during `Cancel`, `Dispose` or a `CancelPrevious` launch are dropped (tagged
  `FOR-REVIEW`).
- **Per-execution allocation** is higher than §5 estimates. A `--short` sanity run of the new
  `bench/Ranvier.Benchmarks/Commands.fs` (not a gate) measured about 1.1 KB per `ExecuteAsync` for a standalone
  command and 1.6 KB for a hosted one with no slots: the token source, the completion source, the dispatch and batch
  closures, and the notification lists of two notify passes. Create and dispose measured about 2 KB for either form.
  The quiet-machine phase should settle both.
- **Benchmarks.** `CommandLifecycleBenchmarks.CreateAndDispose` and `CommandExecuteBenchmarks.ExecuteCompleted`
  (each standalone and hosted) and `CommandSlotsBenchmarks.ExecuteWithSlots` (1, 10, 100 slots); the benchmark project
  now references `Ranvier.CSharp`.
- **Public API baseline.** `docs/.ai/public-api-baseline.txt` is not updated on this branch (§7): it is regenerated
  from master after the merge, and will gain the `Graph.TrackStatus` line then.
- **Docs.** `docs/content/guide/csharp.md#commands`; the ecosystem gap, the roadmap entry and the
  `AsyncRelayCommand` paragraph of `async-and-pending.md` now point to it.

## Reviewer corrections (applied)

Verdict: needs fixes

- Severity: moderate. §6 '`Graph.TrackStatus` uses only `ISource` and `Status`, and compiles under Fable without `#if`': the 'for a source' branch needs `node :? ISource`, a runtime type test against an interface. Fable does not support that test; the code already works around it with `Platform.hasMember` under `#if FABLE_COMPILER` (Trace.fs Tracer.Bind), and design projection-delta-reader.md §8 names `Awaited.source` as the workaround. Correct: 'needs the same Fable workaround (a member probe or a concrete-type test) under #if'.
- §3 'so the button is disabled before a second click can arrive (measured in §5)': §5 measures allocations only ('nothing here measures time') and does not test ordering. Drop '(measured in §5)' or add the ordering test.
- §4 'AsyncMemo reads it from graph.Options (Core.fs:3344, 3385, 3470, 3557)': it is also read in `fail` at Core.fs:3498.
- §3 '`UpdateIfNecessary` (`Core.fs:59`)' → Core.fs:58.
- §5 '**Code that does not use commands.** It pays nothing. A `ReactiveBindings` without commands adds one empty-array length check to each notify pass.' contradicts itself. Say 'Code without commands pays one empty-array length check per notify pass of a ReactiveBindings; nothing elsewhere'.
- Sample hazard (depends on the implementation): `save`'s canExecute reads `load`, which is assigned after `save`. If the command computes its initial CanExecute eagerly, as `BoundValue` does at construction (Bindings.fs:121), `save.Enabled` fails with a null reference and tracks only `IsValid` until that changes. State that the first evaluation is lazy, or reorder the sample.
