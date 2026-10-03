module Ranvier.Tests.Timed

open System
open Expecto
open Ranvier
open Ranvier.Tests.TimedTestClock

[<Tests>]
let clockTests =
    testList
        "Timed clocks"
        [
            testCase "system timer rejects a negative wait"
            <| fun _ ->
                use timer =
                    TimedClock.system.CreateTimer (Action (fun () -> failwith "invalid wait fired"))

                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> timer.Arm (TimeSpan.FromMilliseconds -1.)) "invalid wait"
            testCase "system timer can disarm and dispose an unarmed long wait"
            <| fun _ ->
                use timer =
                    TimedClock.system.CreateTimer (Action (fun () -> failwith "long wait fired"))

                timer.Arm (TimeSpan.FromDays 1000000.)
                timer.Disarm ()
                timer.Dispose ()
#if NET8_0_OR_GREATER && !FABLE_COMPILER
            testCase "time-provider timer chunks long waits and replaces or cancels its deadline"
            <| fun _ ->
                let clock = ManualClock ()
                let adapted = TimedClock.ofTimeProvider (ManualTimeProvider clock)
                let mutable calls = 0
                use timer = adapted.CreateTimer (Action (fun () -> calls <- calls + 1))
                timer.Arm (TimeSpan.FromMilliseconds (2147483647. + 10.))
                clock.AdvanceTo 2147483647.
                Expect.equal calls 0 "chunk boundary is not expiry"
                clock.AdvanceTo (2147483647. + 10.)
                Expect.equal calls 1 "full deadline"
                timer.Arm (TimeSpan.FromMilliseconds 10.)
                timer.Arm (TimeSpan.FromMilliseconds 20.)
                clock.AdvanceTo (2147483647. + 25.)
                Expect.equal calls 1 "replacement deadline"
                timer.Disarm ()
                clock.AdvanceTo (2147483647. + 50.)
                Expect.equal calls 1 "disarmed"
            testCase "time-provider timer rounds a positive sub-millisecond wait up"
            <| fun _ ->
                let clock = ManualClock ()
                let mutable calls = 0

                use timer =
                    (TimedClock.ofTimeProvider (ManualTimeProvider clock)).CreateTimer(Action (fun () -> calls <- calls + 1))

                timer.Arm (TimeSpan.FromMilliseconds 0.1)
                clock.AdvanceTo 0.999
                Expect.equal calls 0 "not early"
                clock.AdvanceTo 1.
                Expect.equal calls 1 "rounded native deadline"
#endif
        ]

let private modes =
    [
        "debounce", debounceWith
        "first", throttleFirstWith
        "last", throttleLastWith
        "both", throttleWith
    ]

