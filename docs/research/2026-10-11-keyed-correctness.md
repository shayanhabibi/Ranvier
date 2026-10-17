# Keyed-list correctness follow-on — 2026-10-11

Continues the [DOM checkpoint](2026-10-11-view-poc-checkpoint.md) from `a765d87` in the DOM worktree. This is a bounded repair of the experimental Foundation keyed renderer; it introduces no generated bindings or public signature changes.

## Contracts and repairs

Initial row setup failures now escape effect error handling and unwind the outer mount. A live duplicate snapshot, failed row setup or failed movement reaches the mount's supplied reporter. Successful rollback preserves the previous rows and DOM identities, and a later valid snapshot can reconcile normally. If rollback cannot restore a usable range, the whole containing mount is disposed so it cannot retain a broken live region. This deliberately trades availability of that mount for resource safety; other mounts on the graph are not selected for disposal.

Disposal is checked immediately after the initial snapshot and around initial/live comparer callbacks. Live reader, setup and movement disposal abort reconciliation through a private cancellation path; ordinary cancellation does not generate an update-error report. No subsequent keyed comparisons or new row setup should run after disposal. Removed-row cleanup can dispose the containing mount without reviving it.

The list effect tracks its snapshot reader. Reconciliation work, equality comparisons, cleanup and error reporting are untracked, so their incidental signal reads cannot subscribe the list. Nested keyed rows retain their native element identities through outer movement, data updates and inner reorder; removal and final disposal run their cleanup once.

A fresh reviewer found two missed combinations. Initial reader/comparer disposal still allowed more validation callbacks; two new tests failed with three comparator calls before guards were added. Failed row setup plus a throwing cleanup produced two reporter calls; the added regression failed before setup cleanup was collected and delivered with the original setup error in one report. Collection is shared through the region reporter, rather than adding a retained reporter closure per row. This does not promise one notification across every independent cleanup failure during a larger teardown.

## Evidence

Seventeen new cases were added to `RangeChecks.allCases`; both existing Node and Chromium runners consume this array. Red runs demonstrated missing error notification, swallowed initial failure, an attached mount after failed rollback, cancellation reported as failure, callbacks continuing after disposal, and unintended subscriptions from reporter/comparer/cleanup reads. Nested movement and several disposal probes already passed and were retained as contract coverage.

Final verification after the review fixes:

- `rtk npm --prefix examples/Fable.Ranvier.Playground test`: 130 Node tests and 16 Chromium tests passed; the range runner executes all 42 range cases in both environments.
- `rtk proxy dotnet build examples/Fable.Ranvier.Playground/tests/DomTests.fsproj --configuration Release`: zero warnings/errors. Fresh scoped FCS check: zero errors/warnings.
- `rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release`: all six portable groups passed.
- `rtk npm --prefix examples/Fable.Ranvier.Playground run build`: passed; JavaScript 71.70 kB, 20.16 kB gzip. These are build sizes, not comparative performance evidence.
- XML documentation audit: zero findings. Formatting and comment-hygiene gates run on the staged F# files during commit.
- `rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release`: 938 F# and 81 C# tests passed on each of net8.0/net10.0, plus AOT smoke. `--quick` skips installation/restoration, not test cases.

The whole-solution Release build was also attempted and still reports the five baseline `Ranvier.Tests.Fable` errors: missing `IcedTasks`, `cancellableTask`, `Expect.isNotNull`, `testSequenced` and `Expect.hasLength`. Existing docs dependency/xUnit warnings remain; overlapping solution/test builds caused one recovered file-copy retry. Whole-solution acceptance remains open.

SageFs's existing worktree session was refreshed, not recreated. A pure probe confirmed a nested owner's effect failure is not listed by `graph.Root.Errors`; root errors therefore cannot substitute for explicit renderer reporting. A successful evaluation was followed by an MCP client's SSE `BrokenResourceError` during connection teardown; this did not invalidate the returned result. DOM regressions run under Fable/Node/Chromium because their native Emit operations are browser-specific. Fable also rejected subclassing `OperationCanceledException` and inspecting `AggregateException.InnerExceptions`; the implementation uses private F# exceptions for internal control flow and reports setup/cleanup errors as the supplied exception list.

## Remaining gates

This closes the tested keyed-list failure/reentry/nested-composition gaps, not framework Task 6 or the public API freeze. Keyed branch selection, structural key/comparer matrices, browser focus/selection during list moves, large-list throughput and allocation budgets remain. Duplicate validation, matching and removal lookup remain quadratic. No benchmark or Ripple/Solid ranking is claimed.

Binding pending/error/status policies and scheduling remain the next runtime task. These tests do not establish whole-tree transactional updates, arbitrary reactive setter rollback, or every combination of reporter failure and reentrant teardown. Broad DOM generation remains separate.
