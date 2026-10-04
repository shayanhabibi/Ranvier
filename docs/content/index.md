---
title: Ranvier
description: Fine-grained reactive computation for .NET.
layout: splash
---

<section class="rv-hero">
<div class="rv-hero__copy">
<span class="rv-hero__status"><span class="rv-badge">Preview</span> APIs may change.</span>
<h1 class="rv-hero__title">ranvier</h1>
<p class="rv-hero__line">Fine-grained reactive computation for .NET.</p>
<p class="rv-hero__sub">Signals, memos and effects where loading and failure are part of the graph. Pending and failed states propagate through dependent <code>Value</code> reads to a boundary, so loading UI can be derived from the graph. Glitch-free, owned and inspired by <a href="https://www.solidjs.com/blog/solid-2-0-rc-the-big-reveal">Solid</a>.<br/>A traced build lets people and <b>agents</b> ask why anything ran; an untraced release build compiles the tracing out.</p>
<div class="rv-hero__actions">
<a class="rv-btn rv-btn--primary" href="/Ranvier/guide/getting-started/">Get started <svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg></a>
<a class="rv-btn rv-btn--secondary" href="/Ranvier/guide/async-and-pending/">Async and pending</a>
</div>
</div>
<figure class="rv-demo" data-rv-demo aria-label="A boundary moving through its states as its source settles, fails and settles again">
<div class="rv-demo__code">
<div class="rv-demo__tabs" role="tablist" aria-label="Language">
<button type="button" role="tab" id="rv-demo-tab-csharp" aria-controls="rv-demo-csharp" aria-selected="true" data-lang="csharp">C#</button>
<button type="button" role="tab" id="rv-demo-tab-fsharp" aria-controls="rv-demo-fsharp" aria-selected="false" tabindex="-1" data-lang="fsharp">F#</button>
</div>
<pre id="rv-demo-fsharp" role="tabpanel" aria-labelledby="rv-demo-tab-fsharp" data-lang="fsharp" hidden><code><span class="k">use</span> graph = <span class="k">new</span> Graph ()
<span class="k">use</span> _ = graph.Activate ()
<span></span>
<span class="k">let</span> price = createAsyncSource&lt;<span class="t">int</span>&gt; ()
<span class="k">let</span> total = createMemo (<span class="k">fun</span> _ -&gt; price.Value * <span class="n">3</span>)
<span class="k">let</span> view =
    createBoundary
        (<span class="k">fun</span> _ -&gt; <span class="s">"Loading…"</span>)
        (<span class="k">fun</span> ex _ -&gt; <span class="s">"Unavailable: "</span> + ex.Message)
        (<span class="k">fun</span> () -&gt; sprintf <span class="s">"Total %d"</span> total.Value)
<span></span>
<span class="rv-demo__step" data-step="0"><span class="c">// view: Fallback</span></span>
<span class="rv-demo__step" data-step="1">price.Settle <span class="n">4</span></span>
<span class="rv-demo__step" data-step="2">price.Fail (exn <span class="s">"feed offline"</span>)</span>
<span class="rv-demo__step" data-step="3">price.Settle <span class="n">5</span></span></code></pre>
<pre id="rv-demo-csharp" role="tabpanel" aria-labelledby="rv-demo-tab-csharp" data-lang="csharp"><code><span class="k">using var</span> graph = <span class="k">new</span> Graph();
<span class="k">using var</span> _ = graph.Activate();
<span></span>
<span class="k">var</span> price = AsyncSource&lt;<span class="t">int</span>&gt;();
<span class="k">var</span> total = Memo(() =&gt; price.Value * <span class="n">3</span>);
<span class="k">var</span> view = Boundary(
    () =&gt; <span class="s">$"Total {total.Value}"</span>,
    () =&gt; <span class="s">"Loading…"</span>,
    ex =&gt; <span class="s">$"Unavailable: {ex.Message}"</span>);