[<Tests>]
let edgeTests =
    testList
        "Timed graph edges"
        [
            for name, factory in modes do
                testCase (name + " rejects negative durations")
                <| fun _ ->
                    use graph = new Graph ()
                    let clock = ManualClock ()

                    Expect.throwsT<ArgumentOutOfRangeException>
                        (fun () ->
                            factory { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds -1.) (fun () -> 0) graph
                            |> ignore)
                        "construction validation"

                testCase (name + " cancels stale wakes on disposal")
                <| fun _ ->
                    use graph = new Graph ()
                    use _active = graph.Activate ()
                    let clock = ManualClock ()
                    let input = createSignal 0

                    let output =
                        factory { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                    input.Value <- 1
                    let previous = output.Value
                    output.Dispose ()
                    input.Value <- 2
                    clock.FireStale ()
                    clock.AdvanceTo 1000.
                    Expect.equal output.Value previous "disposal preserves admitted value"

                testCase (name + " makes failures visible and can recover")
                <| fun _ ->
                    use graph = new Graph ()
                    use _active = graph.Activate ()
                    let clock = ManualClock ()
                    let input = createSignal 0
                    let error = InvalidOperationException "source"

                    let upstream =
                        createMemo (fun _ -> if input.Value < 0 then raise error else input.Value)

                    let output =
                        factory { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> upstream.Value) graph

                    input.Value <- 1
                    input.Value <- -1
                    Expect.throws (fun () -> output.Value |> ignore) "failure is immediate"
                    Expect.isTrue (obj.ReferenceEquals (output.ErrorOrigin, upstream)) "upstream origin"
                    clock.AdvanceTo 200.
                    Expect.throws (fun () -> output.Value |> ignore) "old candidate cancelled"
                    input.Value <- 2
                    clock.AdvanceTo 400.
                    Expect.equal output.Value 2 "recovered candidate admitted"
            testCase "pending retains the published value and cancels a candidate"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let blocked = createSignal false
                let input = createSignal 0
                let pending = createAsyncSource<int>()

                let output =
                    debounceWith
                        { Clock = clock; Comparer = None }
                        (TimeSpan.FromMilliseconds 100.)
                        (fun () -> if blocked.Value then pending.Value else input.Value)
                        graph

                input.Value <- 1
                blocked.Value <- true
                clock.AdvanceTo 200.
                Expect.equal output.Value 0 "holds published value"
                pending.Settle 7
                clock.AdvanceTo 300.
                Expect.equal output.Value 7 "resumes from pending"
            testCase "first ready value after pending is immediate"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let pending = createAsyncSource<int>()

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> pending.Value) graph

                Expect.equal output.Status Status.Pending "initially pending"
                pending.Settle 9
                Expect.equal output.Value 9 "first value"
            testCase "pending after an initial failure replaces that failure and notifies readers"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let failing = createSignal true
                let pending = createAsyncSource<int>()

                let output =
                    debounce TimeSpan.Zero (fun () -> if failing.Value then failwith "initial" else pending.Value) graph

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        match output.TryValue with
                        | Failed _ -> "failed"
                        | Pending -> "pending"
                        | Ready _ -> "ready"
                    ))

                failing.Value <- false
                Expect.equal output.TryValue Pending "no ready value to retain"
                Expect.sequenceEqual seen [ "failed"; "pending" ] "status transition notified"
            testCase "unchanged check does not extend a debounce deadline"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0
                let parity = createMemo (fun _ -> input.Value % 2)

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> parity.Value) graph

                input.Value <- 1
                clock.AdvanceTo 50.
                input.Value <- 3
                clock.AdvanceTo 100.
                Expect.equal output.Value 1 "unchanged upstream cutoff"
            testCase "batched writes returning to the captured input do not extend the deadline"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1
                clock.AdvanceTo 50.

                batch (fun () ->
                    input.Value <- 2
                    input.Value <- 1)

                clock.AdvanceTo 100.
                Expect.equal output.Value 1 "stabilized capture"
            testCase "an expiry inside a batch cannot publish before its last source write"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1

                batch (fun () ->
                    clock.AdvanceTo 100.
                    input.Value <- 2
                    Expect.equal output.Value 0 "batch holds publication")

                clock.AdvanceTo 199.
                Expect.equal output.Value 0 "new deadline"
                clock.AdvanceTo 200.
                Expect.equal output.Value 2 "latest admitted"
            testCase "conditional capture detaches unused dependencies before admission"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let chooseA = createSignal true
                let a = createSignal 1
                let b = createSignal 2

                let output =
                    debounceWith
                        { Clock = clock; Comparer = None }
                        (TimeSpan.FromMilliseconds 100.)
                        (fun () -> if chooseA.Value then a.Value else b.Value)
                        graph

                chooseA.Value <- false
                let runs = output.Runs
                a.Value <- 3
                Expect.equal output.Runs runs "old edge detached"
                clock.AdvanceTo 100.
                Expect.equal output.Value 2 "new branch admitted"
            testCase "reentrant downstream writes preserve the next candidate"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                createEffect (fun () ->
                    if output.Value = 1 then
                        input.Value <- 2)

                input.Value <- 1
                clock.AdvanceTo 100.
                Expect.equal output.Value 1 "first admission"
                clock.AdvanceTo 200.
                Expect.equal output.Value 2 "reentrant candidate survived"
            testCase "equal admission does not rerun downstream"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                let seen = ResizeArray<int>()
                createEffect (fun () -> seen.Add output.Value)
                input.Value <- 1
                input.Value <- 0
                clock.AdvanceTo 100.
                Expect.sequenceEqual seen [ 0 ] "same admitted value"
            testCase "swallowed pure-scope violation still fails capture"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()

                let output =
                    debounce
                        TimeSpan.Zero
                        (fun () ->
                            try
                                createMemo (fun _ -> 1) |> ignore
                            with _ ->
                                ()

                            2)
                        graph

                Expect.throws (fun () -> output.Value |> ignore) "pure capture enforced"
            testCase "throwing comparer publishes a failure and recovers on the next capture"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let comparer =
                    { new Collections.Generic.IEqualityComparer<int> with
                        member _.Equals(a, b) =
                            if b = 13 then failwith "comparison" else a = b

                        member _.GetHashCode value = value
                    }

                let output =
                    debounceWith
                        {
                            Clock = clock
                            Comparer = Some comparer
                        }
                        (TimeSpan.FromMilliseconds 100.)
                        (fun () -> input.Value)
                        graph

                input.Value <- 13
                Expect.throws (fun () -> output.Value |> ignore) "comparer failure is immediate"
                input.Value <- 2
                clock.AdvanceTo 100.
                Expect.equal output.Value 2 "recovered"
            testCase "owner disposal cancels unpublished work and detaches its input"
            <| fun _ ->
                use graph = new Graph ()
                let clock = ManualClock ()
                let input = Signal (graph, 0)

                let owner, output =
                    graph.CreateRoot (fun owner ->
                        owner, debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph)

                input.Value <- 1
                let runs = output.Runs
                owner.Dispose ()
                input.Value <- 2
                clock.FireStale ()
                clock.AdvanceTo 100.
                Expect.equal output.Runs runs "released input"
                Expect.equal output.Value 0 "candidate discarded"
