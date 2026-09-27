---
title: Ranvier
description: Fine-grained reactive computation for .NET.
layout: splash
---

<section class="rv-hero">
<span class="rv-hero__status"><span class="rv-badge">Preview</span> Pre-release. APIs follow Partas.Signals and may change.</span>
<h1><img class="rv-hero__logo rv-only-dark" src="/Ranvier/brand/logos/ranvier-gradient-logo-dark.svg" alt="Ranvier" width="880" height="275"><img class="rv-hero__logo rv-only-light" src="/Ranvier/brand/logos/ranvier-gradient-logo-light.svg" alt="Ranvier" width="880" height="275"></h1>
<p class="rv-hero__line">Fine-grained reactive computation for .NET.</p>
<p class="rv-hero__sub">Signals, memos and effects in F#, with a pending channel that lets a value be in flight while its dependents keep reading it.</p>
<div class="rv-hero__actions">
<a class="rv-btn rv-btn--primary" href="/Ranvier/guide/">Read the guide <svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg></a>
<a class="rv-btn rv-btn--secondary" href="/Ranvier/concepts/">Concepts</a>
</div>
</section>

## A first graph

A graph owns its nodes. Memos recompute when what they read changes, and effects re-run after them.

<div class="rv-taste">

```fsharp
open Ranvier

let graph = new Graph ()

let doubled =
    graph.Run (fun () ->
        let count = createSignal 1
        let doubled = createMemo (fun () -> count.Value * 2)
        createEffect (fun () -> printfn "doubled = %d" doubled.Value)
        count.Value <- 5
        doubled)
```

</div>

## What it provides

A dependency graph with explicit ownership, plus a second channel for values that have not arrived yet.

<div class="rv-cards">
<a class="rv-card rv-card--wide" href="/Ranvier/concepts/"><span class="rv-card__icon"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg></span><strong class="rv-card__title">The pending channel</strong><p>Async sources and async memos mark a node as in flight, and its dependents read the pending flag alongside the value. Suspense and error boundaries catch the pending or failed state their body reads and substitute a fallback or a recovered value.</p><span class="rv-states"><span class="rv-state rv-state--ready">Ready</span><span class="rv-state rv-state--pending">Pending</span><span class="rv-state rv-state--retained">Retained value</span><span class="rv-state rv-state--fallback">Fallback</span><span class="rv-state rv-state--failed">Failed</span><span class="rv-state rv-state--recovered">Recovered</span></span></a>
<a class="rv-card" href="/Ranvier/guide/getting-started/#memos"><span class="rv-card__icon"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8.75 4.5A7.75 7.75 0 1 1 6 17.75"/><circle cx="4" cy="11.25" r="1.75" fill="currentColor" stroke="none"/></svg></span><strong class="rv-card__title">Signals, memos, effects</strong><p>Dependencies are tracked as computations run. Propagation is glitch-free (tested on the diamond case in the guide), and an equality cutoff stops it when a recomputed value is unchanged. Every memo and effect belongs to an owner that disposes it.</p></a>
<a class="rv-card" href="/Ranvier/guide/collections/#selectors"><span class="rv-card__icon"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="4" rx="1"/><rect x="3" y="10" width="18" height="4" rx="1"/><rect x="3" y="16" width="18" height="4" rx="1"/></svg></span><strong class="rv-card__title">Collections and projections</strong><p>Keyed and index projections give each row its own reactive value. Lookups derive a value per key, and selectors test membership: a selection change wakes the readers of the previous and new key.</p></a>
<a class="rv-card" href="/Ranvier/fable/"><span class="rv-card__icon"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg></span><strong class="rv-card__title">Fable target <span class="rv-badge">Planned</span></strong><p>The library targets <code>net10.0</code>. A Fable/JavaScript target is planned; see the Fable page for its status.</p></a>
</div>
