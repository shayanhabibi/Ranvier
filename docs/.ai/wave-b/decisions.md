# Wave B: defaults picked for the open questions

Each note's recommendation is taken unless listed here. Any of these can be overturned; say which.

| Note | Question | Default |
| --- | --- | --- |
| AOT/trim | Keep current `ToString` text by hand | keep |
| AOT/trim | AOT-check the traced build in CI too | untraced only |
| AOT/trim | `string key` in exception messages (no quotes) | yes |
| FinishCurrent | Trailing run only, no pure-drop case | yes |
| FinishCurrent | Name | `FinishCurrent` |
| FinishCurrent | Per-memo policy override next | later, not now |
| Debounce/throttle | Implement now | **no**: the note says do later, after `FinishCurrent` and benchmarks. Corrections applied only |
| Delta readers | Per-reader accumulator for stage 2 | yes |
| Delta readers | `KeyChange` as a CLR enum | yes |
| Delta readers | `ProjectionReader<'K>` without `'V` | yes |
| ReactiveCommand | Default policy | `Disable` |
| ReactiveCommand | Public `Graph.TrackStatus(INode)` in core | yes |
| ReactiveCommand | `Queue`/`Parallel` in the first cut | no |
| ValueOption | Keep voption overloads beside the new ones | keep |
| ValueOption | Seed overloads for `Memo`/`Boundary` | yes |
| ValueOption | `SettledOr`/`TrySettled` in Ranvier.CSharp | yes |
| Failure provenance | Property (`ErrorOrigin`) or function | property |
| Failure provenance | Share one captured stack across the path | yes |
| Failure provenance + C# tracing | `Trace.errorOrigin` | **not added**: `ErrorOrigin` works in every build, so the traced-only query is redundant. C# gets `ErrorOrigin` through the property |
| Serialised affinity | Case name `Serialised` | yes |
| Serialised affinity | `Dispatch` from the captured context outside the holder | queue |
| Serialised affinity | Blazor helper | docs only |
| MVU/store/writable | Name of (c) | renamed to avoid a clash with `ReactiveBindings.Writable` |
| MVU/store/writable | Elmish-shaped `Cmd` | no; plain functions, no Elmish dependency |
| MVU/store/writable | (b) per-field store | docs only |

## Gates

Items that touch the hot path land only if an A/B benchmark shows no regression beyond noise on the existing
Signals, Memos and Suspension benchmarks. If one fails its gate it is held for review with you, not merged:

- Serialised affinity (one branch per top-level entry on guarded graphs)
- Failure provenance (node fields fold into one record)
- FinishCurrent (one int test per flight launch)
- Delta readers stage 2 (one null check per added or removed key)
