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
<pre><code><span class="k">let</span> price = createAsyncSource&lt;<span class="t">decimal</span>&gt; ()
<span class="k">let</span> total = createMemo (<span class="k">fun</span> () -&gt; price.Value * <span class="n">3m</span>)
<span class="k">let</span> view =
    createBoundary
        (<span class="k">fun</span> () -&gt; <span class="s">"Loading…"</span>)
        (<span class="k">fun</span> ex -&gt; <span class="s">"Unavailable: "</span> + ex.Message)
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
<figcaption>Output from running this code against Partas.Signals <code>915f139</code>.</figcaption>
</figure>
</section>

## States you can name

Every reader observes one of six states. The docs, the marks and the API use the same names.

<div class="rv-statelist">
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--ready"></span><strong>Ready</strong><p>A settled value. <code>TryValue</code> returns <code>Ready</code>.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--pending"></span><strong>Pending</strong><p>No usable value yet. <code>TryValue</code> returns <code>Pending</code>, and the pending flag propagates to readers.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--retained"></span><strong>Retained value</strong><p>Pending, but <code>Peek</code> still returns the last settled value.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--fallback"></span><strong>Fallback</strong><p>A boundary shows its fallback while its body is pending. <code>IsWaiting</code> is true.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--failed"></span><strong>Failed</strong><p>A read raised. <code>TryValue</code> returns <code>Failed</code>.</p></div>
<div class="rv-statelist__row"><span class="rv-statelist__mark rv-state rv-state--plain rv-state--recovered"></span><strong>Recovered</strong><p>A boundary shows <code>recover ex</code>. <code>Caught</code> holds the error until a re-run succeeds.</p></div>
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
