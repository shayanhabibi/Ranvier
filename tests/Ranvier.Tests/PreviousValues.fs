module Ranvier.Tests.PreviousValues

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Tests.Readings

/// <summary>A flight the test completes by hand, inline on the settling thread.</summary>
type private Flight<'T>() =
    let source = TaskCompletionSource<'T>()
    member _.Task = source.Task

    member _.Settle(v: 'T) =
        source.SetResult v

    member _.Fail(e: exn) =
        source.SetException e

let private withPolicy policy =
    new Graph (
        { GraphOptions.Default with
            FlightPolicy = policy
        }
    )

/// <summary>
/// An async memo whose runs each start one hand-settled flight, recording the flight and the run's <c>prev.Settled</c>.
/// </summary>
let private recording (g: Graph) (trigger: Signal<int>) =
    let flights = ResizeArray<Flight<int>>()
    let prevs = ResizeArray<Task<int voption>>()

    let a =
        Make.AsyncMemo<int>(
            g,
            fun prev _ ->
                trigger.Value |> ignore
                prevs.Add prev.Settled
                let f = Flight<int>()
                flights.Add f
                f.Task
        )

    a, flights, prevs

/// <summary>Starts <c>count</c> flights, bumping the trigger before each after the first.</summary>
let private launch (a: AsyncMemo<int>) (trigger: Signal<int>) (count: int) =
    for i in 1..count do
        if i > 1 then
            trigger.Value <- trigger.Value + 1

        a.TryValue |> ignore

#if !FABLE_COMPILER
// .NET only: a promise exposes no synchronous completion state.
let private settledWith (t: Task<'T>) =
    if t.IsCompletedSuccessfully then
        ValueSome t.Result
    else
        ValueNone
#endif

#if !FABLE_COMPILER
// .NET only: blocks the test thread until a thread-pool continuation runs.
/// <summary>Waits for <c>condition</c> to hold, failing the test after five seconds.</summary>
let private waitUntil (what: string) (condition: unit -> bool) =
    let deadline = DateTime.UtcNow.AddSeconds 5.0

    while not (condition ()) do
        if DateTime.UtcNow > deadline then
            failtestf "timed out waiting for %s" what

        Thread.Sleep 1
#endif

/// <summary>
/// A memo's compute receives the value the memo last published: <c>ValueNone</c>
/// before the first, and the last settled value after a run that suspends or fails.
/// </summary>
[<Tests>]
let tests =
    testList
        "PreviousValues"
        [
            test "the first run receives ValueNone" {
                let g = new Graph ()
                let seen = ResizeArray ()

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            1
                    )

                m.Value |> ignore
                Expect.sequenceEqual seen [ ValueNone ] "nothing has been published"
            }

            test "a later run receives the value last published" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            s.Value * 10
                    )

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                Expect.sequenceEqual seen [ ValueNone; ValueSome 10 ] "the second run sees the first result"
            }

            test "a fold steps once per unbatched write to an observed memo" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let total = Make.Memo (g, (fun prev -> ValueOption.defaultValue 0 prev + s.Value))

                new Effect (g, (fun () -> total.Value |> ignore))
                |> ignore

                s.Value <- 1
                s.Value <- 2
                s.Value <- 3
                Expect.equal total.Peek 6 "each write is folded in"
            }

            test "a fold steps once per batch" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let total = Make.Memo (g, (fun prev -> ValueOption.defaultValue 0 prev + s.Value))

                new Effect (g, (fun () -> total.Value |> ignore))
                |> ignore

                g.Batch (fun () ->
                    s.Value <- 1
                    s.Value <- 2
                    s.Value <- 3)

                Expect.equal total.Peek 3 "the batch folds its final value once"
                Expect.equal total.Runs 2 "one run for the batch"
            }

            test "a suspended run leaves prev unchanged" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = AsyncSource<int> g
                let gate = Signal (g, false)
                let seen = ResizeArray ()

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            seen.Add prev
                            let v = s.Value
                            if gate.Value then v + a.Value else v
                    )

                Expect.equal m.Value 1 "published"

                gate.Value <- true
                Expect.equal m.TryValue Pending "suspended"
                s.Value <- 2
                Expect.equal m.TryValue Pending "still suspended"

                a.Settle 10
                Expect.equal m.Value 12 "settled"

                Expect.sequenceEqual seen [ ValueNone; ValueSome 1; ValueSome 1; ValueSome 1 ] "every run after the first sees the last settled value"
            }

            test "a failed run leaves prev at the last settled value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            seen.Add prev

                            if s.Value < 0 then
                                failwith "negative"

                            s.Value
                    )

                m.Value |> ignore
                s.Value <- -1
                Expect.isTrue (m.TryValue.IsFailed) "failed"
                s.Value <- 5
                Expect.equal m.Value 5 "recovered"

                Expect.sequenceEqual seen [ ValueNone; ValueSome 1; ValueSome 1 ] "the failure published nothing"
            }

            test "a failed first run passes ValueNone to the next run" {
                let g = new Graph ()
                let s = Signal (g, -1)
                let seen = ResizeArray ()

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            seen.Add prev

                            if s.Value < 0 then
                                failwith "negative"

                            s.Value
                    )

                Expect.isTrue (m.TryValue.IsFailed) "failed"
                s.Value <- 5
                Expect.equal m.Value 5 "recovered"

                Expect.sequenceEqual seen [ ValueNone; ValueNone ] "nothing was ever published"
            }

            test "returning prev triggers the cutoff" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let observed = ref 0

                let m =
                    Make.Memo (
                        g,
                        fun prev ->
                            let v = s.Value

                            match prev with
                            | ValueSome (p: obj) when v > 0 -> p
                            | _ -> box v
                    )

                new Effect (
                    g,
                    fun () ->
                        m.Value |> ignore
                        observed.Value <- observed.Value + 1
                )
                |> ignore

                s.Value <- 2
                Expect.equal m.Runs 2 "the memo re-ran"
                Expect.equal observed.Value 1 "the same instance does not wake the observer"
                Expect.equal (unbox<int> m.Peek) 1 "the retained value is the first"
            }

            test "a seeded Suspense fallback receives the seed, then the last value" {
                let g = new Graph ()
                let first = AsyncSource<int> g
                let second = AsyncSource<int> g
                let current = Signal (g, first)
                let seen = ResizeArray ()

                let b =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () -> current.Value.Value),
                            -1,
                            fun last ->
                                seen.Add last
                                last
                        )

                Expect.equal b.Value -1 "the seed before a value"
                first.Settle 5
                Expect.equal b.Value 5 "settled"
                current.Value <- second
                Expect.equal b.Value 5 "the last value while pending"
                Expect.sequenceEqual seen [ -1; 5 ] "the seed, then the last value"
            }

            test "a seeded Errors recover receives the error and the seed, then the last value" {
                let g = new Graph ()
                let s = Signal (g, -1)
                let seen = ResizeArray ()

                let b =
                    Boundary<int>
                        .Errors(
                            g,
                            (fun () -> if s.Value < 0 then failwith "negative" else s.Value),
                            100,
                            fun error last ->
                                seen.Add (error.Message, last)
                                last
                        )

                Expect.equal b.Value 100 "the seed before a value"
                s.Value <- 3
                Expect.equal b.Value 3 "recovered"
                s.Value <- -2
                Expect.equal b.Value 3 "the last value while failing"
                Expect.sequenceEqual seen [ ("negative", 100); ("negative", 3) ] "the seed, then the last value"
            }

            test "a seeded Catching boundary passes the seed, then its last value, to both handlers" {
                let g = new Graph ()
                let pending = AsyncSource<int> g
                let mode = Signal (g, 0)

                let b =
                    Boundary<int>
                        .Catching(
                            g,
                            (fun () ->
                                match mode.Value with
                                | 0 -> pending.Value
                                | 1 -> failwith "failed"
                                | v -> v),
                            7,
                            (fun last -> last + 1000),
                            fun _ last -> last + 2000
                        )

                Expect.equal b.Value 1007 "the fallback over the seed"
                mode.Value <- 1
                Expect.equal b.Value 3007 "recover over the fallback value"
                mode.Value <- 9
                Expect.equal b.Value 9 "a value"
                mode.Value <- 1
                Expect.equal b.Value 2009 "recover over the last value"
            }