#if !FABLE_COMPILER
            testCase "a callback during owner wake is bounded and preserves later admission"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )

                let clock = ManualClock ()
                let mutable inject = false

                let adapted =
                    { new TimedClock() with
                        member _.NowMilliseconds =
                            if inject then
                                inject <- false
                                let worker = Threading.Thread (Threading.ThreadStart clock.FireStale)
                                worker.Start ()
                                worker.Join ()

                            clock.NowMilliseconds

                        member _.CreateTimer callback =
                            clock.CreateTimer callback
                    }

                let input = Signal (graph, 0)

                let output =
                    debounceWith { Clock = adapted; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                let seen = ResizeArray<int>()
                use effect = new Effect (graph, fun () -> seen.Add output.Value)
                input.Value <- 1

                let first =
                    Threading.Thread (Threading.ThreadStart (fun () -> clock.AdvanceTo 100.))

                first.Start ()
                first.Join ()
                inject <- true
                Expect.equal (graph.Pump ()) 2 "one racing wake plus the original"
                Expect.sequenceEqual seen [ 0; 1 ] "one publication"
                input.Value <- 2
                clock.AdvanceTo 200.
                Expect.sequenceEqual seen [ 0; 1; 2 ] "subsequent publication survived"
            testCase "delayed owner dispatch anchors combined cooldown to admission without catch-up"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )

                let clock = ManualClock ()
                let input = Signal (graph, 0)

                let output =
                    throttleWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1
                input.Value <- 2

                let worker =
                    Threading.Thread (Threading.ThreadStart (fun () -> clock.AdvanceTo 1000.))

                worker.Start ()
                worker.Join ()
                Expect.equal output.Value 1 "held for owner"
                graph.Pump () |> ignore
                Expect.equal output.Value 2 "one overdue admission"
                input.Value <- 3
                Expect.equal output.Value 2 "cooldown starts at actual admission"
                clock.AdvanceTo 1100.
                Expect.equal output.Value 3 "next trailing admission"
            testCase "worker callbacks coalesce on the owner and respect newer input before pumping"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )

                let clock = ManualClock ()
                let input = Signal (graph, 0)

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1

                let worker =
                    Threading.Thread (
                        Threading.ThreadStart (fun () ->
                            clock.AdvanceTo 100.
                            clock.FireStale ()
                            clock.FireStale ())
                    )

                worker.Start ()
                worker.Join ()
                Expect.equal graph.PendingWork 1 "coalesced owner wake"
                Expect.equal output.Value 0 "worker cannot publish"
                input.Value <- 2
                graph.Pump () |> ignore
                Expect.equal output.Value 0 "new capture supersedes expired candidate"
                clock.AdvanceTo 200.
                Expect.equal output.Value 2 "new deadline"
