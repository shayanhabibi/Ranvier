---
title: Ranvier
description: Fine-grained reactive computation for .NET.
layout: splash
---

<section class="rv-hero">
<div class="rv-hero__copy">
<span class="rv-hero__status"><span class="rv-badge">Preview</span> APIs follow Partas.Signals and may change.</span>
<h1 class="rv-hero__title">ranvier</h1>
<p class="rv-hero__line">Fine-grained reactive computation for .NET.</p>
<p class="rv-hero__sub">Signals, memos and effects in F#, with a second channel for values that have not arrived. A boundary shows a fallback while its inputs are in flight and a recovered value when one fails.</p>
<div class="rv-hero__actions">
<a class="rv-btn rv-btn--primary" href="/Ranvier/guide/getting-started/">Get started <svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg></a>
<a class="rv-btn rv-btn--secondary" href="/Ranvier/guide/async-and-pending/">Async and pending</a>
</div>
</div>
<figure class="rv-demo" data-rv-demo aria-label="A boundary moving through its states as its source settles, fails and settles again">
<div class="rv-demo__code">
<pre><code><span class="k">use</span> graph = <span class="k">new</span> Graph ()
<span class="k">use</span> _ = graph.Activate ()
<span></span>
<span class="k">let</span> price = createAsyncSource&lt;<span class="t">decimal</span>&gt; ()
<span class="k">let</span> total = createMemo (<span class="k">fun</span> _ -&gt; price.Value * <span class="n">3m</span>)
<span class="k">let</span> view =
    createBoundary
        (<span class="k">fun</span> _ -&gt; <span class="s">"Loading…"</span>)
        (<span class="k">fun</span> ex _ -&gt; <span class="s">"Unavailable: "</span> + ex.Message)
        (<span class="k">fun</span> () -&gt; sprintf <span class="s">"Total %M"</span> total.Value)
<span></span>
<span class="rv-demo__step" data-step="0"><span class="c">// view: Fallback</span></span>
<span class="rv-demo__step" data-step="1">price.Settle <span class="n">4m</span></span>
<span class="rv-demo__step" data-step="2">price.Fail (exn <span class="s">"feed offline"</span>)</span>
<span class="rv-demo__step" data-step="3">price.Settle <span class="n">5m</span></span></code></pre>
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

Every reader observes one of six states. The docs, the marks and the API use the same names.

```fsharp solid setup
open Browser
open Browser.Types
open Partas.Solid.Svg

[<Import("createTimeline", "animejs")>]
let createTimeline (parameters: obj) : obj = jsNative

[<Import("utils", "animejs")>]
let animeUtils: obj = jsNative

/// A state's key, label, caption and the glyphs of its mark, in the order a flight moves through them.
let stateFlow =
    [|
        "pending", "Pending", "An async source starts a flight.", ".rv-arc__ring"
        "fallback", "Fallback", "A boundary shows its fallback while its body waits.", ".rv-arc__ring, .rv-arc__bar"
        "ready", "Ready", "The flight settles and readers get the value.", ".rv-arc__pulse"
        "retained", "Retained value", "A new flight starts. Peek still returns the last value.", ".rv-arc__ring, .rv-arc__value"
        "failed", "Failed", "The flight fails and the read raises.", ".rv-arc__x"
        "recovered", "Recovered", "A boundary shows its recovered value.", ".rv-arc__x, .rv-arc__value"
    |]

/// The state mark, turning a sixth of a revolution clockwise at each transition.
[<SolidComponent>]
let StateDial () =
    let step, setStep = createSignal 0
    let mutable root: HTMLDivElement = JS.undefined
    let mutable timer = 0.0

    let entry () = stateFlow[step () % stateFlow.Length]
    let name () = let _, name, _, _ = entry () in name
    let note () = let _, _, note, _ = entry () in note

    let show (n: int) (instant: bool) =
        let _, _, _, on = stateFlow[n % stateFlow.Length]
        let turn = root.querySelector ".rv-arc__turn"
        let term = root.querySelector ".rv-arc__term"
        let incoming = root.querySelectorAll on
        let outgoing = root.querySelectorAll $".rv-arc__g:not({on})"
        let degrees = n * 60
        setStep n
        if instant then
            animeUtils?set (turn, {| rotate = degrees |})
            animeUtils?set (term, {| rotate = -degrees |})
            animeUtils?set (outgoing, {| opacity = 0 |})
            animeUtils?set (incoming, {| opacity = 1 |})
        else
            let timeline = createTimeline {| defaults = {| ease = "inOutQuart" |} |}
            timeline?add (outgoing, {| opacity = 0; duration = 200 |}, 0)
            timeline?add (turn, {| rotate = degrees; duration = 700 |}, 0)
            timeline?add (term, {| rotate = -degrees; duration = 700 |}, 0)
            timeline?add (incoming, {| opacity = {| from = 0; ``to`` = 1 |}; scale = {| from = 0.6; ``to`` = 1 |}; duration = 350; ease = "outBack(2)" |}, 560)

    onSettled (fun () ->
        root.setAttribute ("aria-hidden", "true")
        if window?matchMedia("(prefers-reduced-motion: reduce)")?matches then
            show 2 true
        else
            show 0 true
            timer <- window.setInterval ((fun () -> show (step () + 1) false), 2400))

    onCleanup (fun () -> window.clearInterval timer)

    div(class' = "rv-dial").ref (root) {
        svg (class' = "rv-arc", viewBox = "0 0 96 96") {
            g (class' = "rv-arc__turn") {
                path (class' = "rv-arc__frame", d = "M35 18A31 31 0 1 1 24 71")
                g (class' = "rv-arc__term") {
                    circle (class' = "rv-arc__g rv-arc__pulse", cx = 16.0, cy = 45.0, r = 7.0)
                    circle (class' = "rv-arc__g rv-arc__ring", cx = 16.0, cy = 45.0, r = 8.0)
                    path (class' = "rv-arc__g rv-arc__x", d = "m10 39 12 12m0-12L10 51")
                }
            }
            circle (class' = "rv-arc__g rv-arc__value", cx = 48.0, cy = 48.0, r = 5.0)
            rect (class' = "rv-arc__g rv-arc__bar", x = 41.0, y = 45.0, width = 14.0, height = 6.0, rx = 3.0)
        }
        div (class' = "rv-dial__label") {
            strong () { name () }
            span () { note () }
        }
    }
```

