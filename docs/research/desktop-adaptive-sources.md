# FSharp.Data.Adaptive research for a Ranvier desktop comparison

Research date: 2026-10-07. Primary project documentation and source only. Proposed snippets below are original teaching examples, not verified executable samples.

The resulting article is `docs/content/blog/desktop-adaptive.md`, based on Ranvier `master` at
`89b7be9` (also the fetched `origin/master`). Its F# starter and comparison snippets were compiled
against the source-built .NET 8 Ranvier library and FSharp.Data.Adaptive 1.2.26. Its C# view model
and keyed-collection example were also compiled. Verification supplied the declared service
function for the async request fragment. The GUI was not launched interactively, and the XAML
fragment was not built as a separate WPF app. The docs pipeline passed, including internal
link/anchor checks and all 40 signal-map scenarios, including six in the revised article. The
desktop starter now keeps a GUI-thread graph active with `Activate()` through `Application.Run`,
and releases it at exit. The article's prose paragraphs have at most three sentences. It emitted existing package,
Fable and external-link warnings. This verification applies to the article, not every research
sketch below or the Navs integration examples.

## Getting started and teaching structure

The [official tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/) starts with an ordinary physics formula, adapts it with map2, changes inputs, and reads outputs. It progresses through collections, dynamic dependencies and cached recomputation. Its spreadsheet analogy suits desktop forms, inspectors and dashboards.