#if !RANVIER_TRACE
            testCase "warmed primitive debounce captures allocate nothing"
            <| fun _ ->
                use graph = new Graph ()
                let clock = ManualClock ()
                let input = Signal (graph, 0)

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                for i in 1..1000 do
                    input.Value <- i

                let before = GC.GetAllocatedBytesForCurrentThread ()

                for i in 1001..11000 do
                    input.Value <- i

                let allocated = GC.GetAllocatedBytesForCurrentThread () - before
                Expect.equal allocated 0L "changed capture path"
                Expect.equal output.Value 0 "publication held"
#endif
#endif
        ]

#if RANVIER_TRACE
[<Tests>]
let traceTests =
    testList
        "Timed tracing"
        [
            testCase "independent clock origins do not alter deterministic dumps"
            <| fun _ ->
                let script offset =
                    use graph = new Graph ()
                    let a = ManualClock ()
                    let b = ManualClock ()

                    let shifted clock origin =
                        { new TimedClock() with
                            member _.NowMilliseconds = (clock: ManualClock).NowMilliseconds + origin

                            member _.CreateTimer callback =
                                clock.CreateTimer callback
                        }

                    let input = Signal (graph, 0)

                    let x =
                        debounceWith
                            {
                                Clock = shifted a 1000000.
                                Comparer = None
                            }
                            (TimeSpan.FromMilliseconds 100.)
                            (fun () -> input.Value)
                            graph

                    let y =
                        debounceWith
                            {
                                Clock = shifted b offset
                                Comparer = None
                            }
                            (TimeSpan.FromMilliseconds 100.)
                            (fun () -> input.Value)
                            graph

                    input.Value <- 1
                    a.AdvanceTo 100.
                    b.AdvanceTo 100.
                    Expect.equal (x.Value, y.Value) (1, 1) "same admissions"
                    Trace.dumpText graph

                Expect.equal (script 0.) (script 2000000.) "origins are arbitrary"
            testCase "admission comparer failure retains the winning source cause"
            <| fun _ ->
                use graph = new Graph ()
                let clock = ManualClock ()
                let input = Signal (graph, 0)
                let auxiliary = Signal (graph, 0)

                let comparer =
                    { new Collections.Generic.IEqualityComparer<int> with
                        member _.Equals(a, b) =
                            if a = 0 && b = 2 then failwith "admission" else a = b

                        member _.GetHashCode value = value
                    }

                let output =
                    debounceWith
                        {
                            Clock = clock
                            Comparer = Some comparer
                        }
                        (TimeSpan.FromMilliseconds 100.)
                        (fun () ->
                            auxiliary.Value |> ignore
                            input.Value)
                        graph

                let effect = new Effect (graph, fun () -> output.TryValue |> ignore)
                input.Value <- 1
                input.Value <- 2
                auxiliary.Value <- 1

                let write =
                    Trace.events graph
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.Write
                        && e.Node = (input :> INode).Id
                        && e.Flag = 1)
                    |> Array.last

                clock.AdvanceTo 100.
                Expect.equal output.Status Status.Error "comparison failed at admission"
                Expect.equal (Trace.why graph effect).Root (Some (UserWrite write.Seq)) "winning capture retained"
                let history = Trace.history graph output
                Expect.isTrue history.Runs[2].Moved "winning capture owns the failure publication"
                Expect.isFalse history.Runs[3].Moved "later equal capture did not publish"
            testCase "leading suppression retains the published winning capture"
            <| fun _ ->
                use graph = new Graph ()
                let clock = ManualClock ()
                let input = Signal (graph, 0)

                let output =
                    throttleFirstWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1
                let admitted = (Trace.timing graph output |> Option.get).WinningCapture
                input.Value <- 2
                Expect.equal (Trace.timing graph output |> Option.get).WinningCapture admitted "suppressed input did not win"
            testCase "admission names the winning capture and reports the held window"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                let effect = new Effect (graph, fun () -> output.Value |> ignore)
                input.Value <- 1
                clock.AdvanceTo 50.
                input.Value <- 2
                let held = Trace.timing graph output |> Option.get
                Expect.equal (TraceModel.timing (Trace.events graph) (output :> INode).Id) (Some held) "historical held state"
                Expect.isTrue held.WindowOpen "held window"
                Expect.equal held.RemainingMilliseconds 100. "extended deadline"
                clock.AdvanceTo 150.
                let events = Trace.events graph

                let publication =
                    events
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.TimedPublished
                        && e.Node = (output :> INode).Id)
                    |> Array.last

                let captured =
                    events
                    |> Array.find (fun e -> e.Seq = publication.Cause)

                Expect.equal captured.Kind TraceEventKind.TimedCaptured "winning capture"
                Expect.equal captured.Payload (box 2) "latest payload"

                let write =
                    events
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.Write
                        && e.Node = (input :> INode).Id
                        && e.Flag = 1)
                    |> Array.last

                Expect.equal (Trace.why graph effect).Root (Some (UserWrite write.Seq)) "downstream names winning input"
                let history = Trace.history graph output
                Expect.isTrue history.Runs[2].Moved "winning capture owns the publication"
                Expect.isFalse history.Runs[1].Moved "superseded capture did not publish"
                Expect.isFalse (Trace.timing graph output |> Option.get).WindowOpen "window completed"
                Expect.equal (Trace.timing graph output |> Option.get).RemainingMilliseconds 0. "closed window has no remaining wait"

                Expect.isFalse
                    (events
                     |> Array.exists (fun e ->
                         e.Node = (output :> INode).Id
                         && e.Kind = TraceEventKind.TimedCancelled))
                    "successful admission is not cancellation"

                Expect.equal (Trace.origin graph output).Kind TraceNodeKind.Timed "distinct node kind"

                Expect.isFalse
                    (events
                     |> Array.exists (fun e ->
                         e.Node = (output :> INode).Id
                         && e.Kind = TraceEventKind.FlightStart))
                    "no synthetic flights"

                let snapshot = Trace.snapshot graph
                Expect.equal snapshot.Nodes[(output :> INode).Id].Value (Some "2") "folded publication"
            testCase "recorded timing distinguishes pending capture and failure from published state"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let state = createSignal 0
                let pending = createAsyncSource<int>()

                let output =
                    debounceWith
                        { Clock = clock; Comparer = None }
                        (TimeSpan.FromMilliseconds 100.)
                        (fun () ->
                            match state.Value with
                            | 2 -> pending.Value
                            | 3 -> failwith "capture"
                            | n -> n)
                        graph

                let assertState () =
                    let live = Trace.timing graph output
                    Expect.equal (TraceModel.timing (Trace.events graph) (output :> INode).Id) live "event folding"