#if !FABLE_COMPILER
            // .NET only: Fable drops the Memo constructors.
            test "a seeded memo receives the seed, then the value last published" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let m =
                    Memo<int>(
                        g,
                        100,
                        fun last ->
                            seen.Add last
                            last + s.Value
                    )

                Expect.equal m.Value 101 "the seed before the first run"
                s.Value <- 5
                Expect.equal m.Value 106 "the value last published after"
                Expect.sequenceEqual seen [ 100; 101 ] "the seed, then the published value"
            }

            test "a seeded owning memo owns the nodes its body creates" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let m =
                    Memo<int>(g, 0, (fun last -> last + (Memo<int>(g, (fun _ -> s.Value))).Value), true)

                Expect.equal m.Value 1 "the inner memo is owned"
                s.Value <- 2
                Expect.equal m.Value 3 "folded"

                let pureMemo = Memo<int>(g, 0, (fun _ -> (Memo<int>(g, (fun _ -> 1))).Value))
                Expect.throws (fun () -> pureMemo.Value |> ignore) "a pure one raises"
            }

            test "the Memo constructor call forms resolve beside the seeded overloads" {
                let g = new Graph ()
                let s = Signal (g, 2)
                let ignoring = Memo (g, fun _ -> s.Value)
                let owning = Memo (g, (fun _ -> s.Value), true)
                let fold = Memo (g, (fun last -> ValueOption.defaultValue 0 last + s.Value), false)
                let flag = Memo<bool>(g, (fun _ -> s.Value > 1), true)
                let seeded = Memo (g, 10, fun last -> last + s.Value)
                let seededFlag = Memo (g, true, fun last -> not last)

                Expect.equal [ ignoring.Value; owning.Value; fold.Value; seeded.Value ] [ 2; 2; 2; 12 ] "each form builds its memo"
                Expect.equal (flag.Value, seededFlag.Value) (true, false) "and the bool forms"
            }
