# Owned ranges and staged mounting proof

This continues framework Task 3 in the separate DOM worktree. The instruction tree remains provisional. `Mounting.fs` is an experiment; `Browser.fs` remains the earlier representation benchmark baseline.

Scope distinguishes setup, attached and disposed. Successful attachment marks existing live descendants attached. A new child starts in setup, and disposal cannot be reversed by attachment.

Mount setup runs against a detached DocumentFragment. Two comment markers give empty, single-root and fragment views the same placement contract. Initial insertion commits the staged fragment. Setup or insertion failure releases acquired resources and recorded roots while preserving unrelated host siblings. Cleanup runs child first while mounted DOM stays attached. The caller owns the graph; mounts sharing it remain independently disposable.

Movement validates recorded root identity, order, destination and anchor before moving nodes. Node identity and reactive bindings survive movement. Externally changed boundaries or roots invalidate movement. Disposal attempts each recorded release even when roots moved externally; it does not sweep unknown inserted nodes.

Native roots require `Owned` or `Borrowed`. Owned nodes are removed on release. Borrowed nodes return to their original parent and next-sibling anchor; if that anchor disappeared, they append to the original parent. Originally detached borrowed nodes return to detached state. Reverse restoration preserves adjacent borrowed-node ordering. A weak registry rejects overlapping ownership across live mounts of this interpreter.

```fsharp
let view =
    Typed.div Tree.ops [ Typed.id "counter" ]
        [ Tree.ops.Bind (=) (fun () -> string count.Value) ]

let handle =
    Mounting.mount graph None report host None
        (fun context -> Tree.run view context.Backend)

handle.MoveBefore(otherHost, None)
handle.Dispose()
```

Native borrowing uses `context.Native(node, Mounting.Borrowed)` during setup. These are top-level roots. Captured construction operations reject calls after attachment or disposal.

## Keyed-list reconciliation

Mounted keyed lists retain row mounts while keys remain equal, update retained rows with the latest item values, and move their ranges into the new order without replacing their DOM nodes. Removed rows dispose their listeners and mounted resources; a later occurrence of the same key creates a new row. Duplicate keys fail before initial host mutation, and a duplicate live update leaves the current host contents intact.

Row mounts run under the keyed region's stable owner rather than the reconciliation effect's per-run owner. This keeps retained rows alive across effect reruns while preserving disposal with the keyed region.

## Evidence

- The scope phase test failed on the missing API, then passed in SageFs and after rebuilding the session.
- Range tests failed before the mounting interpreter existed.
- Cross-mount native ownership failed before the registry fix. Generated JavaScript showed an Emit-annotated value expanded to a fresh WeakSet at every use. An emitted factory called once now initializes the shared registry.
- Keyed reconciliation tests cover retained DOM identity, latest values, reorder, removal, reinsertion, listener cleanup, and duplicate keys.
- Current checkpoint Node suite: 113/113 passed.
- Current checkpoint Chromium suite: 16/16 passed.
- Release DomTests build: zero warnings and errors.
- Pure .NET lifetime checks: 8/8 passed; portable representation checks: all six groups passed.

Checkpoint `a00997b` recorded the ownership regression before its fix, following the user's "commit then check" instruction. `9d4e169` fixes it.

## Limits

This is a correctness experiment. Markers, staging, entry records and ownership checks add mounting costs that have not been benchmarked. Scalar bindings still write directly to their text node. Recovery and error notification for a failed live reconciliation remain unspecified. JavaScript allocation, retained heap and performance budgets remain open framework Task 2 gates.

Movement retains the original renderer scope. Ownership transfer, cross-document adoption, dynamic structures, nested native borrowing and generated HTML-domain coverage remain later work.

## Decisions and review

- Preserve the baseline interpreter: temporary duplication keeps earlier benchmark evidence comparable; consolidation is later work.
- Restore borrowed placement with append as the missing-anchor fallback: a different policy requires an explicit API change.
- Retain scope ownership during movement: owner and graph transfer require a separate contract.
- The fresh reviewer could not start because of a usage limit. A separate author review is weaker evidence; the findings below were verified in one fix pass.

## Final review and fixes

Four Important findings were reproduced before fixes; no Critical or Minor findings were recorded:

1. Equality could dispose a mount, then the binding wrote to a retained node. Bindings now capture their creation scope and recheck liveness after the comparer. The regression changed from a late value of "1" to the retained "0".
2. Child mounts inherited their parent's reporter, ignoring the explicit mount reporter. A compatible two-argument `Scope.Child` overload accepts a child reporter. Direct child disposal uses it; parent-initiated disposal still aggregates the shared drain through the parent reporter. The existing one-argument API is preserved.
3. A failed movement plus failed rollback left the mount attached in lifecycle state with live bindings. Rollback failure now disposes the scope before surfacing both movement errors. The regression verifies no remaining nodes or signal-reader reruns.
4. Generated descendants escaped release and cross-mount ownership checks after external reparenting. The range now records and claims generated descendants independently and releases them after cleanup, wherever they were moved. Native integration ownership remains explicitly about the supplied root node.

Every finding's regression was observed RED and then GREEN; the full 84 Node and 14 Chromium suites passed after all fixes. The final Release build passed without warnings or errors. The scoped FCS check is the semantic gate; the existing whole-solution baseline failures remain outside this experimental change.

Review focus: boundary mutation is rejected before movement; descendant destinations and cross-document destinations are rejected by explicit validation; borrowed restoration tolerates a missing original anchor; failed insertion after actual mutation unwinds acquired listeners and bindings; retained constructors reject late calls; release/report failures attempt remaining releases; attachment and equality reentry are covered.

Performance parity, production pending/error policies, owner transfer and hydration were considered but are not implemented or claimed by this correctness experiment. Their existing framework gates remain open. No polish findings were deferred.

The additional reporter probe could not reach SageFs: `send_fsharp_code` failed with `httpx.ConnectError: All connection attempts failed`. The same pure candidate passed under `dotnet fsi`, and the eight existing lifetime checks passed. No daemon restart or stop was attempted. The session created for the initial phase probe was `4602caf4`; its final status could not be queried after the endpoint became unavailable.

The 2026-10-11 keyed-list/application checkpoint and additional range regressions are recorded in [current PoC status](2026-10-11-view-poc-checkpoint.md). Keyed structures now work experimentally; production policies and performance gates remain open.
