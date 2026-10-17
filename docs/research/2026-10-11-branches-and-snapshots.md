# Branch selection and snapshot storage — 2026-10-11

## Changes and limits

`Branch.select` accepts an explicit comparer, key selector, scalar selection reader and factory receiving a current-value reader. It delegates to the existing keyed runtime: equal keys retain the mounted instance, changed keys replace it, and retained children read current data. Include the variant discriminator in a key when the component shape changes.

The helper removes the caller's singleton-list wrapper and the keyed adapter's list mapping. It still constructs a singleton keyed list internally. It does not introduce a separate branch range engine or change scheduling, ownership or reconciliation.

The structural composition tests now use this helper across Tree/Program and synchronous/queued mounts. They cover shared source readiness, retained identity, replacement cleanup, returning keys and cancellation after disposal. Portable HTML checks exercise escaped selected content.

Two additional tests characterize descendant effect failure. A text reader that throws retains its previous DOM and retries successfully after a subsequent source change; the enclosing computation boundary's recovery function is not called. Core `Effect.RunBody` records the failure on the effect rather than routing it to an ancestor computation boundary. Descendant error handling therefore remains an open core ownership seam. No callbacks, outcome policies or extra roots were added to ordinary text bindings. Shared computation readiness also does not establish coordinated reveal across independent boundaries.

## List versus array evidence

The portable snapshot benchmark compares F# lists, borrowed arrays and arrays copied before each operation. All start with the same preconstructed key/value pairs. Adaptation boxes pairs and consumes the result; traversal consumes all pairs; lookup searches every key in reverse order with the same linear-search algorithm. Checksums verify the work. These are collection microbenchmarks, not a renderer storage migration or DOM throughput results.

Median microseconds per operation for representative widths:

| Runtime | Width | Operation | List | Array | Copied array |
| --- | ---: | --- | ---: | ---: | ---: |
| Chromium/Fable | 16 | Adapt | 0.319 | 0.159 | 0.167 |
| Chromium/Fable | 256 | Adapt | 5.000 | 2.417 | 2.559 |
| Chromium/Fable | 1024 | Adapt | 19.829 | 10.010 | 10.361 |
| Chromium/Fable | 1024 | Traverse | 9.922 | 4.009 | 4.453 |
| .NET | 1 | Adapt | 0.034 | 0.031 | 0.063 |
| .NET | 256 | Adapt | 3.880 | 2.964 | 3.033 |
| .NET | 1024 | Adapt | 16.510 | 12.484 | 12.735 |
| .NET | 1024 | Traverse | 0.838 | 0.291 | 0.723 |

At width 1024, .NET adaptation allocates 114,744 bytes per operation with lists, 90,192 with arrays and 98,408 with copied arrays. These are current-thread allocated bytes, not retained heap. Browser allocations were not measured.

The repeated linear lookup workload at width 1024 takes 3525.625/255.313/251.875 microseconds in Chromium and 854.050/848.144/860.313 in .NET for list/array/copied array respectively. All remain quadratic. Faster array iteration does not settle comparer-compatible indexing, duplicate validation or large-list renderer complexity.

Copying has material cost for tiny .NET snapshots: a one-item traversal takes approximately 0.0017 microseconds for a list versus 0.0334 for a copied array. Absolute values this small are sensitive to benchmark overhead. Larger adaptation workloads favor arrays on both targets, including the explicit-copy variant.

## Ownership and shims

The next storage experiment should use the same internal array representation on both targets, with explicit adapters for list authoring. The current evidence does not require platform-dependent list/array storage. A small internal module can hide storage operations without introducing `seq` enumeration or per-item interface dispatch. Platform specialization should follow an end-to-end measured need.

A boundary must own a stable snapshot container. Borrowing a caller's mutable array allows later slot changes to affect reconciliation. `Array.copy` freezes the slots shallowly; referenced objects remain shared. `Array.map` already owns its output, so an extra input copy is redundant for a pure mapping operation. If key callbacks can mutate/reenter the input, copying before invoking them may be necessary to preserve a stable input snapshot. Make that contract explicit and copy once at the ownership seam, rather than repeatedly during downstream traversal.

Public signatures and the keyed runtime remain list-based in this checkpoint. Before migrating them, measure realistic reconcile/move/remove workloads and preserve current duplicate, comparer, reentry, failure and identity contracts. This report supports that experiment, not an immediate public collection-type switch.

## Branch measurements and reproduction

The DOM benchmark compares `Branch.select` with `Keyed.list` over a singleton selection, including construction, mount, retained updates, key replacement and disposal at widths 1 and 16 with synchronous and manually flushed queued commits. Final content is checked. Branch/list median elapsed ratios range from 0.923 to 1.108; retained-update ratios range from 0.974 to 1.003. These runs do not establish a meaningful branch throughput improvement. The current helper primarily gives callers an explicit scalar contract.

Run from `examples/Fable.Ranvier.Playground`:

```powershell
rtk npm run bench:branch
rtk npm run bench:snapshot:dotnet
```

Raw samples and environment metadata are in [browser-results.json](branch-snapshots/browser-results.json) and [dotnet-results.json](branch-snapshots/dotnet-results.json). Runs use Release output, three independent processes, three warmup rounds and eleven measured rounds with rotating mode order. Browser data contains 1320 branch and 1188 snapshot samples. Hardware is a Ryzen 9 9900X; browser versions are Fable 5.18, Node 26.7 and Chromium 153. The .NET runner disables tiered compilation for consistent JIT treatment. Borrowed/copied array lookup uses the same search kernel; earlier differing kernels and short samples produced misleading .NET lookup differences and were replaced before recording these results. The benchmark project has its own directory to isolate its MSBuild artifacts from the representation-check executable; both runners were verified sequentially after this repair.

## Verification

The complete scoped acceptance run passes 164 Node cases and 18 Chromium tests. Release DomTests compilation and the scoped FCS check report zero warnings/errors; the portable representation executable passes its six groups. The SageFs pure representation checks pass, and the XML documentation audit reports no findings. The production playground build passes at 81.84 kB JavaScript / 22.61 kB gzip. Whole-solution acceptance and the framework release gates remain open as recorded in the checkpoint.
