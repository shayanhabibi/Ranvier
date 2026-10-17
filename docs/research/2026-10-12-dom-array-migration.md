# DOM keyed snapshot migration

The DOM Foundation keyed interpreter now uses arrays for normalized snapshots, retained entries, matching and removal. Reverse placement walks indices without allocating a reversed collection. `Keyed.array` accepts array readers; `Keyed.list` remains an authoring adapter. Branch selection constructs a singleton array. Core Ranvier is unchanged.

Array slots are copied before user key callbacks and before backend comparer callbacks. These copies protect separate inputs: caller item arrays and low-level backend snapshots. Copies are shallow. Existing immutable view children/modifier lists remain authoring structures; this change targets reactive keyed snapshots and reconciliation storage.

Two new regressions cover key/comparer mutation of unread input slots. They exposed an initial double read: the runtime previously read and validated once before creating the tracked effect, then read again inside it. Initial reconciliation now takes one tracked snapshot, validates before attaching any rows and preserves dependency tracking. The existing initial reader/comparer disposal and duplicate rejection tests pass.

The frozen binding benchmark receives a single array-to-list compatibility adapter for the changed experimental Backend signature; its reconciliation implementation remains list-based. No new performance experiment was run. The prior [snapshot evidence](2026-10-11-branches-and-snapshots.md) motivated this implementation.

Acceptance: full playground suite passes 166 Node cases and 18 Chromium tests; Release DomTests build reports zero warnings/errors; portable .NET representation executable passes all six groups; XML documentation audit reports zero findings. Production JavaScript is 81.20 kB / 22.61 kB gzip. Scope remains the experimental renderer, with quadratic key lookup and broader framework gates still open.
