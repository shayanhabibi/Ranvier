# INotifyPropertyChanged adapter: design

Closes the gap named in `RESEARCH-ecosystem-pain-points.md` §0 row 2, §2, §5, §6 and §14: Ranvier's derived values,
pending channel and owners are invisible to XAML without an `INotifyPropertyChanged` bridge. Lives in
`src/Ranvier.CSharp/Bindings.fs` and uses the public API of `Ranvier` only.

## Goals

- Derived view-model properties with no dependency declarations: a property backed by a tracked `Func<T>` raises
  `PropertyChanged` when, and only when, its settled value changes (CommunityToolkit#857, ReactiveUI `WhenAnyValue`).
- Two-way properties backed by a `Signal<T>`.
- Loading and error state without a hand-written `IsBusy`: per property and aggregated over the view model, with
  errors through `INotifyDataErrorInfo`.
- Gradual adoption: works inside an existing view model (a CommunityToolkit `ObservableObject` subclass, or any class
  with its own `OnPropertyChanged`) without changing its base class.
- Each handler runs on the `SynchronizationContext` it subscribed from (CommunityToolkit#536, microsoft-ui-xaml#2795).
- Deterministic lifetime: every node the adapter creates belongs to an owner and ends with it.
- AOT and trim safe: no reflection, no expression trees. Names are explicit (`nameof`).

## API

```csharp
public sealed class ReactiveBindings : INotifyPropertyChanged, INotifyDataErrorInfo, IDisposable
{
    public ReactiveBindings();                          // Graph.Current; this object is the event sender
    public ReactiveBindings(object sender);             // Graph.Current
    public ReactiveBindings(object sender, Graph graph);

    public BoundValue<T> Computed<T>(string name, Func<T> compute);
    public BoundValue<T> Computed<T>(string name, Func<T> compute, string loadingName);
    public BoundSignal<T> Writable<T>(string name, T initial);
    public BoundSignal<T> Writable<T>(string name, Signal<T> signal);
    public T Run<T>(Func<T> body);                      // runs body with the graph active and Owner as scope
    public void Run(Action body);

    public bool IsLoading { get; }                      // raises PropertyChanged("IsLoading")
    public bool HasErrors { get; }                      // raises PropertyChanged("HasErrors")
    public IEnumerable GetErrors(string? propertyName); // error messages
    public Graph Graph { get; }
    public Owner Owner { get; }
    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;
    public void Dispose();
}

public sealed class BoundValue<T>  { string Name; T Value; bool IsLoading; Exception? Error; Memo<T> Memo; }  // read-only members
public sealed class BoundSignal<T> { string Name; T Value { get; set; } Signal<T> Signal; }

public abstract class ReactiveObject : INotifyPropertyChanged, INotifyDataErrorInfo, IDisposable
{
    ReactiveObject();  ReactiveObject(Graph graph);     // abstract, so reachable from a subclass only
    public ReactiveBindings Bindings { get; }
    public bool IsLoading { get; }  public bool HasErrors { get; }
}
```

Two adoption paths:

- **Existing view model.** Create `new ReactiveBindings(this)` and forward: `bindings.PropertyChanged += (_, e) =>
  OnPropertyChanged(e)`. The class keeps its base and its hand-written properties; migrated properties read
  `total.Value`.
- **Event owner.** A class implementing the interfaces delegates its `add`/`remove` accessors to the bindings, or
  derives from `ReactiveObject`, which does exactly that.

## Semantics

- One effect per `ReactiveBindings` reads every bound node with `TryValue` (tracked, never raises), updates every
  snapshot, then raises the batch of events untracked. All snapshots are current before the first handler runs, so a
  handler reading a sibling property sees the same pass. A diamond raises each property once.
- Cutoff is the graph's equality policy (`Options.Equality`), the policy the nodes use.
- `Value` while pending or failed is the last settled value, `default` before the first. `IsLoading` is true while
  the reading is `Pending`; `Error` is the exception of a `Failed` reading and null otherwise. A transition into or
  out of `Failed`, or a new exception, raises `ErrorsChanged`. A loading or error transition that leaves the value
  equal raises no `PropertyChanged` for the value.
- `IsLoading` and `HasErrors` on the bindings aggregate every property: the boundary idea, one loading and error
  state over several sources.
- `Value` on the graph's thread is a tracked read of the node (`TryValue`; the snapshot while not `Ready`), so
  a `Computed` body may read other bound properties through the view model's getters and track them. Off the graph's
  thread it is the snapshot, which is safe to read from any thread.
  Through `Value` a body sees the last settled value of a loading or failed property; through `Memo.Value` the
  pending or failed reading propagates. Detecting "inside a body" to choose automatically needs API the core does
  not expose.
- `BoundSignal.Value = v` goes through `Graph.Dispatch`: inline on the graph's thread, marshalled otherwise.
- Adding a property reads its initial state untracked and raises nothing for it.

## Threading

- The effect runs where every effect runs: in the graph's flush, on the graph's thread. Async settles reach it
  through the graph's dispatcher (`SynchronizationContextDispatcher` on a UI thread, `ManualDispatcher` otherwise).
- Only the event raise is marshalled. Each subscription stores `SynchronizationContext.Current` at `add`. A handler is
  invoked inline when it captured none or when the raising thread's current context is the same instance; otherwise
  it is `Post`ed to its context. A posted invocation is skipped when the bindings were disposed before it ran.
- An exception from an inline handler does not stop the remaining handlers of the pass; the first is rethrown at the
  end of the pass into the effect, which records it (`Effect.Error`, not surfaced).
- `Dispose` off the graph's thread is dispatched to it.

## Ownership

- The constructor creates a root scope (`Graph.CreateRoot`) under the scope current at creation: `Graph.Root`
  outside any body, or the enclosing effect, owning memo, boundary or projection row when created inside one. The
  effect and every `Computed` memo belong to that root, as does anything created through `Run`.
- `Dispose` disposes the root and drops every handler. Disposing the enclosing scope (a projection row leaving, an
  effect re-running, `Graph.Dispose`) has the same effect, so an undisposed view model does not outlive its owner,
  and its handlers (the view) are released with it.
- Signals passed to `Writable` belong to the caller; `Writable(name, initial)` creates one that nothing disposes
  (signals hold no resources).

## Alternatives considered

- **Per-property handle objects implementing INPC, bound as `{Binding Total.Value}`** (SignalsDotnet style). Rejected
  as the primary path: it changes every binding path, so it is not gradual. The handles still expose `IsLoading` and
  `Error` for a view model that wants per-property flags.
- **`Get<T>([CallerMemberName])` dictionary lookup with lazy creation on first read.** Rejected: nodes would be created
  from inside a binding read, possibly off the graph's thread and inside another body; typed handles avoid the
  lookup and the cast.
- **One effect per property.** Rejected: handlers would observe some snapshots updated and others not within one
  flush. One effect costs a `TryValue` per property per pass, which is a field read for a clean memo.
- **`EffectOn`.** Its cutoff drops pending and failed readings, which the adapter must observe.
- **Capturing the context once, at bindings construction.** Rejected: CommunityToolkit#536's failure mode is exactly a
  subscriber on another context than the creator.
- **Returning exceptions from `GetErrors`.** WPF and Avalonia render error content with `ToString`, which for an
  exception includes the stack trace. `GetErrors` returns messages; the exception is on `BoundValue.Error`.
- **A base class only.** Excluded by the goal of adoption inside `ObservableObject` subclasses; `ReactiveObject` is
  a thin convenience over the composable path. F# has no `protected`, so its helpers stay on `Bindings`.
