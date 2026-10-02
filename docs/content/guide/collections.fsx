(**
---
title: Collections
---
*)
(**
Give each collection row its own reactive value, so an edit updates the readers of that row.
Use a lookup for arbitrary requested keys and a selector for selection membership.

- [Projections](projections.fsx): keyed and index rows, factory lifetimes, pending rows and UI lists.
- [Lookups](lookups.fsx): one derived value per requested key.
- [Selectors](selectors.fsx): selection membership that wakes only the two ends of a change.
- [Deep and keyed updates](collection-updates.fsx): immutable updates and focused reads.
- [Collection views](collection-views.fsx): filtering, mapping, sorting, grouping and paging.
- [Aggregates](aggregates.fsx): incremental totals, counts and folds over rows.

Start with [Signals](signals.fsx) and [Memos](memos.md). For loading and errors, see
[Async and pending](async-and-pending.md).
*)
