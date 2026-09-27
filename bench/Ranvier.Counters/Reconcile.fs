/// <summary>Checks that each Ranvier case's trace log accounts for every library counter changed by the case.</summary>
module CounterBench.Reconcile

#if RANVIER_TRACE && RANVIER_COUNTERS
open System.Collections.Generic
open Ranvier

/// <summary>The library counters as the events of <c>logs</c> account for them, in <c>Counters.Snapshot</c> order.</summary>
let private fromLogs (logs: TraceEvent[] seq) : (string * int64)[] =
    let counts = Dictionary<string, int64> ()
    let add name = counts[name] <- (match counts.TryGetValue name with | true, n -> n | _ -> 0L) + 1L

    for events in logs do
        let kinds = Dictionary<int, TraceNodeKind> ()

        for e in events do
            match e.Kind with
            | TraceEventKind.GraphNew -> add "OwnersCreated"
            | TraceEventKind.OwnerNew -> add "OwnersCreated"
            | TraceEventKind.NodeNew ->
                let kind = enum<TraceNodeKind> e.Arg
                kinds[e.Node] <- kind

                match kind with
                | TraceNodeKind.Signal -> add "SignalsCreated"
                | TraceNodeKind.Memo -> add "MemosCreated"
                | TraceNodeKind.Effect -> add "EffectsCreated"
                | _ -> ()
            | TraceEventKind.RunStart ->
                match kinds.TryGetValue e.Node with
                | true, TraceNodeKind.Memo -> add "MemoRecomputes"
                | true, TraceNodeKind.Effect -> add "EffectRuns"
                | _ -> ()
            | TraceEventKind.EdgeAdd -> add "EdgesAdded"
            | TraceEventKind.EdgeRemove -> add "EdgesRemoved"
            | TraceEventKind.ObserverAdd -> add "ObserverInserts"
            | TraceEventKind.ObserverRemove -> add "ObserverRemoves"
            | TraceEventKind.FlushStart -> add "Flushes"
            | _ -> ()

    [| for name, _ in Counters.Snapshot () -> name, (match counts.TryGetValue name with | true, n -> n | _ -> 0L) |]

/// <summary>
/// Runs every Ranvier case of <c>Scenarios.all scale</c> on its own graph, prints one PASS or FAIL line per
/// case, and returns the number of failing cases.
/// </summary>
/// <remarks>
/// A case fails when a counter differs from the count derived from its log, or when a live source list or observer
/// set differs from the one folded from the log. Both are compared after <c>Run</c> and before <c>Teardown</c>.
/// </remarks>
let run (scale: int) : int =
    let mutable failures = 0

    for case in Scenarios.all scale |> List.filter (fun c -> c.Engine = "Ranvier") do
        Scenarios.takeGraphs () |> ignore
        Counters.Reset ()
        let prepared = case.Prepare case.Ops
        prepared.Run ()
        let counters = Counters.Snapshot ()
        let graphs = Scenarios.takeGraphs ()
        let logged = fromLogs [ for g in graphs -> Trace.events g ]

        let problems =
            [
                if graphs.Length <> 1 then
                    yield $"the case created %d{graphs.Length} graphs; reconciliation needs exactly one"
                for (name, counted), (_, derived) in Array.zip counters logged do
                    if counted <> derived then
                        yield $"%s{name}: counter %d{counted}, log %d{derived}"
                for g in graphs do
                    yield! Trace.reconcile g
            ]

        prepared.Teardown ()

        match problems with
        | [] -> printfn $"PASS  %s{case.Scenario}"
        | problems ->
            failures <- failures + 1
            printfn $"FAIL  %s{case.Scenario}"

            for p in problems |> List.truncate 20 do
                printfn $"        %s{p}"

    failures
#else
/// <summary>Reports that reconciliation needs both defines, and returns 1.</summary>
let run (_: int) : int =
    eprintfn "--reconcile needs a build with -p:RanvierCounters=true -p:RanvierTrace=true."
    1
#endif
