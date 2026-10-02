---
title: Async and pending
---

Async state travels through the graph alongside values. A pending read suspends its reader;
when the source settles, the reader runs again from the start. Boundaries turn pending and
failed states into values your application can display.

- [Pending and failures](pending.md): `Status`, `Reading`, tracked suspension and error propagation.
- [Async sources](async-sources.md): publish values or failures from external work.
- [Async memos](async-memos.md): tracked task bodies, flight policies and previous results.
- [Boundaries](boundaries.md): loading fallbacks, error recovery and stale results during refresh.
- [Threading and dispatch](threading.md): apply completions on the graph's owning thread.
- [Testing async state](testing.md): decide exactly when each flight completes.

Start with [Signals](signals.fsx), [Memos](memos.md) and [Effects](effects.md) for the synchronous
primitives. For async collection rows, see [Projections](projections.fsx#pending-and-failed-rows).