<span></span>
<span class="rv-demo__step" data-step="0"><span class="c">// view: Fallback</span></span>
<span class="rv-demo__step" data-step="1">price.Settle(<span class="n">4</span>);</span>
<span class="rv-demo__step" data-step="2">price.Fail(<span class="k">new</span> Exception(<span class="s">"feed offline"</span>));</span>
<span class="rv-demo__step" data-step="3">price.Settle(<span class="n">5</span>);</span></code></pre>
</div>
<div class="rv-demo__out" aria-live="polite">
<div class="rv-demo__label">view.TryValue</div>
<ol class="rv-demo__frames">
<li data-step="0" class="is-active"><span class="rv-state rv-state--fallback">Fallback</span><code>Ready "Loading…"</code><small>IsWaiting = true</small></li>
<li data-step="1"><span class="rv-state rv-state--ready">Ready</span><code>Ready "Total 12"</code><small>IsWaiting = false</small></li>
<li data-step="2"><span class="rv-state rv-state--recovered">Recovered</span><code>Ready "Unavailable: feed offline"</code><small>Caught ≠ null</small></li>
<li data-step="3"><span class="rv-state rv-state--ready">Ready</span><code>Ready "Total 15"</code><small>Caught = null</small></li>
</ol>
</div>
<figcaption>Output from running this code against Ranvier <code>ad2d84d</code>.</figcaption>
</figure>
</section>

## States you can name

Every reader observes one of six states. The docs, the marks and the API use the same names. Hover a state to hold the map in it.

