# Avalonia.FuncUI task workload

A searchable, paged task dashboard with summary counts, selection, progress edits and a fake background feed. Three implementations use the same model, update function, messages and native control layout:

- `elmish`: Elmish 5's actual program loop; builds the whole view after a changed model, following FuncUI's structural model guard.
- `elmish-cached`: the same loop, with cached section and row views. Unchanged task records reuse their views; the root still patches.
- `ranvier`: Ranvier.Elmish selectors update section and row hosts independently. Search/page changes create and dispose owners for entering and departing rows; surviving rows retain their owners and cached views. FuncUI can replace native hosts when rows move positions, so retained views are attached to those replacement hosts. Rows at unchanged positions preserve native controls.

Requires .NET 10 and the sibling Avalonia.FuncUI checkout at `../Avalonia.FuncUI`. Override `-p:FuncUIRoot=C:/path/to/Avalonia.FuncUI` if needed. Uses the local Ranvier projects and disables tracing for measurement.

From the repository root:

```powershell
dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- ranvier
dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- elmish
dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- elmish-cached
dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- --verify
python examples/Ranvier.FuncUI.Workload/source_metadata.py
dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- --measure --output examples/Ranvier.FuncUI.Workload/results/raw.json
python examples/Ranvier.FuncUI.Workload/report.py examples/Ranvier.FuncUI.Workload/results/raw.json
```

The desktop feed updates one task every 100 ms. The headless verifier checks real control properties after every message in four deterministic traces at 100 and 1,000 tasks, plus native identity, callbacks, disposal and independent instances. `--smoke` closes the desktop after two seconds.

## Measurement method

Each scenario has 600 messages, two untimed warmups and eight measured repetitions. Runner order rotates each repetition. Construction, window attachment and teardown are excluded. Each message times dispatch plus `Dispatcher.UIThread.RunJobs()` on the UI thread. Samples include update, selector propagation, view construction, patching and queued headless UI work. Rendering counters come from a separate instrumented replay; initial rendering is excluded. A separate pure update pass provides context, not a subtraction from UI timings.

Allocation counts cover the current UI thread only. GC collection counts are process-wide; GC runs between trials, outside timing. Results describe this machine and this workload. Headless Avalonia uses real controls and templates but does not measure desktop GPU rendering, presentation, frame rate or input latency. The dashboard deliberately retains just 50 visible rows. Fine-grained selectors still inspect the shared immutable model on writes, and search/page changes incur topology work.

See `results/report.md`, `results/summary.json`, `results/raw.json` and `results/source.json` for the measured results, raw per-message samples and source identity. The metadata script uses RTK, Git and PowerShell to capture this Windows environment. Use `--items <count>` to change the desktop dataset; edit `Measurement.fs` and `Model.fs` to change the benchmark matrix or traces before generalizing to an application.
