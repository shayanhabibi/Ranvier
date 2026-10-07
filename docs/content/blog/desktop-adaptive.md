---
title: Ranvier or Adaptive for a desktop app?
description: A desktop-first comparison of Ranvier and FSharp.Data.Adaptive, with a runnable F# window and practical state, binding, collection and async workflows.
---

## “When would I use this instead of FSharp.Data.Adaptive?”

Max Paige asked a useful question about Ranvier:

> That Ranvier project looks interesting but I am trying to figure out when or how I would use it vs. FSharp.Data.Adaptive. Especially when my use case is a desktop app, maybe that's where my confusion stems from.

**Both can be the state engine of a desktop app.** Your UI framework still creates windows,
lays out controls and handles input. The reactive library maintains the values those controls
display. Choosing Windows rather than a browser does not, by itself, pick one library.

If you already use Adaptive successfully, a counter or a derived property is little reason to
switch. Look at Ranvier when you want its combination of tracked ordinary reads, effects owned
by disposable scopes, a graph tied to your UI thread, and loading/failure propagation through
derived computations. Look at Adaptive when its established adaptive collection operators or
an existing Adaptive-based UI/rendering stack solve your problem well.

:::info Preview
Ranvier is pre-release; its APIs may change. This article describes the APIs on `master`,
not an experimental UI framework. It makes no comparative performance claim.
:::

Let's make that choice concrete, starting with the part that is often missing from a reactive
library introduction: **how does a value actually reach a desktop control?**

## There are three pieces in either app

Imagine a button and a label displaying twice the click count:

1. **State:** a changeable count and a derived doubled count.
2. **Consumer:** an effect, callback or binding adapter that reads the result.
3. **Desktop host:** controls, event handlers, UI scheduling and disposal when the window closes.

Adaptive calls the first two values `cval<int>` and `aval<int>`. Ranvier calls them a signal and
a memo. Both cache derived results and maintain dynamic dependencies. Neither core library
turns an integer into a window on its own.

### The same calculation in Adaptive

```fsharp
open FSharp.Data.Adaptive

let count = cval 0
let doubled = count |> AVal.map (fun n -> n * 2)

let before = AVal.force doubled
transact (fun () -> count.Value <- 1)
let after = AVal.force doubled
```

`before` is `0`; `after` is `2`. `AVal.force` pulls the current result. It does not install a
UI subscription. Adaptive pushes invalidation and pulls evaluation when a consumer asks for a
value; its [official tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) walks through
this model.

### The same calculation in Ranvier

```fsharp
open Ranvier

use graph = new Graph ()

let before, after =
    graph.Run (fun () ->
        let count = createSignal 0
        let doubled = createMemo (fun _ -> count.Value * 2)
        let before = doubled.Value
        count.Value <- 1
        before, doubled.Value)
```

Again, the results are `0` and `2`. The memo's function reads `count.Value`; that read establishes
the dependency. A memo computes on demand, and an effect supplies demand when connected to a UI.

Each comparison below is independent. Ranvier fragments using `create*` run inside `graph.Run`
unless the fragment includes its own graph setup. Adaptive fragments open `FSharp.Data.Adaptive`.

## How would I start a desktop app with Ranvier?

Start with your UI framework, then put Ranvier behind one small interaction. You can write F#
against controls directly, or use the `Ranvier.CSharp` binding adapters for an MVVM/XAML app.
You do not need a Ranvier-specific application template.

Here is a complete **Windows-only F# WinForms counter**. WinForms keeps the example small enough
to show the entire connection, including lifetime and startup. For Avalonia or WPF, the same
state/effect design applies, with that framework's controls and lifecycle events.

### Create the project

With a .NET SDK installed, run:

```shell
dotnet new console -lang F# -n RanvierDesktop
cd RanvierDesktop
dotnet add package Ranvier --prerelease
```

In `RanvierDesktop.fsproj`, change the generated property group to:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

Keep the generated `Compile Include="Program.fs"` and the package reference added by the command.
Ranvier supports .NET 8; this Windows target enables the WinForms desktop APIs.

### Replace Program.fs

```fsharp
open System
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

    window.Shown.Add (fun _ ->
        let graph = new Graph ()
        window.FormClosed.Add (fun _ -> graph.Dispose ())

        graph.Run (fun () ->
            let count = createSignal 0
            let doubled = createMemo (fun _ -> count.Value * 2)

            createEffect (fun () ->
                label.Text <- sprintf "Count: %d; doubled: %d" count.Value doubled.Value)

            let clicks =
                increment.Click.Subscribe (fun _ -> count.Value <- count.Value + 1)
            onCleanup (fun () -> clicks.Dispose ())))

    Application.Run window
    0
```

