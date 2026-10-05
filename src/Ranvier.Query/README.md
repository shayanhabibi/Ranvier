# Ranvier.Query

Typed reactive queries and mutation reconciliation for partially loaded application data.

Preview API. Query results are ordinary immutable records; page-owned leases keep
loaded queries alive. Define a family once per client, acquire only visited keys,
and reconcile successful saves with `UpdateIfLoaded` or explicit invalidation.

```fsharp
use client = new QueryClient(graph)
let sections = client.Define(EqualityComparer<int>.Default, loadSection)
use page = sections.Acquire sectionId
let save draft = client.Mutate(draft, saveWord, fun saved -> [
    sections.UpdateIfLoaded(saved.SectionId, fun old ->
        { old with Words = upsertById (toPreview saved.Word) old.Words })
])
```

Loaders and remote writes return `Task` and accept a `CancellationToken`. Reads do
not fetch; `Acquire` and `Ensure` express demand. `Value` suspends on the initial
load and retains accepted data during refresh; `State` exposes loading/error/stale
metadata. Invalidation never fetches automatically. Dispose page leases on Back
and the client on Home/logout. Saves belong to the client, not the editor owner.

Writes serialize through atomic reconciliation. A `ReconciliationFailed` outcome
retains the saved receipt and invalidates existing queries; refresh explicitly
instead of repeating a successful remote write. Updaters must return immutable
values and must not reenter client operations.

The layer uses public Ranvier primitives without changing the graph engine.
Whole-record and list reconstruction costs remain. Supported targets are .NET 10,
.NET 8, .NET Standard 2.1 and Fable; the untraced build supports NativeAOT. Use the
matching `Ranvier.Query.Traced` package with a traced graph build.
