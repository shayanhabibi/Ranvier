---
title: Instruction counts
order: 8
---

> **Preview** — Ranvier is pre-release; its APIs and these figures may change.

The counter bench measures whole scenarios rather than single primitives: creating and disposing
rows, propagating writes, projections, application-shaped workloads, graph shapes and async
boundaries. Each figure is retired processor instructions per operation, `(m(2N) − m(N)) / N`,
read from hardware counters. Instruction counts are steadier than time, so a change of more than
about 5 % is a real change in work done.

Each panel scales from zero to its largest bar, so bar lengths compare engines within a scenario,
not scenarios with each other.

## .NET

.NET 10.0.12, with tiered compilation, PGO and ReadyToRun off. Ranvier at `d87920f`,
FSharp.Data.Adaptive 1.2.27, R3 1.3.1.

![Instructions per operation under .NET](/Ranvier/benchmarks/counters-dotnet.svg)

## Fable under Node.js

Node.js v26.7.0 (`--expose-gc --single-threaded`), fable-library-js 5.18.0. Ranvier at `d87920f`,
Fable.Ripple 1.0.0-beta.5. Bars are main-thread figures.

![Instructions per operation under Node.js](/Ranvier/benchmarks/counters-node.svg)

Scenarios with a negative figure (`derive-effect`, `derive-on`, `shape-dynamic`, `async-recover`)
do less work per operation than Node's run-to-run noise and show no bar.

## Reading the charts

- **Engines differ in semantics.** R3 pushes each write straight to its subscribers, without
  batching or glitch-free ordering. FSharp.Data.Adaptive disposal removes callback subscriptions
  only.
- **.NET and Node figures do not compare.** The .NET worker runs with PGO off; V8 optimises
  adaptively.
The full report, with cycles, branch misses, allocations and library counters per scenario, is
[`docs/.ai/benchmarks/counters/d87920f.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/d87920f.md)
in the repository.
