---
title: Ranvier or Adaptive for a desktop app?
description: A desktop-first comparison of Ranvier and FSharp.Data.Adaptive, with a runnable F# window and live signal maps.
---

## “When would I use this instead of FSharp.Data.Adaptive?”

Max Paige asked:

> That Ranvier project looks interesting but I am trying to figure out when or how I would use it vs. FSharp.Data.Adaptive. Especially when my use case is a desktop app, maybe that's where my confusion stems from.

**Both can manage the state behind a desktop app.** Your UI framework creates windows and controls; the reactive library maintains the values they display.

- **Already using Adaptive?** Familiar APIs are a reason to stay; better performance on your workload can be a reason to switch.
- **Performance-sensitive app?** Compare the same state updates and UI consumers in both libraries, including latency and allocations.
- **Considering Ranvier?** Look at its disposable owners, UI-thread graph, binding adapters and propagating pending/failure states.
- **Building collection-heavy views?** Compare Adaptive's established set/list/map operators with Ranvier's keyed collection paths.

:::info Preview
Ranvier is pre-release; its APIs may change. This article describes `master` and makes no comparative performance claim.
:::

## What connects state to a desktop control?

A button and a label displaying twice the click count need three pieces:

1. **State:** a writable count and a derived doubled count.
2. **Consumer:** an effect, callback or binding adapter that reads the result.
3. **Host:** controls, event handlers, UI scheduling and exit cleanup.

Adaptive calls the values `cval<int>` and `aval<int>`; Ranvier calls them a signal and a memo. Both cache derived results and track dynamic dependencies.

## First, what are graphs, activation and roots?

### A graph is your state engine

A `Graph` holds reactive nodes, schedules their effects and controls thread affinity. For a simple desktop app, create one on the GUI thread and keep it alive until exit.

Adaptive's examples construct `cval` and `aval` directly, without an explicit graph object. Ranvier makes the engine's configuration and top-level lifetime explicit.

### Activate it once in your GUI setup

The `create*` functions use `Graph.Current`. Creating a graph does not select it; `graph.Activate()` selects it and returns a disposable activation handle.

```fsharp
open Ranvier

use graph = new Graph ()
use active = graph.Activate ()

let count = createSignal 0
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> printfn "doubled = %d" doubled.Value)

count.Value <- 1
```

This prints `0`, then `2`. In a desktop entry point, keep these `use` bindings in the scope containing the GUI message loop, as the complete example below does.

There are two separate handles:

- **Dispose `active`:** restore the previously active graph; existing nodes stay alive.
- **Dispose `graph`:** release its owned computations and run their cleanups.

A `use` binding inside a short setup callback ends when that callback returns. Store the graph for later exit disposal, or put the GUI message loop inside its enclosing `use` scope.

### A root is an ownership scope inside a graph

Each graph already has a `graph.Root` owner. You do **not** need `createRoot` just to start an app.

Use `createRoot` for something that ends before the graph does: a tab, panel or window sharing application state. Dispose its returned owner when that UI is removed.

```fsharp
let panelOwner =
    createRoot (fun owner ->
        createEffect (fun () -> printfn "panel count = %d" count.Value)
        owner)

panelOwner.Dispose ()
count.Value <- 2
```

The panel's effect stops; the outer `count` signal remains available. A root groups lifetimes within the same graph—it does not create another scheduler or UI thread.

**Watch it:** write once, dispose the panel, then write again. The effect disappears and stays silent on the second write.

```fsharp map replay code=collapsed
let count = createSignal 0
let mutable runs = 0
let panelOwner =
    createRoot (fun owner ->
        createEffect (fun () ->
            runs <- runs + 1
            printfn "panel count = %d" count.Value)
        owner)

controls [
    button "Write 1" (fun () -> count.Value <- 1)
    |> describe "The live panel effect reads the new count."
    |> expect "the live panel responds" (fun () -> runs = 2)
    button "Close panel" (fun () -> panelOwner.Dispose ())
    |> describe "Disposing the owner removes its effect."
    |> expect "closing does not rerun the effect" (fun () -> runs = 2)
    button "Write 2" (fun () -> count.Value <- 2)
    |> describe "The signal changes; the closed panel stays silent."
    |> expect "the disposed effect stays silent" (fun () -> runs = 2 && count.Peek = 2)
]
```

Signal maps draw **dependencies**, not the owner tree. See [Graphs](../guide/graph.fsx), [Roots and owners](../guide/roots.md) and [Cleanup](../guide/cleanup.md) for the complete contracts.

### Then what is graph.Run()?