```fsharp solid setup
open Browser
open Browser.Types
open Partas.Solid.Svg

[<Import("animate", "animejs")>]
let animate (targets: obj) (parameters: obj) : obj = jsNative

[<Import("utils", "animejs")>]
let animeUtils: obj = jsNative

let tween (target: Element) (parameters: obj) = animate target parameters |> ignore

/// A state as a signal map draws it: the values and map classes of an async source and the boundary reading it.
type DialState =
    {
        Source: string
        SourceClass: string
        View: string
        ViewClass: string
        Flight: bool
    }

/// The six states, in the order a flight moves through them and the state list presents them.
let dialStates =
    [|
        { Source = ""; SourceClass = "is-pending"; View = ""; ViewClass = "is-pending"; Flight = true }
        { Source = ""; SourceClass = "is-pending"; View = "Loading…"; ViewClass = "is-waiting"; Flight = true }
        { Source = "4"; SourceClass = ""; View = "Total 12"; ViewClass = ""; Flight = false }
        { Source = "4"; SourceClass = "is-pending"; View = "Total 12"; ViewClass = "is-pending"; Flight = true }
        { Source = "error"; SourceClass = "is-failed"; View = "Total 12"; ViewClass = "is-failed"; Flight = false }
        { Source = "error"; SourceClass = "is-failed"; View = "Unavailable"; ViewClass = ""; Flight = false }
    |]

/// A two-node signal map, an async source and the boundary reading it, beside the list of states. The map steps
/// through the states in turn; hovering, focusing or clicking a row holds the map in that row's state.
[<SolidComponent>]
let StateDial () =
    let mutable root: HTMLDivElement = JS.undefined
    let mutable step = 0
    let mutable timer = 0.0
    let interval = 2400
    let reduced () : bool = window?matchMedia("(prefers-reduced-motion: reduce)")?matches
    let find (selector: string) : Element = root.querySelector selector

    /// Sets a node's state class and value. A changed value drops into place unless instant.
    let paint (node: Element) (state: string) (value: string) (instant: bool) =
        for c in [| "is-pending"; "is-waiting"; "is-failed" |] do
            node.classList.toggle (c, (c = state)) |> ignore

        let label = node.querySelector ".rv-map-node__value"

        if label.textContent <> value then
            label.textContent <- value

            if not instant then
                tween label {| translateY = {| from = -6; ``to`` = 0 |}; scale = {| from = 0.7; ``to`` = 1 |}; duration = 420; ease = "outBack(2)" |}

    let pulse (shape: Element) (delay: int) =
        tween shape {| scale = 1.14; duration = 220; delay = delay; alternate = true; loop = 1; ease = "outQuad" |}

    let shake (shape: Element) (delay: int) =
        tween shape {| translateX = [| box -3; 3; -2; 2; 0 |]; duration = 400; delay = delay; ease = "inOutSine" |}

    /// Carries a dot along the edge from the source to the boundary.
    let travel (tone: string) =
        let edge = find ".rv-map-edge"
        let dot = find ".rv-dial__dot"
        let length: float = edge?getTotalLength ()
        let at = createObj [ "t" ==> 0.0 ]

        let move () =
            let p = edge?getPointAtLength (at?t * length)
            dot.setAttribute ("cx", string p?x)
            dot.setAttribute ("cy", string p?y)

        dot.setAttribute ("class", $"rv-dial__dot is-moving {tone}")
        move ()

        animate
            at
            (createObj
                [
                    "t" ==> 1.0
                    "duration" ==> 460
                    "ease" ==> "inOutSine"
                    "onUpdate" ==> move
                    "onComplete" ==> fun () -> dot.setAttribute ("class", "rv-dial__dot")
                ])
        |> ignore

    let show (n: int) (instant: bool) =
        let state = dialStates[n]
        let source = find ".rv-dial__source"
        let view = find ".rv-dial__view"
        let ring = find ".rv-dial__source .rv-map-node__flight"
        let rows = root.querySelectorAll ".rv-statelist__row"
        step <- n

        for i in 0 .. rows.length - 1 do
            (rows.item i :?> Element).classList.toggle ("is-active", (i = n)) |> ignore

        paint source state.SourceClass state.Source instant
        ring?style?stroke <- (if n = 4 then "var(--rv-error)" else "")

        if instant then
            paint view state.ViewClass state.View true
            animeUtils?set (ring, {| opacity = (if state.Flight then 1 else 0); scale = 1 |})
        else
            let sourceShape = find ".rv-dial__source .rv-map-node__shape"
            let viewShape = find ".rv-dial__view .rv-map-node__shape"
            // A write reaches the boundary as its dot arrives; a boundary's own change is immediate.
            let arrives = if n = 1 || n = 5 then 0 else 460
            window.setTimeout ((fun () -> if step = n then paint view state.ViewClass state.View false), arrives) |> ignore

            if n = 4 then
                tween ring {| opacity = {| from = 1; ``to`` = 0 |}; scale = {| from = 1; ``to`` = 1.5 |}; duration = 700; ease = "outQuad" |}
            elif state.Flight then
                animeUtils?set (ring, {| scale = 1 |})
                tween ring {| opacity = 1; duration = 250 |}
            else
                tween ring {| opacity = 0; duration = 250 |}

            match n with
            | 0 | 3 -> travel "rv-map-dot"
            | 2 ->
                pulse sourceShape 0
                travel "rv-map-dot rv-map-dot--bright"
                pulse viewShape 460
            | 4 ->
                shake sourceShape 0
                travel "rv-dial__dot--error"
                shake viewShape 460
            | 5 -> pulse viewShape 0
            | _ -> ()

    /// Resumes the cycle. While it runs, the active row's bar fills over the time to the next state.
    let play () =
        window.clearInterval timer

        if not (reduced ()) then
            (find ".rv-statelist").classList.add "is-cycling"
            timer <- window.setInterval ((fun () -> show ((step + 1) % dialStates.Length) false), interval)

    /// Holds the map in the state of the row containing the target.
    let hold (target: obj) =
        let row: Element = target?closest (".rv-statelist__row")

        if not (isNull row) then
            let rows = root.querySelectorAll ".rv-statelist__row"
            window.clearInterval timer
            (find ".rv-statelist").classList.remove "is-cycling"

            for i in 0 .. rows.length - 1 do
                if obj.ReferenceEquals (rows.item i, row) && i <> step then
                    show i (reduced ())

    /// Resumes the cycle when the pointer or focus moves to a target outside the list.
    let release (next: Node) =
        let list = find ".rv-statelist"

        if isNull next || not (list.contains next) then
            play ()

    onSettled (fun () ->
        (find ".rv-dial__map").setAttribute ("aria-hidden", "true")
        root?style?setProperty ("--rv-dial-step", $"{interval}ms")

        if reduced () then
            show 2 true
        else
            tween (find ".rv-dial__source .rv-map-node__flight") {| rotate = 360; duration = interval; loop = true; ease = "linear" |}
            show 0 true
            play ())

    onCleanup (fun () -> window.clearInterval timer)

    div(class' = "rv-dial").ref (root) {
        svg (class' = "rv-dial__map", viewBox = "0 0 200 92") {
            path (class' = "rv-map-edge", d = "M62 48L136 48")
            g (class' = "rv-map-node rv-map-node--async rv-dial__source") {
                circle (class' = "rv-map-node__flight", cx = 40.0, cy = 48.0, r = 25.0)
                circle (class' = "rv-map-node__shape", cx = 40.0, cy = 48.0, r = 17.0)
                text (class' = "rv-map-node__name", x = 40.0, y = 88.0) { "price" }
                text (class' = "rv-map-node__value", x = 40.0, y = 16.0)
            }
            g (class' = "rv-map-node rv-map-node--memo rv-dial__view") {
                rect (class' = "rv-map-node__shape", x = 137.0, y = 33.0, width = 46.0, height = 30.0, rx = 9.0)
                text (class' = "rv-map-node__name", x = 160.0, y = 88.0) { "view" }
                text (class' = "rv-map-node__value", x = 160.0, y = 22.0)
            }
            circle (class' = "rv-dial__dot", cx = 62.0, cy = 48.0, r = 4.5)
        }
        div (
            class' = "rv-statelist",
            onMouseOver = (fun e -> hold e?target),
            onFocusIn = (fun e -> hold e?target),
            onClick = (fun e -> hold e?target),
            onMouseLeave = (fun e -> release e?relatedTarget),
            onFocusOut = (fun e -> release e?relatedTarget)
        ) {
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Pending" }
                p (innerHTML = "No usable value yet. <code>TryValue</code> returns <code>Pending</code>, and the pending flag propagates to readers.")
            }
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Fallback" }
                p (innerHTML = "A boundary shows its fallback while its body is pending. <code>IsWaiting</code> is true.")
            }
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Ready" }
                p (innerHTML = "A settled value. <code>TryValue</code> returns <code>Ready</code>.")
            }
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Retained value" }
                p (innerHTML = "Pending, but <code>Peek</code> still returns the last settled value.")
            }
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Failed" }
                p (innerHTML = "A read raised. <code>TryValue</code> returns <code>Failed</code>.")
            }
            div (class' = "rv-statelist__row", tabindex = 0) {
                strong () { "Recovered" }
                p (innerHTML = "A boundary shows <code>recover ex</code>. <code>Caught</code> holds the error until a re-run succeeds.")
            }
        }
    }
```