Run `dotnet run`. The label initially displays `Count: 0; doubled: 0`. Each click changes the
signal; the effect reads consistent current values and updates the existing label. Closing
the window disposes the graph and its click subscription.

The graph is created in `Shown`, on the UI thread after the desktop host is running. WinForms
normally installs a `WindowsFormsSynchronizationContext`; Ranvier captures the current context
at graph construction. The host's message loop comes from
[`Application.Run`](https://learn.microsoft.com/dotnet/api/system.windows.forms.application.run).
Do not create the graph on a worker thread and then attach it to controls.

`graph.Run` activates the graph while factories create nodes. Returning from it does **not**
dispose those nodes. Conversely, a `use graph` inside the `Shown` callback would dispose it as
soon as that callback returned. Tie disposal to the window's lifetime instead.

### What would the Adaptive wiring look like?

Inside the same window's `Shown` handler, replace the graph setup with:

```fsharp
let count = cval 0
let doubled = count |> AVal.map (fun n -> n * 2)
let caption =
    AVal.map2 (fun n d -> sprintf "Count: %d; doubled: %d" n d) count doubled

let subscription = caption.AddCallback (fun text -> label.Text <- text)
let clicks =
    increment.Click.Subscribe (fun _ ->
        transact (fun () -> count.Value <- count.Value + 1))

window.FormClosed.Add (fun _ ->
    clicks.Dispose ()
    subscription.Dispose ())
```

Install `FSharp.Data.Adaptive` and replace `open Ranvier` with `open FSharp.Data.Adaptive` for
this version. `AddCallback` delivers the initial value and later evaluated values, and returns
a disposable subscription. It is an Adaptive API, not a XAML binding adapter.

Here, registration and every transaction happen on the UI thread. If transactions later come
from workers, marshal the UI update through your desktop dispatcher. Adaptive's callback API
does not automatically choose a WinForms, WPF or Avalonia dispatcher. See its
[callback implementation](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/FSharp.Data.Adaptive/EvaluationCallbackExtensions.fs).

For this counter, either choice is reasonable. Ranvier gives the subscription a graph-owned
cleanup scope. Adaptive gives you a subscription to dispose explicitly. The surrounding window
is still the same window.

## “But my desktop app uses MVVM and XAML”

Then your consumer is usually a property-notification adapter. A plain signal or `aval` is not,
by itself, a view-model property that a XAML binding will observe.

Ranvier's `Ranvier.CSharp` package includes `ReactiveObject`, writable/computed bindings,
`ICommand` support and an observable collection adapter. For example:

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

For a WPF view whose `DataContext` is that instance:

```xml
<StackPanel>
  <TextBlock Text="{Binding Count}" />
  <TextBlock Text="{Binding Doubled}" />
  <Button Content="Increment" Command="{Binding Increment}" />
</StackPanel>
```

Install `Ranvier.CSharp --prerelease`. Create the graph and view model on the UI thread once
the framework's synchronization context is installed, set `DataContext`, and dispose the view
model and graph when the window closes. The graph may instead be application-owned if multiple
windows share state; closing one window then disposes its view model, not the shared graph.
The [C# guide](../guide/csharp.md#binding-to-xaml) covers the full binding contract, loading, errors and commands.

With Adaptive, use the adapter supplied by your chosen UI stack, or implement a small
`INotifyPropertyChanged` bridge around callbacks. For an existing Avalonia/Adaptive stack,
[Navs.Avalonia's guide](https://angelmunoz.github.io/Navs/Navs-Avalonia.html#Adaptive-Data) demonstrates views,
shared state and binding helpers. Its helpers belong to Navs; they are not all built into
FSharp.Data.Adaptive. Follow the integration you already use before writing a competing bridge.

## A settings form: derive state, then commit related edits together

Suppose quantity and unit price determine a total. In Adaptive:

```fsharp
let quantity = cval 2
let unitPrice = cval 12m
let total = AVal.map2 (fun q p -> decimal q * p) quantity unitPrice

transact (fun () ->
    quantity.Value <- 3
    unitPrice.Value <- 10m)

AVal.force total
```

In Ranvier:

```fsharp
let quantity = createSignal 2
let unitPrice = createSignal 12m
let total = createMemo (fun _ -> decimal quantity.Value * unitPrice.Value)

batch (fun () ->
    quantity.Value <- 3
    unitPrice.Value <- 10m)

total.Value
```

Both yield `30m`. A desktop Reset button can update several fields in one transaction or batch,
so consumers need not paint each intermediate write. These are related update-grouping tools,
not interchangeable database transactions: Ranvier's batch does not roll back writes when the
callback throws. See [Batch](../guide/batch.md) and Adaptive's
[transactions documentation](https://fsprojects.github.io/FSharp.Data.Adaptive/).

For a frequently edited form, put independently changing fields in separate changeable values
or signals. Replacing a whole record and selecting every field can make many selectors recompute
to discover that their result stayed equal. Ranvier's [Forms](../guide/forms.md) and
[Elmish bridge](../guide/elmish.md) explain both approaches; you can keep an existing `update` function
and adopt fine-grained consumers gradually.

## Conditional panels: both libraries track changing dependencies

“Only read the detailed title while the details panel is open” is possible in both libraries.
Adaptive expresses the choice with `AVal.bind` or its `adaptive` computation expression:

```fsharp
let showDetails = cval false
let title = cval "Order details"
let heading =
    showDetails
    |> AVal.bind (fun visible ->
        if visible then title :> aval<string>
        else AVal.constant "Details hidden")
```

Ranvier uses the reads made by the memo's current branch:

```fsharp
let showDetails = createSignal false
let title = createSignal "Order details"
let heading =
    createMemo (fun _ ->
        if showDetails.Value then title.Value
        else "Details hidden")
```

While hidden, changing `title` is not a dependency of the chosen heading calculation. Opening
the panel makes it one; closing the panel removes it again. The distinction is the programming
surface, not “Adaptive has static dependencies and Ranvier has dynamic ones.” Adaptive's
[dynamic dependencies tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) explicitly
demonstrates conditional dependency changes.

## A large desktop list: compare the collection path, not just scalar values

For Adaptive, choose an adaptive collection when you want collection deltas:

```fsharp
let numbers = cset [ 1; 2; 3 ]
let visible = numbers |> ASet.filter (fun n -> n > 1)
let captions = visible |> ASet.map (fun n -> sprintf "Row %d" n)

transact (fun () -> numbers.Add 4 |> ignore)
```

An `aset` is a set: it is not the ordered row model for every grid. Adaptive also supplies
`alist` and `amap`. Choose the shape and operators your UI needs. Its tutorial explains why an
adaptive collection differs from putting a whole immutable collection inside a `cval`.
A reader or UI adapter consumes changes; merely defining `captions` does not update a control.
See the [repository and collection documentation](https://github.com/fsprojects/FSharp.Data.Adaptive).

Ranvier's editable keyed sources also provide a direct edit path. From C# inside an active graph:

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

The edit changes the existing row with key `2`, keeping its position. For a desktop list, use
`AsObservableCollection` to bridge a projection to `ObservableCollection`, or consume change
readers in a custom adapter. Dispose the adapter/readers with the view. The
[C# collection guide](../guide/csharp.md#editable-keyed-collections) and
[reader guide](../guide/projections.fsx#reading-changes) explain resets, positional changes and ownership.

Adaptive has a broader established family of incremental set, map and list operators. Ranvier
has keyed rows, delta readers and adapters, but some filter/group membership paths scan keys,
changed sort ranks use a full sort, and membership changes copy ordered keys. A delta-producing
API does not make every operation proportional to the number of changed rows. Compare your
actual edit/filter/sort/aggregate workload; the [ecosystem page](../concepts/ecosystem.md)
records Ranvier's current collection costs.

## A search panel: where Ranvier's pending channel changes the workflow

A desktop app often needs “loading”, “loaded” and “failed” states. With Adaptive, one useful
application model is an explicit union:

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

Your request workflow publishes `Loading`, then the success or failure. For type-ahead search,
the application or integration also decides cancellation and which result wins when requests
overlap. This is a useful explicit design, especially when loading is domain state that you
want to save, inspect or handle in `update`. Adaptive's core tutorial does not document a
built-in pending channel that propagates through ordinary dependent value reads.

In Ranvier, pending can travel through computations that just read their inputs:

```fsharp
let user = createAsyncSource<string> ()
let greeting = createMemo (fun _ -> "Hello, " + user.Value)
let panel =
    createBoundary
        (fun _ -> "Loading profile…")
        (fun error _ -> "Could not load: " + error.Message)
        (fun () -> greeting.Value)

createEffect (fun () -> printfn "%s" panel.Value)
user.Settle "Ada"
```

The effect first prints `Loading profile…`, then `Hello, Ada`. Replace `printfn` with the
label assignment from the desktop example. `greeting` contains no loading match; its read
suspends until `user` settles. The boundary converts pending/failure into display text.
`user.Fail (Exception "offline")` exercises the error path; a later settle can recover.

For a request driven by a changing signal, use an async memo instead of manually settling a
source. Assuming your service supplies
`fetchName : int -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<string>`:

```fsharp
let selectedId = createSignal 1
let name =
    createAsync (fun _ token ->
        let id = selectedId.Value
        fetchName id token)
let greeting = createMemo (fun _ -> "Hello, " + name.Value)
let panel =
    createBoundary
        (fun _ -> "Loading profile…")
        (fun error _ -> "Could not load: " + error.Message)
        (fun () -> greeting.Value)

createEffect (fun () -> printfn "%s" panel.Value)
selectedId.Value <- 2
```

The effect supplies demand. A new selection makes the observed request run again. The default
`CancelPrevious` policy requests cancellation of the old flight and discards its superseded
result; your I/O must cooperate with the token to stop work. Read reactive request inputs before
the first suspending `await`. Create the async memo outside the boundary, because a boundary
replaces nodes created in its body on each run. See [Async memos](../guide/async-memos.md) and
[Boundaries](../guide/boundaries.md).

This is a reason to consider Ranvier beyond the counter: several intermediate computations can
remain ordinary value computations while a consumer decides how to display waiting or errors.
You can still model an explicit union in Ranvier when that better represents your application.

## Background work and window lifetime are part of the design

For Ranvier, keep UI mutations on the graph's thread. Dispatch a worker's publication:

```fsharp
graph.Dispatch (fun () -> count.Value <- 42)
```

This assumes `count` is the signal retained from your window setup. With a captured UI
`SynchronizationContext`, queued work drains there. Without a context, the graph uses a manual
dispatcher and the owning thread must call `graph.Pump ()`. Do not disable affinity to bypass
a wrong-thread error; fix the host/dispatch connection. See [Threading and dispatch](../guide/threading.md).

For Adaptive, update changeable data inside `transact`, and make your UI adapter schedule
control notifications on the UI thread. Its transaction mechanism is not a desktop dispatcher.

For either library, decide who owns the state and who owns its consumers:

- **One window:** a Ranvier graph can live for that window; dispose it on close. Keep and dispose
  Adaptive callback/reader subscriptions on close.
- **Several windows sharing state:** keep the state alive at application scope and give each
  window its own consumer lifetime. In Ranvier, use a `createRoot` owner for a panel's effects
  and dispose that owner when the panel is removed.
- **Tabs created and removed repeatedly:** release subscriptions, readers and UI resources each
  time. Ranvier's `onCleanup` attaches resource disposal to an owner; with Adaptive, keep the
  integration's disposable handles alongside the tab.

Owners make cleanup composable; they do not decide when a desktop tab closes. That event still
comes from the UI host. See [Roots and owners](../guide/roots.md) and [Cleanup](../guide/cleanup.md).

## What to borrow from Adaptive's getting-started material

Adaptive does have a substantial [official getting-started tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/),
not just an API reference. It starts with a small mutable input, derives a result, evaluates it,
changes the input in a transaction and evaluates again. It then adds collection changes and
dynamic branches. That progression is worth following when learning either engine.

For desktop practice, read that tutorial alongside
[Navs.Avalonia's examples](https://angelmunoz.github.io/Navs/Navs-Avalonia.html#Adaptive-Data). They show the
missing host layer: construct a view, keep state at the right scope, bind it, and handle user
input. Treat Navs helpers as integration APIs, rather than copying their names into a bare
Adaptive project.

Try the same progression in your own app:

1. Bind one derived label and verify its initial value.
2. Change an input from a UI event and observe the label update.
3. Reset two fields together and check that the UI shows the final combination.
4. Toggle a conditional panel and inspect which dependencies remain active.
5. Edit one keyed row and check both identity and order in the list.
6. Complete a slow request after a newer one and verify your chosen result policy.
7. Close and reopen the panel; check that the old consumers no longer run.

Those exercises reveal more about the fit than translating a single counter.

## So which would I choose for Max's desktop app?

For an app already built around Adaptive bindings or Aardvark, keep that advantage unless a
specific workflow is painful. For an app whose main challenge is incremental set/list/map
transformation, investigate Adaptive's collection operators first.

For an MVVM app where you want graph-owned consumers, property/command adapters and async
dependencies whose loading/failure states propagate to a view boundary, try Ranvier in one
screen. Start with the window above or the [XAML binding guide](../guide/csharp.md#binding-to-xaml), then
add one real async operation and a realistic list before adopting it across the application.

For a small form with a few properties and no expensive derived work, ordinary MVVM may already
be sufficient. Ranvier and Adaptive are tools for maintaining dependent state; a desktop app
does not require either one merely because it has buttons.

The useful question is: **which library makes this screen's state, consumers, async policy and
lifetime easiest to express and verify?** Desktop is the host. Those workflows determine the fit.
