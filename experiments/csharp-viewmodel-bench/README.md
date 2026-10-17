# C# order view-model comparison

A naive comparison of the exact two revised examples from the LinkedIn-post discussion: CommunityToolkit.Mvvm and Ranvier.CSharp. Both expose quantity, total, display text, loading, and error state. Both load explicitly, ignore a second request while loading, support reload and recovery, pass a cancellation token to the service, and dispose their request lifetime.

This measures these implementations, including their chosen bindings and boundaries. It does not establish an inherent performance ratio between the libraries.

## Reproduce

From the repository root, on Windows with the .NET 10 SDK:

```powershell
rtk dotnet run --project experiments/csharp-viewmodel-bench/Bench.csproj -c Release -p:RanvierTrace=false
rtk dotnet run --project experiments/csharp-viewmodel-bench/wpf/WpfBench.csproj -c Release -p:RanvierTrace=false
```

The first command writes `results.json`. The second writes `wpf-results.json`. Each contains every trial, summaries, timestamps, environment details, and SHA-256 hashes of the measured sources. The commands run sequentially to avoid competing benchmark processes. These projects are standalone experiments and are not added to the solution.

Measured against repository revision `ac96be31ee37d6e3e27a8fda366ad113bb2d9e94`, with existing untracked files in the working tree. The source project references compile the working copy; source hashes record the benchmark examples. Hardware: AMD Ryzen 9 9900X, 24 logical processors. Runtime and OS versions are recorded in the JSON. CommunityToolkit.Mvvm is pinned to 8.4.0. Ranvier uses the local source with tracing disabled.

## Measurement

- Release builds with runtime tiered compilation disabled; two warmup batches before each workload.
- Twelve trial batches per implementation/workload, alternating which implementation runs first. The delayed-service test uses six batches.
- Timing is total batch elapsed time divided by logical operations. The headline value is the median of trial means; the range is the minimum and maximum trial mean. These are not per-operation latency percentiles or confidence intervals.
- Allocations use `GC.GetAllocatedBytesForCurrentThread`. They cover the UI thread, including mock-service tasks, bindings and harness work; they exclude worker-thread allocations.
- A full collection runs before every measured batch, outside the timing region. Automatic collections during a batch remain part of its timing.
- Setup and teardown are excluded for steady-state scenarios. Opening/closing scenarios include per-screen view model and graph creation/disposal, and WPF control/binding creation/disposal.
- Before timing, the basic harness runs 36 correctness checks and the WPF harness runs 20. They test pending/success/error/recovery, quantity edits during loading, overlap prevention, cancellation, background completion, binding notifications, and row isolation. Measured batches also check final output states.

## Basic workload flows

`Bench.csproj` installs a queued UI synchronization context. A subscriber acts like a binding engine: it reads only registered properties named by `PropertyChanged`. It records all notifications and binding reads. The mock quote service returns a real `Task<decimal>` and completes it through `TaskCompletionSource`; CPU-focused scenarios complete it on the UI thread so arbitrary service latency does not mask local overhead.

- `open-load-close`: open a new screen, load a quote, display its total, then close; each screen has a new graph in Ranvier.
- `quantity-edit`: change quantity on a loaded cart.
- `quantity-no-op`: assign the quantity it already holds.
- `refresh`: reload, edit quantity twice during loading, attempt a second load while pending, then succeed.
- `error-retry`: fail a reload, edit quantity while failed, retry, and succeed.
- `mixed-journey`: two loaded edits, a refresh, two pending edits and an overlapping request attempt; every tenth journey fails and retries.
- `100-cart-edit`: edit one of 100 independently bound carts; Ranvier shares one graph across them.
- `close-in-flight`: start a request, detach the view and dispose, then deliver a late success from a service that ignores cancellation. The disposed view receives no late notification.
- `delayed-service`: request a 10 ms delay on a thread-pool task, complete the request, and pump the UI context. Windows timer resolution, thread-pool scheduling and the pump can make observed time substantially longer than 10 ms. Overlapping trial ranges make small ratios here inconclusive.

Ranvier also reports an internal `CalculatedTotal` property notification. It is counted as a notification, but the basic subscriber does not read it because no UI control is bound to that name. This explains why quantity edits have four Ranvier notifications but three registered binding reads.

## Actual WPF binding workloads

`wpf/WpfBench.csproj` runs on an STA thread with a `DispatcherSynchronizationContext`. Each row has a real `StackPanel`, a `TextBox` two-way bound to `Quantity` with `UpdateSourceTrigger.PropertyChanged`, and five `TextBlock` controls bound to `Display`, `Total`, `IsLoading`, `Error`, and `HasErrors`. Bindings use the actual view models as their data context. The WPF dispatcher drains to application-idle priority during each interaction.

- `wpf-quantity-input`: change the quantity text, let WPF convert/write the source and update the bound text.
- `wpf-refresh`: begin a reload, edit quantity through the text box, attempt a second load, complete the service and drain the dispatcher.
- `wpf-error-retry`: the same flow with a failure, followed by a successful retry; both dispatcher drains are included.
- `wpf-100-row-input`: edit one of 100 independently bound rows, sharing one Ranvier graph.
- `wpf-open-close`: create the controls and view model, attach bindings, load and update the displayed text, detach bindings and dispose.

No window is shown. These are headless WPF controls and actual binding/dispatcher work, not keyboard input latency, layout/paint cost, GPU presentation or frames per second. The quote service is controlled rather than a real HTTP endpoint. Repeated, back-to-back interactions are a synthetic replay of application flows, not a claim about the distribution of user behavior.

## Interpretation

The baseline already uses property-level change notifications, so editing one cart does not rebuild the other 99. This example has a cheap multiplication and a short dependency chain; it offers little computation for a graph to avoid. Treat its absolute costs and allocation figures as evidence about this small flow, and avoid promoting a library-wide speedup claim from it.