Inputs are changeable cells, outputs are dependent cells, updates happen inside transactions, and reads demand results. Adaptive sets/lists/maps maintain element deltas; ordinary collection functions over a collection in one adaptive value do not automatically become incremental. Changes mark affected nodes dirty; demand evaluates them. Marking can reach a downstream node even when evaluation discovers its result did not change. Dynamic branches can depend on different inputs over time. These describe computation behavior, rather than a GUI architecture. [Tutorial](https://fsprojects.github.io/FSharp.Data.Adaptive/).

Teaching takeaway: plain function -> inputs -> derived output -> initial read -> user edit -> new read -> explain which computation ran. Keep one desktop scenario across these steps.

## Original scalar comparison example

```fsharp
open FSharp.Data.Adaptive

let quantity = cval 2
let unitPrice = cval 12.50M
let total = AVal.map2 (fun q price -> decimal q * price) quantity unitPrice

let before = AVal.force total
transact (fun () ->
    quantity.Value <- 3
    unitPrice.Value <- 10M)
let after = AVal.force total
```

Expected values: 25.00M then 30M. map2 combines adaptive inputs; force is a boundary read and must not be used inside another adaptive evaluation because it does not track that dependency. Prefer map/map2/bind or the adaptive computation expression there. [AVal reference](https://fsprojects.github.io/FSharp.Data.Adaptive/reference/fsharp-data-adaptive-avalmodule.html).

Another original sketch:

```fsharp
let showDetails = cval false
let expensiveDetails : aval<string> = makeDetails document
let visibleText =
    showDetails
    |> AVal.bind (fun visible ->
        if visible then expensiveDetails
        else AVal.constant "Details hidden")
```

makeDetails is application code, not an FDA API. The false branch selects a constant rather than depending on expensiveDetails. The bind signature supports selecting an adaptive result dynamically. [AVal reference](https://fsprojects.github.io/FSharp.Data.Adaptive/reference/fsharp-data-adaptive-avalmodule.html).

## Fair collection comparison

FDA is more than aval plus map: compare its adaptive collections with Ranvier's actual collection APIs. The [AList reference](https://fsprojects.github.io/FSharp.Data.Adaptive/reference/fsharp-data-adaptive-alistmodule.html) documents map, filter, collect and adaptive reductions. Cost depends on the operator and operation; avoid promising every update is constant-time.

```fsharp
let prices = clist [12.50M; 4M]
let withTax = prices |> AList.map (fun p -> p * 1.20M)
transact (fun () -> prices.Append 8M |> ignore)
let currentRows = AVal.force withTax.Content
```

This original sketch preserves an adaptive list and element operations. Reading a snapshot does not update GUI rows by itself: a desktop adapter must consume snapshots or deltas and update actual widgets.

## Value observation and UI scheduling

The [evaluation callback implementation](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/FSharp.Data.Adaptive/EvaluationCallbackExtensions.fs) provides AddCallback on adaptive values, readers and collections. Registration outside a transaction evaluates and delivers an initial value. Within a transaction it adds a finalizer. Later invalidation adds a transaction finalizer that forces the value and calls the action. It returns IDisposable. Reader callbacks receive the state before a change and its delta. This implementation does not marshal onto a UI dispatcher.

The [marking callback implementation](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/FSharp.Data.Adaptive/Core/Callbacks.fs) distinguishes AddMarkingCallback: it signals invalidation, not a new value, does not fire merely because the object is already dirty, and returns a disposable. If pulling later, evaluate initially and on scheduled refresh so the value becomes clean again.

Original straightforward bridge sketch:

```fsharp
let subscription =
    total.AddCallback(fun value ->
        Avalonia.Threading.Dispatcher.UIThread.Post(
            System.Action(fun () -> totalLabel.Text <- string value)))
```

Here totalLabel is an existing TextBlock and the window owns subscription. Dispose it when the view closes. The initial callback posts a UI update too. AddCallback forces the computation before posting, so this snippet does not move expensive evaluation onto another scheduler. Heavy computations need a separate scheduling design.

Avalonia requires control access on the UI thread; Post schedules work there without blocking the caller. [Avalonia threading guide](https://docs.avaloniaui.net/docs/app-development/threading). Adaptive data does not replace toolkit threading, lifecycle or startup code.

## Example programs and a desktop ecosystem guide

The [FDA EventInterop example](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/Demo/EventInterop/Program.fs) adapts events into state and wakes a worker from AddMarkingCallback to pull reader deltas. It includes shutdown/disposal and discusses buffering inputs rather than a transaction per event. Caveat: current source calls Environment.Exit 0 before the directory-watcher walkthrough, so this file is not a ready-to-run desktop starter.

The [FDA FileSystem example](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/master/src/Demo/FileSystem/Program.fs) composes sorted adaptive files, adaptive line lists and a collection callback receiving prior state plus delta. Useful input-to-collection workflow; not a GUI template.

The [Navs.Avalonia guide](https://angelmunoz.github.io/Navs/Navs-Avalonia.html#Adaptive-Data) is a first-party desktop ecosystem example owned by its integration project. It demonstrates a counter, derived text, shared sibling state, hoisted events and text editing. AVal.toBinding, AVal.useState, getValue and setValue there come from Navs.Avalonia extensions, not core FDA. Its two-way-binding prose and useState examples/signatures contain inconsistencies; link pedagogically, and verify code against the chosen package before copying it.

## Editorial conclusions

For the planned Windows Forms starter: Microsoft's [Windows Forms overview](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/overview/) documents the desktop UI framework. Its [thread-safe control guide](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls) requires controls to be created/accessed on their UI thread, distinguishes synchronous Invoke from posting InvokeAsync (.NET 9+), and explains InvokeRequired. Use the starter's button/input handlers on the UI thread; explicitly marshal any future background completion. Keep an observer's IDisposable alive for the entire Application.Run lifetime, then dispose it. An FDA AddCallback invokes the initial action synchronously before registration returns outside a transaction, so create controls before subscribing.

Inference: FDA fits desktop apps needing a cached dependency graph over changing values and collections. Desktop GUI choice alone does not distinguish it from Ranvier. Identify Ranvier-specific ergonomics/semantics from master, compare actual behaviors, and acknowledge where both work.

The examined core FDA guide/examples are not a complete desktop starter. A Ranvier article can fill that gap with a toolkit shell, installation commands, a minimal graph, input handlers, initial render, UI-thread observation, cleanup and a run command. Follow with workflows for search, collections, async loading and background work, distinguishing application/toolkit responsibilities from adaptive-library APIs.
