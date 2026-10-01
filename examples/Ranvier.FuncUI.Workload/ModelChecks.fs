namespace Workload

module ModelChecks =
    let run () =
        let ensure condition message = if not condition then failwith message
        let initial = Model.init 100
        ensure (initial.Tasks.Count = 100 && initial.VisibleIds.Length = 50) "initial task count and page"
        ensure (Model.init 100 = initial) "initial data is deterministic"
        ensure (obj.ReferenceEquals(initial, Model.update (SetProgress(-1, 10)) initial)) "invalid ID must be a no-op"
        let edited = Model.update (SetProgress(0, 123)) initial
        ensure (edited.Tasks[0].Progress = 100) "progress is clamped"
        ensure (obj.ReferenceEquals(initial.Tasks[1], edited.Tasks[1])) "unchanged tasks retain identity"
        ensure (obj.ReferenceEquals(edited, Model.update (SetProgress(0, 100)) edited)) "repeated values must be no-ops"
        let empty = Model.update (Search "no matching task") initial
        ensure (empty.VisibleIds.IsEmpty && empty.MatchingCount = 0 && empty.Page = 0) "empty search results"
        ensure (empty.SelectedId = initial.SelectedId) "selection survives filtering"
        let searched = Model.update (Search "ADA") initial
        ensure (searched.VisibleIds |> List.forall (fun id -> searched.Tasks[id].Owner = "Ada")) "case insensitive owner search"
        let paged = Model.update (Page 999) initial
        ensure (paged.Page = 1 && paged.VisibleIds.Head = 50) "page clamps to last page"
        let offscreen = Model.update (SetStatus(99, Done)) initial
        ensure (obj.ReferenceEquals(initial.VisibleIds, offscreen.VisibleIds)) "task edit retains page topology"
        let struct (ready, running, doneCount) = offscreen.Summary
        ensure (ready + running + doneCount = 100) "summary accounts for every task"
        for size in [100; 1000] do
            for name in Workloads.names do
                let trace = Workloads.trace name size
                ensure (trace.Length >= 500 && trace = Workloads.trace name size) "deterministic nonempty trace"
                let final = Array.fold (fun state msg -> Model.update msg state) (Model.init size) trace
                let actual = final.Tasks.Values |> Seq.countBy _.Status |> Map.ofSeq
                let get status = Map.tryFind status actual |> Option.defaultValue 0
                ensure (final.Summary = struct (get Ready, get Running, get Done)) "summary matches task states"
                ensure (final.VisibleIds |> List.forall final.Tasks.ContainsKey) "visible IDs exist"
                ensure (final.VisibleIds.Length <= Model.pageSize) "page size bound"
        printfn "PASS: shared model, identity, search, pagination, summaries and deterministic traces"