```fsharp solid show=inline
StateDial ()
```

## Watch the graph think

A cart changes faster than its shipping service can answer. Watch debounce hold a request until the edits stop, trailing throttle refresh a preview at its fixed deadline, and a superseded quote drop. Switch to pickup and the total stops reading shipping: that edge disappears.

This runs the real engine compiled to JavaScript with tracing on. Start with **Rapid cart edits**, then advance the example clock by **20 ms** and **80 ms**. The preview moves first; shipping waits for quiet. Fail the quote, switch to pickup, then retry delivery and settle it. Hover a node for its state; click it for why it last ran. **Play**, **Step** and the scrub bar replay your recorded events. [How to read a map](guide/signal-maps.md#reading-a-map).

```fsharp map timeline
let graph = Graph.Current
let clock = MapClock()
let options = { Clock = clock; Comparer = None }
let interval = System.TimeSpan.FromMilliseconds 100.
let desk = Desk<int>()
let qty = createSignal 1
let pickup = createSignal false
let subtotal = createMemo (fun _ -> 4 * qty.Value)
let admitted = debounceWith options interval (fun () -> subtotal.Value) graph
let preview = throttleLastWith options interval (fun () -> subtotal.Value) graph
let shipping = createAsync (fun _ _ -> desk.Quote admitted.Value)
let total =
    createMemo (fun _ ->
        subtotal.Value + (if pickup.Value then 0 else shipping.Value))

let view =
    createBoundary
        (fun _ -> "Loading…")
        (fun _ _ -> "Quote offline")
        (fun () -> sprintf "Total %d" total.Value)

createEffect (fun () -> printfn "%s / preview %d" view.Value preview.Value)

controls [
    button "Rapid cart edits" (fun () ->
        qty.Value <- 2
        clock.Advance 40.
        qty.Value <- 3
        clock.Advance 40.
        qty.Value <- 4)
    |> describe "Three captures. Both timed outputs still publish 4; shipping has one request."
    |> expect "captures do not start shipping requests" (fun () -> admitted.Peek = 4 && preview.Peek = 4 && shipping.Runs = 1)
    button "Advance 20 ms" (fun () -> clock.Advance 20.)
    |> describe "At 100 ms the preview publishes 16. Debounce's quiet period ends at 180 ms."
    |> expect "throttle moves before debounce" (fun () -> preview.Peek = 16 && admitted.Peek = 4 && shipping.Runs = 1)
    button "Advance 80 ms" (fun () -> clock.Advance 80.)
    |> describe "Debounce admits 16. A new shipping flight supersedes the initial quote."
    |> expect "one new request follows the burst" (fun () -> admitted.Peek = 16 && shipping.Runs = 2 && desk.Pending = 1)
    button "Fail quote" (fun () -> desk.Fail "service offline")
    |> describe "The boundary turns the shipping failure into a visible fallback."
    |> expect "failure reaches the boundary" (fun () -> view.Peek = "Quote offline")
    toggleSignal "Pickup" pickup [ true ]
    |> describe "Pickup skips shipping. The dependency edge disappears and the total recovers."
    |> expect "pickup ignores the failed quote" (fun () -> view.Peek = "Total 16")
    button "Retry delivery" (fun () ->
        pickup.Value <- false
        qty.Value <- 5
        clock.Advance 100.)
    |> describe "Delivery reads shipping again. A changed cart starts a fresh quote after quiet."
    |> expect "delivery waits for the new quote" (fun () -> not pickup.Peek && admitted.Peek = 20 && view.Peek = "Loading…" && desk.Pending = 1)
    button "Settle quote" (fun () -> desk.Settle 5)
    |> describe "The latest shipping quote settles. The boundary publishes the delivered total."
    |> expect "delivery recovers" (fun () -> view.Peek = "Total 25" && preview.Peek = 20)
]
```

<p class="rv-map-edit"><a href="/Ranvier/guide/timing/">Explore debounce and throttle</a> · <a href="/Ranvier/guide/signal-maps/#edit-a-map">Build your own map in the browser</a></p>

<div class="rv-trace">
<p class="rv-trace__lead">The same log answers questions a call stack cannot. An untraced build compiles it out, IL for IL.</p>
<div class="rv-trace__grid">
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#why-did-it-run"><span>Why did this run?</span><code>Trace.why</code></a>
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#why-did-it-not-run"><span>Why did this not run?</span><code>Trace.whyNot</code></a>
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#what-did-each-run-do"><span>What did each run do?</span><code>Trace.history</code></a>
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#what-is-it-waiting-on"><span>What is it waiting on?</span><code>Trace.waitingOn</code></a>
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#where-did-it-come-from"><span>Where did this node come from?</span><code>Trace.origin</code></a>
<a class="rv-trace__q" href="/Ranvier/guide/tracing/#what-the-graph-looks-like"><span>What does the graph look like?</span><code>Trace.snapshot</code></a>
</div>
<a class="rv-trace__more" href="/Ranvier/guide/tracing/">Read the tracing guide <svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg></a>
</div>

## Why Ranvier

The parts of reactive state that .NET developers most often rebuild by hand, built into the library.

<div class="rv-cards">
<a class="rv-card" href="/Ranvier/guide/pending/"><strong class="rv-card__title">Loading and errors, derived</strong><p>Pending and failure travel on their own channel. They pass from an async source through every memo that reads it to the nearest boundary, which shows a fallback or a recovered value. A flight policy decides what happens to superseded work: cancel it, keep only the latest, queue it, or finish it and run once more. During a refresh <code>Peek</code> keeps the last value.</p></a>
<a class="rv-card" href="/Ranvier/concepts/contracts/#error-recovery"><strong class="rv-card__title">Recoverable error state</strong><p>A failed node stores an exception; a boundary can show a recovered value. Dependencies read during the failed run remain tracked, and a later successful run clears the failure. <code>ErrorOrigin</code> names the node it started in.</p></a>
<a class="rv-card" href="/Ranvier/concepts/contracts/#ownership"><strong class="rv-card__title">Owned lifetimes</strong><p>Memos, effects and projection rows belong to owners. Disposing an owner disposes its nodes in a defined order. Register external resources with <code>onCleanup</code>, and dispose the enclosing scope when its lifetime ends.</p></a>
<a class="rv-card" href="/Ranvier/concepts/ecosystem/#diamonds-without-glitches"><strong class="rv-card__title">Glitch-free diamonds</strong><p>Derived reads settle both paths from a shared source before combining them. Use <code>batch</code> when several source writes belong to one update.</p></a>
<a class="rv-card" href="/Ranvier/guide/csharp/#binding-to-xaml"><strong class="rv-card__title">Built for C# and XAML</strong><p><code>ReactiveBindings</code> raises <code>PropertyChanged</code> for derived properties with no dependency attributes. <code>ReactiveCommand</code> derives <code>CanExecute</code> and <code>IsRunning</code> from the graph. C# callers need no F# option types.</p></a>
<a class="rv-card" href="/Ranvier/guide/projections/#reading-changes"><strong class="rv-card__title">Incremental collections</strong><p>Editable keyed sources update rows directly. Map membership consumes key deltas; value readers report unequal settled rows. A bound <code>ObservableCollection</code> applies individual changes after population, with resets for initialization and recovery. The guide documents which view paths still scan or sort keys.</p></a>
<a class="rv-card" href="/Ranvier/concepts/contracts/#threading"><strong class="rv-card__title">A written threading contract</strong><p>A graph checks that it is called from its own thread, and the contract lists which calls may come from other threads. <code>Serialised</code> mode accepts a Blazor Server circuit's changing threads and raises on genuine concurrency.</p></a>
<a class="rv-card" href="/Ranvier/guide/testing/"><strong class="rv-card__title">Deterministic async tests</strong><p><code>ManualDispatcher</code> and <code>Settle</code> let a test decide when each flight lands. Loading, failure and cancellation are tested with no timers, sleeps or polling.</p></a>
<a class="rv-card" href="/Ranvier/guide/installation/#native-aot-and-trimming"><strong class="rv-card__title">Portable, with no platform package</strong><p>One core for <code>net10.0</code>, <code>net8.0</code> and <code>netstandard2.1</code>, with no UI-framework dependency. The untraced build is Native AOT and trim clean, and the library compiles to JavaScript with Fable.</p></a>
<a class="rv-card" href="/Ranvier/guide/elmish/"><strong class="rv-card__title">Adopt it one view at a time</strong><p><code>Ranvier.Elmish</code> keeps an existing <code>init</code> and <code>update</code> and reads the model through selector memos. Editable values cover forms seeded from upstream data.</p></a>
</div>

<script>
(() => {
  const demo = document.querySelector("[data-rv-demo]");
  if (!demo) return;
  const frames = demo.querySelectorAll(".rv-demo__frames > li");
  const steps = demo.querySelectorAll(".rv-demo__step");
  const tabs = [...demo.querySelectorAll("[role=tab]")];
  const panels = demo.querySelectorAll("[role=tabpanel]");
  let step = 0, lang = 0, timer = 0;
  const show = (n) => {
    step = n;
    for (const f of frames) f.classList.toggle("is-active", +f.dataset.step === n);
    for (const s of steps) s.classList.toggle("is-active", +s.dataset.step === n);
  };
  const pick = (n, focus) => {
    lang = n;
    tabs.forEach((t, i) => {
      t.setAttribute("aria-selected", String(i === n));
      t.tabIndex = i === n ? 0 : -1;
      if (i === n && focus) t.focus();
    });
    for (const p of panels) p.hidden = p.dataset.lang !== tabs[n].dataset.lang;
  };
  // Each wrap back to the first step also moves to the next language.
  const tick = () => {
    const next = (step + 1) % frames.length;
    if (next === 0) pick((lang + 1) % tabs.length);
    show(next);
  };
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const start = () => { if (!reduce && !timer) timer = setInterval(tick, 2400); };
  const stop = () => { clearInterval(timer); timer = 0; };
  for (const el of [...frames, ...steps]) {
    el.addEventListener("mouseenter", () => show(+el.dataset.step));
  }
  demo.addEventListener("mouseenter", stop);
  demo.addEventListener("focusin", stop);
  demo.addEventListener("mouseleave", () => { if (!demo.contains(document.activeElement)) start(); });
  demo.addEventListener("focusout", (e) => { if (!demo.contains(e.relatedTarget) && !demo.matches(":hover")) start(); });
  tabs.forEach((t, i) => {
    t.addEventListener("click", () => pick(i));
    t.addEventListener("keydown", (e) => {
      const d = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
      if (d) pick((i + d + tabs.length) % tabs.length, true);
    });
  });
  pick(0);
  show(0);
  start();
})();
</script>