```fsharp solid show=inline
StateDial ()
```

<div class="rv-statelist">
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--ready"></span><strong>Ready</strong><p>A settled value. <code>TryValue</code> returns <code>Ready</code>.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--pending"></span><strong>Pending</strong><p>No usable value yet. <code>TryValue</code> returns <code>Pending</code>, and the pending flag propagates to readers.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--retained"></span><strong>Retained value</strong><p>Pending, but <code>Peek</code> still returns the last settled value.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--fallback"></span><strong>Fallback</strong><p>A boundary shows its fallback while its body is pending. <code>IsWaiting</code> is true.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--failed"></span><strong>Failed</strong><p>A read raised. <code>TryValue</code> returns <code>Failed</code>.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--recovered"></span><strong>Recovered</strong><p>A boundary shows <code>recover ex</code>. <code>Caught</code> holds the error until a re-run succeeds.</p></div>
</div>

## Watch the graph think

A traced build records every write, mark, run and flight, with the source line that caused it. This is the example from the top of the page, running on the real engine compiled to JavaScript with tracing on. Press a button and follow the event along the edges; hover a node for its state, click it for why it last ran. [How to read a map](guide/signal-maps.md#reading-a-map).

```fsharp map timeline
let price = createAsyncSource<decimal> ()
let total = createMemo (fun _ -> price.Value * 3m)

let view =
    createBoundary
        (fun _ -> "Loading…")
        (fun ex _ -> "Unavailable: " + ex.Message)
        (fun () -> sprintf "Total %M" total.Value)

createEffect (fun () -> printfn "%s" view.Value)

controls [
    "Settle 4", fun () -> price.Settle 4m
    "Fail", fun () -> price.Fail (exn "feed offline")
    "Settle 5", fun () -> price.Settle 5m
]
```

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

## What it provides

A dependency graph with explicit ownership, plus a second channel for values that have not arrived yet.

<div class="rv-cards">
<a class="rv-card" href="/Ranvier/guide/getting-started/#memos"><strong class="rv-card__title">Signals, memos, effects</strong><p>Dependencies are tracked as computations run. Propagation is glitch-free (tested on the diamond case in the guide), and an equality cutoff stops it when a recomputed value is unchanged. Every memo and effect belongs to an owner that disposes it.</p></a>
<a class="rv-card" href="/Ranvier/concepts/suspension/"><strong class="rv-card__title">Suspension and boundaries</strong><p>Async sources and async memos mark a node as in flight. Suspense and error boundaries catch the pending or failed state their body reads and substitute a value of the same type.</p></a>
<a class="rv-card" href="/Ranvier/guide/collections/#selectors"><strong class="rv-card__title">Collections and projections</strong><p>Keyed and index projections give each row its own reactive value. Lookups derive a value per key, and a selection change wakes only the readers of the previous and new key.</p></a>
<a class="rv-card" href="/Ranvier/fable/"><strong class="rv-card__title">Fable target <span class="rv-badge">Planned</span></strong><p>The library targets <code>net10.0</code>. A Fable/JavaScript target is planned; see the Fable page for its status.</p></a>
</div>

<script>
(() => {
  const demo = document.querySelector("[data-rv-demo]");
  if (!demo) return;
  const frames = demo.querySelectorAll(".rv-demo__frames > li");
  const steps = demo.querySelectorAll(".rv-demo__step");
  let step = 0, timer = 0;
  const show = (n) => {
    step = n;
    for (const f of frames) f.classList.toggle("is-active", +f.dataset.step === n);
    for (const s of steps) s.classList.toggle("is-active", +s.dataset.step === n);
  };
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const start = () => { if (!reduce && !timer) timer = setInterval(() => show((step + 1) % frames.length), 2400); };
  const stop = () => { clearInterval(timer); timer = 0; };
  for (const el of [...frames, ...steps]) {
    el.addEventListener("mouseenter", () => { stop(); show(+el.dataset.step); });
  }
  demo.addEventListener("mouseleave", start);
  show(0);
  start();
})();
</script>