`graph.Run` is a short activation scope. It activates the graph for a callback and restores the previous activation when the callback returns.

```fsharp
graph.Run (fun () ->
    let count = createSignal 0
    createEffect (fun () -> printfn "%d" count.Value))
```

It does not start an application loop, create a new root or dispose the nodes afterward. Use it for isolated setup; use `Activate()` when a longer scope reads more naturally.

## The same calculation in both libraries

### Adaptive

```fsharp
open FSharp.Data.Adaptive

let count = cval 0
let doubled = count |> AVal.map (fun n -> n * 2)

let before = AVal.force doubled
transact (fun () -> count.Value <- 1)
let after = AVal.force doubled
```

`before` is `0`; `after` is `2`. `AVal.force` pulls the result without installing a UI subscription.

Adaptive pushes invalidation and pulls evaluation on demand. Its [official tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) explains that model.

### Ranvier

The earlier `Activate()` example uses an effect to consume `doubled`. Here is its dependency graph: change the count and watch the memo and effect respond.

```fsharp map replay code=open
let count = createSignal 0
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> printfn "doubled = %d" doubled.Value)

controls [
    sliderSignal "Count" (0, 5) count [ 1; 3 ]
    |> describe "The count write refreshes doubled and its effect."
    |> expect "doubled follows count" (fun () -> doubled.Peek = count.Peek * 2)
]
```

The docs' map host creates and activates a graph for each map. Plain Ranvier fragments below assume your graph is active; Adaptive fragments open `FSharp.Data.Adaptive`.

## How would I start a desktop app with Ranvier?

Start with your UI framework and add one reactive interaction. You can use F# controls directly or the `Ranvier.CSharp` adapters for MVVM/XAML.

This complete **Windows-only WinForms counter** keeps the graph active throughout the GUI loop. Avalonia and WPF use the same lifetime pattern with their own startup and dispatcher APIs.

### Create the project

```shell
dotnet new console -lang F# -n RanvierDesktop
cd RanvierDesktop
dotnet add package Ranvier --prerelease
```

Change the generated property group in `RanvierDesktop.fsproj`:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

Keep the generated `Compile Include="Program.fs"` and package reference. The Windows target enables WinForms; Ranvier supports .NET 8.

### Replace Program.fs

```fsharp
open System
open System.Threading
open System.Windows.Forms
open Ranvier

[<EntryPoint; STAThread>]
let main _ =
    Application.EnableVisualStyles ()
    Application.SetCompatibleTextRenderingDefault false

    use window = new Form (Text = "Ranvier counter", Width = 320, Height = 160)
    let label = new Label (Left = 20, Top = 20, Width = 260)
    let increment = new Button (Text = "Increment", Left = 20, Top = 55, Width = 120)
    window.Controls.Add label
    window.Controls.Add increment

    use uiContext = new WindowsFormsSynchronizationContext ()
    SynchronizationContext.SetSynchronizationContext uiContext

    use graph = new Graph ()
    use active = graph.Activate ()

    let count = createSignal 0
    let doubled = createMemo (fun _ -> count.Value * 2)
    createEffect (fun () ->
        label.Text <- sprintf "Count: %d; doubled: %d" count.Value doubled.Value)

    let clicks =
        increment.Click.Subscribe (fun _ -> count.Value <- count.Value + 1)
    onCleanup (fun () -> clicks.Dispose ())

    Application.Run window
    0
```

Run `dotnet run`. The label starts at `Count: 0; doubled: 0` and updates on each click.

The lifecycle is:

1. Construct controls and establish the GUI synchronization context.
2. Construct the graph on that thread and activate it.
3. Create state, effects and event subscriptions.
4. Run the GUI loop with [`Application.Run`](https://learn.microsoft.com/dotnet/api/system.windows.forms.application.run).
5. On exit, leave the `use` scope: restore activation, dispose the graph and clean up subscriptions.

WinForms normally installs its synchronization context automatically; this example sets it explicitly before constructing the graph. Other GUI hosts should establish their own context or dispatcher before graph construction.

### The Adaptive version

In the same entry point, replace the graph/state/subscription block with:

```fsharp
let count = cval 0
let doubled = count |> AVal.map (fun n -> n * 2)
let caption =
    AVal.map2 (fun n d -> sprintf "Count: %d; doubled: %d" n d) count doubled

use subscription = caption.AddCallback (fun text -> label.Text <- text)
use clicks =
    increment.Click.Subscribe (fun _ ->
        transact (fun () -> count.Value <- count.Value + 1))
```

Install `FSharp.Data.Adaptive` and replace `open Ranvier` with `open FSharp.Data.Adaptive`. Keep `Application.Run window` inside the subscriptions' `use` scope.

`AddCallback` delivers the initial value, then later evaluated values. Its disposable handle stops the subscription; it does not select a desktop dispatcher ([implementation](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/FSharp.Data.Adaptive/EvaluationCallbackExtensions.fs)).

Both counters register and update on the UI thread. For worker updates, your Adaptive UI adapter must marshal control notifications; Ranvier supplies graph dispatch.

## “My app uses MVVM and XAML”

A signal or `aval` needs an adapter to become an observable view-model property. `Ranvier.CSharp` supplies `ReactiveObject`, writable/computed bindings, commands and collection adapters.

```csharp
using Ranvier;
using Ranvier.CSharp;

public sealed class CounterViewModel : ReactiveObject
{
    readonly BoundSignal<int> count;
    readonly BoundValue<int> doubled;

    public CounterViewModel(Graph graph) : base(graph)
    {
        count = Bindings.Writable(nameof(Count), 0);
        doubled = Bindings.Computed(nameof(Doubled), () => count.Value * 2);
        Increment = Bindings.Command(_ => { Count++; });
    }

    public int Count { get => count.Value; set => count.Value = value; }
    public int Doubled => doubled.Value;
    public ReactiveCommand Increment { get; }
}
```

Set a WPF view's `DataContext` to that instance:

```xml
<StackPanel>
  <TextBlock Text="{Binding Count}" />
  <TextBlock Text="{Binding Doubled}" />
  <Button Content="Increment" Command="{Binding Increment}" />
</StackPanel>
```

Install `Ranvier.CSharp --prerelease` and create the graph/view model on the GUI thread. Dispose the view model when its window closes, and the graph when its owning window or application exits.

For Adaptive, use your UI stack's adapter or bridge callbacks to `INotifyPropertyChanged`. [Navs.Avalonia](https://angelmunoz.github.io/Navs/Navs-Avalonia.html#Adaptive-Data) demonstrates desktop views and shared state; its binding helpers belong to Navs.

See the [C# binding guide](../guide/csharp.md#binding-to-xaml) for loading, errors and commands.

## Forms: update related fields together

In Adaptive, quantity and price feed an adaptive total:

```fsharp
let quantity = cval 2
let unitPrice = cval 12m
let total = AVal.map2 (fun q p -> decimal q * p) quantity unitPrice

transact (fun () ->
    quantity.Value <- 3
    unitPrice.Value <- 10m)

AVal.force total
```

In Ranvier, batch a Reset button's writes so its effect sees the final combination. This map uses integer prices to keep the arithmetic easy to follow.

```fsharp map replay code=open
let quantity = createSignal 2
let unitPrice = createSignal 12
let total = createMemo (fun _ -> quantity.Value * unitPrice.Value)
let mutable paints = 0
createEffect (fun () ->
    paints <- paints + 1
    printfn "total = %d" total.Value)

controls [
    button "Reset both fields" (fun () ->
        batch (fun () ->
            quantity.Value <- 3
            unitPrice.Value <- 10))
    |> describe "Two writes publish total 30 with one additional paint."
    |> expect "the reset paints once" (fun () -> total.Peek = 30 && paints = 2)
]
```

Both results are `30`. Ranvier's batch groups updates but does not roll back writes if the callback throws; see [Batch](../guide/batch.md).

For frequently edited forms, separate independently changing fields. [Forms](../guide/forms.md) and the [Elmish bridge](../guide/elmish.md) cover field signals and gradual adoption around an existing `update` function.

## Conditional panels: both track changing dependencies

Adaptive can select an input with `AVal.bind`:

```fsharp
let showDetails = cval false
let title = cval "Order details"
let heading =
    showDetails
    |> AVal.bind (fun visible ->
        if visible then title :> aval<string>
        else AVal.constant "Details hidden")
```

Ranvier tracks the reads made by the current branch. Replay the controls and watch the `title` edge appear when the panel opens.

```fsharp map replay code=open
let showDetails = createSignal false
let title = createSignal "Order details"
let mutable headingRuns = 0
let heading =
    createMemo (fun _ ->
        headingRuns <- headingRuns + 1
        if showDetails.Value then title.Value else "Details hidden")
createEffect (fun () -> printfn "%s" heading.Value)

controls [
    button "Rename while hidden" (fun () -> title.Value <- "Shipping")
    |> describe "The hidden heading does not read title."
    |> expect "hidden title edits do no heading work" (fun () -> headingRuns = 1)
    button "Open details" (fun () -> showDetails.Value <- true)
    |> describe "The visible branch starts tracking title."
    |> expect "opening reads the current title" (fun () -> heading.Peek = "Shipping")
    button "Rename while visible" (fun () -> title.Value <- "Payment")
    |> describe "Now title changes update the heading."
    |> expect "visible title edits propagate" (fun () -> heading.Peek = "Payment")
    button "Hide details" (fun () -> showDetails.Value <- false)
    |> describe "The title dependency is removed again."
    |> expect "closing restores the hidden heading" (fun () -> heading.Peek = "Details hidden")
]
```

The difference is how you express the dependency. Adaptive also supports dynamic branches; its [tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) demonstrates them.

## Performance can justify a switch

You may prefer Adaptive's APIs and still choose Ranvier if it performs better on the work your app does. The reverse is equally valid: compare the workload, then choose the engine.

Useful desktop measurements include:

- **Input latency:** time from a keystroke or click to the displayed result.
- **Update cost:** time spent propagating changes and evaluating derived values.
- **Allocations and GC:** memory churn during repeated edits or refreshes.
- **UI work:** notifications, changed rows and redraws per update.

Keep the inputs, derived calculations and displayed output equivalent. Test realistic sizes and update patterns, including single-field edits, batches and collection changes.

A counter teaches the API; a benchmark must exercise the path you are choosing between. Ranvier's [benchmarks](../benchmarks/index.md) document its measured scenarios; use a comparison of your own workload to establish a switching benefit.

## Lists: compare the actual collection path

Use Adaptive collections when you want collection deltas:

```fsharp
let numbers = cset [ 1; 2; 3 ]
let visible = numbers |> ASet.filter (fun n -> n > 1)
let captions = visible |> ASet.map (fun n -> sprintf "Row %d" n)

transact (fun () -> numbers.Add 4 |> ignore)
```

Choose `aset`, `alist` or `amap` for your data's shape. A reader or UI adapter consumes changes; defining `captions` alone does not update a control ([collection documentation](https://github.com/fsprojects/FSharp.Data.Adaptive)).

Ranvier has editable keyed sources; from C# inside an active graph:

```csharp
using static Ranvier.CSharp.Reactive;

var items = KeyedCollection<(int Id, string Title), int>(item => item.Id);
items.Edit(edit =>
{
    edit.AddOrUpdate((1, "Write"));
    edit.AddOrUpdate((2, "Test"));
});
var titles = items.Rows.Select(item => item.Title);
items.AddOrUpdate((2, "Retest"));
```

Key `2` keeps its position while its row changes. Use `AsObservableCollection` for desktop binding, or consume change readers in a custom adapter; dispose them with the view.

The trade-offs depend on your operators:

- **Adaptive:** an established, broader family of incremental set/list/map operations.
- **Ranvier:** keyed rows, delta readers and ownership, with some membership paths still scanning keys and changed sort ranks using a full sort.
- **Either:** measure your edit/filter/sort/aggregate workload; deltas do not make every operation proportional to changed rows.

See [collection bindings](../guide/csharp.md#editable-keyed-collections), [reader contracts](../guide/projections.fsx#reading-changes) and [current costs](../concepts/ecosystem.md). The [collection maps](../guide/signal-maps.md#collections) show per-row propagation.

## Loading: explicit state or a propagating pending channel?

With Adaptive, a useful application model is an explicit union:

```fsharp
type LoadState<'T> =
    | Loading
    | Loaded of 'T
    | LoadFailed of string

let user = cval<LoadState<string>> Loading
let greeting =
    user |> AVal.map (function
        | Loading -> "Loading profile…"
        | Loaded name -> "Hello, " + name
        | LoadFailed message -> "Could not load: " + message)

transact (fun () -> user.Value <- Loaded "Ada")
```

Your workflow publishes loading, success and failure, and chooses cancellation/result ordering. This is useful when loading is domain state you want to inspect or handle in `update`.

Ranvier lets pending/failure travel through computations that read ordinary values. The boundary chooses display text while `greeting` remains a simple calculation.

```fsharp map replay code=open
let user = createAsyncSource<string> ()
let greeting = createMemo (fun _ -> "Hello, " + user.Value)
let panel =
    createBoundary
        (fun _ -> "Loading…")
        (fun error _ -> error.Message)
        (fun () -> greeting.Value)
createEffect (fun () -> printfn "%s" panel.Value)

controls [
    button "Load Ada" (fun () -> user.Settle "Ada")
    |> describe "Settling the source wakes greeting and replaces the fallback."
    |> expect "the loaded greeting is visible" (fun () -> panel.Peek = "Hello, Ada")
    button "Fail" (fun () -> user.Fail (exn "offline"))
    |> describe "Failure reaches the boundary through greeting."
    |> expect "the error is displayed" (fun () -> panel.Peek = "offline")
    button "Recover with Grace" (fun () -> user.Settle "Grace")
    |> describe "A new value recovers without rebuilding the graph."
    |> expect "the panel recovers" (fun () -> panel.Peek = "Hello, Grace")
]
```

Adaptive's core guide does not document this built-in propagating pending channel. You can still use an explicit union in Ranvier when it better represents your domain.

### Requests driven by selection

For a real service, replace the manual source with `createAsync`. Assume `fetchName` has type `int -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<string>`:

```fsharp
let selectedId = createSignal 1
let name =
    createAsync (fun _ token ->
        let id = selectedId.Value
        fetchName id token)
let greeting = createMemo (fun _ -> "Hello, " + name.Value)
let panel =
    createBoundary
        (fun _ -> "Loading…")
        (fun error _ -> error.Message)
        (fun () -> greeting.Value)
createEffect (fun () -> printfn "%s" panel.Value)
```

Changing `selectedId` starts a new observed request. The default `CancelPrevious` policy requests cancellation and drops superseded results; your I/O must cooperate with the token.

Watch that workflow with `Desk`, the maps' controllable service:

```fsharp map replay code=collapsed
let desk = Desk<string>()
let selectedId = createSignal 1
let name = createAsync (fun _ _ -> desk.Quote selectedId.Value)
let greeting = createMemo (fun _ -> "Hello, " + name.Value)
let panel =
    createBoundary
        (fun _ -> "Loading…")
        (fun error _ -> error.Message)
        (fun () -> greeting.Value)
createEffect (fun () -> printfn "%s" panel.Value)

controls [
    button "Select 2, then 3" (fun () ->
        selectedId.Value <- 2
        selectedId.Value <- 3)
    |> describe "Each selection supersedes the previous pending flight."
    |> expect "only the latest request remains" (fun () -> desk.Pending = 1)
    button "Answer latest: Grace" (fun () -> desk.Settle "Grace")
    |> describe "The latest answer flows through greeting to the panel."
    |> expect "the latest result is visible" (fun () -> panel.Peek = "Hello, Grace")
]
```

- Read reactive request inputs before the first suspending `await`.
- Create async nodes outside the boundary; nodes created inside its body are replaced on rerun.
- See [Async memos](../guide/async-memos.md) and [Boundaries](../guide/boundaries.md) for flight policies and fallback choices.

## Background work and multiple windows

Publish a worker result on the Ranvier graph's thread:

```fsharp
graph.Dispatch (fun () -> count.Value <- 42)
```

With a captured GUI synchronization context, queued work drains there. Without a context or configured dispatcher, the owning thread must call `graph.Pump()`; see [Threading](../guide/threading.md).

For Adaptive, write inside `transact` and marshal control notifications through your UI adapter. A transaction is not a desktop dispatcher.

Choose lifetimes explicitly:

- **One window:** keep its graph active through the GUI loop and dispose it on exit.
- **Shared application state:** keep one application-owned graph; give each removable view a root owner or binding scope.
- **Tabs and panels:** dispose their owners, subscriptions and readers when removed.

## What to learn from Adaptive's guides

Adaptive's [official tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) teaches inputs, derivation, transactions, reads, collections and dynamic branches. [Navs.Avalonia's examples](https://angelmunoz.github.io/Navs/Navs-Avalonia.html#Adaptive-Data) add the desktop host and binding layer.

Try this progression in your own app:

1. Bind one derived label and check its initial value.
2. Update it from a UI event.
3. Reset related fields together.
4. Toggle a conditional panel and inspect its dependencies.
5. Edit one keyed row and check identity/order.
6. Supersede a slow request and check which result wins.
7. Close a panel and check that its old consumers stop.

## Which would I choose for Max's app?

- **Existing Adaptive/Aardvark stack:** keep its integration advantage unless a specific workflow hurts.
- **Measured performance bottleneck:** switch if the other engine improves the latency, throughput or allocations that matter to your app.
- **Incremental collection transformations:** investigate Adaptive's operators first.
- **Owned consumers, XAML adapters and propagating async state:** try Ranvier in one real screen.
- **A small form with little derived work:** ordinary MVVM may already be enough.

For Ranvier, start with one GUI-thread graph, activate it during setup, and dispose it at exit. Add child roots when views need shorter lifetimes, then test a real async operation and list before expanding adoption.