#if !FABLE_COMPILER
                    let parsed = TraceModel.parseDump (Trace.dumpText graph)
                    Expect.equal (TraceModel.timing parsed.Events (output :> INode).Id) live "schema roundtrip"
#endif
                state.Value <- 1
                assertState ()
                state.Value <- 2
                assertState ()
                let held = Trace.timing graph output |> Option.get
                Expect.equal held.CapturedStatus Status.Pending "capture pending"
                Expect.equal held.PublishedStatus Status.None "published ready"
                Expect.equal held.RemainingMilliseconds 0. "cancelled wait"
                state.Value <- 3
                assertState ()
                output.Dispose ()
                assertState ()
            testCase "timing rejects another graph with colliding node identifiers"
            <| fun _ ->
                use a = new Graph ()
                use b = new Graph ()
                let x = debounce TimeSpan.Zero (fun () -> 1) a
                let y = debounce TimeSpan.Zero (fun () -> 2) b
                Expect.equal (x :> INode).Id (y :> INode).Id "collision fixture"
                Expect.isNone (Trace.timing a y) "graph identity"
            testCase "manual-clock traces and schema roundtrip are deterministic"
            <| fun _ ->
                let script () =
                    use graph = new Graph ()
                    use _active = graph.Activate ()
                    let clock = ManualClock ()
                    let input = createSignal 0

                    let output =
                        throttleLastWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                    createEffect (fun () -> output.Value |> ignore)
                    input.Value <- 1
                    clock.AdvanceTo 50.
                    input.Value <- 2
                    clock.AdvanceTo 100.
                    output.Dispose ()
                    Expect.isEmpty (Trace.reconcile graph) "live edges agree with the log"
                    Trace.dumpText graph

                let baseline = script ()
                Expect.isTrue (baseline.StartsWith ("{\"schema\":2,")) "timed schema"

                for _ in 1..4 do
                    Expect.equal (script ()) baseline "same script and callback order"
