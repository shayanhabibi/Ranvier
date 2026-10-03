# Value readers and incremental map implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. The user explicitly requested implementation first and tests afterward.

**Goal:** Add editable keyed sources, pull-based settled-value deltas, and map membership driven by key deltas.

**Architecture:** Preserve `ProjectionReader<K>` and its independent bounded accumulators. Share row taps and accepted values only while value readers live; settle suspects after memo execution. A separate delta pass path applies map membership directly, retaining reset enumeration as recovery.

**Tech Stack:** F#, .NET 10/8/netstandard2.1, Fable; existing graph and platform maps.

**Spec:** [Research and design](../../RESEARCH-delta-incremental-collections.md).

## Global constraints

- Preserve current public signatures, graph batching, per-key ownership, pending/error behavior, and Fable support.
- No primitive memo hot-path changes. Shared value observation must disappear when the last value reader is disposed.
- Changes are coalesced hints; `Replaced` must survive equal payloads and subsequent `Changed` notifications.
- Work in the current task branch and preserve unrelated user edits. Do not create an additional checkout or daemon.
- The user authorized implementation after the researched design; no additional approval handoff. Tests follow code as requested.

## Review focus

- Comparer/body/purity failure must not announce an uncommitted value.
- Independent readers and retained deltas must not consume or mutate each other's state.
- Hidden/pending keys and first settlement must preserve adapter behavior.
- Failed passes or partial consumer application need a correct reset/retry route.
- One row change must avoid all-row polling; membership/reset discovery cost remains explicit.

## Tasks

- [x] Extend `Deltas.fs` merger to preserve Added/Replaced on Changed and route Changed only to value readers. Share row observation and accepted-value caches; add `NewValueReader()` without changing `NewKeyReader()` behavior.
- [x] Rebuild `AsObservableCollection` around the shared accepted-value cache and value reader. Update changed keys on value-only edits; retain full reconciliation and partial-failure recovery.
- [x] Bypass `Enumerate`/full `ApplyDiff` for map membership. Consume key deltas, share upstream order, and recover from reset/failure by comparing upstream row identity.
- [x] Add `KeyedCollection<K,V>`, F#/C# factories, direct per-key writes, insertion order, removal/clear and synchronous batched edits.
- [x] Add native/Fable/C# tests after implementation. Cover settlement, failures, scopes/replacement, reader independence, immutable deltas, sparse pipelines and fallback recovery; run full suites and all-target Release builds.
- [x] Update collection documentation, inspect public surface, run XML/comment hygiene and independent review, and address findings.
- [x] Commit only this feature's files on the existing task branch.

## Execution record

- Baseline: `ec98516`; previous full suites passed (919 Release, 993 traced Debug, 75 C#, 46 Node, 11 Chromium). Core fresh FCS check was clean during research.
- Ruling: implement shared post-read taps instead of the old pre-commit `RunRow` hook; memo cutoff failures can reject body results.
- Review fixes: seed previously settled rows during pending/failure; retire taps on row identity changes; drain self-invalidations; remove structural keys consistently; preserve equal replacements in the observable adapter; route both map factories through the delta path; reconcile upstream row/signal identity on fallback.
- Verified: 938 Release native tests; 1,012 traced Debug tests (5 existing ignored); 81 C# tests on each of .NET 10 and .NET 8; 65 shared Fable/Node checks; 11 Chromium checks. All-target core/C# Release build succeeded without warnings; fresh FCS core check clean; existing projection public signatures retained.
- Work-count check: one edit in 10,000 rows recomputes one row in each of two maps and preserves the source key array. This is not a timing benchmark.
- XML audit: changed declarations/new files clean; two pre-existing long doc lines remain in Combinators.fs. Added-comment hygiene passes.
- Costs retained: O(N) insertion-order removal and membership key-array copies; filter/sort/group membership scans; coalesced key hints rather than value snapshots; synchronous Edit retains applied writes on callback failure.