#endif
        ]

/// <summary>
/// An async memo's flight receives the value published before it starts; under <c>Queue</c>, the value after the flight
/// started before it is applied.
/// </summary>
[<Tests>]
let asyncTests =
    testList
        "PreviousValues.AsyncMemo"
        [
#if !FABLE_COMPILER
            // .NET only: reads the prev task's completion synchronously, and under Fable it is a promise.
            test "a default-policy prev is complete at launch" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let a, flights, prevs = recording g trigger

                a.TryValue |> ignore
                Expect.equal (settledWith prevs[0]) (ValueSome ValueNone) "the first flight sees nothing published"

                flights[0].Settle 10
                trigger.Value <- 1
                a.TryValue |> ignore
                Expect.equal (settledWith prevs[1]) (ValueSome (ValueSome 10)) "the second flight sees the first result"
            }

            testList
                "a superseded flight's result never appears in prev"
                [
                    for policy in [ CancelPrevious; KeepLatest ] ->
                        test $"%A{policy}" {
                            let g = withPolicy policy
                            let trigger = Signal (g, 0)
                            let a, flights, prevs = recording g trigger

                            launch a trigger 2
                            flights[1].Settle 22
                            flights[0].Settle 11
                            trigger.Value <- 2
                            a.TryValue |> ignore

                            Expect.equal (settledWith prevs[1]) (ValueSome ValueNone) "overlapping flights launch from the same value"
                            Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 22)) "only the newest flight published"
                        }
                ]

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a read of Settled off the graph thread never caches a value older than the one published" {
                let g = withPolicy KeepLatest
                let trigger = Signal (g, 0)
                let flights = Collections.Generic.List<TaskCompletionSource<int>>()
                let handles = Collections.Generic.List<Previous<int>>()

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun prev _ ->
                            trigger.Value |> ignore
                            handles.Add prev
                            let flight = TaskCompletionSource<int>()
                            flights.Add flight
                            flight.Task
                    )

                a.TryValue |> ignore
                flights[0].SetResult 0
                a.TryValue |> ignore

                let read = ref handles[0]
                let running = ref true

                let reader =
                    Thread (fun () ->
                        while running.Value do
                            read.Value.Settled |> ignore)

                reader.Start ()
                let mutable stale = 0

                try
                    for k in 1..20000 do
                        trigger.Value <- k
                        a.TryValue |> ignore
                        read.Value <- handles[handles.Count - 1]
                        flights[flights.Count - 1].SetResult k
                        trigger.Value <- -k
                        a.TryValue |> ignore

                        if
                            handles[handles.Count - 1].Settled.Result
                            <> ValueSome k
                        then
                            stale <- stale + 1

                        flights[flights.Count - 1].SetResult k
                finally
                    running.Value <- false
                    reader.Join ()

                Expect.equal stale 0 "every flight sees the value published before it"
            }