#if !FABLE_COMPILER
                let parsed = TraceModel.parseDump baseline
                let folded = TraceModel.snapshot parsed.Events

                Expect.isTrue
                    (folded.Nodes.Values
                     |> Seq.exists (fun n ->
                         n.Kind = TraceNodeKind.Timed
                         && n.Status = TraceNodeStatus.Disposed))
                    "parsed disposal"
#endif
        ]
#else
let traceTests = testList "Timed tracing" []
#endif

[<Tests>]
let debounceTests =
    testList
        "Timed debounce"
        [
            testCase "computed input extends the quiet deadline on every change"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0
                let computed = createMemo (fun _ -> input.Value * 2)

                let output =
                    debounceWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> computed.Value) graph

                let seen = ResizeArray<int>()
                createEffect (fun () -> seen.Add output.Value)
                input.Value <- 1
                clock.AdvanceTo 50.
                input.Value <- 2
                clock.AdvanceTo 90.
                input.Value <- 3
                clock.AdvanceTo 189.
                Expect.sequenceEqual seen [ 0 ] "publication is held"
                clock.AdvanceTo 190.
                Expect.sequenceEqual seen [ 0; 6 ] "last computed value after silence"
                Expect.equal clock.Arms 2 "lazy extension avoids rearming every input"
            testCase "zero delay uses normal graph propagation without a timer"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    debounceWith { Clock = clock; Comparer = None } TimeSpan.Zero (fun () -> input.Value) graph

                input.Value <- 7
                Expect.equal output.Value 7 "pass-through"
                Expect.equal clock.TimerCount 0 "no timer"
        ]

[<Tests>]
let throttleTests =
    testList
        "Timed throttle"
        [
            testCase "leading throttle never replays suppressed input and creates no timers"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    throttleFirstWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1
                input.Value <- 2
                clock.AdvanceTo 100.
                Expect.equal output.Value 1 "discarded value not replayed"
                input.Value <- 3
                Expect.equal output.Value 3 "new eligible input"
                Expect.equal clock.TimerCount 0 "timestamp only"
            testCase "trailing throttle admits latest at the first input deadline"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    throttleLastWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                input.Value <- 1
                clock.AdvanceTo 90.
                input.Value <- 2
                Expect.equal output.Value 0 "held before deadline"
                clock.AdvanceTo 100.
                Expect.equal output.Value 2 "fixed deadline"
                Expect.equal clock.Arms 1 "one arm per window"
            testCase "leading plus trailing sustains windows without catch-up bursts"
            <| fun _ ->
                use graph = new Graph ()
                use _active = graph.Activate ()
                let clock = ManualClock ()
                let input = createSignal 0

                let output =
                    throttleWith { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph

                let seen = ResizeArray<int>()
                createEffect (fun () -> seen.Add output.Value)
                input.Value <- 1
                clock.AdvanceTo 50.
                input.Value <- 2
                clock.AdvanceTo 100.
                input.Value <- 3
                clock.AdvanceTo 200.
                Expect.sequenceEqual seen [ 0; 1; 2; 3 ] "leading plus one latest trailing per interval"
        ]
