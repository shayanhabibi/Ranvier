# Ranvier.Query

Typed reactive queries and mutation reconciliation for partially loaded application data.

Preview API. Query results are ordinary immutable records; page-owned leases keep
loaded queries alive. Define a family once per client, acquire only visited keys,
and reconcile successful saves with `UpdateIfLoaded` or explicit invalidation.

```fsharp
use client = new QueryClient(graph)
let sections = client.Define(loadSection)
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

Use `AcquireOwned(key, pageOwner)` when a navigation page must outlive its current
view owner. Keep the page owner in History and dispose it on Back. `EnsureAsync()`
and `RefreshAsync()` return `Task<T>` after publication, suitable for Elmish loading
messages. Retired requests and disposed leases cancel waiting tasks; loading errors
fault them. Client disposal also cancels `Mutate` tasks: catch
`OperationCanceledException` when closing the session.

Edit factories return deferred `QueryEdit` descriptions; apply them through
`Commit` or mutation reconciliation. `SetIfLoaded(key, value)` replaces accepted
data. Missing entries remain absent; pending entries without data become stale.
See the compiled navigation model in `examples/Ranvier.Query.Dictionary`.

Writes serialize through atomic reconciliation. A `ReconciliationFailed` outcome
retains the saved receipt and invalidates existing queries; refresh explicitly
instead of repeating a successful remote write. Updaters must return immutable
values and must not reenter client operations.

The layer uses public Ranvier primitives without changing the graph engine.
Whole-record and list reconstruction costs remain. Supported targets are .NET 10,
.NET 8, .NET Standard 2.1 and Fable; the untraced build supports NativeAOT. Use the
matching `Ranvier.Query.Traced` package with a traced graph build.