#endif

            test "Queue chains three or more overlapping flights in start order" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let a, flights, prevs = recording g trigger

                launch a trigger 4
                Expect.equal (settledWith prevs[0]) (ValueSome ValueNone) "the first flight had no predecessor"
                Expect.isFalse prevs[1].IsCompleted "the second waits on the first"

                flights[2].Settle 30
                flights[1].Settle 20
                Expect.isFalse prevs[1].IsCompleted "later results wait their turn"

                flights[0].Settle 10
                Expect.equal (settledWith prevs[1]) (ValueSome (ValueSome 10)) "flight 2 sees flight 1"
                Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 20)) "flight 3 sees flight 2"
                Expect.equal (settledWith prevs[3]) (ValueSome (ValueSome 30)) "flight 4 sees flight 3"
            }

            test "Queue resolves each waiter with its own predecessor" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let a, flights, prevs = recording g trigger

                launch a trigger 3
                flights[0].Settle 10
                Expect.equal (settledWith prevs[1]) (ValueSome (ValueSome 10)) "flight 2 sees flight 1"
                Expect.isFalse prevs[2].IsCompleted "flight 3 still waits on flight 2"

                flights[1].Settle 20
                Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 20)) "flight 3 sees flight 2, not flight 1"
            }

            test "Queue resolves the waiter when the predecessor faults" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let a, flights, prevs = recording g trigger

                launch a trigger 3
                flights[0].Settle 10
                flights[1].Fail(InvalidOperationException "middle")

                Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 10)) "the last settled value"
            }

            test "Queue resolves the waiter when the predecessor is dropped" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let suspend = Signal (g, false)
                let upstream = AsyncSource<int> g
                let flights = ResizeArray<Flight<int>>()
                let prevs = ResizeArray<Task<int voption>>()

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun prev _ ->
                            trigger.Value |> ignore

                            if suspend.Value then
                                completed upstream.Value
                            else
                                prevs.Add prev.Settled
                                let f = Flight<int>()
                                flights.Add f
                                f.Task
                    )

                a.TryValue |> ignore
                flights[0].Settle 5
                trigger.Value <- 1
                a.TryValue |> ignore
                trigger.Value <- 2
                a.TryValue |> ignore
                suspend.Value <- true
                a.TryValue |> ignore
                Expect.equal (Seq.length a.PendingSources) 1 "the newest run is suspended on a source"
                Expect.isFalse prevs[2].IsCompleted "flight 3 waits on flight 2"

                flights[1].Fail(InvalidOperationException "dropped")
                Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 5)) "the last settled value"
            }

            test "Queue passes a result applied while suspended to the next run" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let upstream = AsyncSource<int> g
                let flights = ResizeArray<Flight<int>>()
                let prevs = ResizeArray<Task<int voption>>()

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun prev _ ->
                            if trigger.Value = 1 then
                                upstream.Value |> ignore

                            prevs.Add prev.Settled
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                launch a trigger 2
                flights[0].Settle 10
                Expect.equal a.TryValue Pending "still suspended on the source"

                upstream.Settle 1
                a.TryValue |> ignore
                Expect.equal (settledWith prevs[1]) (ValueSome (ValueSome 10)) "the value applied while suspended"
            }

            test "Queue completes the waiter on disposal" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let a, flights, prevs = recording g trigger

                a.TryValue |> ignore
                flights[0].Settle 10
                trigger.Value <- 1
                launch a trigger 2
                Expect.isFalse prevs[2].IsCompleted "waiting on flight 2"

                a.Dispose ()
                Expect.equal (settledWith prevs[2]) (ValueSome (ValueSome 10)) "the last settled value"
            }

#if !FABLE_COMPILER
            // .NET only: blocks the test thread until a thread-pool continuation runs.
            test "Queue loses a read made after awaiting prev" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let late = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()
                let bodies = ResizeArray<Task<int>>()

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun prev _ ->
                            let n = trigger.Value

                            let body =
                                if n = 0 then
                                    let f = Flight<int>()
                                    flights.Add f
                                    f.Task
                                else
                                    task {
                                        let! p = prev.Settled
                                        return ValueOption.defaultValue 0 p + late.Value
                                    }

                            bodies.Add body
                            body
                    )

                launch a trigger 2
                flights[0].Settle 10
                waitUntil "the second flight" (fun () -> bodies[1].IsCompleted)
                waitUntil "the result in the inbox" (fun () -> g.PendingWork > 0)
                g.Pump () |> ignore
                Expect.equal a.TryValue (Ready 10) "the chain folded"

                let runs = a.Runs
                late.Value <- 5
                a.TryValue |> ignore
                Expect.equal a.Runs runs "the read after the await was not tracked"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: blocks the test thread until a thread-pool continuation runs.
            test "Queue resumes a body awaiting prev outside applyResult" {
                let g = withPolicy FlightPolicy.Queue
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()
                let bodies = ResizeArray<Task<int>>()
                let resumedOn = ref 0

                let a =
                    Make.AsyncMemo<int>(
                        g,
                        fun prev _ ->
                            let n = trigger.Value

                            let body =
                                if n = 0 then
                                    let f = Flight<int>()
                                    flights.Add f
                                    f.Task
                                else
                                    task {
                                        let! p = prev.Settled
                                        resumedOn.Value <- Environment.CurrentManagedThreadId
                                        return ValueOption.defaultValue 0 p + 1
                                    }

                            bodies.Add body
                            body
                    )

                launch a trigger 2
                flights[0].Settle 10
                waitUntil "the second flight" (fun () -> bodies[1].IsCompleted)
                Expect.notEqual resumedOn.Value Environment.CurrentManagedThreadId "the continuation left the settling thread"
            }
#endif
#endif
        ]
